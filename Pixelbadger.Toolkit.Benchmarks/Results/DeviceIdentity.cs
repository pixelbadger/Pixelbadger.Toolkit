using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Portability;

namespace Pixelbadger.Toolkit.Benchmarks.Results;

public static class DeviceIdentity
{
    public static readonly Regex DeviceNamePattern = new("^[a-z0-9][a-z0-9-]{0,62}$", RegexOptions.Compiled);

    public static bool IsValidDeviceName(string? name) => name is not null && DeviceNamePattern.IsMatch(name);

    /// <summary>Lowercases and reduces to [a-z0-9-], with no repeated or edge dashes.</summary>
    public static string Slugify(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            var ch = c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-';
            if (ch == '-' && (sb.Length == 0 || sb[^1] == '-')) continue;
            sb.Append(ch);
        }
        return sb.ToString().TrimEnd('-');
    }

    public static string CleanCpuName(string processorName)
    {
        var name = processorName;
        foreach (var token in new[] { "(R)", "(TM)", "CPU", "Processor", "@" })
            name = name.Replace(token, " ", StringComparison.OrdinalIgnoreCase);
        return Slugify(name);
    }

    public static string NormalizeOs(string os) =>
        os.ToLowerInvariant() switch
        {
            "linux" => "linux",
            "macos" or "osx" => "macos",
            "windows" => "windows",
            "freebsd" => "freebsd",
            _ => "other",
        };

    /// <summary>Pure fingerprint construction from raw inputs.</summary>
    public static DeviceFingerprint BuildFingerprint(
        string os, string osDescription, string arch, string processorName,
        int physicalCores, int logicalCores, long memoryBytes, string? hypervisor)
    {
        var memoryGb = (int)Math.Round(memoryBytes / (double)(1L << 30), MidpointRounding.ToEven);
        return new DeviceFingerprint(
            NormalizeOs(os), osDescription, arch.ToLowerInvariant(), CleanCpuName(processorName),
            physicalCores, logicalCores, memoryGb, hypervisor);
    }

    /// <summary>Slug = os-arch-cpu-{logical}c-{mem}gb.</summary>
    public static string BuildSlug(DeviceFingerprint f)
    {
        const int MaxLength = 63;
        var prefix = Slugify($"{f.Os}-{f.Arch}");
        var suffix = Slugify($"{f.LogicalCores}c-{f.MemoryGb}gb");
        var cpu = Slugify(f.Cpu);
        var room = MaxLength - prefix.Length - suffix.Length - 2;
        if (room < 0) room = 0;
        if (cpu.Length > room) cpu = cpu[..room].TrimEnd('-');
        return Slugify(string.Join('-', new[] { prefix, cpu, suffix }.Where(p => p.Length > 0)));
    }

    public static DeviceFingerprint GatherFingerprint()
    {
        var cpu = HostEnvironmentInfo.GetCurrent().Cpu.Value;
        var os = OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "macos"
            : OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsFreeBSD() ? "freebsd"
            : "other";
        var hypervisor = HostEnvironmentInfo.GetCurrent().VirtualMachineHypervisor.Value?.Name;
        return BuildFingerprint(
            os, RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(),
            cpu?.ProcessorName ?? "unknown",
            cpu?.PhysicalCoreCount ?? Environment.ProcessorCount,
            cpu?.LogicalCoreCount ?? Environment.ProcessorCount,
            GetPhysicalMemoryBytes(),
            hypervisor);
    }

    /// <summary>Pure: extracts MemTotal (reported in kB) from /proc/meminfo text, as bytes. Null if absent or malformed.</summary>
    public static long? ParseMemTotalBytes(string meminfo)
    {
        foreach (var line in meminfo.Split('\n'))
        {
            if (!line.StartsWith("MemTotal:", StringComparison.Ordinal)) continue;
            var parts = line["MemTotal:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && long.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var kb) && kb > 0
                && (parts.Length == 1 || parts[1].Equals("kB", StringComparison.OrdinalIgnoreCase)))
                return kb * 1024;
            return null;
        }
        return null;
    }

    /// <summary>Physical RAM in bytes: /proc/meminfo, sysctl or GlobalMemoryStatusEx; falls back to the GC's available-memory figure.</summary>
    public static long GetPhysicalMemoryBytes()
    {
        try
        {
            long? bytes = null;
            if (OperatingSystem.IsLinux())
                bytes = ParseMemTotalBytes(File.ReadAllText("/proc/meminfo"));
            else if (OperatingSystem.IsMacOS())
                bytes = ReadSysctlMemSize();
            else if (OperatingSystem.IsWindows())
                bytes = ReadWindowsTotalPhys();
            if (bytes is > 0) return bytes.Value;
        }
        catch
        {
            // fall through to the GC figure
        }
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    private static long? ReadSysctlMemSize()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("sysctl", "-n hw.memsize")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return null;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 && long.TryParse(output.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static long? ReadWindowsTotalPhys()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : null;
    }

    /// <summary>Names of fingerprint fields that differ, ignoring OsDescription.</summary>
    public static IReadOnlyList<string> DifferingFields(DeviceFingerprint stored, DeviceFingerprint current)
    {
        var diffs = new List<string>();
        void Check<T>(string name, T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) diffs.Add(name); }
        Check("os", stored.Os, current.Os);
        Check("arch", stored.Arch, current.Arch);
        Check("cpu", stored.Cpu, current.Cpu);
        Check("physicalCores", stored.PhysicalCores, current.PhysicalCores);
        Check("logicalCores", stored.LogicalCores, current.LogicalCores);
        Check("memoryGb", stored.MemoryGb, current.MemoryGb);
        Check("hypervisor", stored.Hypervisor, current.Hypervisor);
        return diffs;
    }
}
