namespace Deskverse.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Design-time factory used only by `dotnet ef`. The database file it names is
/// never opened at runtime; real connections come from the runtime factory.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DeskverseDbContext>
{
    public DeskverseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DeskverseDbContext>()
            .UseSqlite("Data Source=deskverse-design.db")
            .Options;
        return new DeskverseDbContext(options);
    }
}
