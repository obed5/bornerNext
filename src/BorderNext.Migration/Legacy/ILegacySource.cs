namespace BorderNext.Migration.Legacy;

/// <summary>
/// Accès en lecture à la base d'origine. Les lectures sont paginées (page par page)
/// pour que la migration d'un historique de plusieurs dizaines de milliers de lignes
/// reste bornée en mémoire. Aucune écriture n'a lieu côté origine.
/// </summary>
public interface ILegacySource
{
    /// <summary>Vérifie l'accès et retourne la liste des cultures présentes (ex. fr, en).</summary>
    Task<IReadOnlyList<string>> GetCulturesAsync(CancellationToken ct);

    /// <summary>Nomenclatures héritées d'un type donné (table l* et ses cultures).</summary>
    Task<IReadOnlyList<LegacyLookupItem>> ReadLookupAsync(LegacyLookup table, CancellationToken ct);

    Task<IReadOnlyList<LegacyCountry>> ReadCountriesAsync(CancellationToken ct);

    Task<IReadOnlyList<LegacyBorderPoint>> ReadBorderPointsAsync(CancellationToken ct);

    /// <summary>Nombre de lignes d'une table (aperçu et contrôle de fin de migration).</summary>
    Task<long> CountAsync(LegacyTable table, CancellationToken ct);

    /// <summary>
    /// Vrai si la table existe dans la base d'origine. Une installation peut ne pas
    /// utiliser certaines tables (visas, permis) : la section correspondante est alors
    /// simplement ignorée, avec mention dans le rapport.
    /// </summary>
    Task<bool> TableExistsAsync(LegacyTable table, CancellationToken ct);

    IAsyncEnumerable<IReadOnlyList<LegacyPerson>> ReadPersonsAsync(int pageSize, CancellationToken ct);

    /// <summary>
    /// Documents d'origine correspondant à des identifiants donnés. Le dossier frontalier
    /// est le pivot de la reprise : le document est créé au moment du premier mouvement
    /// qui le référence, avec la personne de ce mouvement (l'origine ne lie pas le
    /// document à une personne ailleurs).
    /// </summary>
    Task<IReadOnlyList<LegacyDocument>> ReadDocumentsByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct);

    Task<IReadOnlyList<LegacyPhoto>> ReadPhotosAsync(IReadOnlyCollection<string> documentIds, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyMovement>> ReadMovementsAsync(int pageSize, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyChild>> ReadChildrenAsync(int pageSize, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyVisa>> ReadVisasAsync(int pageSize, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyVisaChild>> ReadVisaChildrenAsync(int pageSize, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyPersonStopList>> ReadPersonStopListAsync(int pageSize, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyDocumentStopList>> ReadDocumentStopListAsync(int pageSize, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyDoubtablePerson>> ReadDoubtablePersonsAsync(int pageSize, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyPermit>> ReadPermitsAsync(int pageSize, CancellationToken ct);
    IAsyncEnumerable<IReadOnlyList<LegacyMedicalHistory>> ReadMedicalHistoriesAsync(int pageSize, CancellationToken ct);
}

/// <summary>Nomenclature héritée et type cible correspondant.</summary>
/// <param name="LegacyTable">Table d'origine (ex. lDocumentTypes).</param>
/// <param name="LegacyCulturesTable">Table des libellés (ex. lDocumentTypesCultures).</param>
/// <param name="LookupTypeCode">Code du type de nomenclature cible (ex. DOCUMENT_TYPE).</param>
public sealed record LegacyLookup(string LegacyTable, string LegacyCulturesTable, string LookupTypeCode);

/// <summary>Tables de l'historique reprises (comptages et aperçu).</summary>
public enum LegacyTable
{
    Persons,
    Documents,
    Photos,
    PassportRegistrationData,
    Children,
    Visa,
    VisaChildren,
    PersonsStopList,
    DocumentsStopList,
    DoubtablePersons,
    Permit,
    PermitMedicalHistory,
}
