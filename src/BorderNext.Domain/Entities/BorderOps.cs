namespace BorderNext.Domain.Entities;

// ---------------------------------------------------------------------------
// Opérations frontière (héritage : lCarriers, lTerminals, Transports,
// TransportCrossedBorder + TransportCrossInfo, Vessels).
//
// Modèle hérité reconstitué :
//   Transports             → Trip          (le voyage programmé : vol/navire/train,
//                                           compagnie, terminaux de départ/arrivée)
//   TransportCrossedBorder → TripCrossing  (le passage effectif : sens, date)
//   TransportCrossInfo     → état d'ouverture de la salle d'enregistrement du
//                            passage (fusionné dans TripCrossing : relation 1:1
//                            sans clé propre dans l'héritage)
// Les mouvements (PassportRegistrationData.TransportCrossBorderID) sont rattachés
// au passage via Movement.TripCrossingId (l'ancien identifiant texte est conservé
// pour la migration du Lot 9).
// ---------------------------------------------------------------------------

/// <summary>Compagnie de transport (héritage : <c>lCarriers</c> — 822 lignes,
/// types transport + pays + cultures fr/en/ar/es/ru).</summary>
public class Carrier
{
    public int Id { get; set; }

    /// <summary>Identifiant hérité (lCarriers.ID smallint) — Lot 9.</summary>
    public int? LegacyId { get; set; }

    /// <summary>Code de la compagnie (héritage : ExternalID, ex. AF, ET, SN — codes IATA).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Type de transport (nomenclature TRANSPORT_TYPE : Flight, Ship, Train…).</summary>
    public int? TransportTypeId { get; set; }
    public Lookup? TransportType { get; set; }

    public int? CountryId { get; set; }
    public Country? Country { get; set; }

    public int SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? UpdateDate { get; set; }

    public ICollection<CarrierCulture> Cultures { get; set; } = new List<CarrierCulture>();

    /// <summary>Libellé affichable (culture française, sinon première disponible) — non mappé.</summary>
    public string? DisplayName => Cultures.FirstOrDefault(c => c.Culture == "fr")?.Description
                                  ?? Cultures.FirstOrDefault()?.Description
                                  ?? Code;
}

/// <summary>Nom traduit d'une compagnie (héritage : lCarriersCultures.Description).</summary>
public class CarrierCulture : IDescribedRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Terminal (héritage : <c>lTerminals</c> — 10 790 lignes dont 10 775 aéroports ;
/// un terminal appartient à un type de transport, un pays et une ville).</summary>
public class Terminal
{
    public int Id { get; set; }
    public int? LegacyId { get; set; }

    /// <summary>Code du terminal (héritage : ExternalID — code IATA d'aéroport, ex. FIH, CDG).</summary>
    public string Code { get; set; } = string.Empty;

    public int? TransportTypeId { get; set; }
    public Lookup? TransportType { get; set; }

    public int? CountryId { get; set; }
    public Country? Country { get; set; }

    /// <summary>Ville de rattachement (héritage : CityID — renseignée pour 19 terminaux seulement).</summary>
    public int? CityId { get; set; }
    public City? City { get; set; }

    public int SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? UpdateDate { get; set; }

    public ICollection<TerminalCulture> Cultures { get; set; } = new List<TerminalCulture>();

    /// <summary>Libellé affichable (culture française, sinon première disponible) — non mappé.</summary>
    public string? DisplayName => Cultures.FirstOrDefault(c => c.Culture == "fr")?.Description
                                  ?? Cultures.FirstOrDefault()?.Description
                                  ?? Code;
}

/// <summary>Description traduite d'un terminal (héritage : lTerminalsCultures.Description).</summary>
public class TerminalCulture : IDescribedRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Voyage programmé (héritage : <c>Transports</c> — vol, navire ou train
/// à destination/départ d'un terminal du poste frontière).</summary>
public class Trip
{
    public int Id { get; set; }

    /// <summary>Identifiant hérité (Transports.ID varchar(20), généré par System.NewID) — Lot 9.</summary>
    public string? LegacyId { get; set; }

    /// <summary>Nom du voyage (héritage : Name — numéro de vol/navire, ex. AF100).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type de transport (nomenclature TRANSPORT_TYPE — l'héritage filtre les
    /// listes de passages ouverts par ExternalID : Flight, Ship, Train).</summary>
    public int? TransportTypeId { get; set; }
    public Lookup? TransportType { get; set; }

    /// <summary>Terminal de départ (héritage : From → lTerminals).</summary>
    public int? FromTerminalId { get; set; }
    public Terminal? FromTerminal { get; set; }

    /// <summary>Terminal d'arrivée (héritage : To → lTerminals).</summary>
    public int? ToTerminalId { get; set; }
    public Terminal? ToTerminal { get; set; }

    /// <summary>Numéro externe (héritage : TransportID).</summary>
    public string? ExternalNumber { get; set; }

    /// <summary>Description libre (héritage : TransportDescription).</summary>
    public string? Description { get; set; }

    public int? CarrierId { get; set; }
    public Carrier? Carrier { get; set; }

    /// <summary>Terminal d'exploitation du voyage (héritage : TerminalID).</summary>
    public int? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }

    /// <summary>Gare/ville de départ (héritage : StationFrom — nullable, non exploité dans les données).</summary>
    public int? StationFromCityId { get; set; }

    /// <summary>Gare/ville d'arrivée (héritage : StationTo).</summary>
    public int? StationToCityId { get; set; }

    /// <summary>Heure programmée (héritage : ScheduledTime time(2)).</summary>
    public TimeSpan? ScheduledTime { get; set; }

    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public string? LastUpdateUser { get; set; }
    public DateTime UpdateDate { get; set; }

    public ICollection<TripCrossing> Crossings { get; set; } = new List<TripCrossing>();
}

/// <summary>Passage d'un voyage à un poste frontière (héritage : <c>TransportCrossedBorder</c>
/// + état de la salle d'enregistrement <c>TransportCrossInfo</c> fusionné).</summary>
public class TripCrossing
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public int TripId { get; set; }
    public Trip? Trip { get; set; }

    /// <summary>Sens (nomenclature MOVEMENT_DIRECTION : Entry/Exit).</summary>
    public int DirectionLookupId { get; set; }
    public Lookup? Direction { get; set; }

    public DateTime Date { get; set; }

    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }

    public string? LastUpdateUser { get; set; }
    public DateTime UpdateDate { get; set; }

    // -- État d'ouverture (héritage : TransportCrossInfo) ---------------------
    /// <summary>Enregistrement en cours (héritage : OpenStatus).</summary>
    public bool IsOpen { get; set; }

    public TimeSpan? OpenTime { get; set; }
    public string? OpenUser { get; set; }
    public TimeSpan? CloseTime { get; set; }
    public string? CloseUser { get; set; }

    public ICollection<Movement> Movements { get; set; } = new List<Movement>();
}

/// <summary>Navire (héritage : <c>Vessels</c> — registre des navires de la zone).</summary>
public class Vessel
{
    public int Id { get; set; }
    public string? LegacyId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? FormerName { get; set; }

    /// <summary>Port d'attache (héritage : HomePort → lCountries.ID).</summary>
    public int? HomePortCountryId { get; set; }
    public Country? HomePortCountry { get; set; }

    /// <summary>Nationalité (pavillon) — héritage : Nationality → lCountries.ID.</summary>
    public int? NationalityCountryId { get; set; }
    public Country? NationalityCountry { get; set; }

    public string RegistrationNumber { get; set; } = string.Empty;
    public string? Dimension { get; set; }
    public string? Color { get; set; }

    /// <summary>Type de navire (nomenclature VESSEL_TYPE : Ferry, Tanker…).</summary>
    public int? VesselTypeId { get; set; }
    public Lookup? VesselType { get; set; }

    public string? RadioCallSign { get; set; }
    public string? Frequencies { get; set; }
    public string? Note { get; set; }
    public DateTime? RegistrationDate { get; set; }

    public string? LastUpdateUser { get; set; }
    public short? BorderPointId { get; set; }
    public DateTime? UpdateDate { get; set; }
}
