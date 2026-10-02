namespace Deskverse.Infrastructure.Persistence;

using Deskverse.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Applies pending migrations at startup, switches SQLite into WAL mode for
/// concurrent readers, and clears leftover staging files from interrupted runs.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly IDbContextFactory<DeskverseDbContext> _contextFactory;
    private readonly IAppEnvironment _environment;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbContextFactory<DeskverseDbContext> contextFactory,
        IAppEnvironment environment,
        ILogger<DatabaseInitializer> logger)
    {
        _contextFactory = contextFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_environment.DataRoot);

        await using (var context = await _contextFactory.CreateDbContextAsync(cancellationToken))
        {
            await context.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database ready at {Path}", _environment.DatabasePath);
        }

        await EnableWalAsync(cancellationToken);
        ClearStaleTempFiles();
    }

    private async Task EnableWalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _environment.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
            };
            await using var connection = new SqliteConnection(connectionString.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // WAL is an optimization, not a correctness requirement.
            _logger.LogWarning(ex, "Could not enable WAL journal mode.");
        }
    }

    private void ClearStaleTempFiles()
    {
        try
        {
            if (!Directory.Exists(_environment.TempDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_environment.TempDirectory))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // A file may still be held by a live download; leave it alone.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Temp directory cleanup failed; continuing.");
        }
    }
}
