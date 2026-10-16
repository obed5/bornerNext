using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderNext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermitsAndEnforcement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RefusalJustificationId",
                table: "Movements",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PenaltyTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MovementId = table.Column<int>(type: "int", nullable: true),
                    PenaltyReasonId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DocumentExpireDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    DocumentIssueDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SexId = table.Column<int>(type: "int", nullable: true),
                    DocumentCountryOfIssueId = table.Column<int>(type: "int", nullable: true),
                    VisaNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VisaIssueDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    VisaExpireDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Duration = table.Column<int>(type: "int", nullable: true),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Paid = table.Column<bool>(type: "bit", nullable: false),
                    PaidDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Revoke = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokeDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenaltyTickets_Lookups_PenaltyReasonId",
                        column: x => x.PenaltyReasonId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PenaltyTickets_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Permits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    VisaId = table.Column<int>(type: "int", nullable: true),
                    PermitTypeId = table.Column<int>(type: "int", nullable: true),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ExpireDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    StatusId = table.Column<int>(type: "int", nullable: true),
                    MaritalStatusId = table.Column<int>(type: "int", nullable: true),
                    OccupationId = table.Column<int>(type: "int", nullable: true),
                    OccupationOther = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                    QualificationId = table.Column<int>(type: "int", nullable: true),
                    CourseOfStudyId = table.Column<int>(type: "int", nullable: true),
                    BloodTypeId = table.Column<int>(type: "int", nullable: true),
                    DocumentIssuingAuthority = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PcNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Revoke = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokeDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatorUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Permits_Lookups_BloodTypeId",
                        column: x => x.BloodTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Lookups_CourseOfStudyId",
                        column: x => x.CourseOfStudyId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Lookups_MaritalStatusId",
                        column: x => x.MaritalStatusId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Lookups_OccupationId",
                        column: x => x.OccupationId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Lookups_PermitTypeId",
                        column: x => x.PermitTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Lookups_QualificationId",
                        column: x => x.QualificationId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Lookups_StatusId",
                        column: x => x.StatusId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_TravelDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "TravelDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_Visas_VisaId",
                        column: x => x.VisaId,
                        principalTable: "Visas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Permits_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RemovalOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    MovementId = table.Column<int>(type: "int", nullable: true),
                    PersonStopListId = table.Column<int>(type: "int", nullable: true),
                    DocNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    Info = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OfficerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true),
                    Revoke = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokeDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemovalOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemovalOrders_Lookups_TypeId",
                        column: x => x.TypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RemovalOrders_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RemovalOrders_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PermitContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    ContactTypeId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitContacts_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermitDependants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    Surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    OtherNames = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime", nullable: true),
                    CountryOfBirthId = table.Column<int>(type: "int", nullable: true),
                    RelationId = table.Column<int>(type: "int", nullable: true),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CitizenshipId = table.Column<int>(type: "int", nullable: true),
                    SexId = table.Column<int>(type: "int", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitDependants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitDependants_Countries_CitizenshipId",
                        column: x => x.CitizenshipId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitDependants_Countries_CountryOfBirthId",
                        column: x => x.CountryOfBirthId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitDependants_Lookups_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitDependants_Lookups_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitDependants_Lookups_SexId",
                        column: x => x.SexId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitDependants_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermitDetails",
                columns: table => new
                {
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    CurrentCountryId = table.Column<int>(type: "int", nullable: true),
                    CurrentProvinceId = table.Column<int>(type: "int", nullable: true),
                    CurrentCityId = table.Column<int>(type: "int", nullable: true),
                    CurrentTown = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CurrentAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CurrentPhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrentLocalAreaId = table.Column<int>(type: "int", nullable: true),
                    PreviousCountryId = table.Column<int>(type: "int", nullable: true),
                    PreviousProvinceId = table.Column<int>(type: "int", nullable: true),
                    PreviousCityId = table.Column<int>(type: "int", nullable: true),
                    PreviousTown = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PreviousAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PreviousPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PreviousCitizenshipId = table.Column<int>(type: "int", nullable: true),
                    PlaceOfBirthCountryId = table.Column<int>(type: "int", nullable: true),
                    PlaceOfBirthProvinceId = table.Column<int>(type: "int", nullable: true),
                    PlaceOfBirthTown = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SecondCitizenshipId = table.Column<int>(type: "int", nullable: true),
                    CurrentPermitTypeId = table.Column<int>(type: "int", nullable: true),
                    CurrentPermitNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReferencePersonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReferencePersonPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PlaceOfResidenceTypeId = table.Column<int>(type: "int", nullable: true),
                    EthnicGroupId = table.Column<int>(type: "int", nullable: true),
                    NumberOfChildrenBornHere = table.Column<int>(type: "int", nullable: true),
                    NumberOfChildrenLivingHere = table.Column<int>(type: "int", nullable: true),
                    NumberOfPeopleLiveWith = table.Column<int>(type: "int", nullable: true),
                    IntendedToStayMore = table.Column<bool>(type: "bit", nullable: false),
                    HadFixJob = table.Column<bool>(type: "bit", nullable: false),
                    HasCitizenFamilyMember = table.Column<bool>(type: "bit", nullable: false),
                    HasChildrenBornHere = table.Column<bool>(type: "bit", nullable: false),
                    LivingWithOtherMigrants = table.Column<bool>(type: "bit", nullable: false),
                    ChildrenLivingInSameHouse = table.Column<bool>(type: "bit", nullable: false),
                    ChildrenEnrolledEducation = table.Column<bool>(type: "bit", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitDetails", x => x.PermitId);
                    table.ForeignKey(
                        name: "FK_PermitDetails_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermitDriverLicenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    CountryOfIssueId = table.Column<int>(type: "int", nullable: true),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitDriverLicenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitDriverLicenses_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermitSecurityRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    RecordType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitSecurityRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitSecurityRecords_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermitSelfEmployed",
                columns: table => new
                {
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProvinceId = table.Column<int>(type: "int", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IndustryId = table.Column<int>(type: "int", nullable: true),
                    EmployeeRangeId = table.Column<int>(type: "int", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitSelfEmployed", x => x.PermitId);
                    table.ForeignKey(
                        name: "FK_PermitSelfEmployed_Lookups_EmployeeRangeId",
                        column: x => x.EmployeeRangeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitSelfEmployed_Lookups_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitSelfEmployed_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermitTravelDetails",
                columns: table => new
                {
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    FreeWillTravel = table.Column<bool>(type: "bit", nullable: false),
                    TravelReasonId = table.Column<int>(type: "int", nullable: true),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    TransportTypeId = table.Column<int>(type: "int", nullable: true),
                    DepartureCountryId = table.Column<int>(type: "int", nullable: true),
                    DepartureProvinceId = table.Column<int>(type: "int", nullable: true),
                    DepartureTown = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DepartureDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ImmigrationCheck = table.Column<bool>(type: "bit", nullable: false),
                    PortOfEntryId = table.Column<short>(type: "smallint", nullable: true),
                    ArrivalDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    IrregularExit = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitTravelDetails", x => x.PermitId);
                    table.ForeignKey(
                        name: "FK_PermitTravelDetails_Countries_DepartureCountryId",
                        column: x => x.DepartureCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitTravelDetails_Lookups_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitTravelDetails_Lookups_TransportTypeId",
                        column: x => x.TransportTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitTravelDetails_Lookups_TravelReasonId",
                        column: x => x.TravelReasonId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitTravelDetails_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PermitTravelDetails_Provinces_DepartureProvinceId",
                        column: x => x.DepartureProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitTravelDetails_lBorderPoints_PortOfEntryId",
                        column: x => x.PortOfEntryId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_RefusalJustificationId",
                table: "Movements",
                column: "RefusalJustificationId");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyTickets_MovementId",
                table: "PenaltyTickets",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyTickets_Number",
                table: "PenaltyTickets",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyTickets_PenaltyReasonId",
                table: "PenaltyTickets",
                column: "PenaltyReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitContacts_PermitId",
                table: "PermitContacts",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitDependants_CitizenshipId",
                table: "PermitDependants",
                column: "CitizenshipId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitDependants_CountryOfBirthId",
                table: "PermitDependants",
                column: "CountryOfBirthId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitDependants_DocumentTypeId",
                table: "PermitDependants",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitDependants_PermitId",
                table: "PermitDependants",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitDependants_RelationId",
                table: "PermitDependants",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitDependants_SexId",
                table: "PermitDependants",
                column: "SexId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitDriverLicenses_PermitId",
                table: "PermitDriverLicenses",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_BloodTypeId",
                table: "Permits",
                column: "BloodTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_BorderPointId",
                table: "Permits",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_CourseOfStudyId",
                table: "Permits",
                column: "CourseOfStudyId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_DocumentId",
                table: "Permits",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_LegacyId",
                table: "Permits",
                column: "LegacyId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_MaritalStatusId",
                table: "Permits",
                column: "MaritalStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_Number",
                table: "Permits",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_OccupationId",
                table: "Permits",
                column: "OccupationId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_PermitTypeId",
                table: "Permits",
                column: "PermitTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_QualificationId",
                table: "Permits",
                column: "QualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_StatusId",
                table: "Permits",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_VisaId",
                table: "Permits",
                column: "VisaId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitSecurityRecords_PermitId",
                table: "PermitSecurityRecords",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitSelfEmployed_EmployeeRangeId",
                table: "PermitSelfEmployed",
                column: "EmployeeRangeId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitSelfEmployed_IndustryId",
                table: "PermitSelfEmployed",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTravelDetails_DepartureCountryId",
                table: "PermitTravelDetails",
                column: "DepartureCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTravelDetails_DepartureProvinceId",
                table: "PermitTravelDetails",
                column: "DepartureProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTravelDetails_DocumentTypeId",
                table: "PermitTravelDetails",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTravelDetails_PortOfEntryId",
                table: "PermitTravelDetails",
                column: "PortOfEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTravelDetails_TransportTypeId",
                table: "PermitTravelDetails",
                column: "TransportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitTravelDetails_TravelReasonId",
                table: "PermitTravelDetails",
                column: "TravelReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_RemovalOrders_MovementId",
                table: "RemovalOrders",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_RemovalOrders_PersonId",
                table: "RemovalOrders",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_RemovalOrders_TypeId",
                table: "RemovalOrders",
                column: "TypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_Lookups_RefusalJustificationId",
                table: "Movements",
                column: "RefusalJustificationId",
                principalTable: "Lookups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_Lookups_RefusalJustificationId",
                table: "Movements");

            migrationBuilder.DropTable(
                name: "PenaltyTickets");

            migrationBuilder.DropTable(
                name: "PermitContacts");

            migrationBuilder.DropTable(
                name: "PermitDependants");

            migrationBuilder.DropTable(
                name: "PermitDetails");

            migrationBuilder.DropTable(
                name: "PermitDriverLicenses");

            migrationBuilder.DropTable(
                name: "PermitSecurityRecords");

            migrationBuilder.DropTable(
                name: "PermitSelfEmployed");

            migrationBuilder.DropTable(
                name: "PermitTravelDetails");

            migrationBuilder.DropTable(
                name: "RemovalOrders");

            migrationBuilder.DropTable(
                name: "Permits");

            migrationBuilder.DropIndex(
                name: "IX_Movements_RefusalJustificationId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "RefusalJustificationId",
                table: "Movements");
        }
    }
}
