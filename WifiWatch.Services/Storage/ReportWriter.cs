using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WifiWatch.Data;
using WifiWatch.Data.Enums;
using WifiWatch.Data.Helpers;
using WifiWatch.Data.ViewModels;
using WifiWatch.Services.Monitoring;

namespace WifiWatch.Services.Storage;

public static class ReportWriter
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    // A Plain Light Page Prints And Mails Well
    private const string Style = """
        body { font-family: "Segoe UI", Arial, sans-serif; margin: 32px; color: #202020; }
        h1 { margin-bottom: 4px; } h2 { margin-top: 32px; }
        .muted { color: #666; }
        table { border-collapse: collapse; width: 100%; margin-top: 8px; font-size: 14px; }
        th, td { border: 1px solid #ddd; padding: 6px 8px; text-align: left; vertical-align: top; }
        th { background: #f3f3f3; }
        .hops td { font-size: 12px; }
        """;

    public static string DirectoryPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WifiWatch",
            "Reports"
        );

    public static async Task<string> SaveAsync(DateTime firstDay, DateTime lastDay)
    {
        var startUtc = firstDay.Date.ToUniversalTime();
        var endUtc = lastDay.Date.AddDays(1).ToUniversalTime();
        var summary = await SummaryWriter.SummarizeAsync(startUtc, endUtc);

        await using var context = new WifiDbContext();
        var incidents = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.OccurredAtUtc >= startUtc
                && wifiEvent.OccurredAtUtc < endUtc
                && wifiEvent.Severity != EventSeverity.Info
            )
            .OrderBy(wifiEvent => wifiEvent.OccurredAtUtc)
            .ToListAsync();
        var speedTests = await context
            .SpeedTests.AsNoTracking()
            .Where(test => test.TestedAtUtc >= startUtc && test.TestedAtUtc < endUtc)
            .OrderBy(test => test.TestedAtUtc)
            .ToListAsync();

        var range = $"{firstDay:yyyy-MM-dd} To {lastDay:yyyy-MM-dd}";
        var html = new StringBuilder();
        html.Append(
            $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{AppInfo.DisplayName} Report {range}</title><style>{Style}</style></head><body>"
        );
        html.Append(
            $"<h1>Connection Report</h1><p class=\"muted\">{range}, Made By {AppInfo.DisplayName} {AppInfo.Version.ToString(2)} On {DateTime.Now:yyyy-MM-dd HH:mm}</p>"
        );

        html.Append("<h2>Summary</h2><table>");
        AppendRow(
            html,
            "Monitored",
            Formatter.FormatDuration(TimeSpan.FromMinutes(summary.MonitoredMinutes))
        );
        AppendRow(html, "Online", $"{summary.OnlinePercent:0.#}%");
        AppendRow(html, "Incidents", summary.IncidentCount.ToString());
        AppendRow(
            html,
            "Longest Outage",
            summary.LongestOutage > TimeSpan.Zero
                ? Formatter.FormatDuration(summary.LongestOutage)
                : "None"
        );
        AppendRow(html, "DFS Evictions", summary.EvictionCount.ToString());
        html.Append("</table>");

        html.Append("<h2>Incidents</h2>");
        if (incidents.Count == 0)
        {
            html.Append("<p>None In This Range</p>");
        }
        else
        {
            html.Append(
                "<table><tr><th>Start</th><th>End</th><th>Duration</th><th>What</th><th>Where</th><th>Details</th></tr>"
            );
            foreach (var incident in incidents)
            {
                var details = EventDetails.Parse(incident.Details);
                var isInstant = incident.EndedAtUtc == incident.OccurredAtUtc;
                var duration =
                    isInstant ? "-"
                    : incident.EndedAtUtc is { } endedAtUtc
                        ? Formatter.FormatDuration(endedAtUtc - incident.OccurredAtUtc)
                    : "Ongoing";
                html.Append("<tr>");
                AppendCells(
                    html,
                    Formatter.FormatLocal(incident.OccurredAtUtc, TimeFormat),
                    incident.EndedAtUtc is { } end && !isInstant
                        ? Formatter.FormatLocal(end, TimeFormat)
                        : "-",
                    duration,
                    incident.Message,
                    incident.Scope ?? "-"
                );
                html.Append("<td>");
                AppendLine(html, "Windows Reason", details.Reason);
                AppendLine(html, "Context", details.Context);
                AppendLine(html, "Trace", details.TraceSummary);
                if (details.Trace is { Count: > 0 } hops)
                {
                    html.Append(
                        "<table class=\"hops\"><tr><th>Hop</th><th>Address</th><th>Name</th><th>Loss</th><th>Average</th></tr>"
                    );
                    foreach (var hop in hops)
                    {
                        html.Append("<tr>");
                        AppendCells(
                            html,
                            hop.Number.ToString(),
                            hop.Address ?? "No Answer",
                            hop.Name ?? "-",
                            $"{hop.LossPercent:0}%",
                            Formatter.FormatNumber(hop.AverageMilliseconds, "ms")
                        );
                        html.Append("</tr>");
                    }

                    html.Append("</table>");
                }

                html.Append("</td></tr>");
            }

            html.Append("</table>");
        }

        html.Append("<h2>Speed Tests</h2>");
        if (speedTests.Count == 0)
        {
            html.Append("<p>None In This Range</p>");
        }
        else
        {
            html.Append(
                "<table><tr><th>Time</th><th>Link</th><th>Download</th><th>Upload</th><th>Idle Ping</th><th>Ping Under Load</th><th>Grade</th></tr>"
            );
            foreach (var test in speedTests)
            {
                html.Append("<tr>");
                AppendCells(
                    html,
                    Formatter.FormatLocal(test.TestedAtUtc, TimeFormat),
                    test.Link,
                    $"{test.DownloadMbps:0} Mbps",
                    $"{test.UploadMbps:0} Mbps",
                    Formatter.FormatNumber(test.IdlePingMilliseconds, "ms"),
                    Formatter.FormatNumber(
                        Math.Max(
                            test.DownloadPingMilliseconds ?? 0,
                            test.UploadPingMilliseconds ?? 0
                        ),
                        "ms"
                    ),
                    test.Grade
                );
                html.Append("</tr>");
            }

            html.Append("</table>");
        }

        html.Append("</body></html>");

        // Unique Name, A Report Never Overwrites Another
        Directory.CreateDirectory(DirectoryPath);
        var filePath = Path.Combine(
            DirectoryPath,
            $"WifiWatch Report {range} Made {DateTime.Now:yyyy-MM-dd HH-mm-ss}.html"
        );
        await File.WriteAllTextAsync(filePath, html.ToString());
        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        return filePath;
    }

    private static void AppendRow(StringBuilder html, string label, string value) =>
        html.Append(
            $"<tr><th>{WebUtility.HtmlEncode(label)}</th><td>{WebUtility.HtmlEncode(value)}</td></tr>"
        );

    private static void AppendCells(StringBuilder html, params string[] values)
    {
        foreach (var value in values)
        {
            html.Append($"<td>{WebUtility.HtmlEncode(value)}</td>");
        }
    }

    private static void AppendLine(StringBuilder html, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            html.Append(
                $"<div><b>{WebUtility.HtmlEncode(label)}</b>: {WebUtility.HtmlEncode(value)}</div>"
            );
        }
    }
}
