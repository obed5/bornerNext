using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderNext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Biometrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "FirstFingerprint",
                table: "Users",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FirstFingerprintTemplate",
                table: "Users",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SecondFingerprint",
                table: "Users",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SecondFingerprintTemplate",
                table: "Users",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Fingerprints",
                columns: table => new
                {
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    FingerNumber = table.Column<short>(type: "smallint", nullable: false),
                    FingerprintImage = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Template = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Quality = table.Column<int>(type: "int", nullable: true),
                    CaptureDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    CaptureDevice = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fingerprints", x => new { x.PersonId, x.FingerNumber });
                    table.ForeignKey(
                        name: "FK_Fingerprints_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fingerprints");

            migrationBuilder.DropColumn(
                name: "FirstFingerprint",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FirstFingerprintTemplate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecondFingerprint",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecondFingerprintTemplate",
                table: "Users");
        }
    }
}
