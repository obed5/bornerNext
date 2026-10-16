using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BorderNext.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MachineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PcNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Entity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EventTime = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lBorderPoints",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<short>(type: "smallint", nullable: true),
                    Revoke = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokeDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lBorderPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lUserRole",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<short>(type: "smallint", nullable: true),
                    Revoke = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokeDate = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lUserRole", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lBorderPointsCultures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<short>(type: "smallint", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RegionalSite = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lBorderPointsCultures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lBorderPointsCultures_lBorderPoints_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MachineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Value = table.Column<int>(type: "int", nullable: true),
                    DateTimeValue = table.Column<DateTime>(type: "datetime", nullable: true),
                    StringValue = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: true),
                    EncryptedValue = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ShortDescription = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Settings_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lUserRoleCultures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<short>(type: "smallint", nullable: false),
                    Culture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lUserRoleCultures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lUserRoleCultures_lUserRole_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "lUserRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserRoleId = table.Column<short>(type: "smallint", nullable: false),
                    BorderPointId = table.Column<short>(type: "smallint", nullable: true),
                    Revoke = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokeDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "datetime", nullable: true),
                    UpdateUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_lBorderPoints_BorderPointId",
                        column: x => x.BorderPointId,
                        principalTable: "lBorderPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_lUserRole_UserRoleId",
                        column: x => x.UserRoleId,
                        principalTable: "lUserRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "lUserRole",
                columns: new[] { "Id", "Code", "RevokeDate", "SortOrder" },
                values: new object[,]
                {
                    { (short)1, "ADMIN", null, (short)1 },
                    { (short)2, "SHIFT_OFF", null, (short)2 },
                    { (short)3, "IMMIG_OFF", null, (short)3 },
                    { (short)4, "ALERT_OFF", null, (short)4 },
                    { (short)5, "VISA_OFF", null, (short)5 },
                    { (short)6, "SUPERUSER", null, (short)6 }
                });

            migrationBuilder.InsertData(
                table: "lUserRoleCultures",
                columns: new[] { "Id", "Culture", "Description", "OwnerId" },
                values: new object[,]
                {
                    { 1, "fr", "Administrateur", (short)1 },
                    { 2, "fr", "Officier de quart", (short)2 },
                    { 3, "fr", "Officier d'immigration", (short)3 },
                    { 4, "fr", "Officier d'alerte", (short)4 },
                    { 5, "fr", "Officier des visas", (short)5 },
                    { 6, "fr", "Super-utilisateur", (short)6 },
                    { 7, "en", "Administrator", (short)1 },
                    { 8, "en", "Shift Officer", (short)2 },
                    { 9, "en", "Immigration Officer", (short)3 },
                    { 10, "en", "Alert Officer", (short)4 },
                    { 11, "en", "Visa Officer", (short)5 },
                    { 12, "en", "Super User", (short)6 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EventType_EventTime",
                table: "AuditLog",
                columns: new[] { "EventType", "EventTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId",
                table: "AuditLog",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_lBorderPointsCultures_OwnerId_Culture",
                table: "lBorderPointsCultures",
                columns: new[] { "OwnerId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lUserRole_Code",
                table: "lUserRole",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lUserRoleCultures_OwnerId_Culture",
                table: "lUserRoleCultures",
                columns: new[] { "OwnerId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_BorderPointId",
                table: "Settings",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Type_BorderPointId_UserId",
                table: "Settings",
                columns: new[] { "Type", "BorderPointId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_BorderPointId",
                table: "Users",
                column: "BorderPointId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserRoleId",
                table: "Users",
                column: "UserRoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "lBorderPointsCultures");

            migrationBuilder.DropTable(
                name: "lUserRoleCultures");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "lBorderPoints");

            migrationBuilder.DropTable(
                name: "lUserRole");
        }
    }
}
