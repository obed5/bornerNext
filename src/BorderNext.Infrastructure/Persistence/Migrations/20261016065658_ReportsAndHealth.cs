using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderNext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportsAndHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CountryGeoPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Iso2 = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Iso3 = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryGeoPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PermitMedicalHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    PermitDependantId = table.Column<int>(type: "int", nullable: true),
                    CurrentIllness = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CurrentMedication = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    MalariaSuffered = table.Column<bool>(type: "bit", nullable: false),
                    YellowFeverSuffered = table.Column<bool>(type: "bit", nullable: false),
                    TuberculosisSuffered = table.Column<bool>(type: "bit", nullable: false),
                    MeaslesSuffered = table.Column<bool>(type: "bit", nullable: false),
                    CholeraSuffered = table.Column<bool>(type: "bit", nullable: false),
                    TyphoidSuffered = table.Column<bool>(type: "bit", nullable: false),
                    JaundiceSuffered = table.Column<bool>(type: "bit", nullable: false),
                    ChronicCoughSuffered = table.Column<bool>(type: "bit", nullable: false),
                    OtherSuffered = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    YellowFeverVaccinated = table.Column<bool>(type: "bit", nullable: false),
                    MMRVaccinated = table.Column<bool>(type: "bit", nullable: false),
                    DiphtheriaVaccinated = table.Column<bool>(type: "bit", nullable: false),
                    PolioVaccinated = table.Column<bool>(type: "bit", nullable: false),
                    HealthCentreVisited = table.Column<bool>(type: "bit", nullable: false),
                    ChronicDisease = table.Column<bool>(type: "bit", nullable: false),
                    InfectiousDisease = table.Column<bool>(type: "bit", nullable: false),
                    IsPublicHealthCentre = table.Column<bool>(type: "bit", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitMedicalHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitMedicalHistories_PermitDependants_PermitDependantId",
                        column: x => x.PermitDependantId,
                        principalTable: "PermitDependants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitMedicalHistories_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionnaireEndingMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MessageBody = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Revoke = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionnaireEndingMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Questionnaires",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExpireDays = table.Column<int>(type: "int", nullable: false),
                    Revoke = table.Column<bool>(type: "bit", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questionnaires", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Questions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    QuestionTypeId = table.Column<int>(type: "int", nullable: false),
                    IsInitialQuestion = table.Column<bool>(type: "bit", nullable: false),
                    IsFollowUpQuestion = table.Column<bool>(type: "bit", nullable: false),
                    QuestionTag = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Revoke = table.Column<bool>(type: "bit", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Questions_Lookups_QuestionTypeId",
                        column: x => x.QuestionTypeId,
                        principalTable: "Lookups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppliedQuestionnaires",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    QuestionnaireId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: true),
                    MovementId = table.Column<int>(type: "int", nullable: true),
                    AppliedDate = table.Column<DateTime>(type: "datetime", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true),
                    AppliedByUserId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppliedQuestionnaires", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppliedQuestionnaires_Movements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "Movements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppliedQuestionnaires_Questionnaires_QuestionnaireId",
                        column: x => x.QuestionnaireId,
                        principalTable: "Questionnaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppliedQuestionnaires_TravelDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "TravelDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Revoke = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionAnswers_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionnaireQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    QuestionnaireId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    QuestionOrder = table.Column<int>(type: "int", nullable: false),
                    Revoke = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionnaireQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionnaireQuestions_Questionnaires_QuestionnaireId",
                        column: x => x.QuestionnaireId,
                        principalTable: "Questionnaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionnaireQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConditionalQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    QuestionAnswerId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false),
                    Revoke = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionalQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConditionalQuestions_QuestionAnswers_QuestionAnswerId",
                        column: x => x.QuestionAnswerId,
                        principalTable: "QuestionAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConditionalQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionAnswerQuestionnaireEndings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    QuestionAnswerId = table.Column<int>(type: "int", nullable: false),
                    QuestionnaireEndingMessageId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionAnswerQuestionnaireEndings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionAnswerQuestionnaireEndings_QuestionAnswers_QuestionAnswerId",
                        column: x => x.QuestionAnswerId,
                        principalTable: "QuestionAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionAnswerQuestionnaireEndings_QuestionnaireEndingMessages_QuestionnaireEndingMessageId",
                        column: x => x.QuestionnaireEndingMessageId,
                        principalTable: "QuestionnaireEndingMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionnaireResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegacyId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AppliedQuestionnaireId = table.Column<int>(type: "int", nullable: false),
                    QuestionAnswerId = table.Column<int>(type: "int", nullable: false),
                    OtherAnswer = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionnaireResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionnaireResults_AppliedQuestionnaires_AppliedQuestionnaireId",
                        column: x => x.AppliedQuestionnaireId,
                        principalTable: "AppliedQuestionnaires",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionnaireResults_QuestionAnswers_QuestionAnswerId",
                        column: x => x.QuestionAnswerId,
                        principalTable: "QuestionAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppliedQuestionnaires_AppliedDate",
                table: "AppliedQuestionnaires",
                column: "AppliedDate");

            migrationBuilder.CreateIndex(
                name: "IX_AppliedQuestionnaires_DocumentId",
                table: "AppliedQuestionnaires",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppliedQuestionnaires_MovementId",
                table: "AppliedQuestionnaires",
                column: "MovementId");

            migrationBuilder.CreateIndex(
                name: "IX_AppliedQuestionnaires_QuestionnaireId",
                table: "AppliedQuestionnaires",
                column: "QuestionnaireId");

            migrationBuilder.CreateIndex(
                name: "IX_ConditionalQuestions_QuestionAnswerId",
                table: "ConditionalQuestions",
                column: "QuestionAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_ConditionalQuestions_QuestionId",
                table: "ConditionalQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_CountryGeoPoints_Iso3",
                table: "CountryGeoPoints",
                column: "Iso3");

            migrationBuilder.CreateIndex(
                name: "IX_PermitMedicalHistories_PermitDependantId",
                table: "PermitMedicalHistories",
                column: "PermitDependantId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitMedicalHistories_PermitId",
                table: "PermitMedicalHistories",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswerQuestionnaireEndings_QuestionAnswerId",
                table: "QuestionAnswerQuestionnaireEndings",
                column: "QuestionAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswerQuestionnaireEndings_QuestionnaireEndingMessageId",
                table: "QuestionAnswerQuestionnaireEndings",
                column: "QuestionnaireEndingMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionAnswers_QuestionId",
                table: "QuestionAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionnaireQuestions_QuestionId",
                table: "QuestionnaireQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionnaireQuestions_QuestionnaireId_QuestionOrder",
                table: "QuestionnaireQuestions",
                columns: new[] { "QuestionnaireId", "QuestionOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionnaireResults_AppliedQuestionnaireId",
                table: "QuestionnaireResults",
                column: "AppliedQuestionnaireId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionnaireResults_QuestionAnswerId",
                table: "QuestionnaireResults",
                column: "QuestionAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_QuestionTypeId",
                table: "Questions",
                column: "QuestionTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConditionalQuestions");

            migrationBuilder.DropTable(
                name: "CountryGeoPoints");

            migrationBuilder.DropTable(
                name: "PermitMedicalHistories");

            migrationBuilder.DropTable(
                name: "QuestionAnswerQuestionnaireEndings");

            migrationBuilder.DropTable(
                name: "QuestionnaireQuestions");

            migrationBuilder.DropTable(
                name: "QuestionnaireResults");

            migrationBuilder.DropTable(
                name: "QuestionnaireEndingMessages");

            migrationBuilder.DropTable(
                name: "AppliedQuestionnaires");

            migrationBuilder.DropTable(
                name: "QuestionAnswers");

            migrationBuilder.DropTable(
                name: "Questionnaires");

            migrationBuilder.DropTable(
                name: "Questions");
        }
    }
}
