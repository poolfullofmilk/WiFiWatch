using Microsoft.EntityFrameworkCore;

namespace WifiWatch.Data;

[Index(nameof(OccurredAtUtc))]
public sealed class WifiEvent
{
    public int Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public EventKind Kind { get; set; }

    public required string Message { get; set; }

    public bool IsAlert { get; set; }
}
