using System.IO;
using Microsoft.EntityFrameworkCore;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Models;

namespace WifiWatch.Data;

public sealed class WifiDbContext : DbContext
{
    public static string DataDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WifiWatch"
        );

    public DbSet<WifiEvent> Events => Set<WifiEvent>();

    public DbSet<MinuteSample> MinuteSamples => Set<MinuteSample>();

    public DbSet<NeighborSample> NeighborSamples => Set<NeighborSample>();

    public DbSet<SpeedTest> SpeedTests => Set<SpeedTest>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={Path.Combine(DataDirectory, "WifiWatch.db")}");

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<EventKind>().HaveConversion<string>();
        configurationBuilder.Properties<EventSeverity>().HaveConversion<string>();
    }
}
