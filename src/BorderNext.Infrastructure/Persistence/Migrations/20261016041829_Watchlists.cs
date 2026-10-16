using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderNext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Watchlists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentsStopList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LegacyOldId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PersonalNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CountryId = table.Column<int>(type: "int", nullable: true),
                    Surname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FathersName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime", nullable: true),
                    SexId = table.Column<int>(type: "int", nullable: true),
                    SetUpDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CancelDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    StopListSourceId = table.Column<int>(type: "int", nullable: true),
                    SourceDocument = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Info = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ActionRequired = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MatchInStopListActionId = table.Column<int>(type: "int", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TimeStamp = table.Column<DateTime>(type: "datetime", nullable: true),
                    CancelStopListSourceId = table.Column<int>(type: "int", nullable: true),
                    CancelSourceDocument = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentsStopList", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentsStopList_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentsStopList_Lookups_CancelStopListSourceId",
                        column: x => x.CancelStopListSourceId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentsStopList_Lookups_MatchInStopListActionId",
                        column: x => x.MatchInStopListActionId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentsStopList_Lookups_StopListSourceId",
                        column: x => x.StopListSourceId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonsStopList",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LegacyOldId = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    Surname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FathersName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SurnameAlt = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GivenNameAlt = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FathersNameAlt = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime", nullable: true),
                    SexId = table.Column<int>(type: "int", nullable: true),
                    CitizenshipId = table.Column<int>(type: "int", nullable: true),
                    WhoSetUp = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SetUpDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CancelDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    StopListSourceId = table.Column<int>(type: "int", nullable: true),
                    SourceDocument = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Info = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Photo = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PersonalNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ActionRequired = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MatchInStopListActionId = table.Column<int>(type: "int", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TimeStamp = table.Column<DateTime>(type: "datetime", nullable: true),
                    CancelStopListSourceId = table.Column<int>(type: "int", nullable: true),
                    CancelSourceDocument = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsAuthorized = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonsStopList", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonsStopList_Countries_CitizenshipId",
                        column: x => x.CitizenshipId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonsStopList_Lookups_CancelStopListSourceId",
                        column: x => x.CancelStopListSourceId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonsStopList_Lookups_MatchInStopListActionId",
                        column: x => x.MatchInStopListActionId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonsStopList_Lookups_StopListSourceId",
                        column: x => x.StopListSourceId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoubtablePersons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MovementId = table.Column<int>(type: "int", nullable: false),
                    PhotoJustScanned = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Supervisor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupervisorUserId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SupervisorRegistrationTime = table.Column<DateTime>(type: "datetime", nullable: true),
                    SupervisorPcNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: true),
                    PerformedActionId = table.Column<int>(type: "int", nullable: true),
                    WorkflowStatus = table.Column<short>(type: "smallint", nullable: false),
                    PersonStopListId = table.Column<int>(type: "int", nullable: true),
                    DocumentStopListId = table.Column<int>(type: "int", nullable: true),
                    InspectorNote = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SupervisorNote = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CheckingReasonId = table.Column<int>(type: "int", nullable: true),
                    RefusalOfficerName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RefusalOfficerRank = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RefusalDocNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RefusalCountryId = table.Column<int>(type: "int", nullable: true),
                    RefusalJustificationId = table.Column<int>(type: "int", nullable: true),
                    RefusalJustificationOther = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RefusalCreateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoubtablePersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoubtablePersons_Countries_RefusalCountryId",
                        column: x => x.RefusalCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoubtablePersons_DocumentsStopList_DocumentStopListId",
                        column: x => x.DocumentStopListId,
                        principalTable: "DocumentsStopList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoubtablePersons_Lookups_CheckingReasonId",
                        column: x => x.CheckingReasonId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoubtablePersons_Lookups_RefusalJustificationId",
                        column: x => x.RefusalJustificationId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoubtablePersons_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoubtablePersons_PersonsStopList_PersonStopListId",
                        column: x => x.PersonStopListId,
                        principalTable: "PersonsStopList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoubtablePersons_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StopListFingerprints",
                columns: table => new
                {
                    PersonStopListId = table.Column<int>(type: "int", nullable: false),
                    FingerNumber = table.Column<short>(type: "smallint", nullable: false),
                    FingerprintImage = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Template = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StopListFingerprints", x => new { x.PersonStopListId, x.FingerNumber });
                    table.ForeignKey(
                        name: "FK_StopListFingerprints_PersonsStopList_PersonStopListId",
                        column: x => x.PersonStopListId,
                        principalTable: "PersonsStopList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemovalOrders_PersonStopListId",
                table: "RemovalOrders",
                column: "PersonStopListId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentsStopList_CancelStopListSourceId",
                table: "DocumentsStopList",
                column: "CancelStopListSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentsStopList_CountryId",
                table: "DocumentsStopList",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentsStopList_DocumentNumber",
                table: "DocumentsStopList",
                column: "DocumentNumber");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentsStopList_MatchInStopListActionId",
                table: "DocumentsStopList",
                column: "MatchInStopListActionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentsStopList_StopListSourceId",
                table: "DocumentsStopList",
                column: "StopListSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DoubtablePersons_BorderPointId",
                table: "DoubtablePersons",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_DoubtablePersons_CheckingReasonId",
                table: "DoubtablePersons",
                column: "CheckingReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_DoubtablePersons_DocumentStopListId",
                table: "DoubtablePersons",
                column: "DocumentStopListId");

            migrationBuilder.CreateIndex(
                name: "IX_DoubtablePersons_MovementId",
                table: "DoubtablePersons",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_DoubtablePersons_PersonStopListId",
                table: "DoubtablePersons",
                column: "PersonStopListId");

            migrationBuilder.CreateIndex(
                name: "IX_DoubtablePersons_RefusalCountryId",
                table: "DoubtablePersons",
                column: "RefusalCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_DoubtablePersons_RefusalJustificationId",
                table: "DoubtablePersons",
                column: "RefusalJustificationId");

            migrationBuilder.CreateIndex(
                name: "IX_DoubtablePersons_WorkflowStatus",
                table: "DoubtablePersons",
                column: "WorkflowStatus");

            migrationBuilder.CreateIndex(
                name: "IX_PersonsStopList_CancelStopListSourceId",
                table: "PersonsStopList",
                column: "CancelStopListSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonsStopList_CitizenshipId",
                table: "PersonsStopList",
                column: "CitizenshipId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonsStopList_DocumentNumber",
                table: "PersonsStopList",
                column: "DocumentNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PersonsStopList_MatchInStopListActionId",
                table: "PersonsStopList",
                column: "MatchInStopListActionId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonsStopList_PersonalNumber",
                table: "PersonsStopList",
                column: "PersonalNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PersonsStopList_StopListSourceId",
                table: "PersonsStopList",
                column: "StopListSourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_RemovalOrders_PersonsStopList_PersonStopListId",
                table: "RemovalOrders",
                column: "PersonStopListId",
                principalTable: "PersonsStopList",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RemovalOrders_PersonsStopList_PersonStopListId",
                table: "RemovalOrders");

            migrationBuilder.DropTable(
                name: "DoubtablePersons");

            migrationBuilder.DropTable(
                name: "StopListFingerprints");

            migrationBuilder.DropTable(
                name: "DocumentsStopList");

            migrationBuilder.DropTable(
                name: "PersonsStopList");

            migrationBuilder.DropIndex(
                name: "IX_RemovalOrders_PersonStopListId",
                table: "RemovalOrders");
        }
    }
}
