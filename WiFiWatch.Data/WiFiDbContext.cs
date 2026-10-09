using System.IO;
using Microsoft.EntityFrameworkCore;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Models;

namespace WiFiWatch.Data;

public sealed class WiFiDbContext : DbContext
{
    // Debug Builds Keep Apart From Real Data
    public static string DataDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
#if DEBUG
            "WiFiWatch Debug"
#else
            "WiFiWatch"
#endif
        );

    public DbSet<WiFiEvent> Events => Set<WiFiEvent>();

    public DbSet<MinuteSample> MinuteSamples => Set<MinuteSample>();

    public DbSet<NeighborSample> NeighborSamples => Set<NeighborSample>();

    public DbSet<SpeedTest> SpeedTests => Set<SpeedTest>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={Path.Combine(DataDirectory, "WiFiWatch.db")}");

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<EventKind>().HaveConversion<string>();
        configurationBuilder.Properties<EventSeverity>().HaveConversion<string>();
    }
}
