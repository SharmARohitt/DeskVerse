using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Deskverse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StoreTimestampsAsUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Timestamps used to be written as "yyyy-MM-dd HH:mm:ss.fffffff+00:00".
            // The UTC converter reads plain date times, so rewrite the offset suffix
            // on every existing row instead of failing to materialize it.
            foreach (var (table, column) in TimestampColumns)
            {
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"{column}\" = replace(\"{column}\", '+00:00', '') " +
                    $"WHERE \"{column}\" IS NOT NULL AND \"{column}\" LIKE '%+00:00';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }

        private static readonly (string Table, string Column)[] TimestampColumns =
        [
            ("Wallpapers", "CreatedAt"),
            ("Wallpapers", "LastUsedAt"),
            ("WallpaperUsage", "AppliedAt"),
            ("Preferences", "UpdatedAt"),
            ("Collections", "CreatedAt"),
            ("CollectionItems", "AddedAt"),
            ("ProviderConfigurations", "LastSuccessfulSync"),
        ];
    }
}
