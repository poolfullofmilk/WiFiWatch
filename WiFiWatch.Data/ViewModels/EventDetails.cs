using System.Text.Json;

namespace WiFiWatch.Data.ViewModels;

public sealed record EventDetails(
    string? Context = null,
    string? Reason = null,
    string? TraceSummary = null,
    List<TraceHop>? Trace = null
)
{
    public static EventDetails Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();

        try
        {
            return JsonSerializer.Deserialize<EventDetails>(json) ?? new();
        }
        catch (JsonException)
        {
            // Older Rows Hold Plain Text
            return new(Context: json);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}
