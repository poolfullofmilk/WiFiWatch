using Microsoft.EntityFrameworkCore;
using WiFiWatch.Data.Enums;

namespace WiFiWatch.Data.Models;

[Index(nameof(OccurredAtUtc))]
public sealed class WiFiEvent
{
    public int Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    public EventKind Kind { get; set; }

    public EventSeverity Severity { get; set; }

    public string? Scope { get; set; }

    public required string Message { get; set; }

    public string? Details { get; set; }

    public bool IsAlert { get; set; }
}
