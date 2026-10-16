namespace BorderNext.Domain.Entities;

/// <summary>Point frontière (table héritée : lBorderPoints).</summary>
public class BorderPoint
{
    public short Id { get; set; }

    /// <summary>Code externe (héritage : ExternalID, ex. FIH, LUFU, KAS).</summary>
    public string ExternalId { get; set; } = string.Empty;

    public short? SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? UpdateDate { get; set; }

    public ICollection<BorderPointCulture> Cultures { get; set; } = new List<BorderPointCulture>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

/// <summary>Traduction d'un point frontière (table héritée : lBorderPointsCultures).</summary>
public class BorderPointCulture : INamedRow
{
    public int Id { get; set; }
    public short OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string? Name { get; set; }

    /// <summary>Site régional (héritage : RegionalSite).</summary>
    public string? RegionalSite { get; set; }

    public BorderPoint? Owner { get; set; }
}
