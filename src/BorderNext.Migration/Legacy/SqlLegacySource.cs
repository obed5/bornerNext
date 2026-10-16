using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace BorderNext.Migration.Legacy;

/// <summary>
/// Lecture de la base d'origine (SQL Server). Toutes les requêtes sont paramétrées et
/// se limitent aux colonnes utiles ; les pages sont lues en triant sur la clé primaire.
/// Les collations et types sont lus tels quels, sans conversion implicite de culture.
/// </summary>
public sealed class SqlLegacySource : ILegacySource
{
    private readonly string _connectionString;

    public SqlLegacySource(string connectionString) => _connectionString = connectionString;

    /// <summary>Vérifie que la base répond (connexion ouverte puis refermée).</summary>
    public async Task<string> DescribeAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT DB_NAME(), COUNT(*) FROM sys.tables", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return $"{reader.GetString(0)} ({reader.GetInt32(1)} tables)";
    }

    public async Task<IReadOnlyList<string>> GetCulturesAsync(CancellationToken ct)
    {
        var cultures = new List<string>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT DISTINCT Culture FROM lCountriesCultures WHERE Culture IS NOT NULL ORDER BY Culture", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) cultures.Add(reader.GetString(0).Trim());
        return cultures;
    }

    // ---- Nomenclatures ---------------------------------------------------------

    public async Task<IReadOnlyList<LegacyLookupItem>> ReadLookupAsync(LegacyLookup table, CancellationToken ct)
    {
        var items = new Dictionary<short, LegacyLookupItem>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var labelColumns = await LabelColumnsAsync(connection, table.LegacyCulturesTable, ct);
        if (labelColumns.Count == 0) return Array.Empty<LegacyLookupItem>();
        var labelColumn = labelColumns[0];

        // Éléments (identifiant hérité, code externe, ordre, révocation).
        var sql = $"SELECT ID, ExternalID, SortOrder, [Revoke], [RevokeDate] FROM [{table.LegacyTable}]";
        await using (var command = new SqlCommand(sql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = Convert.ToInt16(reader["ID"]);
                items[id] = new LegacyLookupItem(
                    id,
                    reader["ExternalID"] as string,
                    reader["SortOrder"] is DBNull ? (short)0 : Convert.ToInt16(reader["SortOrder"]),
                    reader["Revoke"] is bool revoke && revoke,
                    reader["RevokeDate"] as DateTime?,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        if (items.Count == 0) return Array.Empty<LegacyLookupItem>();

        // Libellés par culture.
        var culturesSql = $"SELECT OwnerID, Culture, {labelColumn} FROM [{table.LegacyCulturesTable}]";
        await using (var command = new SqlCommand(culturesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var ownerId = Convert.ToInt16(reader["OwnerID"]);
                if (!items.TryGetValue(ownerId, out var item)) continue;
                var culture = (reader["Culture"] as string ?? "fr").Trim().ToLowerInvariant();
                if (culture.Length > 3) culture = culture[..2];
                var label = reader[labelColumn] as string;
                if (!string.IsNullOrWhiteSpace(label))
                    (item.Labels as Dictionary<string, string>)![culture] = label!.Trim();
            }
        }

        return items.Values.OrderBy(i => i.Id).ToList();
    }

    /// <summary>Colonnes de libellé disponibles d'une table de cultures (Description, Name, Abbreviation…).</summary>
    private static async Task<List<string>> LabelColumnsAsync(SqlConnection connection, string table, CancellationToken ct)
    {
        var columns = new List<string>();
        var sql = @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @table
                      AND COLUMN_NAME IN ('Description', 'Name', 'Abbreviation')
                    ORDER BY CASE COLUMN_NAME WHEN 'Description' THEN 1 WHEN 'Name' THEN 2 ELSE 3 END";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(0));
        return columns;
    }

    // ---- Référentiels ----------------------------------------------------------

    public async Task<IReadOnlyList<LegacyCountry>> ReadCountriesAsync(CancellationToken ct)
    {
        var countries = new Dictionary<short, LegacyCountry>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using (var command = new SqlCommand(
            "SELECT ID, ExternalID, Alpha2, Alpha3, SortOrder, [Revoke] FROM lCountries", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = Convert.ToInt16(reader["ID"]);
                countries[id] = new LegacyCountry(id,
                    reader["ExternalID"] as string,
                    reader["Alpha2"] as string,
                    reader["Alpha3"] as string,
                    reader["SortOrder"] is DBNull ? (short)0 : Convert.ToInt16(reader["SortOrder"]),
                    reader["Revoke"] is bool revoke && revoke,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        await using (var command = new SqlCommand(
            "SELECT OwnerID, Culture, Name FROM lCountriesCultures", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var ownerId = Convert.ToInt16(reader["OwnerID"]);
                if (!countries.TryGetValue(ownerId, out var country)) continue;
                var culture = (reader["Culture"] as string ?? "fr").Trim().ToLowerInvariant();
                if (culture.Length > 3) culture = culture[..2];
                if (reader["Name"] as string is { Length: > 0 } name)
                    (country.Names as Dictionary<string, string>)![culture] = name.Trim();
            }
        }

        return countries.Values.OrderBy(c => c.Id).ToList();
    }

    public async Task<IReadOnlyList<LegacyBorderPoint>> ReadBorderPointsAsync(CancellationToken ct)
    {
        var points = new Dictionary<short, LegacyBorderPoint>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using (var command = new SqlCommand(
            "SELECT ID, ExternalID, SortOrder, [Revoke] FROM lBorderPoints", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var id = Convert.ToInt16(reader["ID"]);
                points[id] = new LegacyBorderPoint(id,
                    reader["ExternalID"] as string,
                    reader["SortOrder"] is DBNull ? (short)0 : Convert.ToInt16(reader["SortOrder"]),
                    reader["Revoke"] is bool revoke && revoke,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        await using (var command = new SqlCommand(
            "SELECT OwnerID, Culture, Name, RegionalSite FROM lBorderPointsCultures", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var ownerId = Convert.ToInt16(reader["OwnerID"]);
                if (!points.TryGetValue(ownerId, out var point)) continue;
                var culture = (reader["Culture"] as string ?? "fr").Trim().ToLowerInvariant();
                if (culture.Length > 3) culture = culture[..2];
                if (reader["Name"] as string is { Length: > 0 } name)
                    (point.Names as Dictionary<string, string>)![culture] = name.Trim();
            }
        }

        return points.Values.OrderBy(p => p.Id).ToList();
    }

    // ---- Comptages -------------------------------------------------------------

    public async Task<long> CountAsync(LegacyTable table, CancellationToken ct)
    {
        var name = TableName(table);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM [{name}]", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    public async Task<bool> TableExistsAsync(LegacyTable table, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT OBJECT_ID(@table, 'U')", connection);
        command.Parameters.AddWithValue("@table", TableName(table));
        return await command.ExecuteScalarAsync(ct) is not (null or DBNull);
    }

    private static string TableName(LegacyTable table) => table switch
    {
        LegacyTable.Persons => "Persons",
        LegacyTable.Documents => "Documents",
        LegacyTable.Photos => "Photos",
        LegacyTable.PassportRegistrationData => "PassportRegistrationData",
        LegacyTable.Children => "Children",
        LegacyTable.Visa => "Visa",
        LegacyTable.VisaChildren => "VisaChildren",
        LegacyTable.PersonsStopList => "PersonsStopList",
        LegacyTable.DocumentsStopList => "DocumentsStopList",
        LegacyTable.DoubtablePersons => "DoubtablePersons",
        LegacyTable.Permit => "Permit",
        LegacyTable.PermitMedicalHistory => "PermitMedicalHistory",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    // ---- Historique (pages) ----------------------------------------------------

    public async IAsyncEnumerable<IReadOnlyList<LegacyPerson>> ReadPersonsAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT ID, UpdateUser, UpdateDate, BorderPointID FROM Persons
                             WHERE ID > @after ORDER BY ID OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyPerson(
                           r.GetString(0),
                           r[1] as string,
                           r[2] as DateTime?,
                           r[3] is DBNull ? null : Convert.ToInt16(r[3])), ct))
            yield return page;
    }

    public async Task<IReadOnlyList<LegacyDocument>> ReadDocumentsByIdsAsync(IReadOnlyCollection<string> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0) return Array.Empty<LegacyDocument>();

        var documents = new List<LegacyDocument>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        foreach (var chunk in Chunk(ids, 500))
        {
            var names = chunk.Select((_, i) => $"@id{i}").ToArray();
            var sql = $@"SELECT ID, DocumentTypeID, DocumentNumber, DocumentCountryOfIssueID,
                                DocumentIssueDate, DocumentExpireDate, Surname, GivenName, FatherName,
                                DateOfBirth, SexID, Citizenship, UpdateDate, BorderPointID, UpdateUser,
                                PersonalNumber, CreateDate, CreatorUser
                         FROM Documents WHERE ID IN ({string.Join(", ", names)})";
            await using var command = new SqlCommand(sql, connection);
            for (var i = 0; i < chunk.Count; i++) command.Parameters.AddWithValue($"@id{i}", chunk[i]);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                documents.Add(new LegacyDocument(
                    reader.GetString(0),
                    Convert.ToInt16(reader[1]),
                    reader.GetString(2).Trim(),
                    Convert.ToInt16(reader[3]),
                    reader[4] as DateTime?,
                    reader[5] as DateTime?,
                    (reader[6] as string ?? string.Empty).Trim(),
                    (reader[7] as string ?? string.Empty).Trim(),
                    reader[8] as string,
                    reader[9] as DateTime?,
                    Convert.ToInt16(reader[10]),
                    Convert.ToInt16(reader[11]),
                    reader[12] as DateTime?,
                    reader[13] is DBNull ? null : Convert.ToInt16(reader[13]),
                    reader[14] as string,
                    reader[15] as string,
                    reader[16] as DateTime?,
                    reader[17] as string));
            }
        }

        return documents;
    }

    public async Task<IReadOnlyList<LegacyPhoto>> ReadPhotosAsync(IReadOnlyCollection<string> documentIds,
        CancellationToken ct)
    {
        if (documentIds.Count == 0) return Array.Empty<LegacyPhoto>();

        var photos = new List<LegacyPhoto>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Lots d'identifiants paramétrés (aucune concaténation de valeurs).
        foreach (var chunk in Chunk(documentIds, 500))
        {
            var names = chunk.Select((_, i) => $"@id{i}").ToArray();
            var sql = $"SELECT DocumentID, Photo, ScannedDocument FROM Photos WHERE DocumentID IN ({string.Join(", ", names)})";
            await using var command = new SqlCommand(sql, connection);
            for (var i = 0; i < chunk.Count; i++) command.Parameters.AddWithValue($"@id{i}", chunk[i]);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                photos.Add(new LegacyPhoto(
                    reader.GetString(0),
                    reader[1] as byte[],
                    reader[2] as byte[]));
            }
        }

        return photos;
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyMovement>> ReadMovementsAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT ID, DocumentID, TravelDate, TransportTypeID, TransportCrossBorderID,
                                    MovementDirectionID, IsDriver, PersonID, TravelReasonID, Granted,
                                    DeportationReasonID, IsTransit, HasVisa, VisaID, ChildrenCount, Note,
                                    ResidentCountryID, DestinationCountryID, Occupation, AccomodationAddress,
                                    AccomodationTypeID, DurationOfStay, Inspector, PCNumber, RegistrationTime,
                                    UpdateDate, BorderPointID
                             FROM PassportRegistrationData WHERE ID > @after ORDER BY ID
                             OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyMovement(
                           r.GetString(0),
                           r.GetString(1),
                           (r[2] as DateTime?) ?? DateTime.MinValue,
                           r[3] is DBNull ? null : Convert.ToInt16(r[3]),
                           r[4] as string,
                           r[5] is DBNull ? (short)0 : Convert.ToInt16(r[5]),
                           r[6] is bool driver && driver,
                           r[7] as string ?? string.Empty,
                           r[8] is DBNull ? null : Convert.ToInt16(r[8]),
                           r[9] is bool granted && granted,
                           r[10] is DBNull ? null : Convert.ToInt16(r[10]),
                           r[11] is bool transit && transit,
                           r[12] is bool visa && visa,
                           r[13] as string,
                           r[14] is DBNull ? 0 : Convert.ToInt32(r[14]),
                           r[15] as string,
                           r[16] is DBNull ? null : Convert.ToInt16(r[16]),
                           r[17] is DBNull ? null : Convert.ToInt16(r[17]),
                           r[18] as string,
                           r[19] as string,
                           r[20] is DBNull ? null : Convert.ToInt16(r[20]),
                           r[21] is DBNull ? null : Convert.ToInt16(r[21]),
                           r[22] as string,
                           r[23] as string,
                           (r[24] as DateTime?) ?? DateTime.MinValue,
                           r[25] as DateTime?,
                           r[26] is DBNull ? null : Convert.ToInt16(r[26])), ct))
            yield return page;
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyChild>> ReadChildrenAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT ID, PassportRegistrationDataID, Surname, GivenName, DateOfBirth,
                                    SexID, Citizenship, DocumentTypeID, DocumentNumber
                             FROM Children WHERE ID > @after ORDER BY ID
                             OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyChild(
                           r.GetString(0),
                           r.GetString(1),
                           (r[2] as string ?? string.Empty).Trim(),
                           (r[3] as string ?? string.Empty).Trim(),
                           r[4] as DateTime?,
                           r[5] is DBNull ? null : Convert.ToInt16(r[5]),
                           r[6] is DBNull ? null : Convert.ToInt16(r[6]),
                           r[7] is DBNull ? null : Convert.ToInt16(r[7]),
                           r[8] as string), ct))
            yield return page;
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyVisa>> ReadVisasAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT ID, DocumentID, VisaTypeID, VisaEntryID, VisaNumber, IssueDate, ExpireDate,
                                    Duration, ReferenceNumber, ReceiptNumber, Address, ContactNumber, Employer,
                                    InvitedBy, RelationID, UniversityID, PermitStatusID, Occupation, PeriodTypeID,
                                    Note, VoucherNumber, PermitConditionID, ProvinceID, Barcode, PCNumber,
                                    [Revoke], [RevokeDate], CreateDate, CreatorUser, UpdateDate, UpdateUser, BorderPointID
                             FROM Visa WHERE ID > @after ORDER BY ID
                             OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyVisa(
                           r.GetString(0),
                           r[1] as string,
                           r[2] is DBNull ? (short)0 : Convert.ToInt16(r[2]),
                           r[3] is DBNull ? null : Convert.ToInt16(r[3]),
                           r[4] as string,
                           r[5] as DateTime?,
                           r[6] as DateTime?,
                           r[7] is DBNull ? null : Convert.ToInt16(r[7]),
                           r[8] as string, r[9] as string, r[10] as string, r[11] as string,
                           r[12] as string, r[13] as string,
                           r[14] is DBNull ? null : Convert.ToInt16(r[14]),
                           r[15] is DBNull ? null : Convert.ToInt16(r[15]),
                           r[16] is DBNull ? null : Convert.ToInt16(r[16]),
                           r[17] as string,
                           r[18] is DBNull ? null : Convert.ToInt16(r[18]),
                           r[19] as string, r[20] as string,
                           r[21] is DBNull ? null : Convert.ToInt16(r[21]),
                           r[22] is DBNull ? null : Convert.ToInt16(r[22]),
                           r[23] as string, r[24] as string,
                           r[25] is bool revoke && revoke,
                           r[26] as DateTime?,
                           r[27] as DateTime?, r[28] as string,
                           r[29] as DateTime?, r[30] as string,
                           r[31] is DBNull ? null : Convert.ToInt16(r[31])), ct))
            yield return page;
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyVisaChild>> ReadVisaChildrenAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT ID, VisaID, Surname, GivenName, DateOfBirth, SexID, Citizenship
                             FROM VisaChildren WHERE ID > @after ORDER BY ID
                             OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyVisaChild(
                           r.GetString(0),
                           r.GetString(1),
                           r[2] as string, r[3] as string,
                           r[4] as DateTime?,
                           r[5] is DBNull ? null : Convert.ToInt16(r[5]),
                           r[6] is DBNull ? null : Convert.ToInt16(r[6])), ct))
            yield return page;
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyPersonStopList>> ReadPersonStopListAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT ID, OldID, Surname, GivenName, FathersName, SurnameAlt, GivenNameAlt,
                                    FathersNameAlt, DateOfBirth, SexID, Citizenship, WhoSetUp, SetUpDate, CancelDate,
                                    StopListSourceID, SourceDocument, Info, Photo, DocumentTypeID, DocumentNumber,
                                    PersonalNumber, ActionRequired, MatchInStopListActionID, UpdateDate,
                                    LastUpdateUser, Timestamp, CancelStopListSourceID, CancelSourceDocument,
                                    IsAuthorized
                             FROM PersonsStopList WHERE ID > @after ORDER BY ID
                             OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyPersonStopList(
                           r.GetString(0),
                           r[1] as string,
                           r[2] as string, r[3] as string, r[4] as string,
                           r[5] as string, r[6] as string, r[7] as string,
                           r[8] as DateTime?,
                           r[9] is DBNull ? null : Convert.ToInt16(r[9]),
                           r[10] is DBNull ? null : Convert.ToInt16(r[10]),
                           r[11] as string,
                           r[12] as DateTime?, r[13] as DateTime?,
                           r[14] is DBNull ? null : Convert.ToInt16(r[14]),
                           r[15] as string, r[16] as string, r[17] as byte[],
                           r[18] is DBNull ? null : Convert.ToInt16(r[18]),
                           r[19] as string, r[20] as string, r[21] as string,
                           r[22] is DBNull ? null : Convert.ToInt16(r[22]),
                           r[23] as DateTime?, r[24] as string, r[25] as DateTime?,
                           r[26] is DBNull ? null : Convert.ToInt16(r[26]),
                           r[27] as string,
                           r[28] is DBNull ? null : (r[28] is bool auth && auth)), ct))
            yield return page;
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyDocumentStopList>> ReadDocumentStopListAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT ID, OldID, DocumentTypeID, DocumentNumber, PersonalNumber, CountryID,
                                    Surname, GivenName, FathersName, DateOfBirth, SexID, SetUpDate, CancelDate,
                                    StopListSourceID, SourceDocument, Info, ActionRequired,
                                    MatchInStopListActionID, UpdateDate, LastUpdateUser, [Timestamp],
                                    CancelStopListSourceID, CancelSourceDocument
                             FROM DocumentsStopList WHERE ID > @after ORDER BY ID
                             OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyDocumentStopList(
                           r.GetString(0),
                           r[1] as string,
                           r[2] is DBNull ? null : Convert.ToInt16(r[2]),
                           r[3] as string, r[4] as string,
                           r[5] is DBNull ? null : Convert.ToInt16(r[5]),
                           r[6] as string, r[7] as string, r[8] as string,
                           r[9] as DateTime?,
                           r[10] is DBNull ? null : Convert.ToInt16(r[10]),
                           r[11] as DateTime?, r[12] as DateTime?,
                           r[13] is DBNull ? null : Convert.ToInt16(r[13]),
                           r[14] as string, r[15] as string, r[16] as string,
                           r[17] is DBNull ? null : Convert.ToInt16(r[17]),
                           r[18] as DateTime?, r[19] as string, r[20] as DateTime?,
                           r[21] is DBNull ? null : Convert.ToInt16(r[21]),
                           r[22] as string), ct))
            yield return page;
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyDoubtablePerson>> ReadDoubtablePersonsAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Table sans identifiant : pagination par OFFSET sur le dossier frontalier (clé logique).
        var offset = 0;
        var sql = @"SELECT PassportRegistrationID, Supervisor, SupervisorRegistrationTime, SupervisorPCNumber,
                           Status, PersonStopID, DocumentStopID, InspectorsNote, SupervisorsNote,
                           PerformedActionID, CheckingReason, RefusalOfficerName, RefusalOfficerRank,
                           RefusalDocNumber, RefusalCountryID, RefusalJustificationID,
                           RefusalJustificationOther, RefusalCreateDate
                    FROM DoubtablePersons ORDER BY PassportRegistrationID
                    OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        while (true)
        {
            var page = new List<LegacyDoubtablePerson>();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@offset", offset);
            command.Parameters.AddWithValue("@size", pageSize);
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    page.Add(new LegacyDoubtablePerson(
                        reader.GetString(0),
                        reader[1] as string,
                        reader[2] as DateTime?,
                        reader[3] as string,
                        reader[4] is DBNull ? null : Convert.ToInt16(reader[4]),
                        reader[5] as string,
                        reader[6] as string,
                        reader[7] as string,
                        reader[8] as string,
                        reader[9] is DBNull ? null : Convert.ToInt16(reader[9]),
                        reader[10] is DBNull ? null : Convert.ToInt16(reader[10]),
                        reader[11] as string,
                        reader[12] as string,
                        reader[13] as string,
                        reader[14] is DBNull ? null : Convert.ToInt16(reader[14]),
                        reader[15] is DBNull ? null : Convert.ToInt16(reader[15]),
                        reader[16] as string,
                        reader[17] as DateTime?));
                }
            }

            if (page.Count == 0) yield break;
            offset += page.Count;
            yield return page;
            if (page.Count < pageSize) yield break;
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyPermit>> ReadPermitsAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT ID, DocumentID, VisaID, PermitTypeID, Number, IssueDate, ExpireDate,
                                    StatusID, MaritalStatusID, OccupationID, OccupationOther, QualificationID,
                                    CourseOfStudyID, DocumentIssuingAuthority, BloodTypeID, UpdateDate,
                                    UpdateUser, CreateDate, CreateUser, Note, PCNumber
                             FROM Permit WHERE ID > @after ORDER BY ID
                             OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyPermit(
                           r.GetString(0),
                           r.GetString(1),
                           r[2] as string,
                           r[3] is DBNull ? null : Convert.ToInt16(r[3]),
                           r[4] as string,
                           r[5] as DateTime?, r[6] as DateTime?,
                           r[7] is DBNull ? null : Convert.ToInt16(r[7]),
                           r[8] is DBNull ? null : Convert.ToInt16(r[8]),
                           r[9] is DBNull ? null : Convert.ToInt16(r[9]),
                           r[10] as string,
                           r[11] is DBNull ? null : Convert.ToInt16(r[11]),
                           r[12] is DBNull ? null : Convert.ToInt16(r[12]),
                           r[13] as string,
                           r[14] is DBNull ? null : Convert.ToInt16(r[14]),
                           r[15] as DateTime?, r[16] as string,
                           r[17] as DateTime?, r[18] as string,
                           r[19] as string, r[20] as string), ct))
            yield return page;
    }

    public async IAsyncEnumerable<IReadOnlyList<LegacyMedicalHistory>> ReadMedicalHistoriesAsync(int pageSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const string sql = @"SELECT PermitID, PermitDependantID, CurrentIllness, CurrentMedication,
                                    MalariaSuffered, YellowFeverSuffered, TBSuffered, MeaslesSuffered,
                                    CholeraSuffered, TyphoidSuffered, JaundiceSuffered, ChronicCoughSuffered,
                                    OtherSuffered, YellowFeverVaccinated, MMRVaccinated, DiphtheriaVaccinated,
                                    PolioVaccinated, HealthCentreVisited, ChronicDesease, InfectiousDesease,
                                    isPublicHealthCentre, UpdateDate, UpdateUser
                             FROM PermitMedicalHistory WHERE PermitID > @after ORDER BY PermitID
                             OFFSET 0 ROWS FETCH NEXT @size ROWS ONLY";
        await foreach (var page in PageAsync(sql, pageSize, r => new LegacyMedicalHistory(
                           r.GetString(0),
                           r[1] as string,
                           r[2] as string, r[3] as string,
                           r[4] is bool a && a, r[5] is bool b && b, r[6] is bool c && c,
                           r[7] is bool d && d, r[8] is bool e && e, r[9] is bool f && f,
                           r[10] is bool g && g, r[11] is bool h && h,
                           r[12] as string,
                           r[13] is bool i && i, r[14] is bool j && j, r[15] is bool k && k,
                           r[16] is bool l && l, r[17] is bool m && m, r[18] is bool n && n,
                           r[19] is bool o && o, r[20] is bool p && p,
                           r[21] as DateTime?, r[22] as string), ct))
            yield return page;
    }

    // ---- Pagination (curseur = dernier identifiant lu) -------------------------

    private async IAsyncEnumerable<IReadOnlyList<T>> PageAsync<T>(string sql, int pageSize,
        Func<System.Data.Common.DbDataReader, T> map, [EnumeratorCancellation] CancellationToken ct)
    {
        string? after = null;
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        while (true)
        {
            var page = new List<T>(pageSize);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@after", (object?)after ?? string.Empty);
            command.Parameters.AddWithValue("@size", pageSize);
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct)) page.Add(map(reader));
            }

            if (page.Count == 0) yield break;
            yield return page;
            if (page.Count < pageSize) yield break;

            after = page[^1] switch
            {
                LegacyPerson p => p.Id,
                LegacyDocument d => d.Id,
                LegacyMovement m => m.Id,
                LegacyChild c => c.Id,
                LegacyVisa v => v.Id,
                LegacyVisaChild vc => vc.Id,
                LegacyPersonStopList s => s.Id,
                LegacyDocumentStopList s => s.Id,
                LegacyPermit p => p.Id,
                LegacyMedicalHistory h => h.PermitId,
                _ => throw new NotSupportedException(typeof(T).Name),
            };
        }
    }

    private static IEnumerable<IReadOnlyList<string>> Chunk(IReadOnlyCollection<string> source, int size)
    {
        var chunk = new List<string>(size);
        foreach (var item in source)
        {
            chunk.Add(item);
            if (chunk.Count < size) continue;
            yield return chunk;
            chunk = new List<string>(size);
        }
        if (chunk.Count > 0) yield return chunk;
    }
}
