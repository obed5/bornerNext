namespace BorderNext.Domain.Entities;

/// <summary>Réglage applicatif (table héritée : Settings).</summary>
public class Setting
{
    public int Id { get; set; }

    /// <summary>Clé de réglage (héritage : Type, ex. BorderPoint, UserTimeOut, AUDIT_LOG_*).</summary>
    public string Type { get; set; } = string.Empty;

    public string? UserId { get; set; }
    public string? MachineName { get; set; }
    public string? OwnerType { get; set; }

    public int? Value { get; set; }
    public DateTime? DateTimeValue { get; set; }
    public string? StringValue { get; set; }

    /// <summary>Valeur chiffrée (héritage : EncryptedValue varbinary).</summary>
    public byte[]? EncryptedValue { get; set; }

    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }
}

/// <summary>Journal d'audit (table héritée : AuditLog).</summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>Type d'événement (héritage : types AUDIT_LOG_*).</summary>
    public string EventType { get; set; } = string.Empty;

    public string? UserId { get; set; }
    public string? MachineName { get; set; }
    public string? PcNumber { get; set; }

    /// <summary>Entité concernée (table/écran).</summary>
    public string? Entity { get; set; }

    /// <summary>Identifiant de l'élément concerné.</summary>
    public string? EntityId { get; set; }

    /// <summary>Détail complémentaire (message court, valeurs avant/après résumées).</summary>
    public string? Details { get; set; }

    public DateTime EventTime { get; set; }
}

/// <summary>Types d'événements d'audit — valeurs héritées des réglages AUDIT_LOG_*.</summary>
public static class AuditEventTypes
{
    public const string UserLogin = "AUDIT_LOG_UserLogIn";
    public const string UserLogout = "AUDIT_LOG_UserLogout";
    public const string FormLoad = "AUDIT_LOG_FormLoad";
    public const string FormClosed = "AUDIT_LOG_FormClosed";
    public const string ButtonOrMenuClicked = "AUDIT_LOG_ButtonOrMenuClicked";
    public const string SearchPerformed = "AUDIT_LOG_SearchPerformed";
    public const string SearchResult = "AUDIT_LOG_SearchResult";
    public const string RowAdded = "AUDIT_LOG_RowAdded";
    public const string RowUpdated = "AUDIT_LOG_RowUpdated";
    public const string RowRevoked = "AUDIT_LOG_RowRevoked";
    public const string DataExported = "AUDIT_LOG_DataExported";
    public const string ReportExported = "AUDIT_LOG_ReportExported";
    public const string ReportPreviewed = "AUDIT_LOG_ReportPreviewed";
    public const string ReportPrinted = "AUDIT_LOG_ReportPrinted";
}
