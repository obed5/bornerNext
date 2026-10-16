using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderNext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Exchanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApisMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlightNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DepartureLocation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ArrivalLocation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DepartureDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ArrivalDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    PassengerCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceFormat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ControlReference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SenderId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RecipientId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CarrierName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApisMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApisMessages_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DependingOnBorderPoint = table.Column<bool>(type: "bit", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    FileDirectory = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Parameters = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApisMessageFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApisMessageId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApisMessageFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApisMessageFiles_ApisMessages_ApisMessageId",
                        column: x => x.ApisMessageId,
                        principalTable: "ApisMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApisPassengers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApisMessageId = table.Column<int>(type: "int", nullable: false),
                    PassportRegistrationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CountryOfIssue = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    ExpireDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    Surname = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GivenName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "datetime", nullable: true),
                    Citizenship = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CountryOfResidence = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CheckingReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsPassenger = table.Column<bool>(type: "bit", nullable: false),
                    MatchedDocumentId = table.Column<int>(type: "int", nullable: true),
                    MatchedMovementId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApisPassengers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApisPassengers_ApisMessages_ApisMessageId",
                        column: x => x.ApisMessageId,
                        principalTable: "ApisMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApisPassengers_Movements_MatchedMovementId",
                        column: x => x.MatchedMovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApisPassengers_TravelDocuments_MatchedDocumentId",
                        column: x => x.MatchedDocumentId,
                        principalTable: "TravelDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExchangeTaskId = table.Column<int>(type: "int", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FilePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DataFrom = table.Column<DateTime>(type: "datetime", nullable: true),
                    DataTo = table.Column<DateTime>(type: "datetime", nullable: true),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    SuccessCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeRuns_ExchangeTasks_ExchangeTaskId",
                        column: x => x.ExchangeTaskId,
                        principalTable: "ExchangeTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ApisDoubtfulPersons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApisPassengerId = table.Column<int>(type: "int", nullable: false),
                    CheckingReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PerformActionId = table.Column<int>(type: "int", nullable: true),
                    SupervisorsNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Supervisor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupervisorPcNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ActionStatus = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApisDoubtfulPersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApisDoubtfulPersons_ApisPassengers_ApisPassengerId",
                        column: x => x.ApisPassengerId,
                        principalTable: "ApisPassengers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApisDoubtfulPersons_Lookups_PerformActionId",
                        column: x => x.PerformActionId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeErrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExchangeRunId = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeErrors_ExchangeRuns_ExchangeRunId",
                        column: x => x.ExchangeRunId,
                        principalTable: "ExchangeRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApisDoubtfulPersons_ApisPassengerId",
                table: "ApisDoubtfulPersons",
                column: "ApisPassengerId");

            migrationBuilder.CreateIndex(
                name: "IX_ApisDoubtfulPersons_PerformActionId",
                table: "ApisDoubtfulPersons",
                column: "PerformActionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApisMessageFiles_ApisMessageId",
                table: "ApisMessageFiles",
                column: "ApisMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ApisMessageFiles_ContentHash",
                table: "ApisMessageFiles",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_ApisMessages_ArrivalDate",
                table: "ApisMessages",
                column: "ArrivalDate");

            migrationBuilder.CreateIndex(
                name: "IX_ApisMessages_BorderPointId",
                table: "ApisMessages",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_ApisMessages_CreatedDate",
                table: "ApisMessages",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ApisMessages_FlightNumber",
                table: "ApisMessages",
                column: "FlightNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ApisPassengers_ApisMessageId",
                table: "ApisPassengers",
                column: "ApisMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ApisPassengers_DocumentNumber",
                table: "ApisPassengers",
                column: "DocumentNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ApisPassengers_MatchedDocumentId",
                table: "ApisPassengers",
                column: "MatchedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApisPassengers_MatchedMovementId",
                table: "ApisPassengers",
                column: "MatchedMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_ApisPassengers_Status",
                table: "ApisPassengers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeErrors_ExchangeRunId",
                table: "ExchangeErrors",
                column: "ExchangeRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRuns_ExchangeTaskId",
                table: "ExchangeRuns",
                column: "ExchangeTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRuns_StartedAt",
                table: "ExchangeRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeTasks_Channel",
                table: "ExchangeTasks",
                column: "Channel");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApisDoubtfulPersons");

            migrationBuilder.DropTable(
                name: "ApisMessageFiles");

            migrationBuilder.DropTable(
                name: "ExchangeErrors");

            migrationBuilder.DropTable(
                name: "ApisPassengers");

            migrationBuilder.DropTable(
                name: "ExchangeRuns");

            migrationBuilder.DropTable(
                name: "ApisMessages");

            migrationBuilder.DropTable(
                name: "ExchangeTasks");
        }
    }
}
