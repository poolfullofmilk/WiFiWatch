using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WiFiWatch.Data.Migrations;

/// <inheritdoc />
public partial class AddNeighborSamples : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NeighborSamples",
            columns: table => new
            {
                Id = table
                    .Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ScanUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Ssid = table.Column<string>(type: "TEXT", nullable: false),
                Bssid = table.Column<string>(type: "TEXT", nullable: false),
                Channel = table.Column<int>(type: "INTEGER", nullable: false),
                SignalPercent = table.Column<int>(type: "INTEGER", nullable: false),
                ChannelUtilizationPercent = table.Column<int>(type: "INTEGER", nullable: true),
                IsOwn = table.Column<bool>(type: "INTEGER", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NeighborSamples", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_NeighborSamples_ScanUtc",
            table: "NeighborSamples",
            column: "ScanUtc"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NeighborSamples");
    }
}
