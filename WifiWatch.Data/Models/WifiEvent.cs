using Microsoft.EntityFrameworkCore;
using WifiWatch.Data.Enums;

namespace WifiWatch.Data.Models;

[Index(nameof(OccurredAtUtc))]
public sealed class WifiEvent
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
