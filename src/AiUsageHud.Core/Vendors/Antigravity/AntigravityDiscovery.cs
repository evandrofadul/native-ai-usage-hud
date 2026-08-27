using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AiUsageHud.Core.Vendors.Antigravity;

/// <summary>
/// Probes local language servers for Google Antigravity (Antigravity 2.0, Antigravity IDE, agy CLI).
/// Ports are dynamically assigned on loopback, so we discover active listeners across running processes.
/// </summary>
public static class AntigravityDiscovery
{
    public const string EnvironmentVariableAddress = "ANTIGRAVITY_LS_ADDRESS";

    /// <summary>Base URLs worth probing, most specific first.</summary>
    public static List<string> CandidateBases(string? explicitOverride = null)
    {
        var envOverride = explicitOverride ?? Environment.GetEnvironmentVariable(EnvironmentVariableAddress);
        return CandidateBasesWith(envOverride, DiscoverLsPorts());
    }

    /// <summary>Pure helper: combines optional override and discovered ports into unique base URLs.</summary>
    public static List<string> CandidateBasesWith(string? overrideAddr, IEnumerable<int> discoveredPorts)
    {
        var bases = new List<string>();
        if (NormalizeBase(overrideAddr) is { } normalized)
        {
            bases.Add(normalized);
        }

        foreach (var port in discoveredPorts)
        {
            var candidate = $"http://127.0.0.1:{port}";
            if (!bases.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                bases.Add(candidate);
            }
        }

        return bases;
    }

    /// <summary>
    /// Turn a configured address into a normalized base URL: trim surrounding whitespace,
    /// supply default http scheme if missing, and drop trailing slashes.
    /// </summary>
    public static string? NormalizeBase(string? addr)
    {
        if (string.IsNullOrWhiteSpace(addr)) return null;

        var trimmed = addr.Trim();
        string scheme;
        string authority;

        var idx = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (idx >= 0)
        {
            scheme = trimmed[..idx].ToLowerInvariant();
            if (scheme != "http" && scheme != "https")
                scheme = "http";
            authority = trimmed[(idx + 3)..];
        }
        else
        {
            scheme = "http";
            authority = trimmed;
        }

        authority = authority.TrimEnd('/');
        return authority.Length > 0 ? $"{scheme}://{authority}" : null;
    }

    /// <summary>Does this process look like one of the Google Antigravity products?</summary>
    public static bool IsAntigravityProcess(string comm, string? exe)
    {
        var c = comm.Trim().ToLowerInvariant();
        if (c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            c = c[..^4];

        if (c.Contains("language_server", StringComparison.OrdinalIgnoreCase) ||
            c == "agy" || c == "antigravity")
        {
            return true;
        }

        if (!string.IsNullOrEmpty(exe))
        {
            var p = exe.ToLowerInvariant().Replace('\\', '/');
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                p = p[..^4];

            if (p.Contains("antigravity", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith("/agy", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Flatten per-process listener ports into probe order. Ports are sorted descending per PID
    /// (RPC listener is typically bound after HTTPS, drawing a higher port number) and interleaved by rank.
    /// </summary>
    public static List<int> ProbeOrder(IDictionary<int, List<int>> perPid)
    {
        var groups = new List<List<int>>();
        foreach (var pair in perPid.OrderBy(kv => kv.Key))
        {
            var sorted = pair.Value.Distinct().OrderByDescending(p => p).ToList();
            if (sorted.Count > 0)
                groups.Add(sorted);
        }

        var maxLen = groups.Count > 0 ? groups.Max(g => g.Count) : 0;
        var ports = new List<int>();

        for (var rank = 0; rank < maxLen; rank++)
        {
            foreach (var group in groups)
            {
                if (rank < group.Count)
                {
                    var port = group[rank];
                    if (!ports.Contains(port))
                        ports.Add(port);
                }
            }
        }

        return ports;
    }

    /// <summary>Discover loopback ports listened on by any running Antigravity product.</summary>
    public static List<int> DiscoverLsPorts()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return DiscoverWindowsLsPorts();
            if (OperatingSystem.IsLinux())
                return DiscoverLinuxLsPorts();
            if (OperatingSystem.IsMacOS())
                return DiscoverMacOsLsPorts();
        }
        catch
        {
            // best effort
        }

        return [];
    }

    [SupportedOSPlatform("windows")]
    private static List<int> DiscoverWindowsLsPorts()
    {
        var matchingPids = new HashSet<int>();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var exe = "";
                    try { exe = proc.MainModule?.FileName ?? ""; } catch { /* ignore access denied */ }

                    if (IsAntigravityProcess(proc.ProcessName, exe))
                    {
                        matchingPids.Add(proc.Id);
                    }
                }
                catch
                {
                    // ignore process access exceptions
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // ignore
        }

        if (matchingPids.Count == 0)
            return [];

        var rows = WindowsTcpRows();
        var perPid = new Dictionary<int, List<int>>();

        foreach (var (localAddr, localPort, pid) in rows)
        {
            if (!matchingPids.Contains(pid)) continue;
            // 127.0.0.1 in network byte order is 0x0100007F (little-endian: 127, 0, 0, 1)
            if (localAddr != 0x0100007F && localAddr != 0x7F000001) continue;

            // dwLocalPort is in network byte order
            var port = ((localPort & 0xFF) << 8) | ((localPort >> 8) & 0xFF);
            if (port != 0)
            {
                if (!perPid.TryGetValue(pid, out var list))
                {
                    list = [];
                    perPid[pid] = list;
                }
                list.Add((int)port);
            }
        }

        return ProbeOrder(perPid);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    private const uint AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref uint pdwSize,
        bool bOrder,
        uint ulAf,
        int TableClass,
        uint Reserved);

    [SupportedOSPlatform("windows")]
    private static List<(uint LocalAddr, uint LocalPort, int Pid)> WindowsTcpRows()
    {
        var result = new List<(uint, uint, int)>();
        uint size = 0;

        var status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
        if (status != ERROR_INSUFFICIENT_BUFFER || size == 0) return result;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (status != 0) return result;

            var numEntries = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, 4);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (var i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                result.Add((row.dwLocalAddr, row.dwLocalPort, (int)row.dwOwningPid));
                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    [SupportedOSPlatform("linux")]
    private static List<int> DiscoverLinuxLsPorts()
    {
        var owners = new Dictionary<ulong, int>();
        if (!Directory.Exists("/proc")) return [];

        foreach (var dir in Directory.GetDirectories("/proc"))
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, out var pid)) continue;

            string comm;
            try { comm = File.ReadAllText(Path.Combine(dir, "comm")).Trim(); } catch { continue; }

            string? exe = null;
            try
            {
                var exePath = Path.Combine(dir, "exe");
                var linkTarget = File.ResolveLinkTarget(exePath, true);
                exe = linkTarget?.FullName;
            }
            catch { /* best effort */ }

            if (!IsAntigravityProcess(comm, exe)) continue;

            var fdDir = Path.Combine(dir, "fd");
            if (!Directory.Exists(fdDir)) continue;

            try
            {
                foreach (var fd in Directory.GetFiles(fdDir))
                {
                    try
                    {
                        var target = File.ResolveLinkTarget(fd, true)?.FullName ?? "";
                        if (target.StartsWith("socket:[", StringComparison.Ordinal) && target.EndsWith(']'))
                        {
                            var inoStr = target[8..^1];
                            if (ulong.TryParse(inoStr, out var inode))
                                owners[inode] = pid;
                        }
                    }
                    catch { /* best effort */ }
                }
            }
            catch { /* best effort */ }
        }

        if (owners.Count == 0) return [];

        var perPid = new Dictionary<int, List<int>>();
        foreach (var table in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            if (!File.Exists(table)) continue;
            try
            {
                var lines = File.ReadAllLines(table);
                foreach (var line in lines.Skip(1))
                {
                    var cols = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (cols.Length < 10) continue;
                    // st column: 0A = TCP_LISTEN
                    if (cols[3] != "0A") continue;

                    // local_address: 0100007F:PORT_HEX or similar
                    var addrParts = cols[1].Split(':');
                    if (addrParts.Length != 2) continue;
                    if (!int.TryParse(addrParts[1], System.Globalization.NumberStyles.HexNumber, null, out var port)) continue;
                    if (!ulong.TryParse(cols[9], out var inode)) continue;

                    if (owners.TryGetValue(inode, out var pid))
                    {
                        if (!perPid.TryGetValue(pid, out var list))
                        {
                            list = [];
                            perPid[pid] = list;
                        }
                        list.Add(port);
                    }
                }
            }
            catch { /* best effort */ }
        }

        return ProbeOrder(perPid);
    }

    [SupportedOSPlatform("macos")]
    private static List<int> DiscoverMacOsLsPorts()
    {
        try
        {
            var psi = new ProcessStartInfo("lsof", "-nP -iTCP -sTCP:LISTEN -F pcn")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return [];

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            return ParseLsofPcn(output);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Pure parser for lsof -F pcn machine-readable output.</summary>
    public static List<int> ParseLsofPcn(string output)
    {
        var perPid = new Dictionary<int, List<int>>();
        int? currentPid = null;
        int? matchingOwner = null;

        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length < 2) continue;
            var tag = line[0];
            var rest = line[1..];

            switch (tag)
            {
                case 'p':
                    currentPid = int.TryParse(rest, out var p) ? p : null;
                    matchingOwner = null;
                    break;
                case 'c':
                    matchingOwner = currentPid is { } pid && IsAntigravityProcess(rest, null) ? pid : null;
                    break;
                case 'n':
                    if (matchingOwner is { } ownerPid)
                    {
                        var lastColon = rest.LastIndexOf(':');
                        if (lastColon >= 0 && int.TryParse(rest[(lastColon + 1)..], out var port) && port > 0)
                        {
                            if (!perPid.TryGetValue(ownerPid, out var list))
                            {
                                list = [];
                                perPid[ownerPid] = list;
                            }
                            list.Add(port);
                        }
                    }
                    break;
            }
        }

        return ProbeOrder(perPid);
    }
}
