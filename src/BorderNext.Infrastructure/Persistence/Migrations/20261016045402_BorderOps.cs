using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderNext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BorderOps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TripCrossingId",
                table: "Movements",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Carriers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TransportTypeId = table.Column<int>(type: "int", nullable: true),
                    CountryId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Revoke = table.Column<bool>(type: "bit", nullable: false),
                    RevokeDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carriers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Carriers_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Carriers_Lookups_TransportTypeId",
                        column: x => x.TransportTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Terminals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TransportTypeId = table.Column<int>(type: "int", nullable: true),
                    CountryId = table.Column<int>(type: "int", nullable: true),
                    CityId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Revoke = table.Column<bool>(type: "bit", nullable: false),
                    RevokeDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terminals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Terminals_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Terminals_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Terminals_Lookups_TransportTypeId",
                        column: x => x.TransportTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vessels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    FormerName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    HomePortCountryId = table.Column<int>(type: "int", nullable: true),
                    NationalityCountryId = table.Column<int>(type: "int", nullable: true),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Dimension = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VesselTypeId = table.Column<int>(type: "int", nullable: true),
                    RadioCallSign = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Frequencies = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegistrationDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vessels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vessels_Countries_HomePortCountryId",
                        column: x => x.HomePortCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vessels_Countries_NationalityCountryId",
                        column: x => x.NationalityCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vessels_Lookups_VesselTypeId",
                        column: x => x.VesselTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierCultures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierCultures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarrierCultures_Carriers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalCultures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalCultures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalCultures_Terminals_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TransportTypeId = table.Column<int>(type: "int", nullable: true),
                    FromTerminalId = table.Column<int>(type: "int", nullable: true),
                    ToTerminalId = table.Column<int>(type: "int", nullable: true),
                    ExternalNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CarrierId = table.Column<int>(type: "int", nullable: true),
                    TerminalId = table.Column<int>(type: "int", nullable: true),
                    StationFromCityId = table.Column<int>(type: "int", nullable: true),
                    StationToCityId = table.Column<int>(type: "int", nullable: true),
                    ScheduledTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Lookups_TransportTypeId",
                        column: x => x.TransportTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Terminals_FromTerminalId",
                        column: x => x.FromTerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_Terminals_ToTerminalId",
                        column: x => x.ToTerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trips_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TripCrossings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    DirectionLookupId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime", nullable: false),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true),
                    LastUpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    OpenTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    OpenUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CloseTime = table.Column<TimeSpan>(type: "time", nullable: true),
                    CloseUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripCrossings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripCrossings_Lookups_DirectionLookupId",
                        column: x => x.DirectionLookupId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TripCrossings_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TripCrossings_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_TripCrossingId",
                table: "Movements",
                column: "TripCrossingId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierCultures_OwnerId_Culture",
                table: "CarrierCultures",
                columns: new[] { "OwnerId", "Culture" });

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_Code",
                table: "Carriers",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_CountryId",
                table: "Carriers",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_TransportTypeId",
                table: "Carriers",
                column: "TransportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalCultures_OwnerId_Culture",
                table: "TerminalCultures",
                columns: new[] { "OwnerId", "Culture" });

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_CityId",
                table: "Terminals",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_Code",
                table: "Terminals",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_CountryId",
                table: "Terminals",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_TransportTypeId",
                table: "Terminals",
                column: "TransportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TripCrossings_BorderPointId",
                table: "TripCrossings",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_TripCrossings_DirectionLookupId",
                table: "TripCrossings",
                column: "DirectionLookupId");

            migrationBuilder.CreateIndex(
                name: "IX_TripCrossings_IsOpen",
                table: "TripCrossings",
                column: "IsOpen");

            migrationBuilder.CreateIndex(
                name: "IX_TripCrossings_TripId",
                table: "TripCrossings",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_BorderPointId",
                table: "Trips",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CarrierId",
                table: "Trips",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_FromTerminalId",
                table: "Trips",
                column: "FromTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TerminalId",
                table: "Trips",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_ToTerminalId",
                table: "Trips",
                column: "ToTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_TransportTypeId",
                table: "Trips",
                column: "TransportTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Vessels_HomePortCountryId",
                table: "Vessels",
                column: "HomePortCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Vessels_NationalityCountryId",
                table: "Vessels",
                column: "NationalityCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Vessels_RegistrationNumber",
                table: "Vessels",
                column: "RegistrationNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Vessels_VesselTypeId",
                table: "Vessels",
                column: "VesselTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Movements_TripCrossings_TripCrossingId",
                table: "Movements",
                column: "TripCrossingId",
                principalTable: "TripCrossings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Movements_TripCrossings_TripCrossingId",
                table: "Movements");

            migrationBuilder.DropTable(
                name: "CarrierCultures");

            migrationBuilder.DropTable(
                name: "TerminalCultures");

            migrationBuilder.DropTable(
                name: "TripCrossings");

            migrationBuilder.DropTable(
                name: "Vessels");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropTable(
                name: "Carriers");

            migrationBuilder.DropTable(
                name: "Terminals");

            migrationBuilder.DropIndex(
                name: "IX_Movements_TripCrossingId",
                table: "Movements");

            migrationBuilder.DropColumn(
                name: "TripCrossingId",
                table: "Movements");
        }
    }
}
