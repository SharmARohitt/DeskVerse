using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deskverse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Preferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredCategories = table.Column<string>(type: "TEXT", nullable: false),
                    PreferredColors = table.Column<string>(type: "TEXT", nullable: false),
                    PreferredBrightness = table.Column<double>(type: "REAL", nullable: true),
                    PreferredStyles = table.Column<string>(type: "TEXT", nullable: false),
                    AllowVideoWallpapers = table.Column<bool>(type: "INTEGER", nullable: false),
                    PauseVideoOnBattery = table.Column<bool>(type: "INTEGER", nullable: false),
                    PauseVideoOnFullscreen = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnimatedPreviewsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    BackgroundPolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    StorageLimitBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheDirectory = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    AutoCleanupEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RunAtStartup = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimizeToTray = table.Column<bool>(type: "INTEGER", nullable: false),
                    TelemetryOptIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstRunComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Preferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderConfigurations",
                columns: table => new
                {
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: true),
                    LastSuccessfulSync = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderConfigurations", x => x.ProviderId);
                });

            migrationBuilder.CreateTable(
                name: "Wallpapers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SourceProvider = table.Column<string>(type: "TEXT", nullable: true),
                    SourceId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalImportPath = table.Column<string>(type: "TEXT", nullable: true),
                    CacheRelativePath = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Format = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    License = table.Column<string>(type: "TEXT", nullable: true),
                    Attribution = table.Column<string>(type: "TEXT", nullable: true),
                    Categories = table.Column<string>(type: "TEXT", nullable: false),
                    DominantColor = table.Column<string>(type: "TEXT", nullable: true),
                    Brightness = table.Column<double>(type: "REAL", nullable: true),
                    VisualDensity = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCached = table.Column<bool>(type: "INTEGER", nullable: false),
                    SafetyStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    IsUserImported = table.Column<bool>(type: "INTEGER", nullable: false),
                    CacheLastAccessTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    UseCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDisliked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallpapers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CollectionItems",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WallpaperId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionItems", x => new { x.CollectionId, x.WallpaperId });
                    table.ForeignKey(
                        name: "FK_CollectionItems_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Wallpapers_WallpaperId",
                        column: x => x.WallpaperId,
                        principalTable: "Wallpapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WallpaperUsage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WallpaperId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    UserFeedback = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WallpaperUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WallpaperUsage_Wallpapers_WallpaperId",
                        column: x => x.WallpaperId,
                        principalTable: "Wallpapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_WallpaperId",
                table: "CollectionItems",
                column: "WallpaperId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Name",
                table: "Collections",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Wallpapers_FileHash",
                table: "Wallpapers",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_Wallpapers_IsCached_CacheLastAccessTicks",
                table: "Wallpapers",
                columns: new[] { "IsCached", "CacheLastAccessTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_Wallpapers_IsFavorite",
                table: "Wallpapers",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_Wallpapers_LastUsedAt",
                table: "Wallpapers",
                column: "LastUsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Wallpapers_SourceProvider_SourceId",
                table: "Wallpapers",
                columns: new[] { "SourceProvider", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WallpaperUsage_AppliedAt",
                table: "WallpaperUsage",
                column: "AppliedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WallpaperUsage_WallpaperId_AppliedAt",
                table: "WallpaperUsage",
                columns: new[] { "WallpaperId", "AppliedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionItems");

            migrationBuilder.DropTable(
                name: "Preferences");

            migrationBuilder.DropTable(
                name: "ProviderConfigurations");

            migrationBuilder.DropTable(
                name: "WallpaperUsage");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "Wallpapers");
        }
    }
}
