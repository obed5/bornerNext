using BorderNext.Application.Abstractions;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Nature de la liste d'alerte visée.</summary>
public enum StopListKind
{
    Person,
    Document
}

/// <summary>Critères d'un contrôle de personne (héritage : paramètres des fonctions
/// <c>fn_CheckPersonInPersonsStopListWithSimulationOption</c> /
/// <c>fn_CheckPersonInDocumentsStopListWithSimulationOption</c>).</summary>
/// <param name="Surname">Nom (les parties — nom entier, première partie, reste — sont
/// testées séparément par les critères 3 à 7, comme le curseur hérité sur SplitName).</param>
/// <param name="GivenName">Prénoms.</param>
/// <param name="DocumentNumber">Numéro de document (numéro personnel pour les listes documents).</param>
/// <param name="PersonalNumber">Numéro personnel (critère 1 des listes de personnes uniquement).</param>
/// <param name="DateOfBirth">Date de naissance (critères exacts + fenêtre ± 5 ans).</param>
/// <param name="CitizenshipId">Nationalité, comparée au réglage « Country » pour le seuil national.</param>
public sealed record StopListCriteria(
    string? Surname = null,
    string? GivenName = null,
    string? DocumentNumber = null,
    string? PersonalNumber = null,
    DateTime? DateOfBirth = null,
    int? CitizenshipId = null);

/// <summary>Correspondance trouvée dans une liste d'alerte.</summary>
/// <param name="Criterion">Numéro de critère hérité : personnes 1 (numéro personnel) et 2
/// (numéro de document) puis 3–7 (nominaux) ; documents 1–6.</param>
public sealed record StopListHit(
    StopListKind Kind,
    int EntryId,
    int Criterion,
    int? ActionId,
    string? ActionCode,
    string? ActionDescription,
    string? ActionRequired,
    string? Info,
    string? Surname,
    string? GivenName,
    string? FathersName,
    string? DocumentNumber,
    string? PersonalNumber,
    int? CitizenshipId,
    int? DocumentTypeId,
    DateTime? DateOfBirth,
    DateTime? SetUpDate,
    int? StopListSourceId,
    string? SourceDocument);

/// <summary>
/// Moteur de correspondance des listes d'alerte, reconstruit depuis les fonctions
/// héritées (fonctions du système précédent) :
///  - contrôle « personne » : 7 critères ordonnés, retour au premier critère satisfait,
///    boucle héritée parties × parties (SplitName), priorité interne par code d'action
///    (ORDER BY ExternalID ASC, valeurs nulles d'abord) ;
///  - contrôle « document » : 6 critères, noms complets uniquement (ni parties ni variantes) ;
///  - seuil des numéros (personnel / document) : égalité pour les nationaux (réglage
///    « Country »), distance de Levenshtein ≤ 1 sinon ;
///  - recherche par type (héritage : GetPersonsStopListByMatchingType 1–8,
///    GetDocumentsStopListByMatchingType 1–8) pour le simulateur d'alerte et le
///    contrôle inverse « une entrée de liste correspond-elle au registre ? ».
///
/// Différences assumées :
///  - le SQL hérité concaténait les chaînes (surface d'injection) : tout passe par
///    des requêtes EF paramétrées ;
///  - les clauses copiées-collées défaillantes de l'héritage sont reproduites telles
///    quelles et signalées (commentaires « Fidélité ») ;
///  - les entrées annulées (CancelDate renseignée) ne participent jamais, comme le
///    prédicat hérité (CancelDate IS NULL OR CancelDate > GETDATE()).
/// </summary>
public sealed class StopListMatcher
{
    private readonly DbContext _db;
    private readonly IClock _clock;

    public StopListMatcher(DbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    // -----------------------------------------------------------------------
    // Contrôle temps réel (enregistrement frontière)
    // -----------------------------------------------------------------------

    /// <summary>Contrôle « cette personne est-elle sur la liste d'alerte ? »
    /// (héritage : fn_CheckPersonInPersonsStopListWithSimulationOption).</summary>
    /// <param name="onlyEntryId">Contrôle inverse : restreint la recherche à une entrée
    /// donnée (héritage : @PersonStopListID). Fidélité : le critère 7 (noms inversés)
    /// n'appliquait pas ce filtre dans l'héritage.</param>
    public async Task<StopListHit?> CheckPersonAsync(StopListCriteria criteria, int? onlyEntryId = null, CancellationToken ct = default)
    {
        var country = await GetCountrySettingAsync(ct);
        var isCitizen = country is { } c && criteria.CitizenshipId == c;
        var bound = isCitizen ? 0 : 1;

        // Critère 1 — numéro personnel (uniquement lorsqu'il est fourni, comme @personalNumber <> '').
        if (!string.IsNullOrEmpty(criteria.PersonalNumber))
        {
            var candidates = await ActivePersons()
                .Where(p => p.CitizenshipId == criteria.CitizenshipId && p.PersonalNumber != null)
                .Where(p => onlyEntryId == null || p.Id == onlyEntryId)
                .ToListAsync(ct);

            var entry = FirstByPriority(candidates, p => isCitizen
                ? NameMatching.NamesEqual(p.PersonalNumber, criteria.PersonalNumber)
                : NameMatching.DistanceAtMostFolded(p.PersonalNumber, criteria.PersonalNumber, bound));
            if (entry != null) return ToHit(entry, 1);
        }

        // Critère 2 — numéro de document.
        if (!string.IsNullOrEmpty(criteria.DocumentNumber))
        {
            var candidates = await ActivePersons()
                .Where(p => p.CitizenshipId == criteria.CitizenshipId && p.DocumentNumber != null)
                .Where(p => onlyEntryId == null || p.Id == onlyEntryId)
                .ToListAsync(ct);

            var entry = FirstByPriority(candidates, p => isCitizen
                ? NameMatching.NamesEqual(p.DocumentNumber, criteria.DocumentNumber)
                : NameMatching.DistanceAtMostFolded(p.DocumentNumber, criteria.DocumentNumber, bound));
            if (entry != null) return ToHit(entry, 2);
        }

        if (criteria.DateOfBirth is not { } dob) return null;

        var surnameParts = NameMatching.SplitNameFolded(criteria.Surname);
        var givenParts = NameMatching.SplitNameFolded(criteria.GivenName);

        var dobCandidates = await ActivePersons()
            .Where(p => p.DateOfBirth == dob)
            .Where(p => onlyEntryId == null || p.Id == onlyEntryId)
            .ToListAsync(ct);

        // Critères 3 à 7 : le curseur hérité parcourt les parties du nom puis des prénoms ;
        // le premier critère satisfait (dans cet ordre de critères) gagne.
        foreach (var (s, g) in PartPairs(surnameParts, givenParts))
        {
            // 3 — prénom exact (principal ou variante) + date exacte + nom proche (< 3).
            var entry = FirstByPriority(dobCandidates, p =>
                (NameMatching.NamesEqual(p.GivenName, g) || NameMatching.NamesEqual(p.GivenNameAlt, g))
                && (NameMatching.DistanceBelow(s, p.Surname, NameMatching.SurnameFuzzyBound)
                    || NameMatching.DistanceBelow(s, p.SurnameAlt, NameMatching.SurnameFuzzyBound)));
            if (entry != null) return ToHit(entry, 3);
        }

        foreach (var (s, g) in PartPairs(surnameParts, givenParts))
        {
            // 4 — nom exact (principal ou variante) + date exacte + prénom proche (< 3).
            var entry = FirstByPriority(dobCandidates, p =>
                (NameMatching.NamesEqual(p.Surname, s) || NameMatching.NamesEqual(p.SurnameAlt, s))
                && (NameMatching.DistanceBelow(g, p.GivenName, NameMatching.SurnameFuzzyBound)
                    || NameMatching.DistanceBelow(g, p.GivenNameAlt, NameMatching.SurnameFuzzyBound)));
            if (entry != null) return ToHit(entry, 4);
        }

        foreach (var (s, g) in PartPairs(surnameParts, givenParts))
        {
            // 5 — les deux noms proches (< 2) + date exacte.
            var entry = FirstByPriority(dobCandidates, p =>
                (NameMatching.DistanceBelow(g, p.GivenName, NameMatching.NamesFuzzyBound)
                 || NameMatching.DistanceBelow(g, p.GivenNameAlt, NameMatching.NamesFuzzyBound))
                && (NameMatching.DistanceBelow(s, p.Surname, NameMatching.NamesFuzzyBound)
                    || NameMatching.DistanceBelow(s, p.SurnameAlt, NameMatching.NamesFuzzyBound)));
            if (entry != null) return ToHit(entry, 5);
        }

        // Critère 6 — noms exacts (principaux ou variantes) + année de naissance ± 5.
        var yearCandidates = await ActivePersons()
            .Where(p => p.DateOfBirth != null
                        && p.DateOfBirth >= dob.AddYears(-(NameMatching.BirthYearWindow - 1))
                        && p.DateOfBirth <= dob.AddYears(NameMatching.BirthYearWindow - 1).AddYears(1).AddDays(-1))
            .Where(p => onlyEntryId == null || p.Id == onlyEntryId)
            .ToListAsync(ct);

        foreach (var (s, g) in PartPairs(surnameParts, givenParts))
        {
            var entry = FirstByPriority(yearCandidates, p =>
                NameMatching.WithinBirthYearWindow(p.DateOfBirth, dob)
                && ((NameMatching.NamesEqual(p.Surname, s) && NameMatching.NamesEqual(p.GivenName, g))
                    || (NameMatching.NamesEqual(p.SurnameAlt, s) && NameMatching.NamesEqual(p.GivenNameAlt, g))));
            if (entry != null) return ToHit(entry, 6);
        }

        foreach (var (s, g) in PartPairs(surnameParts, givenParts))
        {
            // 7 — nom et prénom inversés + date exacte. Fidélité : le filtre d'entrée
            // unique (@PersonStopListID) n'était pas appliqué à ce critère.
            var entry = FirstByPriority(dobCandidates, p =>
                (NameMatching.NamesEqual(p.Surname, g) && NameMatching.NamesEqual(p.GivenName, s))
                || (NameMatching.NamesEqual(p.SurnameAlt, g) && NameMatching.NamesEqual(p.GivenNameAlt, s)));
            if (entry != null) return ToHit(entry, 7);
        }

        return null;
    }

    /// <summary>Contrôle « ce document est-il sur la liste d'alerte documents ? »
    /// (héritage : fn_CheckPersonInDocumentsStopListWithSimulationOption — ni parties
    /// ni variantes « Alt » dans cette fonction).</summary>
    public async Task<StopListHit?> CheckDocumentAsync(StopListCriteria criteria, int? onlyEntryId = null, CancellationToken ct = default)
    {
        var country = await GetCountrySettingAsync(ct);
        var isCitizen = country is { } c && criteria.CitizenshipId == c;
        var bound = isCitizen ? 0 : 1;

        var surname = criteria.Surname ?? string.Empty;

        if (criteria.DateOfBirth is { } dob)
        {
            var withDob = (await ActiveDocuments()
                    .Where(d => d.DateOfBirth == dob)
                    .Where(d => onlyEntryId == null || d.Id == onlyEntryId)
                    .ToListAsync(ct))
                .OrderBy(ActionPriority).ThenBy(d => d.Id).ToList();

            // 1 — prénom exact + date exacte + nom proche (< 3).
            var match = withDob.FirstOrDefault(d =>
                NameMatching.NamesEqual(d.GivenName, criteria.GivenName)
                && NameMatching.DistanceBelow(surname, d.Surname ?? string.Empty, NameMatching.SurnameFuzzyBound));
            if (match != null) return ToHit(match, 1);

            // 2 — nom exact + date exacte + prénom proche (< 3).
            match = withDob.FirstOrDefault(d =>
                NameMatching.NamesEqual(d.Surname, criteria.Surname)
                && NameMatching.DistanceBelow(criteria.GivenName, d.GivenName ?? string.Empty, NameMatching.SurnameFuzzyBound));
            if (match != null) return ToHit(match, 2);

            // 3 — les deux noms proches (< 2) + date exacte.
            match = withDob.FirstOrDefault(d =>
                NameMatching.DistanceBelow(criteria.GivenName, d.GivenName ?? string.Empty, NameMatching.NamesFuzzyBound)
                && NameMatching.DistanceBelow(surname, d.Surname ?? string.Empty, NameMatching.NamesFuzzyBound));
            if (match != null) return ToHit(match, 3);

            // 4 — les deux noms exacts + année ± 5.
            var yearCandidates = (await ActiveDocuments()
                    .Where(d => d.DateOfBirth != null
                                && d.DateOfBirth >= dob.AddYears(-4)
                                && d.DateOfBirth <= dob.AddYears(4))
                    .Where(d => onlyEntryId == null || d.Id == onlyEntryId)
                    .ToListAsync(ct))
                .Where(d => NameMatching.WithinBirthYearWindow(d.DateOfBirth, dob))
                .OrderBy(ActionPriority).ThenBy(d => d.Id).ToList();
            match = yearCandidates.FirstOrDefault(d =>
                NameMatching.NamesEqual(d.Surname, criteria.Surname)
                && NameMatching.NamesEqual(d.GivenName, criteria.GivenName));
            if (match != null) return ToHit(match, 4);

            // 5 — nom et prénom inversés + date exacte.
            match = withDob.FirstOrDefault(d =>
                NameMatching.NamesEqual(d.Surname, criteria.GivenName)
                && NameMatching.NamesEqual(d.GivenName, criteria.Surname));
            if (match != null) return ToHit(match, 5);
        }

        // 6 — pays de délivrance = nationalité + numéro proche (≤ 0 pour un national, ≤ 1 sinon).
        var documentNumber = criteria.DocumentNumber ?? string.Empty;
        var byCountry = (await ActiveDocuments()
                .Where(d => d.CountryId == criteria.CitizenshipId)
                .Where(d => onlyEntryId == null || d.Id == onlyEntryId)
                .ToListAsync(ct))
            .OrderBy(ActionPriority).ThenBy(d => d.Id).ToList();
        var numberMatch = byCountry.FirstOrDefault(d =>
            NameMatching.DistanceAtMostFolded(d.DocumentNumber ?? string.Empty, documentNumber, bound));
        if (numberMatch != null) return ToHit(numberMatch, 6);

        return null;
    }

    // -----------------------------------------------------------------------
    // Recherche par type (simulateur d'alerte et contrôle inverse)
    // -----------------------------------------------------------------------

    /// <summary>Recherche par type de correspondance, personnes (héritage
    /// GetPersonsStopListByMatchingType) : 1 exact+date, 2 document, 3 nom proche (&lt;3),
    /// 4 prénom proche (&lt;3), 5 exact±5 ans, 6 proches (&lt;2), 7 inversés, 8 document proche (&lt;2).</summary>
    public async Task<IReadOnlyList<PersonStopList>> SearchPersonsAsync(int matchingType, StopListCriteria criteria, int take = 200, CancellationToken ct = default)
    {
        var entries = await ActivePersons().ToListAsync(ct);

        var surnameParts = NameMatching.SplitNameFolded(criteria.Surname);
        var givenParts = NameMatching.SplitNameFolded(criteria.GivenName);
        var fullSurname = criteria.Surname?.ToLowerInvariant();
        var fullGiven = criteria.GivenName?.ToLowerInvariant();
        var dob = criteria.DateOfBirth;

        bool AnyPartEquals(IReadOnlyList<string> parts, string? value) => parts.Any(p => NameMatching.NamesEqual(value, p));
        bool AnyPartBelow(IReadOnlyList<string> parts, string? value, int bound) => parts.Any(p => NameMatching.DistanceBelow(p, value, bound));

        var filtered = entries.Where(e => matchingType switch
        {
            // 1 — noms exacts (parties, principaux ou variantes appariées) + date exacte.
            1 => dob is { } d1
                 && ((AnyPartEquals(givenParts, e.GivenName) && AnyPartEquals(surnameParts, e.Surname))
                     || (AnyPartEquals(givenParts, e.GivenNameAlt) && AnyPartEquals(surnameParts, e.SurnameAlt)))
                 && e.DateOfBirth == d1,

            // 2 — nationalité + numéro de document exact.
            2 => e.CitizenshipId == criteria.CitizenshipId
                 && NameMatching.NamesEqual(e.DocumentNumber, criteria.DocumentNumber),

            // 3 — prénom exact (parties, principal ou variante) + date exacte + nom proche (< 3).
            //     Fidélité : la clause « Alt » héritée ne teste que le nom entier (copie-coller défaillant).
            3 => dob is { } d3
                 && (AnyPartEquals(givenParts, e.GivenName) || AnyPartEquals(givenParts, e.GivenNameAlt))
                 && e.DateOfBirth == d3
                 && (AnyPartBelow(surnameParts, e.Surname, NameMatching.SurnameFuzzyBound)
                     || NameMatching.DistanceBelow(fullSurname, e.SurnameAlt, NameMatching.SurnameFuzzyBound)),

            // 4 — nom exact (parties) + date exacte + prénom proche (< 3). Même fidélité que 3.
            4 => dob is { } d4
                 && (AnyPartEquals(surnameParts, e.Surname) || AnyPartEquals(surnameParts, e.SurnameAlt))
                 && e.DateOfBirth == d4
                 && (AnyPartBelow(givenParts, e.GivenName, NameMatching.SurnameFuzzyBound)
                     || NameMatching.DistanceBelow(fullGiven, e.GivenNameAlt, NameMatching.SurnameFuzzyBound)),

            // 5 — noms exacts (parties, principaux ou variantes appariées) + année ± 5.
            5 => dob is { } d5
                 && ((AnyPartEquals(givenParts, e.GivenName) && AnyPartEquals(surnameParts, e.Surname))
                     || (AnyPartEquals(givenParts, e.GivenNameAlt) && AnyPartEquals(surnameParts, e.SurnameAlt)))
                 && NameMatching.WithinBirthYearWindow(e.DateOfBirth, d5),

            // 6 — date exacte + deux noms proches (< 2). Fidélité : la clause « Alt » du prénom
            //     héritée teste à nouveau le prénom principal (variantes ignorées).
            6 => dob is { } d6
                 && e.DateOfBirth == d6
                 && (AnyPartBelow(surnameParts, e.Surname, NameMatching.NamesFuzzyBound)
                     || NameMatching.DistanceBelow(fullSurname, e.SurnameAlt, NameMatching.NamesFuzzyBound))
                 && AnyPartBelow(givenParts, e.GivenName, NameMatching.NamesFuzzyBound),

            // 7 — nom et prénom inversés (parties) + date exacte. Fidélité : les deux blocs
            //     hérités sont identiques (variantes « Alt » ignorées).
            7 => dob is { } d7
                 && e.DateOfBirth == d7
                 && AnyPartEquals(givenParts, e.Surname) && AnyPartEquals(surnameParts, e.GivenName),

            // 8 — nationalité + numéro de document proche (< 2, seuil hérité conservé).
            8 => e.CitizenshipId == criteria.CitizenshipId
                 && NameMatching.DistanceBelow(e.DocumentNumber ?? string.Empty, criteria.DocumentNumber, NameMatching.NamesFuzzyBound),

            _ => false
        });

        return filtered
            .OrderByDescending(e => e.UpdateDate)
            .ThenBy(e => e.Id)
            .Take(take)
            .ToList();
    }

    /// <summary>Recherche par type de correspondance, documents (héritage
    /// GetDocumentsStopListByMatchingType) : 1 exact+date, 2 document, 3 nom proche,
    /// 4 prénom proche, 5 exact±5 ans, 6 proches (&lt;2), 7 inversés, 8 document proche (&lt;2).</summary>
    public async Task<IReadOnlyList<DocumentStopList>> SearchDocumentsAsync(int matchingType, StopListCriteria criteria, int take = 200, CancellationToken ct = default)
    {
        var entries = await ActiveDocuments().ToListAsync(ct);
        var dob = criteria.DateOfBirth;

        var filtered = entries.Where(e => matchingType switch
        {
            1 => dob is { } d1 && e.DateOfBirth == d1
                 && NameMatching.NamesEqual(e.GivenName, criteria.GivenName)
                 && NameMatching.NamesEqual(e.Surname, criteria.Surname),
            2 => e.CountryId == criteria.CitizenshipId
                 && NameMatching.NamesEqual(e.DocumentNumber, criteria.DocumentNumber),
            3 => dob is { } d3 && e.DateOfBirth == d3
                 && NameMatching.NamesEqual(e.GivenName, criteria.GivenName)
                 && NameMatching.DistanceBelow(criteria.Surname, e.Surname, NameMatching.SurnameFuzzyBound),
            4 => dob is { } d4 && e.DateOfBirth == d4
                 && NameMatching.NamesEqual(e.Surname, criteria.Surname)
                 && NameMatching.DistanceBelow(criteria.GivenName, e.GivenName, NameMatching.SurnameFuzzyBound),
            5 => dob is { } d5 && NameMatching.WithinBirthYearWindow(e.DateOfBirth, d5)
                 && NameMatching.NamesEqual(e.GivenName, criteria.GivenName)
                 && NameMatching.NamesEqual(e.Surname, criteria.Surname),
            6 => dob is { } d6 && e.DateOfBirth == d6
                 && NameMatching.DistanceBelow(criteria.GivenName, e.GivenName, NameMatching.NamesFuzzyBound)
                 && NameMatching.DistanceBelow(criteria.Surname, e.Surname, NameMatching.NamesFuzzyBound),
            7 => dob is { } d7 && e.DateOfBirth == d7
                 && NameMatching.NamesEqual(e.GivenName, criteria.Surname)
                 && NameMatching.NamesEqual(e.Surname, criteria.GivenName),
            8 => e.CountryId == criteria.CitizenshipId
                 && NameMatching.DistanceBelow(e.DocumentNumber ?? string.Empty, criteria.DocumentNumber, NameMatching.NamesFuzzyBound),
            _ => false
        });

        return filtered
            .OrderByDescending(e => e.UpdateDate)
            .ThenBy(e => e.Id)
            .Take(take)
            .ToList();
    }

    /// <summary>Contrôle inverse du simulateur : une personne déjà enregistrée au
    /// registre correspond-elle à cette entrée de liste d'alerte ?
    /// (héritage : ExternalAlertListMatchForm, contrôle « reverse ».)</summary>
    public async Task<IReadOnlyList<TravelDocument>> MatchEntryAgainstRegisterAsync(int personStopListId, int take = 50, CancellationToken ct = default)
    {
        var entry = await _db.Set<PersonStopList>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == personStopListId, ct);
        if (entry == null) return Array.Empty<TravelDocument>();

        var documents = await _db.Set<TravelDocument>().AsNoTracking().ToListAsync(ct);

        var surnameParts = NameMatching.SplitNameFolded(entry.Surname);
        var givenParts = NameMatching.SplitNameFolded(entry.GivenName);
        var dob = entry.DateOfBirth;

        bool AnyPartEquals(IReadOnlyList<string> parts, string? value) => parts.Any(p => NameMatching.NamesEqual(value, p));
        bool AnyPartBelow(IReadOnlyList<string> parts, string? value, int bound) => parts.Any(p => NameMatching.DistanceBelow(p, value, bound));

        bool MatchesDocument(TravelDocument d)
        {
            if (entry.CitizenshipId != null && d.CitizenshipId != entry.CitizenshipId) return false;

            if (entry.PersonalNumber is { Length: > 0 } personal && NameMatching.NamesEqual(d.PersonalNumber, personal)) return true;
            if (entry.DocumentNumber is { Length: > 0 } number && NameMatching.NamesEqual(d.DocumentNumber, number)) return true;

            if (dob is not { } reference) return false;

            var sameDate = d.DateOfBirth == reference;
            if (sameDate && AnyPartEquals(givenParts, d.GivenName)
                && AnyPartBelow(surnameParts, d.Surname, NameMatching.SurnameFuzzyBound)) return true;
            if (sameDate && AnyPartEquals(surnameParts, d.Surname)
                && AnyPartBelow(givenParts, d.GivenName, NameMatching.SurnameFuzzyBound)) return true;
            if (sameDate
                && AnyPartBelow(givenParts, d.GivenName, NameMatching.NamesFuzzyBound)
                && AnyPartBelow(surnameParts, d.Surname, NameMatching.NamesFuzzyBound)) return true;
            if (sameDate && AnyPartEquals(givenParts, d.Surname) && AnyPartEquals(surnameParts, d.GivenName)) return true;
            if (NameMatching.WithinBirthYearWindow(d.DateOfBirth, reference)
                && AnyPartEquals(givenParts, d.GivenName) && AnyPartEquals(surnameParts, d.Surname)) return true;

            return false;
        }

        return documents
            .Where(MatchesDocument)
            .OrderByDescending(d => d.CreateDate)
            .Take(take)
            .ToList();
    }

    // -----------------------------------------------------------------------
    // Interne
    // -----------------------------------------------------------------------

    /// <summary>Entrées actives : non annulées, ou annulation programmée dans le futur
    /// (prédicat hérité <c>CancelDate IS NULL OR CancelDate &gt; GETDATE()</c>).</summary>
    private IQueryable<PersonStopList> ActivePersons()
    {
        var now = _clock.Now;
        return _db.Set<PersonStopList>().AsNoTracking()
            .Include(p => p.MatchInStopListAction!).ThenInclude(a => a.Cultures)
            .Where(p => p.CancelDate == null || p.CancelDate > now);
    }

    private IQueryable<DocumentStopList> ActiveDocuments()
    {
        var now = _clock.Now;
        return _db.Set<DocumentStopList>().AsNoTracking()
            .Include(d => d.MatchInStopListAction!).ThenInclude(a => a.Cultures)
            .Where(d => d.CancelDate == null || d.CancelDate > now);
    }

    /// <summary>Réglage « Country » (pays de référence) : un national exige une
    /// correspondance exacte des numéros, un étranger tolère une distance de 1
    /// (héritage : @documentIdentificatorUpperBound).</summary>
    private async Task<int?> GetCountrySettingAsync(CancellationToken ct)
    {
        var value = await _db.Set<Setting>().AsNoTracking()
            .Where(s => s.Type == "Country")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return value;
    }

    /// <summary>Parties × parties dans l'ordre du curseur hérité ; au minimum une
    /// itération avec les valeurs nulles pour ne pas perdre un critère indépendant
    /// des noms (boucle vide → curseur inactif dans l'héritage).</summary>
    private static IEnumerable<(string? Surname, string? GivenName)> PartPairs(
        IReadOnlyList<string> surnameParts, IReadOnlyList<string> givenParts)
    {
        if (surnameParts.Count == 0 || givenParts.Count == 0) yield break;
        foreach (var s in surnameParts)
        foreach (var g in givenParts)
            yield return (s, g);
    }

    /// <summary>Priorité héritée : code d'action croissant (valeurs nulles d'abord,
    /// comme ORDER BY ExternalID ASC), puis identifiant.</summary>
    private static int ActionPriority(PersonStopList e) => e.MatchInStopListAction == null ? 0 : 1;
    private static int ActionPriority(DocumentStopList e) => e.MatchInStopListAction == null ? 0 : 1;

    private static PersonStopList? FirstByPriority(List<PersonStopList> candidates, Func<PersonStopList, bool> predicate)
        => candidates
            .Where(predicate)
            .OrderBy(ActionPriority)
            .ThenBy(e => e.MatchInStopListAction?.Code ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(e => e.Id)
            .FirstOrDefault();

    private static StopListHit ToHit(PersonStopList e, int criterion)
        => new(StopListKind.Person, e.Id, criterion,
            e.MatchInStopListActionId, e.MatchInStopListAction?.Code, Describe(e.MatchInStopListAction),
            e.ActionRequired, e.Info, e.Surname, e.GivenName, e.FathersName,
            e.DocumentNumber, e.PersonalNumber, e.CitizenshipId, e.DocumentTypeId,
            e.DateOfBirth, e.SetUpDate, e.StopListSourceId, e.SourceDocument);

    private static StopListHit ToHit(DocumentStopList e, int criterion)
        => new(StopListKind.Document, e.Id, criterion,
            e.MatchInStopListActionId, e.MatchInStopListAction?.Code, Describe(e.MatchInStopListAction),
            e.ActionRequired, e.Info, e.Surname, e.GivenName, e.FathersName,
            e.DocumentNumber, e.PersonalNumber, e.CountryId, e.DocumentTypeId,
            e.DateOfBirth, e.SetUpDate, e.StopListSourceId, e.SourceDocument);

    private static string? Describe(Lookup? lookup)
        => lookup?.Cultures?.FirstOrDefault(c => c.Culture == "fr")?.Description
           ?? lookup?.Cultures?.FirstOrDefault()?.Description;
}
