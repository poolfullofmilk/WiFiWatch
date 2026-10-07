using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WifiWatch.Data.Migrations;

/// <inheritdoc />
public partial class AddIncidentsSpeedTestsAndDns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "DnsMilliseconds",
            table: "MinuteSamples",
            type: "REAL",
            nullable: true
        );

        migrationBuilder.AddColumn<double>(
            name: "ReferenceDnsMilliseconds",
            table: "MinuteSamples",
            type: "REAL",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "Details",
            table: "Events",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTime>(
            name: "EndedAtUtc",
            table: "Events",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "Scope",
            table: "Events",
            type: "TEXT",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "Severity",
            table: "Events",
            type: "TEXT",
            nullable: false,
            defaultValue: "Info"
        );

        // Older Rows Become Instants, Severity From Their Alert Flag
        migrationBuilder.Sql(
            "UPDATE Events SET EndedAtUtc = OccurredAtUtc, Severity = CASE WHEN IsAlert = 1 THEN 'Warning' ELSE 'Info' END"
        );
        migrationBuilder.Sql(
            "UPDATE Events SET Kind = 'JitterSpike' WHERE Kind = 'PingSpike' AND Message LIKE '%Jitter%'"
        );

        migrationBuilder.CreateTable(
            name: "SpeedTests",
            columns: table => new
            {
                Id = table
                    .Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                TestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Link = table.Column<string>(type: "TEXT", nullable: false),
                DownloadMbps = table.Column<double>(type: "REAL", nullable: false),
                UploadMbps = table.Column<double>(type: "REAL", nullable: false),
                IdlePingMilliseconds = table.Column<double>(type: "REAL", nullable: true),
                DownloadPingMilliseconds = table.Column<double>(type: "REAL", nullable: true),
                UploadPingMilliseconds = table.Column<double>(type: "REAL", nullable: true),
                Grade = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SpeedTests", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_SpeedTests_TestedAtUtc",
            table: "SpeedTests",
            column: "TestedAtUtc"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SpeedTests");

        migrationBuilder.DropColumn(name: "DnsMilliseconds", table: "MinuteSamples");

        migrationBuilder.DropColumn(name: "ReferenceDnsMilliseconds", table: "MinuteSamples");

        migrationBuilder.DropColumn(name: "Details", table: "Events");

        migrationBuilder.DropColumn(name: "EndedAtUtc", table: "Events");

        migrationBuilder.DropColumn(name: "Scope", table: "Events");

        migrationBuilder.DropColumn(name: "Severity", table: "Events");
    }
}
