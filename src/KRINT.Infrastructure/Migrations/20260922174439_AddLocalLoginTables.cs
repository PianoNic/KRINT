using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KRINT.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalLoginTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToamaisutaaUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PictureUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SecurityStamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaExternalLogins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    LastSignInAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaExternalLogins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaExternalLogins_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaInvitationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaInvitationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaInvitationTokens_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaPasswordCredentials",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    FirstFailedAttemptAt = table.Column<long>(type: "bigint", nullable: true),
                    LockedOutUntil = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaPasswordCredentials", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaPasswordCredentials_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaPasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaPasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaPasswordResetTokens_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAt = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaRecoveryCodes_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    FamilyStartedAt = table.Column<long>(type: "bigint", nullable: false),
                    SecurityStamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AuthenticationMethods = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TwoFactorSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SecondFactorAt = table.Column<long>(type: "bigint", nullable: true),
                    RotatedAt = table.Column<long>(type: "bigint", nullable: true),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaRefreshTokens_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaTrustedDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecurityStamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SecondFactorAt = table.Column<long>(type: "bigint", nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    FamilyStartedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    LastUsedAt = table.Column<long>(type: "bigint", nullable: false),
                    RotatedAt = table.Column<long>(type: "bigint", nullable: true),
                    RevokedAt = table.Column<long>(type: "bigint", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaTrustedDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaTrustedDevices_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaTwoFactorChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAt = table.Column<long>(type: "bigint", nullable: true),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaTwoFactorChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaTwoFactorChallenges_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToamaisutaaUserTwoFactors",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretCiphertext = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: false),
                    SecretNonce = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    SecretTag = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    EncryptionKeyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfirmedAt = table.Column<long>(type: "bigint", nullable: true),
                    LastUsedStep = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToamaisutaaUserTwoFactors", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_ToamaisutaaUserTwoFactors_ToamaisutaaUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ToamaisutaaUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaExternalLogins_ProviderKey_Subject",
                table: "ToamaisutaaExternalLogins",
                columns: new[] { "ProviderKey", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaExternalLogins_UserId",
                table: "ToamaisutaaExternalLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaInvitationTokens_TokenHash",
                table: "ToamaisutaaInvitationTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaInvitationTokens_UserId",
                table: "ToamaisutaaInvitationTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaPasswordCredentials_NormalizedEmail",
                table: "ToamaisutaaPasswordCredentials",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaPasswordCredentials_NormalizedUserName",
                table: "ToamaisutaaPasswordCredentials",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaPasswordResetTokens_TokenHash",
                table: "ToamaisutaaPasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaPasswordResetTokens_UserId",
                table: "ToamaisutaaPasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaRecoveryCodes_UserId_CodeHash",
                table: "ToamaisutaaRecoveryCodes",
                columns: new[] { "UserId", "CodeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaRefreshTokens_FamilyId",
                table: "ToamaisutaaRefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaRefreshTokens_TokenHash",
                table: "ToamaisutaaRefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaRefreshTokens_UserId",
                table: "ToamaisutaaRefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaTrustedDevices_FamilyId",
                table: "ToamaisutaaTrustedDevices",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaTrustedDevices_TokenHash",
                table: "ToamaisutaaTrustedDevices",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaTrustedDevices_UserId",
                table: "ToamaisutaaTrustedDevices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaTwoFactorChallenges_TokenHash",
                table: "ToamaisutaaTwoFactorChallenges",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaTwoFactorChallenges_UserId",
                table: "ToamaisutaaTwoFactorChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ToamaisutaaUsers_Email",
                table: "ToamaisutaaUsers",
                column: "Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToamaisutaaExternalLogins");

            migrationBuilder.DropTable(
                name: "ToamaisutaaInvitationTokens");

            migrationBuilder.DropTable(
                name: "ToamaisutaaPasswordCredentials");

            migrationBuilder.DropTable(
                name: "ToamaisutaaPasswordResetTokens");

            migrationBuilder.DropTable(
                name: "ToamaisutaaRecoveryCodes");

            migrationBuilder.DropTable(
                name: "ToamaisutaaRefreshTokens");

            migrationBuilder.DropTable(
                name: "ToamaisutaaTrustedDevices");

            migrationBuilder.DropTable(
                name: "ToamaisutaaTwoFactorChallenges");

            migrationBuilder.DropTable(
                name: "ToamaisutaaUserTwoFactors");

            migrationBuilder.DropTable(
                name: "ToamaisutaaUsers");
        }
    }
}
