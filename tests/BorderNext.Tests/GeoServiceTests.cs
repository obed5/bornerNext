using BorderNext.Application.Abstractions;
using BorderNext.Application.Services;
using BorderNext.Domain.Entities;
using BorderNext.Infrastructure.Persistence;
using BorderNext.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Tests;

/// <summary>Tests géographie et postes frontières (Lot 1).</summary>
public class GeoServiceTests : IDisposable
{
    private readonly BorderNextDbContext _db;
    private readonly CurrentUser _currentUser = new();
    private readonly GeoService _service;

    public GeoServiceTests()
    {
        _db = new BorderNextDbContext(new DbContextOptionsBuilder<BorderNextDbContext>()
            .UseInMemoryDatabase($"geo-{Guid.NewGuid()}")
            .Options);
        _service = new GeoService(_db, _currentUser, new SystemClock(), new AuditWriter(_db, _currentUser, new SystemClock()));
        _currentUser.Set(new User { Id = "ADM001", UserName = "admin", UserRoleId = 1 }, "ADMIN");
        Seed();
    }

    private void Seed()
    {
        var regionType = new LookupType { Id = 1, Code = "REGION", NameFr = "Régions", NameEn = "Regions" };
        regionType.Items.Add(new Lookup { Id = 1, Code = "CA", Cultures = { new LookupCulture { Culture = "fr", Description = "Afrique Centrale" }, new LookupCulture { Culture = "en", Description = "Central Africa" } } });
        _db.LookupTypes.Add(regionType);

        _db.Countries.Add(new Country
        {
            Id = 1, Alpha2 = "CD", Alpha3 = "COD", RegionLookupId = 1,
            Cultures = { new CountryCulture { Culture = "fr", Name = "CONGO, RDC" }, new CountryCulture { Culture = "en", Name = "CONGO, DRC" } }
        });
        _db.Cities.Add(new City
        {
            Id = 1, CountryId = 1, ExternalId = "FIH",
            Cultures = { new CityCulture { Culture = "fr", Name = "Kinshasa", Abbreviation = "FIH" }, new CityCulture { Culture = "en", Name = "Kinshasa", Abbreviation = "FIH" } }
        });
        _db.BorderPoints.Add(new BorderPoint
        {
            Id = 1, ExternalId = "FIH", SortOrder = 2,
            Cultures = { new BorderPointCulture { Culture = "fr", Name = "FIH" }, new BorderPointCulture { Culture = "en", Name = "FIH" } }
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetCountries_ReturnsNameAndRegionCode()
    {
        var countries = await _service.GetCountriesAsync();

        var cd = Assert.Single(countries);
        Assert.Equal("CD", cd.Alpha2);
        Assert.Equal("CONGO, RDC", cd.Fr);
        Assert.Equal("CA", cd.RegionCode);
    }

    [Fact]
    public async Task CreateCountry_ResolvesRegionLookup()
    {
        var id = await _service.CreateCountryAsync("FR", "FRA", "CA", 1000, "FRANCE", "FRANCE");

        var created = await _db.Countries.SingleAsync(c => c.Id == id);
        Assert.Equal(1, created.RegionLookupId);
        Assert.Contains(_db.AuditLogs, a => a.EventType == AuditEventTypes.RowAdded && a.Entity == nameof(Country));
    }

    [Fact]
    public async Task CreateCountry_UnknownRegionCode_StoresNull()
    {
        var id = await _service.CreateCountryAsync("BE", "BEL", "ZZ", 0, "BELGIQUE", "BELGIUM");
        Assert.Null((await _db.Countries.SingleAsync(c => c.Id == id)).RegionLookupId);
    }

    [Fact]
    public async Task RevokeCountry_WithActiveCity_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.SetCountryRevokedAsync(1, true));
        Assert.Equal("Admin_Error_CountryInUse", ex.ErrorKey);
    }

    [Fact]
    public async Task RevokeCountry_WithoutCities_Succeeds()
    {
        var id = await _service.CreateCountryAsync("FR", "FRA", null, 0, "FRANCE", "FRANCE");
        await _service.SetCountryRevokedAsync(id, true);

        var country = await _db.Countries.SingleAsync(c => c.Id == id);
        Assert.True(country.Revoke);
        Assert.NotNull(country.RevokeDate);
    }

    [Fact]
    public async Task UpdateCountry_ChangesNamesAndCodes()
    {
        await _service.UpdateCountryAsync(1, "CD", "COD", "CA", 5, "CONGO (RDC)", "CONGO (DRC)");

        var country = await _db.Countries.Include(c => c.Cultures).SingleAsync(c => c.Id == 1);
        Assert.Equal("CONGO (RDC)", country.Cultures.Single(c => c.Culture == "fr").Name);
        Assert.Equal(5, country.SortOrder);
        Assert.Equal(1, country.RegionLookupId);
    }

    [Fact]
    public async Task CreateProvince_RequiresExistingCountry()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateProvinceAsync(999, "KN", 0, "Kinshasa", "", null));
        Assert.Equal("Admin_Error_CountryRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task CreateAndListProvince_WithAbbreviation()
    {
        var id = await _service.CreateProvinceAsync(1, "KN", 0, "Kinshasa", "Kinshasa", "KN");

        var provinces = await _service.GetProvincesAsync();
        var province = Assert.Single(provinces);
        Assert.Equal(id, province.Id);
        Assert.Equal("Kinshasa", province.Fr);
        Assert.Equal("KN", province.Abbreviation);
        Assert.Equal("CONGO, RDC", province.CountryFr);
    }

    [Fact]
    public async Task CreateCity_RequiresExistingCountry()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateCityAsync(999, null, "XXX", 0, "Ville", "", null));
        Assert.Equal("Admin_Error_CountryRequired", ex.ErrorKey);
    }

    [Fact]
    public async Task GetCities_SearchMatchesCodeOrNames()
    {
        Assert.Single(await _service.GetCitiesAsync(search: "Kinshasa"));
        Assert.Single(await _service.GetCitiesAsync(search: "FIH"));
        Assert.Empty(await _service.GetCitiesAsync(search: "Paris"));
    }

    [Fact]
    public async Task CreateBorderPoint_DuplicateCode_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _service.CreateBorderPointAsync("FIH", 0, "FIH bis", "", null, null));
        Assert.Equal("Admin_Error_DuplicateCode", ex.ErrorKey);
    }

    [Fact]
    public async Task CreateAndUpdateBorderPoint_RoundTrips()
    {
        var id = await _service.CreateBorderPointAsync("GBG", 11, "Goma", "Goma", "Nord-Kivu", "North Kivu");

        await _service.UpdateBorderPointAsync(id, "GBG", 12, "GOMA", "GOMA", "NORD-KIVU", "NORTH KIVU");
        var points = await _service.GetBorderPointsAsync();
        var point = points.Single(p => p.Id == id);
        Assert.Equal("GOMA", point.Fr);
        Assert.Equal("NORD-KIVU", point.RegionalSiteFr);
        Assert.Equal(12, point.SortOrder);
    }

    [Fact]
    public async Task RevokeBorderPoint_AssignedToActiveUser_IsRejected()
    {
        _db.Users.Add(new User { Id = "U001", UserName = "officer", PasswordHash = "x", UserRoleId = 1, BorderPointId = 1 });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _service.SetBorderPointRevokedAsync(1, true));
        Assert.Equal("Admin_Error_BorderPointInUse", ex.ErrorKey);
    }

    public void Dispose() => _db.Dispose();
}
