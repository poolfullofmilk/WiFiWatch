using System.IO;
using Microsoft.EntityFrameworkCore;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Models;

namespace WiFiWatch.Data;

public sealed class WiFiDbContext : DbContext
{
    // Debug Builds Keep Apart From Real Data
    private static readonly string s_defaultDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
#if DEBUG
        "WiFiWatch Debug"
#else
        "WiFiWatch"
#endif
    );

    // Regression Tests Redirect Every Store Before Loading Any Settings Or Database
    public static string DataDirectory =>
        AppContext.GetData("WiFiWatch.TestDirectory") as string ?? s_defaultDataDirectory;

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
