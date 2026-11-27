using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Suite.Core;

public enum EndpointProtocol { Tcp, Udp }
public sealed record LocalEndpoint(EndpointProtocol Protocol, IPAddress Address, int Port, int ProcessId,
    bool Listening, bool Established, IPAddress? RemoteAddress);

public static class LocalEndpoints
{
    public static List<LocalEndpoint> Read()
    {
        var endpoints = new List<LocalEndpoint>();
        foreach (bool ipv4 in new[] { true, false })
        {
            int family = ipv4 ? 2 : 23;
            ReadTable(GetExtendedTcpTable, family, 5, ipv4 ? 24 : 56, (buffer, row) =>
            {
                int state = Marshal.ReadInt32(buffer, row + (ipv4 ? 0 : 48));
                int local = row + (ipv4 ? 4 : 0);
                endpoints.Add(new(EndpointProtocol.Tcp, Address(buffer, local, ipv4), Port(buffer, local, ipv4),
                    Marshal.ReadInt32(buffer, row + (ipv4 ? 20 : 52)), state == 2, state == 5,
                    Address(buffer, row + (ipv4 ? 12 : 24), ipv4)));
            });
            ReadTable(GetExtendedUdpTable, family, 1, ipv4 ? 12 : 28, (buffer, row) =>
                endpoints.Add(new(EndpointProtocol.Udp, Address(buffer, row, ipv4), Port(buffer, row, ipv4),
                    Marshal.ReadInt32(buffer, row + (ipv4 ? 8 : 24)), false, false, null)));
        }
        return endpoints;
    }

    public static string? Executable(int pid)
    {
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid)
            return null;
        var path = new StringBuilder(32768);
        int size = path.Capacity;
        return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
    }

    private static IPAddress Address(nint buffer, int offset, bool ipv4)
    {
        var bytes = new byte[ipv4 ? 4 : 16];
        Marshal.Copy(buffer + offset, bytes, 0, bytes.Length);
        return ipv4 ? new IPAddress(bytes) : new IPAddress(bytes, (uint)Marshal.ReadInt32(buffer, offset + 16));
    }

    private static int Port(nint buffer, int address, bool ipv4)
    {
        int offset = address + (ipv4 ? 4 : 20);
        return (Marshal.ReadByte(buffer, offset) << 8) | Marshal.ReadByte(buffer, offset + 1);
    }

    private static void ReadTable(TableReader read, int family, int kind, int stride, Action<nint, int> add)
    {
        int bytes = 0;
        uint status = read(0, ref bytes, false, family, kind, 0);
        for (int attempt = 0; status == 122 && attempt < 5; attempt++)
        {
            int capacity = bytes;
            if (capacity < 4) throw new Win32Exception(13);
            nint buffer = Marshal.AllocHGlobal(capacity);
            try
            {
                status = read(buffer, ref bytes, false, family, kind, 0);
                if (status != 0) continue;
                int count = Marshal.ReadInt32(buffer);
                if (count < 0 || count > (capacity - 4) / stride)
                    throw new Win32Exception(13);
                for (int index = 0; index < count; index++)
                    add(buffer, 4 + index * stride);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        if (status is not (0 or 50 or 232))
            throw new Win32Exception((int)status);
    }

    private delegate uint TableReader(nint table, ref int size, bool sort, int family, int type, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(nint table, ref int size, bool sort, int family, int type, uint reserved);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedUdpTable(nint table, ref int size, bool sort, int family, int type, uint reserved);
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
}
