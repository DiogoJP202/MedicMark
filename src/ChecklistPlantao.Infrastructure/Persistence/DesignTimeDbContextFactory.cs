using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChecklistPlantao.Infrastructure.Persistence;

/// <summary>
/// Usado apenas pelas ferramentas do EF Core (<c>dotnet ef migrations add</c>).
/// Aponta para um arquivo descartável: gerar migration nunca deve tocar o banco real.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design-time.db", sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        return new AppDbContext(builder.Options);
    }
}
