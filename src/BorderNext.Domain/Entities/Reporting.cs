namespace BorderNext.Domain.Entities;

/// <summary>
/// Point cartographique d'un pays (héritage : rpt.DashboardCountryGPSCoordinates) :
/// coordonnées de référence pour la carte des nationalités du tableau de bord.
/// </summary>
public class CountryGeoPoint
{
    public int Id { get; set; }

    /// <summary>Code ISO 3166-1 alpha-2 (héritage : ISO2).</summary>
    public string? Iso2 { get; set; }

    /// <summary>Code ISO 3166-1 alpha-3 (héritage : ISO3).</summary>
    public string? Iso3 { get; set; }

    /// <summary>Libellé du pays (héritage : name).</summary>
    public string Name { get; set; } = string.Empty;

    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}
