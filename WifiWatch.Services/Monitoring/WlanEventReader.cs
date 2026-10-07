using System.Diagnostics.Eventing.Reader;
using WifiWatch.Data.ViewModels;

namespace WifiWatch.Services.Monitoring;

public static class WlanEventReader
{
    // WLAN AutoConfig Operational Log, Readable Without Admin
    public const int ConnectedEvent = 8001;
    public const int ConnectFailedEvent = 8002;
    public const int DisconnectedEvent = 8003;

    private const string LogName = "Microsoft-Windows-WLAN-AutoConfig/Operational";
    private const int SsidIndex = 4;
    private const int ReasonIndex = 6;

    public static long LatestRecordId()
    {
        try
        {
            using var reader = new EventLogReader(
                new EventLogQuery(LogName, PathType.LogName) { ReverseDirection = true }
            );
            using var latest = reader.ReadEvent();
            return latest?.RecordId ?? 0;
        }
        catch (EventLogException)
        {
            return 0;
        }
    }

    public static List<WlanNotice> ReadAfter(long recordId)
    {
        List<WlanNotice> notices = [];
        try
        {
            var query = new EventLogQuery(
                LogName,
                PathType.LogName,
                $"*[System[(EventID={ConnectedEvent} or EventID={ConnectFailedEvent} or EventID={DisconnectedEvent}) and EventRecordID>{recordId}]]"
            );
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    notices.Add(
                        new(
                            record.RecordId ?? recordId,
                            record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                            record.Id,
                            ReadProperty(record, SsidIndex),
                            record.Id == ConnectedEvent ? null : ReadProperty(record, ReasonIndex)
                        )
                    );
                }
            }
        }
        catch (EventLogException)
        {
            // A Missing Or Locked Log Only Means No Reasons
        }

        return notices;
    }

    private static string? ReadProperty(EventRecord record, int index) =>
        record.Properties.Count > index
            ? record.Properties[index].Value?.ToString()?.Trim().TrimEnd('.')
            : null;
}
