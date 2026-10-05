using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WifiWatch.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    IsAlert = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "MinuteSamples",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MinuteUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Link = table.Column<string>(type: "TEXT", nullable: false),
                    Ssid = table.Column<string>(type: "TEXT", nullable: true),
                    Band = table.Column<string>(type: "TEXT", nullable: true),
                    Channel = table.Column<int>(type: "INTEGER", nullable: true),
                    Rssi = table.Column<int>(type: "INTEGER", nullable: true),
                    ReceiveRateMbps = table.Column<int>(type: "INTEGER", nullable: true),
                    TransmitRateMbps = table.Column<int>(type: "INTEGER", nullable: true),
                    RouterPingMilliseconds = table.Column<double>(type: "REAL", nullable: true),
                    RouterJitterMilliseconds = table.Column<double>(type: "REAL", nullable: true),
                    RouterLossPercent = table.Column<double>(type: "REAL", nullable: false),
                    InternetPingMilliseconds = table.Column<double>(type: "REAL", nullable: true),
                    InternetJitterMilliseconds = table.Column<double>(type: "REAL", nullable: true),
                    InternetLossPercent = table.Column<double>(type: "REAL", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinuteSamples", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Events_OccurredAtUtc",
                table: "Events",
                column: "OccurredAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MinuteSamples_MinuteUtc",
                table: "MinuteSamples",
                column: "MinuteUtc"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Events");

            migrationBuilder.DropTable(name: "MinuteSamples");
        }
    }
}
