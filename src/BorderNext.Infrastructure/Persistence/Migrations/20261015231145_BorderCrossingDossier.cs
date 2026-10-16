using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderNext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BorderCrossingDossier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Persons_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TravelDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CountryOfIssueId = table.Column<int>(type: "int", nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ExpireDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    FatherName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime", nullable: true),
                    SexId = table.Column<int>(type: "int", nullable: true),
                    CitizenshipId = table.Column<int>(type: "int", nullable: true),
                    PersonalNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatorUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelDocuments_Countries_CitizenshipId",
                        column: x => x.CitizenshipId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelDocuments_Countries_CountryOfIssueId",
                        column: x => x.CountryOfIssueId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelDocuments_Lookups_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelDocuments_Lookups_SexId",
                        column: x => x.SexId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelDocuments_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelDocuments_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentImages",
                columns: table => new
                {
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Photo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ScannedDocument = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentImages", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocumentImages_TravelDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "TravelDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Visas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VisaTypeId = table.Column<int>(type: "int", nullable: false),
                    VisaEntryId = table.Column<int>(type: "int", nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ExpireDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Duration = table.Column<short>(type: "smallint", nullable: true),
                    DocumentId = table.Column<int>(type: "int", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ContactNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Employer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvitedBy = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RelationId = table.Column<int>(type: "int", nullable: true),
                    UniversityId = table.Column<int>(type: "int", nullable: true),
                    PermitStatusId = table.Column<int>(type: "int", nullable: true),
                    Occupation = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    PeriodTypeId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoucherNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PermitConditionId = table.Column<int>(type: "int", nullable: true),
                    ProvinceId = table.Column<int>(type: "int", nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PcNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Photo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
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
                    table.PrimaryKey("PK_Visas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Visas_Lookups_PeriodTypeId",
                        column: x => x.PeriodTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_Lookups_PermitConditionId",
                        column: x => x.PermitConditionId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_Lookups_PermitStatusId",
                        column: x => x.PermitStatusId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_Lookups_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_Lookups_UniversityId",
                        column: x => x.UniversityId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_Lookups_VisaEntryId",
                        column: x => x.VisaEntryId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_Lookups_VisaTypeId",
                        column: x => x.VisaTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_Provinces_ProvinceId",
                        column: x => x.ProvinceId,
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_TravelDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "TravelDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Visas_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Movements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    TravelDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    DirectionLookupId = table.Column<int>(type: "int", nullable: false),
                    TransportTypeId = table.Column<int>(type: "int", nullable: true),
                    TransportCrossBorderId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsDriver = table.Column<bool>(type: "bit", nullable: false),
                    TravelReasonId = table.Column<int>(type: "int", nullable: true),
                    Granted = table.Column<bool>(type: "bit", nullable: false),
                    DeportationReasonId = table.Column<int>(type: "int", nullable: true),
                    IsTransit = table.Column<bool>(type: "bit", nullable: false),
                    HasVisa = table.Column<bool>(type: "bit", nullable: false),
                    VisaId = table.Column<int>(type: "int", nullable: true),
                    ChildrenCount = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResidentCountryId = table.Column<int>(type: "int", nullable: true),
                    DestinationCountryId = table.Column<int>(type: "int", nullable: true),
                    Occupation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccommodationAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AccommodationTypeId = table.Column<int>(type: "int", nullable: true),
                    DurationOfStay = table.Column<short>(type: "smallint", nullable: true),
                    Inspector = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PcNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RegistrationTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movements_Countries_DestinationCountryId",
                        column: x => x.DestinationCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Countries_ResidentCountryId",
                        column: x => x.ResidentCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Lookups_AccommodationTypeId",
                        column: x => x.AccommodationTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Lookups_DeportationReasonId",
                        column: x => x.DeportationReasonId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Lookups_DirectionLookupId",
                        column: x => x.DirectionLookupId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Lookups_TransportTypeId",
                        column: x => x.TransportTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Lookups_TravelReasonId",
                        column: x => x.TravelReasonId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_TravelDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "TravelDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_Visas_VisaId",
                        column: x => x.VisaId,
                        principalTable: "Visas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Movements_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisaChildren",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VisaId = table.Column<int>(type: "int", nullable: false),
                    Surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime", nullable: true),
                    SexId = table.Column<int>(type: "int", nullable: true),
                    CitizenshipId = table.Column<int>(type: "int", nullable: true),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Photo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisaChildren", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisaChildren_Visas_VisaId",
                        column: x => x.VisaId,
                        principalTable: "Visas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovementChildren",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MovementId = table.Column<int>(type: "int", nullable: false),
                    Surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime", nullable: true),
                    SexId = table.Column<int>(type: "int", nullable: true),
                    CitizenshipId = table.Column<int>(type: "int", nullable: true),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Attorney = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Ticket = table.Column<bool>(type: "bit", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovementChildren", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovementChildren_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovementChildren_MovementId",
                table: "MovementChildren",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_AccommodationTypeId",
                table: "Movements",
                column: "AccommodationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_BorderPointId",
                table: "Movements",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_DeportationReasonId",
                table: "Movements",
                column: "DeportationReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_DestinationCountryId",
                table: "Movements",
                column: "DestinationCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_DirectionLookupId",
                table: "Movements",
                column: "DirectionLookupId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_DocumentId",
                table: "Movements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_LegacyId",
                table: "Movements",
                column: "LegacyId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_PersonId",
                table: "Movements",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ResidentCountryId",
                table: "Movements",
                column: "ResidentCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_TransportTypeId",
                table: "Movements",
                column: "TransportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_TravelDate_DirectionLookupId",
                table: "Movements",
                columns: new[] { "TravelDate", "DirectionLookupId" });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_TravelReasonId",
                table: "Movements",
                column: "TravelReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_VisaId",
                table: "Movements",
                column: "VisaId");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_BorderPointId",
                table: "Persons",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_LegacyId",
                table: "Persons",
                column: "LegacyId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_BorderPointId",
                table: "TravelDocuments",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_CitizenshipId",
                table: "TravelDocuments",
                column: "CitizenshipId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_CountryOfIssueId",
                table: "TravelDocuments",
                column: "CountryOfIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_DateOfBirth",
                table: "TravelDocuments",
                column: "DateOfBirth");

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_DocumentTypeId_DocumentNumber",
                table: "TravelDocuments",
                columns: new[] { "DocumentTypeId", "DocumentNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_LegacyId",
                table: "TravelDocuments",
                column: "LegacyId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_PersonId",
                table: "TravelDocuments",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_SexId",
                table: "TravelDocuments",
                column: "SexId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelDocuments_Surname_GivenName",
                table: "TravelDocuments",
                columns: new[] { "Surname", "GivenName" });

            migrationBuilder.CreateIndex(
                name: "IX_VisaChildren_VisaId",
                table: "VisaChildren",
                column: "VisaId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_BorderPointId",
                table: "Visas",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_DocumentId",
                table: "Visas",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_LegacyId",
                table: "Visas",
                column: "LegacyId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_Number",
                table: "Visas",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_PeriodTypeId",
                table: "Visas",
                column: "PeriodTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_PermitConditionId",
                table: "Visas",
                column: "PermitConditionId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_PermitStatusId",
                table: "Visas",
                column: "PermitStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_ProvinceId",
                table: "Visas",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_RelationId",
                table: "Visas",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_UniversityId",
                table: "Visas",
                column: "UniversityId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_VisaEntryId",
                table: "Visas",
                column: "VisaEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Visas_VisaTypeId",
                table: "Visas",
                column: "VisaTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentImages");

            migrationBuilder.DropTable(
                name: "MovementChildren");

            migrationBuilder.DropTable(
                name: "VisaChildren");

            migrationBuilder.DropTable(
                name: "Movements");

            migrationBuilder.DropTable(
                name: "Visas");

            migrationBuilder.DropTable(
                name: "TravelDocuments");

            migrationBuilder.DropTable(
                name: "Persons");
        }
    }
}
