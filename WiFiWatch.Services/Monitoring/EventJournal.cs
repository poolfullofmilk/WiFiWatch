using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using WiFiWatch.Data;
using WiFiWatch.Data.Enums;
using WiFiWatch.Data.Helpers;
using WiFiWatch.Data.Models;
using WiFiWatch.Data.ViewModels;

namespace WiFiWatch.Services.Monitoring;

public sealed class EventJournal
{
    // Sign In And Wake Flap, Alerts Wait Out This Window
    private static readonly TimeSpan s_quietWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan s_warningAlertAfter = TimeSpan.FromMinutes(
        Problems.WarningMinutes
    );

    // One Writer At A Time, Several Tasks Report
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, OpenIncident> _openIncidents = new();
    private DateTime _quietUntilUtc;

    public event Action<string, string>? Alerted;

    public event Action? EventRecorded;

    public bool IsQuiet => DateTime.UtcNow < _quietUntilUtc;

    public void StartQuietWindow() => _quietUntilUtc = DateTime.UtcNow + s_quietWindow;

    public bool IsOpen(string key) => _openIncidents.ContainsKey(key);

    public DateTime? OpenedAtUtc(string key) =>
        _openIncidents.TryGetValue(key, out var incident) ? incident.StartedAtUtc : null;

    public int? OpenIdOf(string key) =>
        _openIncidents.TryGetValue(key, out var incident) ? incident.Id : null;

    #region Record Methods
    public async Task RecordAsync(
        EventKind kind,
        EventSeverity severity,
        string message,
        string? scope = null,
        EventDetails? details = null,
        bool isAlwaysAlerted = false
    )
    {
        var nowUtc = DateTime.UtcNow;
        await WithGateAsync(async () =>
        {
            await using var context = new WiFiDbContext();
            context.Events.Add(
                new WiFiEvent
                {
                    OccurredAtUtc = nowUtc,
                    EndedAtUtc = nowUtc,
                    Kind = kind,
                    Severity = severity,
                    Scope = scope,
                    Message = message,
                    Details = details?.ToJson(),
                    IsAlert = severity != EventSeverity.Info,
                }
            );
            await context.SaveChangesAsync();
        });

        EventRecorded?.Invoke();
        if (severity == EventSeverity.Critical || isAlwaysAlerted)
        {
            Alert(kind, message, isAlwaysAlerted);
        }
    }

    public async Task TryRecordAsync(
        EventKind kind,
        EventSeverity severity,
        string message,
        string? scope = null
    )
    {
        try
        {
            await RecordAsync(kind, severity, message, scope);
        }
        catch
        {
            // A Broken Database Must Not Stop Watching
        }
    }

    public static async Task<bool> HasMessageStartingWithAsync(EventKind kind, string prefix)
    {
        await using var context = new WiFiDbContext();
        return await context.Events.AnyAsync(wifiEvent =>
            wifiEvent.Kind == kind && wifiEvent.Message.StartsWith(prefix)
        );
    }

    public static async Task<List<WiFiEvent>> OpenProblemsAsync()
    {
        await using var context = new WiFiDbContext();
        var problems = await context
            .Events.AsNoTracking()
            .Where(wifiEvent =>
                wifiEvent.EndedAtUtc == null && wifiEvent.Severity != EventSeverity.Info
            )
            .ToListAsync();

        // Severity Is Stored As Text, So Rank It Here
        return
        [
            .. problems
                .OrderByDescending(wifiEvent => wifiEvent.Severity)
                .ThenByDescending(wifiEvent => wifiEvent.OccurredAtUtc),
        ];
    }
    #endregion

    #region Incident Methods
    public async Task OpenAsync(
        string key,
        EventKind kind,
        EventSeverity severity,
        string message,
        string? scope,
        EventDetails? details,
        DateTime? startedAtUtc = null
    )
    {
        var isOpened = false;
        await WithGateAsync(async () =>
        {
            if (_openIncidents.ContainsKey(key))
                return;

            var row = new WiFiEvent
            {
                OccurredAtUtc = startedAtUtc ?? DateTime.UtcNow,
                Kind = kind,
                Severity = severity,
                Scope = scope,
                Message = message,
                Details = details?.ToJson(),
                IsAlert = severity != EventSeverity.Info,
            };
            await using var context = new WiFiDbContext();
            context.Events.Add(row);
            await context.SaveChangesAsync();
            _openIncidents[key] = new(row.Id, kind, severity, row.OccurredAtUtc, message);
            isOpened = true;
        });

        if (!isOpened)
            return;

        EventRecorded?.Invoke();
        if (severity == EventSeverity.Critical)
        {
            _openIncidents[key].HasAlerted = Alert(kind, message, false);
        }
    }

    public async Task UpdateAsync(
        string key,
        string? message = null,
        EventSeverity? severity = null,
        Func<EventDetails, EventDetails>? changeDetails = null
    )
    {
        if (!_openIncidents.TryGetValue(key, out var incident))
            return;

        await WithGateAsync(() => WriteAsync(incident.Id, message, severity, changeDetails, null));
        incident.Message = message ?? incident.Message;

        // Escalation Alerts Once, Like A New Critical Incident
        if (severity == EventSeverity.Critical && !incident.HasAlerted)
        {
            incident.HasAlerted = Alert(incident.Kind, incident.Message, false);
        }

        incident.Severity = severity ?? incident.Severity;
        EventRecorded?.Invoke();
    }

    public async Task CloseAsync(
        string key,
        string message,
        DateTime? endedAtUtc = null,
        Func<EventDetails, EventDetails>? changeDetails = null
    )
    {
        if (!_openIncidents.TryRemove(key, out var incident))
            return;

        await WithGateAsync(() =>
            WriteAsync(incident.Id, message, null, changeDetails, endedAtUtc ?? DateTime.UtcNow)
        );
        EventRecorded?.Invoke();
    }

    public async Task CloseAllAsync(DateTime endedAtUtc)
    {
        foreach (var (key, incident) in _openIncidents.ToList())
        {
            await CloseAsync(key, $"{incident.Message}, Cut Short By Sleep", endedAtUtc);
        }
    }

    public async Task ChangeDetailsAsync(int id, Func<EventDetails, EventDetails> changeDetails)
    {
        await WithGateAsync(() => WriteAsync(id, null, null, changeDetails, null));
        EventRecorded?.Invoke();
    }

    public async Task<bool> AttachToLatestAsync(
        EventKind kind,
        TimeSpan within,
        Func<EventDetails, EventDetails> changeDetails
    )
    {
        var sinceUtc = DateTime.UtcNow - within;
        var isFound = false;
        await WithGateAsync(async () =>
        {
            await using var context = new WiFiDbContext();
            var latestId = await context
                .Events.Where(wifiEvent =>
                    wifiEvent.Kind == kind && wifiEvent.OccurredAtUtc >= sinceUtc
                )
                .OrderByDescending(wifiEvent => wifiEvent.Id)
                .Select(wifiEvent => (int?)wifiEvent.Id)
                .FirstOrDefaultAsync();
            if (latestId is { } id)
            {
                await WriteAsync(id, null, null, changeDetails, null);
                isFound = true;
            }
        });
        EventRecorded?.Invoke();
        return isFound;
    }

    public void AlertLongWarnings()
    {
        // A Warning That Will Not Clear Deserves One Notification
        foreach (var incident in _openIncidents.Values.ToList())
        {
            if (
                incident.Severity != EventSeverity.Warning
                || DateTime.UtcNow - incident.StartedAtUtc < s_warningAlertAfter
            )
                continue;

            if (!incident.HasAlerted)
            {
                incident.HasAlerted = Alert(
                    incident.Kind,
                    $"{incident.Message}, Still Going After {Formatter.FormatDuration(DateTime.UtcNow - incident.StartedAtUtc)}",
                    false
                );
            }

            // Pages Listing Problems Count It From Now On
            if (!incident.IsLasting)
            {
                incident.IsLasting = true;
                EventRecorded?.Invoke();
            }
        }
    }

    public static async Task CloseLeftoversAsync()
    {
        // Incidents Still Open At Shutdown End At The Last Data
        await using var context = new WiFiDbContext();
        var lastDataUtc =
            await context.MinuteSamples.MaxAsync(sample => (DateTime?)sample.MinuteUtc)
            ?? DateTime.UtcNow;
        await context
            .Events.Where(wifiEvent => wifiEvent.EndedAtUtc == null)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(wifiEvent => wifiEvent.EndedAtUtc, lastDataUtc)
                    .SetProperty(
                        wifiEvent => wifiEvent.Message,
                        wifiEvent => wifiEvent.Message + ", Cut Short When Monitoring Stopped"
                    )
            );
    }
    #endregion

    private static async Task WriteAsync(
        int id,
        string? message,
        EventSeverity? severity,
        Func<EventDetails, EventDetails>? changeDetails,
        DateTime? endedAtUtc
    )
    {
        await using var context = new WiFiDbContext();
        var row = await context.Events.FindAsync(id);
        if (row is null)
            return;

        row.Message = message ?? row.Message;
        row.Severity = severity ?? row.Severity;
        row.IsAlert = row.Severity != EventSeverity.Info;
        row.EndedAtUtc = endedAtUtc ?? row.EndedAtUtc;
        if (changeDetails is not null)
        {
            row.Details = changeDetails(EventDetails.Parse(row.Details)).ToJson();
        }

        await context.SaveChangesAsync();
    }

    private async Task WithGateAsync(Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool Alert(EventKind kind, string message, bool isForced)
    {
        // Location Still Warns At Once, It Blocks Everything Else
        if (!isForced && IsQuiet && kind != EventKind.LocationBlocked)
            return false;

        Alerted?.Invoke(kind.ToLabel(), message);
        return true;
    }

    private sealed class OpenIncident(
        int id,
        EventKind kind,
        EventSeverity severity,
        DateTime startedAtUtc,
        string message
    )
    {
        public int Id { get; } = id;

        public EventKind Kind { get; } = kind;

        public DateTime StartedAtUtc { get; } = startedAtUtc;

        public EventSeverity Severity { get; set; } = severity;

        public string Message { get; set; } = message;

        public bool HasAlerted { get; set; }

        public bool IsLasting { get; set; }
    }
}
