namespace BorderNext.Domain.Entities;

/// <summary>Ligne traduite générique (tables cultures héritées : l*Cultures).</summary>
public interface ITranslatedRow
{
    string Culture { get; set; }
}

/// <summary>Ligne traduite portant une description (LookupCulture, ControlCulture).</summary>
public interface IDescribedRow : ITranslatedRow
{
    string Description { get; set; }
}

/// <summary>Ligne traduite portant un nom (CountryCulture, ProvinceCulture, CityCulture).</summary>
public interface INamedRow : ITranslatedRow
{
    string? Name { get; set; }
}

// ---------------------------------------------------------------------------
// Nomenclatures génériques (héritage : ~50 tables l* + l*Cultures du système
// précédent). Le schéma cible normalise ces listes en un trio :
//   LookupTypes  → la liste (ex. DOCUMENT_TYPE)
//   Lookups      → l'élément (ex. code OP)
//   LookupCultures → les libellés par culture (fr, en, …)
// ---------------------------------------------------------------------------

/// <summary>Type de nomenclature (ex. DOCUMENT_TYPE, TRAVEL_REASON).</summary>
public class LookupType
{
    public int Id { get; set; }

    /// <summary>Code stable utilisé par le code (ex. DOCUMENT_TYPE).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Libellé d'administration (français).</summary>
    public string NameFr { get; set; } = string.Empty;

    /// <summary>Libellé d'administration (anglais).</summary>
    public string NameEn { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public bool Revoke { get; set; }

    public ICollection<Lookup> Items { get; set; } = new List<Lookup>();
}

/// <summary>Élément d'une nomenclature (héritage : ligne d'une table l*).</summary>
public class Lookup
{
    public int Id { get; set; }
    public int LookupTypeId { get; set; }
    public LookupType? Type { get; set; }

    /// <summary>Code externe (héritage : ExternalID, ex. OP, PD, F, M).</summary>
    public string? Code { get; set; }

    public int SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public ICollection<LookupCulture> Cultures { get; set; } = new List<LookupCulture>();
}

/// <summary>Libellé traduit d'un élément de nomenclature (héritage : l*Cultures).</summary>
public class LookupCulture : IDescribedRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Lookup? Owner { get; set; }
}

// ---------------------------------------------------------------------------
// Contrôles d'interface et droits par rôle (héritage : lControls,
// lControlsCultures, ControlsToRoles).
// ---------------------------------------------------------------------------

/// <summary>Contrôle d'interface soumis à droits (ex. DM_USERS, PAL_SURNAME).</summary>
public class Control
{
    public int Id { get; set; }

    /// <summary>Code du contrôle (héritage : ExternalID).</summary>
    public string Code { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public ICollection<ControlCulture> Cultures { get; set; } = new List<ControlCulture>();
    public ICollection<ControlToRole> RoleMappings { get; set; } = new List<ControlToRole>();
}

/// <summary>Libellé traduit d'un contrôle (héritage : lControlsCultures).</summary>
public class ControlCulture : IDescribedRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Control? Owner { get; set; }
}

/// <summary>
/// Droit d'un rôle sur un contrôle (héritage : ControlsToRoles).
/// Portée optionnelle par point frontière ; <see cref="Permission"/> = action
/// autorisée, <see cref="Visibility"/> = contrôle visible.
/// </summary>
public class ControlToRole
{
    public int Id { get; set; }
    public short UserRoleId { get; set; }
    public UserRole? UserRole { get; set; }
    public int ControlId { get; set; }
    public Control? Control { get; set; }
    public short? BorderPointId { get; set; }
    public BorderPoint? BorderPoint { get; set; }
    public bool Permission { get; set; }
    public bool Visibility { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }
}

// ---------------------------------------------------------------------------
// Géographie (héritage : lCountries, lProvinces, lCities + cultures).
// ---------------------------------------------------------------------------

/// <summary>Pays (héritage : lCountries).</summary>
public class Country
{
    public int Id { get; set; }

    /// <summary>Code externe hérité (ex. AND, 000 pour « pays inconnu »).</summary>
    public string? ExternalId { get; set; }

    /// <summary>Code ISO 3166-1 alpha-2 (ex. CD, FR).</summary>
    public string? Alpha2 { get; set; }

    /// <summary>Code ISO 3166-1 alpha-3 (ex. COD, FRA).</summary>
    public string? Alpha3 { get; set; }

    /// <summary>Région (nomenclature REGION).</summary>
    public int? RegionLookupId { get; set; }
    public Lookup? Region { get; set; }

    public int SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public ICollection<CountryCulture> Cultures { get; set; } = new List<CountryCulture>();
}

/// <summary>Nom traduit d'un pays (héritage : lCountriesCultures.Name).</summary>
public class CountryCulture : INamedRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string? Name { get; set; } = string.Empty;

    public Country? Owner { get; set; }
}

/// <summary>Province / État (héritage : lProvinces).</summary>
public class Province
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public string? ExternalId { get; set; }
    public int SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public ICollection<ProvinceCulture> Cultures { get; set; } = new List<ProvinceCulture>();
}

/// <summary>Nom traduit d'une province (héritage : lProvincesCultures).</summary>
public class ProvinceCulture : INamedRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string? Name { get; set; } = string.Empty;
    public string? Abbreviation { get; set; }

    public Province? Owner { get; set; }
}

/// <summary>Ville / localité (héritage : lCities).</summary>
public class City
{
    public int Id { get; set; }
    public int CountryId { get; set; }
    public Country? Country { get; set; }
    public int? ProvinceId { get; set; }
    public Province? Province { get; set; }

    public string? ExternalId { get; set; }
    public int SortOrder { get; set; }
    public bool Revoke { get; set; }
    public DateTime? RevokeDate { get; set; }
    public DateTime? UpdateDate { get; set; }
    public string? UpdateUser { get; set; }

    public ICollection<CityCulture> Cultures { get; set; } = new List<CityCulture>();
}

/// <summary>Nom traduit d'une ville (héritage : lCitiesCultures).</summary>
public class CityCulture : INamedRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Culture { get; set; } = string.Empty;
    public string? Name { get; set; } = string.Empty;
    public string? Abbreviation { get; set; }

    public City? Owner { get; set; }
}
