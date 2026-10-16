using BorderNext.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BorderNext.Infrastructure.Persistence;

/// <summary>Contexte EF Core — schéma cible normalisé (Lot 0 : sécurité, configuration, audit).</summary>
public class BorderNextDbContext : DbContext
{
    public BorderNextDbContext(DbContextOptions<BorderNextDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserRoleCulture> UserRoleCultures => Set<UserRoleCulture>();
    public DbSet<BorderPoint> BorderPoints => Set<BorderPoint>();
    public DbSet<BorderPointCulture> BorderPointCultures => Set<BorderPointCulture>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Lot 1 — référentiels et administration.
    public DbSet<LookupType> LookupTypes => Set<LookupType>();
    public DbSet<Lookup> Lookups => Set<Lookup>();
    public DbSet<LookupCulture> LookupCultures => Set<LookupCulture>();
    public DbSet<Control> Controls => Set<Control>();
    public DbSet<ControlCulture> ControlCultures => Set<ControlCulture>();
    public DbSet<ControlToRole> ControlsToRoles => Set<ControlToRole>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<CountryCulture> CountryCultures => Set<CountryCulture>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<ProvinceCulture> ProvinceCultures => Set<ProvinceCulture>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<CityCulture> CityCultures => Set<CityCulture>();

    // Lot 2 — dossier frontalier (passeports, mouvements, visas).
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<TravelDocument> TravelDocuments => Set<TravelDocument>();
    public DbSet<DocumentImage> DocumentImages => Set<DocumentImage>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<MovementChild> MovementChildren => Set<MovementChild>();
    public DbSet<Visa> Visas => Set<Visa>();
    public DbSet<VisaChild> VisaChildren => Set<VisaChild>();

    // Lot 3 — biométrie (empreintes des voyageurs).
    public DbSet<Fingerprint> Fingerprints => Set<Fingerprint>();

    // Lot 4 — permis et enforcement.
    public DbSet<Permit> Permits => Set<Permit>();
    public DbSet<PermitDetails> PermitDetails => Set<PermitDetails>();
    public DbSet<PermitContact> PermitContacts => Set<PermitContact>();
    public DbSet<PermitDriverLicense> PermitDriverLicenses => Set<PermitDriverLicense>();
    public DbSet<PermitSecurityRecord> PermitSecurityRecords => Set<PermitSecurityRecord>();
    public DbSet<PermitSelfEmployed> PermitSelfEmployed => Set<PermitSelfEmployed>();
    public DbSet<PermitTravelDetails> PermitTravelDetails => Set<PermitTravelDetails>();
    public DbSet<PermitDependant> PermitDependants => Set<PermitDependant>();
    public DbSet<RemovalOrder> RemovalOrders => Set<RemovalOrder>();
    public DbSet<PenaltyTicket> PenaltyTickets => Set<PenaltyTicket>();

    // Lot 5 — listes d'alerte et personnes douteuses
    public DbSet<PersonStopList> PersonsStopList => Set<PersonStopList>();
    public DbSet<DocumentStopList> DocumentsStopList => Set<DocumentStopList>();
    public DbSet<StopListFingerprint> StopListFingerprints => Set<StopListFingerprint>();
    public DbSet<DoubtablePerson> DoubtablePersons => Set<DoubtablePerson>();

    // Lot 6 — opérations frontière
    public DbSet<Carrier> Carriers => Set<Carrier>();
    public DbSet<CarrierCulture> CarrierCultures => Set<CarrierCulture>();
    public DbSet<Terminal> Terminals => Set<Terminal>();
    public DbSet<TerminalCulture> TerminalCultures => Set<TerminalCulture>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripCrossing> TripCrossings => Set<TripCrossing>();
    public DbSet<Vessel> Vessels => Set<Vessel>();

    // Lot 7 — échanges (messages APIS, passagers, fichiers, journal d'échanges).
    public DbSet<ApisMessage> ApisMessages => Set<ApisMessage>();
    public DbSet<ApisMessageFile> ApisMessageFiles => Set<ApisMessageFile>();
    public DbSet<ApisPassenger> ApisPassengers => Set<ApisPassenger>();
    public DbSet<ApisDoubtfulPerson> ApisDoubtfulPersons => Set<ApisDoubtfulPerson>();
    public DbSet<ExchangeTask> ExchangeTasks => Set<ExchangeTask>();
    public DbSet<ExchangeRun> ExchangeRuns => Set<ExchangeRun>();
    public DbSet<ExchangeError> ExchangeErrors => Set<ExchangeError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("lUserRole");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.SortOrder).HasColumnType("smallint");
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<UserRoleCulture>(e =>
        {
            e.ToTable("lUserRoleCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(10).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.OwnerId, x.Culture }).IsUnique();
            e.HasOne(x => x.Owner).WithMany(o => o.Cultures).HasForeignKey(x => x.OwnerId);
        });

        modelBuilder.Entity<BorderPoint>(e =>
        {
            e.ToTable("lBorderPoints");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
            e.Property(x => x.SortOrder).HasColumnType("smallint");
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
        });

        modelBuilder.Entity<BorderPointCulture>(e =>
        {
            e.ToTable("lBorderPointsCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.RegionalSite).HasMaxLength(100);
            e.HasIndex(x => new { x.OwnerId, x.Culture }).IsUnique();
            e.HasOne(x => x.Owner).WithMany(o => o.Cultures).HasForeignKey(x => x.OwnerId);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(20);
            e.Property(x => x.UserName).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(400).IsRequired();
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.MustChangePassword).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.LastLoginDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.UserRoleId).HasColumnType("smallint");
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            // Suppression logique : pas de cascade sur les rôles/points frontières.
            e.HasOne(x => x.UserRole).WithMany(r => r.Users).HasForeignKey(x => x.UserRoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany(b => b.Users).HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(100).IsRequired();
            e.Property(x => x.UserId).HasMaxLength(20);
            e.Property(x => x.MachineName).HasMaxLength(100);
            e.Property(x => x.OwnerType).HasMaxLength(10);
            e.Property(x => x.StringValue).HasMaxLength(510);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.ShortDescription).HasMaxLength(100);
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.DateTimeValue).HasColumnType("datetime");
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasIndex(x => new { x.Type, x.BorderPointId, x.UserId });
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.UserId).HasMaxLength(20);
            e.Property(x => x.MachineName).HasMaxLength(100);
            e.Property(x => x.PcNumber).HasMaxLength(20);
            e.Property(x => x.Entity).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Details).HasMaxLength(2000);
            e.Property(x => x.EventTime).HasColumnType("datetime");
            e.HasIndex(x => new { x.EventType, x.EventTime });
            e.HasIndex(x => x.UserId);
        });

        // ---- Lot 1 : nomenclatures génériques ----------------------------------
        modelBuilder.Entity<LookupType>(e =>
        {
            e.ToTable("LookupTypes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.NameFr).HasMaxLength(200).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
        });

        modelBuilder.Entity<Lookup>(e =>
        {
            e.ToTable("Lookups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(100);
            e.HasIndex(x => new { x.LookupTypeId, x.Code });
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Type).WithMany(t => t.Items).HasForeignKey(x => x.LookupTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LookupCulture>(e =>
        {
            e.ToTable("LookupCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(10).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.OwnerId, x.Culture }).IsUnique();
            e.HasOne(x => x.Owner).WithMany(o => o.Cultures).HasForeignKey(x => x.OwnerId);
        });

        // ---- Lot 1 : contrôles et droits --------------------------------------
        modelBuilder.Entity<Control>(e =>
        {
            e.ToTable("Controls");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
        });

        modelBuilder.Entity<ControlCulture>(e =>
        {
            e.ToTable("ControlCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(10).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.OwnerId, x.Culture }).IsUnique();
            e.HasOne(x => x.Owner).WithMany(o => o.Cultures).HasForeignKey(x => x.OwnerId);
        });

        modelBuilder.Entity<ControlToRole>(e =>
        {
            e.ToTable("ControlsToRoles");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserRoleId, x.ControlId, x.BorderPointId });
            e.Property(x => x.UserRoleId).HasColumnType("smallint");
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.Property(x => x.Permission).HasColumnType("bit");
            e.Property(x => x.Visibility).HasColumnType("bit");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.UserRole).WithMany().HasForeignKey(x => x.UserRoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Control).WithMany(c => c.RoleMappings).HasForeignKey(x => x.ControlId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- Lot 1 : géographie ------------------------------------------------
        modelBuilder.Entity<Country>(e =>
        {
            e.ToTable("Countries");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalId).HasMaxLength(20);
            e.Property(x => x.Alpha2).HasMaxLength(2);
            e.Property(x => x.Alpha3).HasMaxLength(3);
            e.HasIndex(x => x.Alpha2);
            e.HasIndex(x => x.Alpha3);
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Region).WithMany().HasForeignKey(x => x.RegionLookupId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CountryCulture>(e =>
        {
            e.ToTable("CountryCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.OwnerId, x.Culture }).IsUnique();
            e.HasOne(x => x.Owner).WithMany(o => o.Cultures).HasForeignKey(x => x.OwnerId);
        });

        modelBuilder.Entity<Province>(e =>
        {
            e.ToTable("Provinces");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalId).HasMaxLength(20);
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProvinceCulture>(e =>
        {
            e.ToTable("ProvinceCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Abbreviation).HasMaxLength(20);
            e.HasIndex(x => new { x.OwnerId, x.Culture }).IsUnique();
            e.HasOne(x => x.Owner).WithMany(o => o.Cultures).HasForeignKey(x => x.OwnerId);
        });

        modelBuilder.Entity<City>(e =>
        {
            e.ToTable("Cities");
            e.HasKey(x => x.Id);
            e.Property(x => x.ExternalId).HasMaxLength(20);
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Province).WithMany().HasForeignKey(x => x.ProvinceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CityCulture>(e =>
        {
            e.ToTable("CityCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(10).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Abbreviation).HasMaxLength(20);
            e.HasIndex(x => new { x.OwnerId, x.Culture }).IsUnique();
            e.HasOne(x => x.Owner).WithMany(o => o.Cultures).HasForeignKey(x => x.OwnerId);
        });

        // ---- Lot 2 : dossier frontalier ---------------------------------------
        modelBuilder.Entity<Person>(e =>
        {
            e.ToTable("Persons");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(40);
            e.HasIndex(x => x.LegacyId);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TravelDocument>(e =>
        {
            e.ToTable("TravelDocuments");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.HasIndex(x => x.LegacyId);
            e.Property(x => x.DocumentNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Surname).HasMaxLength(80);
            e.Property(x => x.GivenName).HasMaxLength(80);
            e.Property(x => x.FatherName).HasMaxLength(80);
            e.Property(x => x.PersonalNumber).HasMaxLength(40);
            e.Property(x => x.IssueDate).HasColumnType("datetime");
            e.Property(x => x.ExpireDate).HasColumnType("datetime");
            e.Property(x => x.DateOfBirth).HasColumnType("datetime");
            e.Property(x => x.CreateDate).HasColumnType("datetime");
            e.Property(x => x.CreatorUser).HasMaxLength(100);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasIndex(x => new { x.DocumentTypeId, x.DocumentNumber });
            e.HasIndex(x => new { x.Surname, x.GivenName });
            e.HasIndex(x => x.DateOfBirth);
            e.HasOne(x => x.Person).WithMany(p => p.Documents).HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DocumentType).WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CountryOfIssue).WithMany().HasForeignKey(x => x.CountryOfIssueId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Sex).WithMany().HasForeignKey(x => x.SexId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Citizenship).WithMany().HasForeignKey(x => x.CitizenshipId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentImage>(e =>
        {
            e.ToTable("DocumentImages");
            e.HasKey(x => x.DocumentId);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Document).WithOne(d => d.Image).HasForeignKey<DocumentImage>(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Movement>(e =>
        {
            e.ToTable("Movements");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.HasIndex(x => x.LegacyId);
            e.Property(x => x.TravelDate).HasColumnType("datetime");
            e.Property(x => x.RegistrationTime).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Occupation).HasMaxLength(100);
            e.Property(x => x.AccommodationAddress).HasMaxLength(200);
            e.Property(x => x.DurationOfStay).HasColumnType("smallint");
            e.Property(x => x.Inspector).HasMaxLength(100);
            e.Property(x => x.PcNumber).HasMaxLength(20);
            e.Property(x => x.TransportCrossBorderId).HasMaxLength(20);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasIndex(x => new { x.TravelDate, x.DirectionLookupId });
            e.HasIndex(x => x.PersonId);
            e.HasIndex(x => x.DocumentId);
            e.HasOne(x => x.Person).WithMany(p => p.Movements).HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Document).WithMany(d => d.Movements).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Direction).WithMany().HasForeignKey(x => x.DirectionLookupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TransportType).WithMany().HasForeignKey(x => x.TransportTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TravelReason).WithMany().HasForeignKey(x => x.TravelReasonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeportationReason).WithMany().HasForeignKey(x => x.DeportationReasonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RefusalJustification).WithMany().HasForeignKey(x => x.RefusalJustificationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Visa).WithMany().HasForeignKey(x => x.VisaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ResidentCountry).WithMany().HasForeignKey(x => x.ResidentCountryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DestinationCountry).WithMany().HasForeignKey(x => x.DestinationCountryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AccommodationType).WithMany().HasForeignKey(x => x.AccommodationTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MovementChild>(e =>
        {
            e.ToTable("MovementChildren");
            e.HasKey(x => x.Id);
            e.Property(x => x.Surname).HasMaxLength(80);
            e.Property(x => x.GivenName).HasMaxLength(80);
            e.Property(x => x.DateOfBirth).HasColumnType("datetime");
            e.Property(x => x.DocumentNumber).HasMaxLength(40);
            e.Property(x => x.Attorney).HasMaxLength(200);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Movement).WithMany(m => m.Children).HasForeignKey(x => x.MovementId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Visa>(e =>
        {
            e.ToTable("Visas");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.HasIndex(x => x.LegacyId);
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number);
            e.Property(x => x.IssueDate).HasColumnType("datetime");
            e.Property(x => x.ExpireDate).HasColumnType("datetime");
            e.Property(x => x.Duration).HasColumnType("smallint");
            e.Property(x => x.ReferenceNumber).HasMaxLength(50);
            e.Property(x => x.ReceiptNumber).HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.ContactNumber).HasMaxLength(50);
            e.Property(x => x.Employer).HasMaxLength(100);
            e.Property(x => x.InvitedBy).HasMaxLength(500);
            e.Property(x => x.Occupation).HasMaxLength(150);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.VoucherNumber).HasMaxLength(40);
            e.Property(x => x.Barcode).HasMaxLength(20);
            e.Property(x => x.PcNumber).HasMaxLength(20);
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.CreateDate).HasColumnType("datetime");
            e.Property(x => x.CreatorUser).HasMaxLength(100);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasOne(x => x.VisaType).WithMany().HasForeignKey(x => x.VisaTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.VisaEntry).WithMany().HasForeignKey(x => x.VisaEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Province).WithMany().HasForeignKey(x => x.ProvinceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Document).WithMany(d => d.Visas).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.RelationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.UniversityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.PermitStatusId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.PeriodTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.PermitConditionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VisaChild>(e =>
        {
            e.ToTable("VisaChildren");
            e.HasKey(x => x.Id);
            e.Property(x => x.Surname).HasMaxLength(80);
            e.Property(x => x.GivenName).HasMaxLength(80);
            e.Property(x => x.DateOfBirth).HasColumnType("datetime");
            e.Property(x => x.DocumentNumber).HasMaxLength(40);
            e.Property(x => x.IsDeleted).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasOne(x => x.Visa).WithMany(v => v.Children).HasForeignKey(x => x.VisaId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Lot 3 : biométrie ------------------------------------------------
        modelBuilder.Entity<Fingerprint>(e =>
        {
            e.ToTable("Fingerprints");
            e.HasKey(x => new { x.PersonId, x.FingerNumber });
            e.Property(x => x.FingerNumber).HasColumnType("smallint");
            e.Property(x => x.CaptureDate).HasColumnType("datetime");
            e.Property(x => x.CaptureDevice).HasMaxLength(20);
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasOne(x => x.Person).WithMany(p => p.Fingerprints).HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Lot 4 : permis et enforcement ------------------------------------
        modelBuilder.Entity<Permit>(e =>
        {
            e.ToTable("Permits");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.HasIndex(x => x.LegacyId);
            e.Property(x => x.Number).HasMaxLength(20);
            e.HasIndex(x => x.Number);
            e.Property(x => x.IssueDate).HasColumnType("datetime");
            e.Property(x => x.ExpireDate).HasColumnType("datetime");
            e.Property(x => x.DocumentIssuingAuthority).HasMaxLength(300);
            e.Property(x => x.OccupationOther).HasMaxLength(800);
            e.Property(x => x.Note).HasMaxLength(400);
            e.Property(x => x.PcNumber).HasMaxLength(20);
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.CreateDate).HasColumnType("datetime");
            e.Property(x => x.CreatorUser).HasMaxLength(100);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasIndex(x => x.DocumentId);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Visa).WithMany().HasForeignKey(x => x.VisaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PermitType).WithMany().HasForeignKey(x => x.PermitTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MaritalStatus).WithMany().HasForeignKey(x => x.MaritalStatusId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Occupation).WithMany().HasForeignKey(x => x.OccupationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Qualification).WithMany().HasForeignKey(x => x.QualificationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.CourseOfStudyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.BloodTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PermitDetails>(e =>
        {
            e.ToTable("PermitDetails");
            e.HasKey(x => x.PermitId);
            e.Property(x => x.CurrentTown).HasMaxLength(200);
            e.Property(x => x.CurrentAddress).HasMaxLength(300);
            e.Property(x => x.CurrentPhoneNumber).HasMaxLength(50);
            e.Property(x => x.PreviousTown).HasMaxLength(200);
            e.Property(x => x.PreviousAddress).HasMaxLength(300);
            e.Property(x => x.PreviousPostalCode).HasMaxLength(20);
            e.Property(x => x.PlaceOfBirthTown).HasMaxLength(200);
            e.Property(x => x.CurrentPermitNumber).HasMaxLength(20);
            e.Property(x => x.ReferencePersonName).HasMaxLength(200);
            e.Property(x => x.ReferencePersonPhone).HasMaxLength(50);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Permit).WithOne(p => p.Details).HasForeignKey<PermitDetails>(x => x.PermitId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermitContact>(e =>
        {
            e.ToTable("PermitContacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(300);
            e.HasOne(x => x.Permit).WithMany(p => p.Contacts).HasForeignKey(x => x.PermitId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermitDriverLicense>(e =>
        {
            e.ToTable("PermitDriverLicenses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(40);
            e.HasOne(x => x.Permit).WithMany(p => p.DriverLicenses).HasForeignKey(x => x.PermitId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermitSecurityRecord>(e =>
        {
            e.ToTable("PermitSecurityRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.RecordType).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne(x => x.Permit).WithMany(p => p.SecurityRecords).HasForeignKey(x => x.PermitId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermitSelfEmployed>(e =>
        {
            e.ToTable("PermitSelfEmployed");
            e.HasKey(x => x.PermitId);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Permit).WithOne(p => p.SelfEmployed).HasForeignKey<PermitSelfEmployed>(x => x.PermitId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.IndustryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.EmployeeRangeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PermitTravelDetails>(e =>
        {
            e.ToTable("PermitTravelDetails");
            e.HasKey(x => x.PermitId);
            e.Property(x => x.DepartureTown).HasMaxLength(200);
            e.Property(x => x.DepartureDate).HasColumnType("datetime");
            e.Property(x => x.ArrivalDate).HasColumnType("datetime");
            e.Property(x => x.PortOfEntryId).HasColumnType("smallint");
            e.HasOne(x => x.Permit).WithOne(p => p.TravelDetails).HasForeignKey<PermitTravelDetails>(x => x.PermitId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.TravelReasonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.TransportTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Country>().WithMany().HasForeignKey(x => x.DepartureCountryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Province>().WithMany().HasForeignKey(x => x.DepartureProvinceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BorderPoint>().WithMany().HasForeignKey(x => x.PortOfEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PermitDependant>(e =>
        {
            e.ToTable("PermitDependants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Surname).HasMaxLength(80);
            e.Property(x => x.GivenName).HasMaxLength(80);
            e.Property(x => x.OtherNames).HasMaxLength(150);
            e.Property(x => x.DateOfBirth).HasColumnType("datetime");
            e.Property(x => x.DocumentNumber).HasMaxLength(40);
            e.Property(x => x.PhoneNumber).HasMaxLength(50);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.HasOne(x => x.Permit).WithMany(p => p.Dependants).HasForeignKey(x => x.PermitId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.RelationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lookup>().WithMany().HasForeignKey(x => x.SexId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Country>().WithMany().HasForeignKey(x => x.CountryOfBirthId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Country>().WithMany().HasForeignKey(x => x.CitizenshipId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RemovalOrder>(e =>
        {
            e.ToTable("RemovalOrders");
            e.HasKey(x => x.Id);
            e.Property(x => x.DocNumber).HasMaxLength(40);
            e.Property(x => x.Info).HasMaxLength(1000);
            e.Property(x => x.OfficerName).HasMaxLength(100);
            e.Property(x => x.CreateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(100);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.HasIndex(x => x.PersonId);
            e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Movement).WithMany().HasForeignKey(x => x.MovementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Type).WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PenaltyTicket>(e =>
        {
            e.ToTable("PenaltyTickets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.DocumentNumber).HasMaxLength(40);
            e.Property(x => x.Surname).HasMaxLength(80);
            e.Property(x => x.GivenName).HasMaxLength(80);
            e.Property(x => x.VisaNumber).HasMaxLength(20);
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.CurrencyCode).HasMaxLength(3);
            e.Property(x => x.ReceiptNumber).HasMaxLength(40);
            e.Property(x => x.DocumentExpireDate).HasColumnType("datetime");
            e.Property(x => x.DocumentIssueDate).HasColumnType("datetime");
            e.Property(x => x.VisaIssueDate).HasColumnType("datetime");
            e.Property(x => x.VisaExpireDate).HasColumnType("datetime");
            e.Property(x => x.CreateDate).HasColumnType("datetime");
            e.Property(x => x.UserId).HasMaxLength(20);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.PaidDate).HasColumnType("datetime");
            e.Property(x => x.Revoke).HasDefaultValue(false).HasColumnType("bit");
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasIndex(x => x.MovementId);
            e.HasIndex(x => x.Number);
            e.HasOne(x => x.Movement).WithMany().HasForeignKey(x => x.MovementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PenaltyReason).WithMany().HasForeignKey(x => x.PenaltyReasonId).OnDelete(DeleteBehavior.Restrict);
        });

        // -------------------------------------------------------------------
        // Lot 5 — listes d'alerte (héritage : PersonsStopList, DocumentsStopList,
        // StopListFingerprints) et personnes douteuses (DoubtablePersons).
        // -------------------------------------------------------------------

        modelBuilder.Entity<PersonStopList>(e =>
        {
            e.ToTable("PersonsStopList");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.Property(x => x.LegacyOldId).HasMaxLength(25);
            e.Property(x => x.Surname).HasMaxLength(100);
            e.Property(x => x.GivenName).HasMaxLength(100);
            e.Property(x => x.FathersName).HasMaxLength(100);
            e.Property(x => x.SurnameAlt).HasMaxLength(100);
            e.Property(x => x.GivenNameAlt).HasMaxLength(100);
            e.Property(x => x.FathersNameAlt).HasMaxLength(100);
            e.Property(x => x.DateOfBirth).HasColumnType("datetime");
            e.Property(x => x.WhoSetUp).HasMaxLength(255);
            e.Property(x => x.SetUpDate).HasColumnType("datetime");
            e.Property(x => x.CancelDate).HasColumnType("datetime");
            e.Property(x => x.SourceDocument).HasMaxLength(255);
            e.Property(x => x.Info).HasMaxLength(255);
            e.Property(x => x.DocumentNumber).HasMaxLength(40);
            e.Property(x => x.PersonalNumber).HasMaxLength(40);
            e.Property(x => x.ActionRequired).HasMaxLength(255);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.LastUpdateUser).HasMaxLength(100);
            e.Property(x => x.TimeStamp).HasColumnType("datetime");
            e.Property(x => x.CancelSourceDocument).HasMaxLength(255);
            e.Property(x => x.IsAuthorized).HasColumnType("bit");
            e.Ignore(x => x.IsActive);
            e.Ignore(x => x.CitizenshipName);
            e.HasIndex(x => x.PersonalNumber);
            e.HasIndex(x => x.DocumentNumber);
            e.HasOne(x => x.Citizenship).WithMany().HasForeignKey(x => x.CitizenshipId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.StopListSource).WithMany().HasForeignKey(x => x.StopListSourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MatchInStopListAction).WithMany().HasForeignKey(x => x.MatchInStopListActionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CancelStopListSource).WithMany().HasForeignKey(x => x.CancelStopListSourceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentStopList>(e =>
        {
            e.ToTable("DocumentsStopList");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.Property(x => x.LegacyOldId).HasMaxLength(20);
            e.Property(x => x.DocumentNumber).HasMaxLength(40);
            e.Property(x => x.PersonalNumber).HasMaxLength(40);
            e.Property(x => x.Surname).HasMaxLength(100);
            e.Property(x => x.GivenName).HasMaxLength(100);
            e.Property(x => x.FathersName).HasMaxLength(100);
            e.Property(x => x.DateOfBirth).HasColumnType("datetime");
            e.Property(x => x.SetUpDate).HasColumnType("datetime");
            e.Property(x => x.CancelDate).HasColumnType("datetime");
            e.Property(x => x.SourceDocument).HasMaxLength(100);
            e.Property(x => x.Info).HasMaxLength(255);
            e.Property(x => x.ActionRequired).HasMaxLength(255);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.LastUpdateUser).HasMaxLength(100);
            e.Property(x => x.TimeStamp).HasColumnType("datetime");
            e.Property(x => x.CancelSourceDocument).HasMaxLength(100);
            e.Ignore(x => x.IsActive);
            e.Ignore(x => x.CountryName);
            e.HasIndex(x => x.DocumentNumber);
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.StopListSource).WithMany().HasForeignKey(x => x.StopListSourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MatchInStopListAction).WithMany().HasForeignKey(x => x.MatchInStopListActionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CancelStopListSource).WithMany().HasForeignKey(x => x.CancelStopListSourceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StopListFingerprint>(e =>
        {
            e.ToTable("StopListFingerprints");
            e.HasKey(x => new { x.PersonStopListId, x.FingerNumber });
            e.Property(x => x.FingerNumber).HasColumnType("smallint");
            e.HasOne(x => x.PersonStopList).WithMany(p => p.Fingerprints)
                .HasForeignKey(x => x.PersonStopListId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DoubtablePerson>(e =>
        {
            e.ToTable("DoubtablePersons");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.Property(x => x.Supervisor).HasMaxLength(100);
            e.Property(x => x.SupervisorUserId).HasMaxLength(20);
            e.Property(x => x.SupervisorRegistrationTime).HasColumnType("datetime");
            e.Property(x => x.SupervisorPcNumber).HasMaxLength(20);
            e.Property(x => x.WorkflowStatus).HasColumnType("smallint");
            e.Property(x => x.InspectorNote).HasMaxLength(255);
            e.Property(x => x.SupervisorNote).HasMaxLength(255);
            e.Property(x => x.RefusalOfficerName).HasMaxLength(255);
            e.Property(x => x.RefusalOfficerRank).HasMaxLength(100);
            e.Property(x => x.RefusalDocNumber).HasMaxLength(100);
            e.Property(x => x.RefusalJustificationOther).HasMaxLength(255);
            e.Property(x => x.RefusalCreateDate).HasColumnType("datetime");
            e.Property(x => x.CreateDate).HasColumnType("datetime");
            e.Property(x => x.CreatedByUserId).HasMaxLength(20);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.HasIndex(x => x.MovementId);
            e.HasIndex(x => x.WorkflowStatus);
            e.HasOne(x => x.Movement).WithMany().HasForeignKey(x => x.MovementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PersonStopList).WithMany().HasForeignKey(x => x.PersonStopListId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DocumentStopList).WithMany().HasForeignKey(x => x.DocumentStopListId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CheckingReason).WithMany().HasForeignKey(x => x.CheckingReasonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RefusalCountry).WithMany().HasForeignKey(x => x.RefusalCountryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RefusalJustification).WithMany().HasForeignKey(x => x.RefusalJustificationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        // -------------------------------------------------------------------
        // Lot 6 — opérations frontière (héritage : lCarriers, lTerminals,
        // Transports, TransportCrossedBorder + TransportCrossInfo, Vessels).
        // -------------------------------------------------------------------

        modelBuilder.Entity<Carrier>(e =>
        {
            e.ToTable("Carriers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Ignore(x => x.DisplayName);
            e.HasIndex(x => x.Code);
            e.HasOne(x => x.TransportType).WithMany().HasForeignKey(x => x.TransportTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CarrierCulture>(e =>
        {
            e.ToTable("CarrierCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(5).IsRequired();
            e.Property(x => x.Description).HasMaxLength(150).IsRequired();
            e.HasIndex(x => new { x.OwnerId, x.Culture });
            e.HasOne<Carrier>().WithMany(c => c.Cultures).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Terminal>(e =>
        {
            e.ToTable("Terminals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.RevokeDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Ignore(x => x.DisplayName);
            e.HasIndex(x => x.Code);
            e.HasOne(x => x.TransportType).WithMany().HasForeignKey(x => x.TransportTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Country).WithMany().HasForeignKey(x => x.CountryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.City).WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TerminalCulture>(e =>
        {
            e.ToTable("TerminalCultures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(5).IsRequired();
            e.Property(x => x.Description).HasMaxLength(150).IsRequired();
            e.HasIndex(x => new { x.OwnerId, x.Culture });
            e.HasOne<Terminal>().WithMany(t => t.Cultures).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Trip>(e =>
        {
            e.ToTable("Trips");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.ExternalNumber).HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.ScheduledTime).HasColumnType("time");
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.Property(x => x.LastUpdateUser).HasMaxLength(100);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.HasOne(x => x.TransportType).WithMany().HasForeignKey(x => x.TransportTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FromTerminal).WithMany().HasForeignKey(x => x.FromTerminalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToTerminal).WithMany().HasForeignKey(x => x.ToTerminalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Carrier).WithMany().HasForeignKey(x => x.CarrierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Terminal).WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TripCrossing>(e =>
        {
            e.ToTable("TripCrossings");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.Property(x => x.Date).HasColumnType("datetime");
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.Property(x => x.LastUpdateUser).HasMaxLength(100);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.IsOpen).HasColumnType("bit");
            e.Property(x => x.OpenTime).HasColumnType("time");
            e.Property(x => x.OpenUser).HasMaxLength(100);
            e.Property(x => x.CloseTime).HasColumnType("time");
            e.Property(x => x.CloseUser).HasMaxLength(100);
            e.HasIndex(x => x.TripId);
            e.HasIndex(x => x.IsOpen);
            e.HasOne(x => x.Trip).WithMany(t => t.Crossings).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Direction).WithMany().HasForeignKey(x => x.DirectionLookupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vessel>(e =>
        {
            e.ToTable("Vessels");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegacyId).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(250).IsRequired();
            e.Property(x => x.FormerName).HasMaxLength(250);
            e.Property(x => x.RegistrationNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Dimension).HasMaxLength(40);
            e.Property(x => x.Color).HasMaxLength(50);
            e.Property(x => x.RadioCallSign).HasMaxLength(100);
            e.Property(x => x.Frequencies).HasMaxLength(40);
            e.Property(x => x.RegistrationDate).HasColumnType("datetime");
            e.Property(x => x.LastUpdateUser).HasMaxLength(100);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.HasIndex(x => x.RegistrationNumber);
            e.HasOne(x => x.VesselType).WithMany().HasForeignKey(x => x.VesselTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.HomePortCountry).WithMany().HasForeignKey(x => x.HomePortCountryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.NationalityCountry).WithMany().HasForeignKey(x => x.NationalityCountryId).OnDelete(DeleteBehavior.Restrict);
        });

        // Rattachement des mouvements au passage de voyage (Lot 6).
        modelBuilder.Entity<Movement>()
            .HasOne(x => x.TripCrossing).WithMany(c => c.Movements)
            .HasForeignKey(x => x.TripCrossingId).OnDelete(DeleteBehavior.Restrict);

        // Rattachement de l'ordre de reconduite à l'entrée de liste d'alerte (Lot 5).
        modelBuilder.Entity<RemovalOrder>()
            .HasOne<PersonStopList>().WithMany().HasForeignKey(x => x.PersonStopListId).OnDelete(DeleteBehavior.Restrict);

        // -------------------------------------------------------------------
        // Lot 7 — échanges (héritage : APIMessage, APIPassenger, APIMessageFiles,
        // APIDoubtablePersons, sync.ImportExportManager / ExportHistory /
        // ImportExportErrorLog).
        // -------------------------------------------------------------------

        modelBuilder.Entity<ApisMessage>(e =>
        {
            e.ToTable("ApisMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.FlightNumber).HasMaxLength(20);
            e.Property(x => x.DepartureLocation).HasMaxLength(50);
            e.Property(x => x.ArrivalLocation).HasMaxLength(50);
            e.Property(x => x.DepartureDate).HasColumnType("datetime");
            e.Property(x => x.ArrivalDate).HasColumnType("datetime");
            e.Property(x => x.SourceFormat).HasMaxLength(20).IsRequired();
            e.Property(x => x.ControlReference).HasMaxLength(30);
            e.Property(x => x.SenderId).HasMaxLength(50);
            e.Property(x => x.RecipientId).HasMaxLength(50);
            e.Property(x => x.CarrierName).HasMaxLength(150);
            e.Property(x => x.BorderPointId).HasColumnType("smallint");
            e.Property(x => x.CreatedDate).HasColumnType("datetime");
            e.Property(x => x.CreatedByUserId).HasMaxLength(50);
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(50);
            e.Ignore(x => x.FlightLabel);
            e.HasIndex(x => x.CreatedDate);
            e.HasIndex(x => x.ArrivalDate);
            e.HasIndex(x => x.FlightNumber);
            e.HasOne(x => x.BorderPoint).WithMany().HasForeignKey(x => x.BorderPointId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApisMessageFile>(e =>
        {
            e.ToTable("ApisMessageFiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.FilePath).HasMaxLength(250).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(150);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.CreatedDate).HasColumnType("datetime");
            e.HasIndex(x => x.ContentHash);
            e.HasOne(x => x.ApisMessage).WithMany(m => m.Files).HasForeignKey(x => x.ApisMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApisPassenger>(e =>
        {
            e.ToTable("ApisPassengers");
            e.HasKey(x => x.Id);
            e.Property(x => x.PassportRegistrationId).HasMaxLength(50);
            e.Property(x => x.DocumentType).HasMaxLength(10);
            e.Property(x => x.DocumentNumber).HasMaxLength(50);
            e.Property(x => x.CountryOfIssue).HasMaxLength(50);
            e.Property(x => x.IssueDate).HasColumnType("datetime");
            e.Property(x => x.ExpireDate).HasColumnType("datetime");
            e.Property(x => x.Surname).HasMaxLength(50);
            e.Property(x => x.GivenName).HasMaxLength(50);
            e.Property(x => x.DateOfBirth).HasColumnType("datetime");
            e.Property(x => x.Citizenship).HasMaxLength(50);
            e.Property(x => x.Gender).HasMaxLength(10);
            e.Property(x => x.CountryOfResidence).HasMaxLength(50);
            e.Property(x => x.CheckingReason).HasMaxLength(30);
            e.Property(x => x.IsPassenger).HasColumnType("bit");
            e.Property(x => x.CreatedDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(50);
            e.Ignore(x => x.FullName);
            e.HasIndex(x => x.DocumentNumber);
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.ApisMessage).WithMany(m => m.Passengers).HasForeignKey(x => x.ApisMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MatchedDocument).WithMany().HasForeignKey(x => x.MatchedDocumentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MatchedMovement).WithMany().HasForeignKey(x => x.MatchedMovementId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApisDoubtfulPerson>(e =>
        {
            e.ToTable("ApisDoubtfulPersons");
            e.HasKey(x => x.Id);
            e.Property(x => x.CheckingReason).HasMaxLength(30);
            e.Property(x => x.SupervisorsNote).HasMaxLength(500);
            e.Property(x => x.Supervisor).HasMaxLength(50);
            e.Property(x => x.SupervisorPcNumber).HasMaxLength(50);
            e.Property(x => x.CreateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(50);
            e.HasIndex(x => x.ApisPassengerId);
            e.HasOne(x => x.ApisPassenger).WithMany(p => p.DoubtfulRecords).HasForeignKey(x => x.ApisPassengerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PerformAction).WithMany().HasForeignKey(x => x.PerformActionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeTask>(e =>
        {
            e.ToTable("ExchangeTasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Channel).HasMaxLength(20).IsRequired();
            e.Property(x => x.Direction).HasMaxLength(10).IsRequired();
            e.Property(x => x.Endpoint).HasMaxLength(400);
            e.Property(x => x.FileDirectory).HasMaxLength(400);
            e.Property(x => x.Parameters).HasMaxLength(1000);
            e.Property(x => x.Description).HasMaxLength(400);
            e.Property(x => x.CreatedDate).HasColumnType("datetime");
            e.Property(x => x.UpdateDate).HasColumnType("datetime");
            e.Property(x => x.UpdateUser).HasMaxLength(50);
            e.HasIndex(x => x.Channel);
        });

        modelBuilder.Entity<ExchangeRun>(e =>
        {
            e.ToTable("ExchangeRuns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasMaxLength(20).IsRequired();
            e.Property(x => x.Direction).HasMaxLength(10).IsRequired();
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.FilePath).HasMaxLength(400);
            e.Property(x => x.StartedAt).HasColumnType("datetime");
            e.Property(x => x.CompletedAt).HasColumnType("datetime");
            e.Property(x => x.DataFrom).HasColumnType("datetime");
            e.Property(x => x.DataTo).HasColumnType("datetime");
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.Property(x => x.UserId).HasMaxLength(50);
            e.HasIndex(x => x.StartedAt);
            e.HasOne(x => x.ExchangeTask).WithMany().HasForeignKey(x => x.ExchangeTaskId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExchangeError>(e =>
        {
            e.ToTable("ExchangeErrors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Step).HasMaxLength(100);
            e.Property(x => x.ItemReference).HasMaxLength(100);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.OccurredAt).HasColumnType("datetime");
            e.HasIndex(x => x.ExchangeRunId);
            e.HasOne(x => x.ExchangeRun).WithMany(r => r.Errors).HasForeignKey(x => x.ExchangeRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Référentiels de base (Lot 0 minimal pour un premier démarrage).
        modelBuilder.Entity<UserRole>().HasData(
            new UserRole { Id = 1, Code = "ADMIN", SortOrder = 1 },
            new UserRole { Id = 2, Code = "SHIFT_OFF", SortOrder = 2 },
            new UserRole { Id = 3, Code = "IMMIG_OFF", SortOrder = 3 },
            new UserRole { Id = 4, Code = "ALERT_OFF", SortOrder = 4 },
            new UserRole { Id = 5, Code = "VISA_OFF", SortOrder = 5 },
            new UserRole { Id = 6, Code = "SUPERUSER", SortOrder = 6 });

        modelBuilder.Entity<UserRoleCulture>().HasData(
            new UserRoleCulture { Id = 1, OwnerId = 1, Culture = "fr", Description = "Administrateur" },
            new UserRoleCulture { Id = 2, OwnerId = 2, Culture = "fr", Description = "Officier de quart" },
            new UserRoleCulture { Id = 3, OwnerId = 3, Culture = "fr", Description = "Officier d'immigration" },
            new UserRoleCulture { Id = 4, OwnerId = 4, Culture = "fr", Description = "Officier d'alerte" },
            new UserRoleCulture { Id = 5, OwnerId = 5, Culture = "fr", Description = "Officier des visas" },
            new UserRoleCulture { Id = 6, OwnerId = 6, Culture = "fr", Description = "Super-utilisateur" },
            new UserRoleCulture { Id = 7, OwnerId = 1, Culture = "en", Description = "Administrator" },
            new UserRoleCulture { Id = 8, OwnerId = 2, Culture = "en", Description = "Shift Officer" },
            new UserRoleCulture { Id = 9, OwnerId = 3, Culture = "en", Description = "Immigration Officer" },
            new UserRoleCulture { Id = 10, OwnerId = 4, Culture = "en", Description = "Alert Officer" },
            new UserRoleCulture { Id = 11, OwnerId = 5, Culture = "en", Description = "Visa Officer" },
            new UserRoleCulture { Id = 12, OwnerId = 6, Culture = "en", Description = "Super User" });
    }
}
