using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Sunrise.Shared.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRegistrationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_registration_identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    DiscordSubjectHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false),
                    IpHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false),
                    InstallationIdHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false),
                    BrowserFingerprintHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false),
                    FingerprintVersion = table.Column<int>(type: "int", nullable: false),
                    DiscordAccountCreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_registration_identity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_registration_identity_user_UserId",
                        column: x => x.UserId,
                        principalTable: "user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_user_registration_identity_BrowserFingerprintHash",
                table: "user_registration_identity",
                column: "BrowserFingerprintHash");

            migrationBuilder.CreateIndex(
                name: "IX_user_registration_identity_DiscordSubjectHash",
                table: "user_registration_identity",
                column: "DiscordSubjectHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_registration_identity_InstallationIdHash",
                table: "user_registration_identity",
                column: "InstallationIdHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_registration_identity_IpHash",
                table: "user_registration_identity",
                column: "IpHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_registration_identity_UserId",
                table: "user_registration_identity",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_registration_identity");
        }
    }
}
