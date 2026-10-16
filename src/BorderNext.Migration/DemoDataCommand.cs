using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Migration;

/// <summary>
/// Jeu de démonstration synthétique et déterministe (même graine = mêmes données) :
/// personnes, documents, mouvements, refus, visas, listes de surveillance et dossiers
/// douteux. Aucune donnée réelle n'est utilisée ; les images ne sont produites que sur
/// demande (--with-photos). Sert aussi de base aux mesures de charge.
/// </summary>
public static class DemoDataCommand
{
    private static readonly string[] Surnames =
    {
        "MARTIN", "KABEYA", "MOISE", "LUKAS", "MUKENDI", "DIALLO", "OWONA", "TSHIBANGU",
        "BAKALA", "NSIMBA", "ILUNGA", "KALALA", "MABIALA", "NGOMA", "PEMBA", "SITA",
    };

    private static readonly string[] GivenNames =
    {
        "CLAIRE", "JOSEPH", "ALINE", "PATRICK", "GRACE", "EMMANUEL", "NADINE", "SERGE",
        "BERNADETTE", "JEAN-PAUL", "CHRISTINE", "FISTON", "MARIE", "DIDIER", "SYLVIE", "ALAIN",
    };

    public static async Task<int> RunAsync(CommandLine options)
    {
        var persons = options.GetInt("persons", 300);
        var movements = options.GetInt("movements", 1200);
        var seed = options.GetInt("seed", 2026);
        var withPhotos = options.HasFlag("with-photos");

        Console.WriteLine($"Jeu de démonstration : {persons} personne(s), {movements} mouvement(s), graine {seed}"
                          + (withPhotos ? ", avec images synthétiques" : string.Empty));

        await using var db = await Program.OpenTargetAsync(options);

        // Référentiels (nomenclatures, pays, postes) : mêmes données que l'application.
        await Infrastructure.Persistence.Seeding.ReferenceDataSeeder.SeedAsync(db);

        var random = new Random(seed);
        var borderPointId = (await db.BorderPoints.OrderBy(p => p.Id).Select(p => p.Id).FirstAsync());
        var documentTypeId = await LookupIdAsync(db, "DOCUMENT_TYPE", "OP")
                             ?? await LookupIdAsync(db, "DOCUMENT_TYPE", null);
        var directionEntry = await LookupIdAsync(db, "MOVEMENT_DIRECTION", "Entry")
                             ?? await LookupIdAsync(db, "MOVEMENT_DIRECTION", null);
        var directionExit = await LookupIdAsync(db, "MOVEMENT_DIRECTION", "Exit") ?? directionEntry;
        var sexF = await LookupIdAsync(db, "SEX", "F");
        var sexM = await LookupIdAsync(db, "SEX", "M");
        var countries = await db.Countries.Where(c => c.Alpha3 != null && c.Revoke == false)
            .Select(c => c.Id).Take(60).ToListAsync();
        var visaTypeId = await LookupIdAsync(db, "VISA_TYPE", null);
        var stopSourceId = await LookupIdAsync(db, "STOP_LIST_SOURCE", null);

        if (documentTypeId is null || directionEntry is null || countries.Count == 0)
            throw new InvalidOperationException("Référentiels absents : lancez d'abord l'application une fois.");

        var firstNames = random.Next(1, 200);

        // Personnes + documents : une personne par document (comme l'enregistrement au poste).
        var created = 0;
        for (var i = 0; i < persons; i++)
        {
            var surname = Surnames[random.Next(Surnames.Length)];
            var givenName = GivenNames[random.Next(GivenNames.Length)];
            var person = new Person
            {
                BorderPointId = borderPointId,
                UpdateDate = DateTime.Now.AddDays(-random.Next(365)),
                UpdateUser = "demo",
            };
            var document = new TravelDocument
            {
                Person = person,
                DocumentTypeId = documentTypeId.Value,
                DocumentNumber = $"{surname[..Math.Min(2, surname.Length)]}{firstNames + i:000000}",
                CountryOfIssueId = countries[random.Next(countries.Count)],
                CitizenshipId = countries[random.Next(countries.Count)],
                Surname = surname,
                GivenName = givenName,
                DateOfBirth = DateTime.Today.AddDays(-random.Next(18 * 365, 70 * 365)),
                SexId = random.Next(2) == 0 ? sexF : sexM,
                IssueDate = DateTime.Today.AddDays(-random.Next(365, 3000)),
                ExpireDate = DateTime.Today.AddDays(random.Next(-200, 2000)),
                BorderPointId = borderPointId,
                CreateDate = DateTime.Now.AddDays(-random.Next(365)),
                CreatorUser = "demo",
                UpdateDate = DateTime.Now.AddDays(-random.Next(365)),
                UpdateUser = "demo",
            };

            if (withPhotos)
            {
                document.Image = new DocumentImage
                {
                    Photo = SyntheticImage(random, 96),
                    ScannedDocument = SyntheticImage(random, 320),
                    UpdateDate = DateTime.Now,
                    UpdateUser = "demo",
                };
            }

            db.Persons.Add(person);
            db.TravelDocuments.Add(document);
            created++;

            if (created % 200 == 0)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"  {created}/{persons} personnes");
            }
        }

        await db.SaveChangesAsync();

        // Mouvements : la majorité accordés, une part refusée, quelques transits.
        var documents = await db.TravelDocuments
            .Select(d => new { d.Id, d.PersonId, d.LegacyId })
            .ToListAsync();
        var deportationReasonId = await LookupIdAsync(db, "DEPORTATION_REASON", null);
        var refusalJustificationId = await LookupIdAsync("REFUSAL_JUSTIFICATION", db);
        var travelReasonId = await LookupIdAsync(db, "TRAVEL_REASON", null);
        var transportTypeId = await LookupIdAsync(db, "TRANSPORT_TYPE", null);

        var made = 0;
        for (var i = 0; i < movements; i++)
        {
            var document = documents[random.Next(documents.Count)];
            var travelDate = DateTime.Today.AddDays(-random.Next(90)).AddMinutes(random.Next(1440));
            var granted = random.Next(100) >= 8;

            db.Movements.Add(new Movement
            {
                PersonId = document.PersonId,
                DocumentId = document.Id,
                TravelDate = travelDate,
                DirectionLookupId = (random.Next(2) == 0 ? directionEntry : directionExit).Value,
                TransportTypeId = transportTypeId,
                TravelReasonId = travelReasonId,
                Granted = granted,
                DeportationReasonId = granted ? null : deportationReasonId,
                RefusalJustificationId = granted ? null : refusalJustificationId,
                IsTransit = random.Next(100) < 3,
                HasVisa = random.Next(100) < 20,
                ChildrenCount = random.Next(100) < 25 ? random.Next(1, 4) : 0,
                ResidentCountryId = countries[random.Next(countries.Count)],
                DestinationCountryId = countries[random.Next(countries.Count)],
                Occupation = random.Next(100) < 30 ? "Commerçant" : null,
                DurationOfStay = (short)random.Next(0, 120),
                Inspector = random.Next(2) == 0 ? "jdoe" : "admin",
                PcNumber = $"A{random.Next(1, 12):00}",
                RegistrationTime = travelDate.AddMinutes(random.Next(30)),
                BorderPointId = borderPointId,
                UpdateDate = travelDate,
            });
            made++;

            if (made % 500 == 0)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"  {made}/{movements} mouvements");
            }
        }

        await db.SaveChangesAsync();

        // Visas (sur une partie des documents) et listes de surveillance.
        if (visaTypeId is not null)
        {
            for (var i = 0; i < Math.Max(1, persons / 5); i++)
            {
                var document = documents[random.Next(documents.Count)];
                db.Visas.Add(new Visa
                {
                    VisaTypeId = visaTypeId.Value,
                    DocumentId = document.Id,
                    Number = $"VISA-{2020 + random.Next(7)}-{random.Next(100000):000000}",
                    IssueDate = DateTime.Today.AddDays(-random.Next(30, 900)),
                    ExpireDate = DateTime.Today.AddDays(random.Next(1, 720)),
                    Duration = (short)random.Next(7, 180),
                    PcNumber = "A01",
                    BorderPointId = borderPointId,
                    CreateDate = DateTime.Now.AddDays(-random.Next(365)),
                    CreatorUser = "demo",
                });
            }
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < Math.Max(1, persons / 20); i++)
        {
            var surname = Surnames[random.Next(Surnames.Length)];
            db.PersonsStopList.Add(new PersonStopList
            {
                Surname = surname,
                GivenName = GivenNames[random.Next(GivenNames.Length)],
                DateOfBirth = DateTime.Today.AddDays(-random.Next(20 * 365, 60 * 365)),
                SetUpDate = DateTime.Now.AddDays(-random.Next(500)),
                WhoSetUp = "demo",
                StopListSourceId = stopSourceId,
                ActionRequired = "Signaler au superviseur",
                DocumentNumber = $"{surname[..2]}{random.Next(100000, 999999)}",
                UpdateDate = DateTime.Now,
                LastUpdateUser = "demo",
            });
        }

        await db.SaveChangesAsync();

        // Après une insertion en masse, les statistiques doivent être recalculées : sans
        // cela, le premier plan d'exécution d'une requête d'édition peut être très mauvais.
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [Movements]");
            await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [TravelDocuments]");
            await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [Persons]");
        }

        Console.WriteLine($"Terminé : {persons} personne(s), {documents.Count} document(s), {movements} mouvement(s), "
                          + $"{await db.Visas.CountAsync()} visa(s), {await db.PersonsStopList.CountAsync()} entrée(s) de liste.");
        Console.WriteLine("Jeu de démonstration uniquement : aucune donnée réelle n'a été copiée.");
        return 0;
    }

    private static async Task<int?> LookupIdAsync(BorderNext.Infrastructure.Persistence.BorderNextDbContext db,
        string typeCode, string? code)
    {
        var query = db.Lookups.Where(l => l.Type!.Code == typeCode);
        if (!string.IsNullOrWhiteSpace(code)) query = query.Where(l => l.Code == code);
        return await query.OrderBy(l => l.SortOrder).Select(l => (int?)l.Id).FirstOrDefaultAsync();
    }

    private static Task<int?> LookupIdAsync(string typeCode,
        BorderNext.Infrastructure.Persistence.BorderNextDbContext db)
        => LookupIdAsync(db, typeCode, null);

    /// <summary>Image synthétique (dégradé + motif) : aucune personne réelle n'y figure.</summary>
    private static byte[] SyntheticImage(Random random, int size)
    {
        // BMP 24 bits minimal : en-tête, puis un motif déterministe.
        var rowSize = (size * 3 + 3) / 4 * 4;
        var pixelBytes = rowSize * size;
        var fileSize = 54 + pixelBytes;
        var data = new byte[fileSize];

        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(size).CopyTo(data, 18);
        BitConverter.GetBytes(size).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        BitConverter.GetBytes(pixelBytes).CopyTo(data, 34);

        var blue = (byte)random.Next(60, 200);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var offset = 54 + y * rowSize + x * 3;
                data[offset] = (byte)(x * 255 / size);
                data[offset + 1] = (byte)(y * 255 / size);
                data[offset + 2] = blue;
            }
        }

        return data;
    }
}
