using System.Runtime.InteropServices;
using System.Text;
using WiFiWatch.Data.ViewModels;

namespace WiFiWatch.Services.Monitoring;

public static partial class NativeWiFiReader
{
    // Return Codes And Opcodes From wlanapi.h
    private const uint Success = 0;
    private const uint AccessDenied = 5;
    private const uint ClientVersion = 2;
    private const int CurrentConnectionOpcode = 7;
    private const int ChannelNumberOpcode = 8;
    private const int RssiOpcode = 0x10000102;
    private const int ConnectedState = 1;

    // Byte Offsets In WLAN_INTERFACE_INFO_LIST And WLAN_CONNECTION_ATTRIBUTES
    private const int FirstInterfaceOffset = 8;
    private const int InterfaceSize = 532;
    private const int InterfaceStateOffset = 528;
    private const int AssociationOffset = 520;
    private const int SsidBytesOffset = 4;
    private const int MaximumSsidBytes = 32;
    private const int ReceiveRateOffset = 60;
    private const int TransmitRateOffset = 64;
    private const int LastTwoGigahertzChannel = 14;

    private static readonly WiFiReading s_disconnected = new(
        false,
        false,
        null,
        null,
        null,
        null,
        null,
        null
    );
    private static readonly WiFiReading s_blocked = s_disconnected with { IsBlocked = true };

    // Ponytail: One Handle For The App's Life, Closed On Exit
    private static IntPtr s_clientHandle;

    public static WiFiReading Read()
    {
        if (
            s_clientHandle == IntPtr.Zero
            && WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out s_clientHandle) != Success
        )
            return s_disconnected;

        if (FindInterface() is not { } interfaceGuid)
            return s_disconnected;

        var result = WlanQueryInterface(
            s_clientHandle,
            in interfaceGuid,
            CurrentConnectionOpcode,
            IntPtr.Zero,
            out _,
            out var connection,
            IntPtr.Zero
        );

        // Windows 11 Refuses Wi-Fi Details Without Location Access
        if (result == AccessDenied)
            return s_blocked;

        if (result != Success)
            return s_disconnected;

        try
        {
            var association = connection + AssociationOffset;
            var ssidLength = Math.Clamp(Marshal.ReadInt32(association), 0, MaximumSsidBytes);
            var ssidBytes = new byte[ssidLength];
            Marshal.Copy(association + SsidBytesOffset, ssidBytes, 0, ssidLength);
            var channel = QueryNumber(interfaceGuid, ChannelNumberOpcode);

            // Ponytail: Band From The Channel, 6 GHz Needs The Frequency
            return new(
                false,
                true,
                Encoding.UTF8.GetString(ssidBytes),
                channel is null ? null
                    : channel <= LastTwoGigahertzChannel ? "2.4 GHz"
                    : "5 GHz",
                channel,
                QueryNumber(interfaceGuid, RssiOpcode),
                Marshal.ReadInt32(association + ReceiveRateOffset) / 1000,
                Marshal.ReadInt32(association + TransmitRateOffset) / 1000
            );
        }
        finally
        {
            WlanFreeMemory(connection);
        }
    }

    private static Guid? FindInterface()
    {
        if (WlanEnumInterfaces(s_clientHandle, IntPtr.Zero, out var interfaceList) != Success)
            return null;

        try
        {
            // The Connected Adapter Wins, Else The First One
            var count = Marshal.ReadInt32(interfaceList);
            Guid? firstGuid = null;
            for (var index = 0; index < count; index++)
            {
                var item = interfaceList + FirstInterfaceOffset + (index * InterfaceSize);
                var guid = Marshal.PtrToStructure<Guid>(item);
                if (Marshal.ReadInt32(item + InterfaceStateOffset) == ConnectedState)
                    return guid;

                firstGuid ??= guid;
            }

            return firstGuid;
        }
        finally
        {
            WlanFreeMemory(interfaceList);
        }
    }

    private static int? QueryNumber(Guid interfaceGuid, int opcode)
    {
        if (
            WlanQueryInterface(
                s_clientHandle,
                in interfaceGuid,
                opcode,
                IntPtr.Zero,
                out _,
                out var data,
                IntPtr.Zero
            ) != Success
        )
            return null;

        try
        {
            return Marshal.ReadInt32(data);
        }
        finally
        {
            WlanFreeMemory(data);
        }
    }

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanOpenHandle(
        uint clientVersion,
        IntPtr reserved,
        out uint negotiatedVersion,
        out IntPtr clientHandle
    );

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanEnumInterfaces(
        IntPtr clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList
    );

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanQueryInterface(
        IntPtr clientHandle,
        in Guid interfaceGuid,
        int opcode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        IntPtr opcodeValueType
    );

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(IntPtr memory);
}
