using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace WifiWatch.Services;

public static class CsvExport
{
    public static string DirectoryPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WifiWatch",
            "Exports"
        );

    public static void Save(string name, DateTime? day, IEnumerable<object?[]> rows)
    {
        // Excel Splits On The Regional List Separator
        var separator = CultureInfo.CurrentCulture.TextInfo.ListSeparator;

        // Unique Name, An Export Never Overwrites Another
        var dayLabel = day?.ToString("yyyy-MM-dd") ?? "All";
        var filePath = Path.Combine(
            DirectoryPath,
            $"WifiWatch {name} {dayLabel} Exported {DateTime.Now:yyyy-MM-dd HH-mm-ss}.csv"
        );

        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllLines(
            filePath,
            rows.Select(row =>
                string.Join(separator, row.Select(value => Escape(value, separator)))
            )
        );
        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }

    private static string Escape(object? value, string separator)
    {
        var text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        return text.Contains(separator) || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}
