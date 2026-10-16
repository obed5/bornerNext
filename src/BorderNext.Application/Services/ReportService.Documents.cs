using BorderNext.Application.Abstractions;
using BorderNext.Application.Reporting;
using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Application.Services;

/// <summary>Impressions individuelles (fiches du formulaire d'édition hérité) :
/// enregistrement de passage, visa, permis, refus d'entrée, amende, ordre de
/// reconduite, antécédents médicaux et questionnaire sanitaire vierge.
/// Chaque fiche retrouve la pièce par numéro (visa, permis, amende) ou par
/// numéro de document de voyage, et se présente en tableaux libellé/valeur.</summary>
public partial class ReportService
{
    private static ReportTableTemplate InfoTable(string? titleKey, IReadOnlyList<KeyValuePair<string, string?>> pairs)
        => new(titleKey,
            new[] { new ReportColumnTemplate("RP_Col_Field"), new ReportColumnTemplate("RP_Col_Value") },
            pairs.Select(p => new object?[] { p.Key, p.Value ?? "—" }).ToList());

    private static string Flag(bool value) => value ? "Oui" : "Non";

    private static KeyValuePair<string, string?> Pair(string label, string? value) => new(label, value);

    // -----------------------------------------------------------------------
    // Fiche d'enregistrement (passage)
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildRegistrationFormAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.RegistrationForm)!;

        var query = _db.Set<Movement>().AsNoTracking()
            .Include(m => m.Document).ThenInclude(d => d!.DocumentType).ThenInclude(t => t!.Cultures)
            .Include(m => m.Document).ThenInclude(d => d!.Citizenship).ThenInclude(c => c!.Cultures)
            .Include(m => m.Document).ThenInclude(d => d!.CountryOfIssue).ThenInclude(c => c!.Cultures)
            .Include(m => m.Document).ThenInclude(d => d!.Sex).ThenInclude(s => s!.Cultures)
            .Include(m => m.Direction).ThenInclude(d => d!.Cultures)
            .Include(m => m.TravelReason).ThenInclude(r => r!.Cultures)
            .Include(m => m.AccommodationType).ThenInclude(a => a!.Cultures)
            .Include(m => m.ResidentCountry).ThenInclude(c => c!.Cultures)
            .Include(m => m.DeportationReason).ThenInclude(r => r!.Cultures)
            .Include(m => m.RefusalJustification).ThenInclude(j => j!.Cultures)
            .Include(m => m.Visa).ThenInclude(v => v!.VisaType).ThenInclude(t => t!.Cultures)
            .Include(m => m.Children)
            .Include(m => m.TripCrossing).ThenInclude(c => c!.Trip)
            .AsQueryable();

        query = ApplyFilters(query, p);
        var movement = await query.OrderByDescending(m => m.TravelDate).ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainValidationException("RP_Error_RecordNotFound");

        var document = movement.Document;
        var tables = new List<ReportTableTemplate>
        {
            InfoTable("RP_Section_Identity", new[]
            {
                Pair("Nom", document?.Surname), Pair("Prénom(s)", document?.GivenName),
                Pair("Nom du père", document?.FatherName), Pair("Date de naissance", D(document?.DateOfBirth)),
                Pair("Sexe", Description(document?.Sex?.Cultures, "fr")),
                Pair("Nationalité", Description(document?.Citizenship?.Cultures, "fr")),
                Pair("Pays de résidence", Description(movement.ResidentCountry?.Cultures, "fr")),
                Pair("Profession", movement.Occupation),
            }),
            InfoTable("RP_Section_Document", new[]
            {
                Pair("Type", Description(document?.DocumentType?.Cultures, "fr")),
                Pair("Numéro", document?.DocumentNumber),
                Pair("Délivré par", Description(document?.CountryOfIssue?.Cultures, "fr")),
                Pair("Délivré le", D(document?.IssueDate)), Pair("Expire le", D(document?.ExpireDate)),
                Pair("Numéro personnel", document?.PersonalNumber),
            }),
            InfoTable("RP_Section_Movement", new[]
            {
                Pair("Date et heure", T(movement.TravelDate)),
                Pair("Sens", Description(movement.Direction?.Cultures, "fr")),
                Pair("Poste frontière", movement.BorderPointId?.ToString()),
                Pair("Voyage", movement.TripCrossing?.Trip?.ExternalNumber ?? movement.TripCrossing?.Trip?.Name),
                Pair("Motif du voyage", Description(movement.TravelReason?.Cultures, "fr")),
                Pair("Visa", movement.Visa is null ? "—" : $"{movement.Visa.Number} ({Description(movement.Visa.VisaType?.Cultures, "fr")})"),
                Pair("Durée de séjour", movement.DurationOfStay is { } days ? $"{days} jour(s)" : null),
                Pair("Hébergement", Description(movement.AccommodationType?.Cultures, "fr")),
                Pair("Adresse d'hébergement", movement.AccommodationAddress),
                Pair("En transit", Flag(movement.IsTransit)),
                Pair("Passage accordé", Flag(movement.Granted)),
                Pair("Motif de déportation", Description(movement.DeportationReason?.Cultures, "fr")),
                Pair("Base légale du refus", Description(movement.RefusalJustification?.Cultures, "fr")),
                Pair("Observations", movement.Note),
                Pair("Inspecteur", movement.Inspector),
            }),
        };

        var childRows = movement.Children.Select(c => new object?[]
        {
            c.Surname, c.GivenName, D(c.DateOfBirth), c.DocumentNumber,
        }).ToList();
        if (childRows.Count > 0)
        {
            tables.Add(new ReportTableTemplate("RP_Section_Children", new[]
            {
                new ReportColumnTemplate("WL_Col_Surname"), new ReportColumnTemplate("WL_Col_GivenName"),
                new ReportColumnTemplate("WL_Col_BirthDate"), new ReportColumnTemplate("WL_Col_Document"),
            }, childRows));
        }

        return Template(definition, p) with
        {
            Tables = tables,
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_MovementId", movement.Id.ToString()),
                new("RP_Sum_Children", (movement.ChildrenCount > 0 ? movement.ChildrenCount : movement.Children.Count).ToString()),
                new("RP_Sum_Refused", movement.Granted ? "Non" : "Oui"),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Fiche visa
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildVisaFormAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.VisaForm)!;

        Visa? visa = null;
        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
        {
            var term = p.DocumentNumber.Trim().ToUpper();
            visa = await _db.Set<Visa>().AsNoTracking()
                .Include(v => v.VisaType).ThenInclude(t => t!.Cultures)
                .Include(v => v.VisaEntry).ThenInclude(e => e!.Cultures)
                .Include(v => v.Document).ThenInclude(d => d!.Citizenship).ThenInclude(c => c!.Cultures)
                .Include(v => v.Children)
                .Where(v => (v.Number != null && v.Number.ToUpper() == term)
                    || (v.Document!.DocumentNumber != null && v.Document.DocumentNumber.ToUpper() == term))
                .OrderByDescending(v => v.IssueDate).ThenByDescending(v => v.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (visa is null) throw new DomainValidationException("RP_Error_RecordNotFound");
        var document = visa.Document;

        var tables = new List<ReportTableTemplate>
        {
            InfoTable("RP_Section_Identity", new[]
            {
                Pair("Nom", document?.Surname), Pair("Prénom(s)", document?.GivenName),
                Pair("Date de naissance", D(document?.DateOfBirth)),
                Pair("Nationalité", Description(document?.Citizenship?.Cultures, "fr")),
                Pair("Document", document?.DocumentNumber),
            }),
            InfoTable("RP_Section_Visa", new[]
            {
                Pair("Numéro", visa.Number),
                Pair("Type", Description(visa.VisaType?.Cultures, "fr")),
                Pair("Entrées", Description(visa.VisaEntry?.Cultures, "fr")),
                Pair("Délivré le", D(visa.IssueDate)), Pair("Expire le", D(visa.ExpireDate)),
                Pair("Durée (jours)", visa.Duration?.ToString()),
                Pair("Référence", visa.ReferenceNumber), Pair("Reçu", visa.ReceiptNumber),
                Pair("Adresse", visa.Address), Pair("Contact", visa.ContactNumber),
                Pair("Employeur", visa.Employer), Pair("Invité par", visa.InvitedBy),
                Pair("Observations", visa.Note),
                Pair("Code-barres", visa.Barcode),
                Pair("Révoqué", visa.Revoke ? $"Oui ({D(visa.RevokeDate)})" : "Non"),
            }),
        };

        var childRows = visa.Children.Where(c => !c.IsDeleted).Select(c => new object?[]
        {
            c.Surname, c.GivenName, D(c.DateOfBirth), c.DocumentNumber,
        }).ToList();
        if (childRows.Count > 0)
        {
            tables.Add(new ReportTableTemplate("RP_Section_Children", new[]
            {
                new ReportColumnTemplate("WL_Col_Surname"), new ReportColumnTemplate("WL_Col_GivenName"),
                new ReportColumnTemplate("WL_Col_BirthDate"), new ReportColumnTemplate("WL_Col_Document"),
            }, childRows));
        }

        return Template(definition, p) with
        {
            Tables = tables,
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_VisaNumber", visa.Number ?? "—"),
                new("RP_Sum_Children", visa.Children.Count(c => !c.IsDeleted).ToString()),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Fiche permis de séjour
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildPermitFormAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.PermitForm)!;

        Permit? permit = null;
        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
        {
            var term = p.DocumentNumber.Trim().ToUpper();
            permit = await _db.Set<Permit>().AsNoTracking()
                .Include(x => x.PermitType).ThenInclude(t => t!.Cultures)
                .Include(x => x.Status).ThenInclude(s => s!.Cultures)
                .Include(x => x.MaritalStatus).ThenInclude(s => s!.Cultures)
                .Include(x => x.Occupation).ThenInclude(o => o!.Cultures)
                .Include(x => x.Qualification).ThenInclude(q => q!.Cultures)
                .Include(x => x.Document).ThenInclude(d => d!.Citizenship).ThenInclude(c => c!.Cultures)
                .Include(x => x.Details)
                .Include(x => x.Contacts)
                .Include(x => x.Dependants)
                .Include(x => x.SelfEmployed)
                .Where(x => (x.Number != null && x.Number.ToUpper() == term)
                    || (x.Document!.DocumentNumber != null && x.Document.DocumentNumber.ToUpper() == term))
                .OrderByDescending(x => x.IssueDate ?? x.CreateDate).ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (permit is null) throw new DomainValidationException("RP_Error_RecordNotFound");
        var document = permit.Document;
        var details = permit.Details;

        var tables = new List<ReportTableTemplate>
        {
            InfoTable("RP_Section_Identity", new[]
            {
                Pair("Nom", document?.Surname), Pair("Prénom(s)", document?.GivenName),
                Pair("Nom du père", document?.FatherName),
                Pair("Date de naissance", D(document?.DateOfBirth)),
                Pair("Nationalité", Description(document?.Citizenship?.Cultures, "fr")),
                Pair("Situation familiale", Description(permit.MaritalStatus?.Cultures, "fr")),
                Pair("Profession", Description(permit.Occupation?.Cultures, "fr")),
                Pair("Qualification", Description(permit.Qualification?.Cultures, "fr")),
            }),
            InfoTable("RP_Section_Permit", new[]
            {
                Pair("Numéro", permit.Number),
                Pair("Type", Description(permit.PermitType?.Cultures, "fr")),
                Pair("Statut", Description(permit.Status?.Cultures, "fr")),
                Pair("Délivré le", D(permit.IssueDate)), Pair("Expire le", D(permit.ExpireDate)),
                Pair("Autorité émettrice", permit.DocumentIssuingAuthority),
                Pair("Province de résidence", details?.CurrentProvinceId?.ToString()),
                Pair("Adresse actuelle", details?.CurrentAddress),
                Pair("Téléphone", details?.CurrentPhoneNumber),
                Pair("Personne de référence", details?.ReferencePersonName),
                Pair("Activité indépendante", permit.SelfEmployed?.Name),
                Pair("Observations", permit.Note),
                Pair("Révoqué", permit.Revoke ? $"Oui ({D(permit.RevokeDate)})" : "Non"),
            }),
        };

        if (permit.Contacts.Count > 0)
        {
            var contactTypes = await _db.Set<Lookup>().AsNoTracking().Include(l => l.Cultures)
                .Where(l => l.Type!.Code == "CONTACT_TYPE").ToDictionaryAsync(l => l.Id, ct);
            tables.Add(new ReportTableTemplate("RP_Section_Contacts", new[]
            {
                new ReportColumnTemplate("RP_Col_ContactType"), new ReportColumnTemplate("RP_Col_Value"),
            }, permit.Contacts.Select(c => new object?[]
            {
                c.ContactTypeId is { } id && contactTypes.TryGetValue(id, out var type)
                    ? Description(type.Cultures, "fr") : null,
                c.Description,
            }).ToList()));
        }

        if (permit.Dependants.Count > 0)
        {
            tables.Add(new ReportTableTemplate("RP_Section_Children", new[]
            {
                new ReportColumnTemplate("WL_Col_Surname"), new ReportColumnTemplate("WL_Col_GivenName"),
                new ReportColumnTemplate("WL_Col_BirthDate"), new ReportColumnTemplate("WL_Col_Document"),
            }, permit.Dependants.Select(d => new object?[]
            {
                d.Surname, d.GivenName, D(d.DateOfBirth), d.DocumentNumber,
            }).ToList()));
        }

        return Template(definition, p) with
        {
            Tables = tables,
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_PermitNumber", permit.Number ?? "—"),
                new("RP_Sum_Dependants", permit.Dependants.Count.ToString()),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Refus d'entrée
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildRefusalFormAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.RefusalForm)!;

        var query = _db.Set<Movement>().AsNoTracking()
            .Include(m => m.Document).ThenInclude(d => d!.Citizenship).ThenInclude(c => c!.Cultures)
            .Include(m => m.Document).ThenInclude(d => d!.CountryOfIssue).ThenInclude(c => c!.Cultures)
            .Include(m => m.Direction).ThenInclude(d => d!.Cultures)
            .Include(m => m.DeportationReason).ThenInclude(r => r!.Cultures)
            .Include(m => m.RefusalJustification).ThenInclude(j => j!.Cultures)
            .Where(m => !m.Granted);

        query = ApplyFilters(query, p);
        var movement = await query.OrderByDescending(m => m.TravelDate).ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainValidationException("RP_Error_RecordNotFound");

        var document = movement.Document;
        return Template(definition, p) with
        {
            Tables = new[]
            {
                InfoTable("RP_Section_Identity", new[]
                {
                    Pair("Nom", document?.Surname), Pair("Prénom(s)", document?.GivenName),
                    Pair("Nom du père", document?.FatherName), Pair("Date de naissance", D(document?.DateOfBirth)),
                    Pair("Nationalité", Description(document?.Citizenship?.Cultures, "fr")),
                    Pair("Document", $"{document?.DocumentNumber} ({Description(document?.CountryOfIssue?.Cultures, "fr")})"),
                    Pair("Expire le", D(document?.ExpireDate)),
                }),
                InfoTable("RP_Section_Refusal", new[]
                {
                    Pair("Date et heure", T(movement.TravelDate)),
                    Pair("Sens", Description(movement.Direction?.Cultures, "fr")),
                    Pair("Poste frontière", movement.BorderPointId?.ToString()),
                    Pair("Motif de déportation", Description(movement.DeportationReason?.Cultures, "fr")),
                    Pair("Base légale", Description(movement.RefusalJustification?.Cultures, "fr")),
                    Pair("Observations", movement.Note),
                    Pair("Inspecteur", movement.Inspector),
                }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_MovementId", movement.Id.ToString()),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Amende
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildPenaltyFormAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.PenaltyForm)!;

        PenaltyTicket? ticket = null;
        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
        {
            var term = p.DocumentNumber.Trim().ToUpper();
            ticket = await _db.Set<PenaltyTicket>().AsNoTracking()
                .Include(t => t.PenaltyReason).ThenInclude(r => r!.Cultures)
                .Where(t => (t.Number != null && t.Number.ToUpper() == term)
                    || (t.DocumentNumber != null && t.DocumentNumber.ToUpper() == term))
                .OrderByDescending(t => t.CreateDate).ThenByDescending(t => t.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (ticket is null) throw new DomainValidationException("RP_Error_RecordNotFound");

        return Template(definition, p) with
        {
            Tables = new[]
            {
                InfoTable("RP_Section_Holder", new[]
                {
                    Pair("Nom", ticket.Surname), Pair("Prénom(s)", ticket.GivenName),
                    Pair("Document", ticket.DocumentNumber),
                    Pair("Délivré le", D(ticket.DocumentIssueDate)), Pair("Expire le", D(ticket.DocumentExpireDate)),
                    Pair("Visa", ticket.VisaNumber),
                }),
                InfoTable("RP_Section_Penalty", new[]
                {
                    Pair("Numéro", ticket.Number),
                    Pair("Date", T(ticket.CreateDate)),
                    Pair("Motif", Description(ticket.PenaltyReason?.Cultures, "fr")),
                    Pair("Montant", $"{ticket.Amount.ToString("N2", Format)} {ticket.CurrencyCode}"),
                    Pair("Durée (jours)", ticket.Duration?.ToString()),
                    Pair("Payée", ticket.Paid ? $"Oui ({D(ticket.PaidDate)})" : "Non"),
                    Pair("Reçu", ticket.ReceiptNumber),
                    Pair("Agent", ticket.UserId),
                    Pair("Révoquée", ticket.Revoke ? $"Oui ({D(ticket.RevokeDate)})" : "Non"),
                }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_PenaltyNumber", ticket.Number ?? "—"),
                new("RP_Sum_Paid", ticket.Paid ? "Oui" : "Non"),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Ordre de reconduite
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildRemovalOrderFormAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.RemovalOrderForm)!;

        var query = _db.Set<RemovalOrder>().AsNoTracking()
            .Include(o => o.Type).ThenInclude(t => t!.Cultures)
            .Include(o => o.Movement).ThenInclude(m => m!.Document).ThenInclude(d => d!.Citizenship).ThenInclude(c => c!.Cultures)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
        {
            var term = p.DocumentNumber.Trim().ToUpper();
            query = query.Where(o => o.DocNumber != null && o.DocNumber.ToUpper() == term);
        }
        if (p.From is { } from) query = query.Where(o => o.CreateDate >= from.Date);
        if (p.To is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(o => o.CreateDate < end);
        }

        var order = await query.OrderByDescending(o => o.CreateDate).ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainValidationException("RP_Error_RecordNotFound");

        var document = order.Movement?.Document;
        return Template(definition, p) with
        {
            Tables = new[]
            {
                InfoTable("RP_Section_Holder", new[]
                {
                    Pair("Nom", document?.Surname), Pair("Prénom(s)", document?.GivenName),
                    Pair("Date de naissance", D(document?.DateOfBirth)),
                    Pair("Nationalité", Description(document?.Citizenship?.Cultures, "fr")),
                    Pair("Document", order.DocNumber ?? document?.DocumentNumber),
                }),
                InfoTable("RP_Section_Order", new[]
                {
                    Pair("Date", T(order.CreateDate)),
                    Pair("Type", Description(order.Type?.Cultures, "fr")),
                    Pair("Motif / informations", order.Info),
                    Pair("Officier", order.OfficerName),
                    Pair("Poste frontière", order.BorderPointId?.ToString()),
                    Pair("Révoqué", order.Revoke ? $"Oui ({D(order.RevokeDate)})" : "Non"),
                }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_OrderId", order.Id.ToString()),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Antécédents médicaux (permis)
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildMedicalHistoryFormAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.MedicalHistoryForm)!;

        var query = _db.Set<PermitMedicalHistory>().AsNoTracking()
            .Include(h => h.Permit).ThenInclude(x => x!.Document).ThenInclude(d => d!.Citizenship).ThenInclude(c => c!.Cultures)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.DocumentNumber))
        {
            var term = p.DocumentNumber.Trim().ToUpper();
            query = query.Where(h => (h.Permit!.Number != null && h.Permit.Number.ToUpper() == term)
                || (h.Permit!.Document!.DocumentNumber != null && h.Permit.Document.DocumentNumber.ToUpper() == term));
        }

        var history = await query.OrderByDescending(h => h.UpdateDate ?? h.Permit!.CreateDate)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainValidationException("RP_Error_RecordNotFound");

        var document = history.Permit?.Document;
        return Template(definition, p) with
        {
            Tables = new[]
            {
                InfoTable("RP_Section_Holder", new[]
                {
                    Pair("Nom", document?.Surname), Pair("Prénom(s)", document?.GivenName),
                    Pair("Date de naissance", D(document?.DateOfBirth)),
                    Pair("Nationalité", Description(document?.Citizenship?.Cultures, "fr")),
                    Pair("Permis", history.Permit?.Number),
                }),
                InfoTable("RP_Section_CurrentHealth", new[]
                {
                    Pair("Maladie actuelle", history.CurrentIllness),
                    Pair("Traitement en cours", history.CurrentMedication),
                    Pair("Maladie chronique", Flag(history.ChronicDisease)),
                    Pair("Maladie infectieuse", Flag(history.InfectiousDisease)),
                    Pair("Passage en centre de santé", Flag(history.HealthCentreVisited)),
                    Pair("Centre de santé public", Flag(history.IsPublicHealthCentre)),
                }),
                InfoTable("RP_Section_Suffered", new[]
                {
                    Pair("Paludisme", Flag(history.MalariaSuffered)),
                    Pair("Fièvre jaune", Flag(history.YellowFeverSuffered)),
                    Pair("Tuberculose", Flag(history.TuberculosisSuffered)),
                    Pair("Rougeole", Flag(history.MeaslesSuffered)),
                    Pair("Choléra", Flag(history.CholeraSuffered)),
                    Pair("Typhoïde", Flag(history.TyphoidSuffered)),
                    Pair("Jaunisse", Flag(history.JaundiceSuffered)),
                    Pair("Toux chronique", Flag(history.ChronicCoughSuffered)),
                    Pair("Autres", history.OtherSuffered),
                }),
                InfoTable("RP_Section_Vaccinated", new[]
                {
                    Pair("Fièvre jaune", Flag(history.YellowFeverVaccinated)),
                    Pair("ROR", Flag(history.MMRVaccinated)),
                    Pair("Diphtérie", Flag(history.DiphtheriaVaccinated)),
                    Pair("Polio", Flag(history.PolioVaccinated)),
                }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_UpdatedOn", D(history.UpdateDate)),
                new("RP_Sum_UpdatedBy", history.UpdateUser),
            },
        };
    }

    // -----------------------------------------------------------------------
    // Questionnaire sanitaire vierge
    // -----------------------------------------------------------------------

    private async Task<ReportTemplate> BuildBlankQuestionnaireAsync(ReportParameters p, CancellationToken ct)
    {
        var definition = ReportCatalog.Find(ReportCatalog.BlankQuestionnaire)!;

        var questionnaire = await _db.Set<Questionnaire>().AsNoTracking()
            .Include(q => q.Questions).ThenInclude(x => x.Question).ThenInclude(q => q!.QuestionType)
            .Where(q => !q.Revoke)
            .OrderBy(q => q.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainValidationException("RP_Error_RecordNotFound");

        var rows = questionnaire.Questions
            .Where(x => !x.Revoke && x.Question is not null && !x.Question.Revoke)
            .OrderBy(x => x.QuestionOrder)
            .Select(x => new object?[]
            {
                x.QuestionOrder.ToString(),
                x.Question!.Title,
                x.Question.QuestionType?.Cultures.FirstOrDefault(c => c.Culture == "fr")?.Description
                    ?? x.Question.QuestionType?.Code,
                string.Empty,
            }).ToList();

        return Template(definition, p) with
        {
            Tables = new[]
            {
                InfoTable("RP_Section_Questionnaire", new[]
                {
                    Pair("Questionnaire", questionnaire.Description),
                    Pair("Validité (jours)", questionnaire.ExpireDays.ToString()),
                    Pair("Rubriques", questionnaire.Questions.Count(x => !x.Revoke).ToString()),
                }),
                new ReportTableTemplate("RP_Section_Questions", new[]
                {
                    new ReportColumnTemplate("RP_Col_Order"), new ReportColumnTemplate("RP_Col_Question"),
                    new ReportColumnTemplate("RP_Col_AnswerType"), new ReportColumnTemplate("RP_Col_Answer"),
                }, rows),
                InfoTable("RP_Section_Holder", new[]
                {
                    Pair("Nom et prénom(s)", null), Pair("Date de naissance", null),
                    Pair("Document", null), Pair("Signature", null), Pair("Date", null),
                }),
            },
            Summary = new List<ReportSummaryLine>
            {
                new("RP_Sum_Questions", rows.Count.ToString()),
            },
        };
    }
}
