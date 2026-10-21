using FluentAssertions;
using Pixelbadger.Toolkit.Benchmarks.Results;

namespace Pixelbadger.Toolkit.Tests.Benchmarks;

public class DeviceIdentityTests
{
    [Fact]
    public void BuildSlug_ShouldProduceExpectedSlug_WhenGivenXeonLinuxInputs()
    {
        var f = DeviceIdentity.BuildFingerprint("linux", "Ubuntu", "X64", "Intel(R) Xeon(R) CPU @ 2.80GHz", 2, 4, 16L << 30, null);

        DeviceIdentity.BuildSlug(f).Should().Be("linux-x64-intel-xeon-2-80ghz-4c-16gb");
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-9750H CPU @ 2.60GHz", "intel-core-i7-9750h-2-60ghz")]
    [InlineData("AMD Ryzen 9 5900X 12-Core Processor", "amd-ryzen-9-5900x-12-core")]
    [InlineData("Apple M2 Pro", "apple-m2-pro")]
    [InlineData("  --Weird__Name!!  ", "weird-name")]
    public void CleanCpuName_ShouldSlugifyProcessorName_WhenVariousVendors(string input, string expected)
    {
        DeviceIdentity.CleanCpuName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("a--b", "a-b")]
    [InlineData("-a-", "a")]
    [InlineData("A B_C", "a-b-c")]
    public void Slugify_ShouldCollapseAndTrimDashes_WhenInputHasSeparators(string input, string expected)
    {
        DeviceIdentity.Slugify(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Linux", "linux")]
    [InlineData("MacOS", "macos")]
    [InlineData("OSX", "macos")]
    [InlineData("Windows", "windows")]
    [InlineData("FreeBSD", "freebsd")]
    [InlineData("Solaris", "other")]
    public void NormalizeOs_ShouldMapToKnownNames(string input, string expected)
    {
        DeviceIdentity.NormalizeOs(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(16L << 30, 16)]
    [InlineData((16L << 30) - (300L << 20), 16)]
    [InlineData(8L << 30, 8)]
    [InlineData((long)(7.4 * (1L << 30)), 7)]
    public void BuildFingerprint_ShouldRoundMemoryToWholeGb(long bytes, int expectedGb)
    {
        DeviceIdentity.BuildFingerprint("linux", "x", "x64", "cpu", 1, 1, bytes, null).MemoryGb.Should().Be(expectedGb);
    }

    [Fact]
    public void BuildFingerprint_ShouldLowercaseArch_AndKeepOsDescription()
    {
        var f = DeviceIdentity.BuildFingerprint("linux", "Ubuntu 24.04", "Arm64", "Apple M2", 4, 8, 8L << 30, "KVM");

        f.Arch.Should().Be("arm64");
        f.OsDescription.Should().Be("Ubuntu 24.04");
        f.Hypervisor.Should().Be("KVM");
    }

    [Theory]
    [InlineData("my-laptop", true)]
    [InlineData("a", true)]
    [InlineData("0abc", true)]
    [InlineData("", false)]
    [InlineData("-abc", false)]
    [InlineData("Abc", false)]
    [InlineData("a_b", false)]
    [InlineData("a b", false)]
    [InlineData("../x", false)]
    public void IsValidDeviceName_ShouldMatchPattern(string name, bool expected)
    {
        DeviceIdentity.IsValidDeviceName(name).Should().Be(expected);
    }

    [Fact]
    public void IsValidDeviceName_ShouldAccept63Characters_AndRejectLonger()
    {
        DeviceIdentity.IsValidDeviceName(new string('a', 63)).Should().BeTrue();
        DeviceIdentity.IsValidDeviceName(new string('a', 64)).Should().BeFalse();
        DeviceIdentity.IsValidDeviceName(null).Should().BeFalse();
    }

    [Fact]
    public void DifferingFields_ShouldIgnoreOsDescription_AndNameOtherDifferences()
    {
        var a = BenchmarkTestData.Fingerprint();
        var b = a with { OsDescription = "Other", LogicalCores = 8, MemoryGb = 32, Hypervisor = "KVM" };

        DeviceIdentity.DifferingFields(a, b).Should().Equal("logicalCores", "memoryGb", "hypervisor");
        DeviceIdentity.DifferingFields(a, a with { OsDescription = "Other" }).Should().BeEmpty();
    }

    [Fact]
    public void GatherFingerprint_ShouldYieldValidSlug_OnThisMachine()
    {
        var f = DeviceIdentity.GatherFingerprint();
        var slug = DeviceIdentity.BuildSlug(f);

        DeviceIdentity.IsValidDeviceName(slug).Should().BeTrue();
        f.LogicalCores.Should().BeGreaterThan(0);
        f.MemoryGb.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ParseMemTotalBytes_ShouldConvertKbToBytes_WhenMeminfoHasMemTotal()
    {
        var text = "MemTotal:       16465532 kB\nMemFree:         1000000 kB\n";

        DeviceIdentity.ParseMemTotalBytes(text).Should().Be(16465532L * 1024);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MemFree: 100 kB\n")]
    [InlineData("MemTotal: abc kB\n")]
    [InlineData("MemTotal: 100 MB\n")]
    [InlineData("MemTotal: 0 kB\n")]
    public void ParseMemTotalBytes_ShouldReturnNull_WhenMemTotalMissingOrMalformed(string text)
    {
        DeviceIdentity.ParseMemTotalBytes(text).Should().BeNull();
    }

    [Fact]
    public void ParseMemTotalBytes_ShouldGive16Gb_WhenMemTotalIs15Point7GiB()
    {
        var bytes = DeviceIdentity.ParseMemTotalBytes("MemTotal:       16465532 kB\n")!.Value;

        DeviceIdentity.BuildFingerprint("linux", "x", "X64", "Intel(R) Xeon(R) CPU @ 2.80GHz", 2, 4, bytes, null).MemoryGb.Should().Be(16);
    }

    [Fact]
    public void GetPhysicalMemoryBytes_ShouldBePositive_OnThisMachine()
    {
        DeviceIdentity.GetPhysicalMemoryBytes().Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildSlug_ShouldNotExceed63CharsAndKeepPrefixAndSuffix_WhenProcessorNameIsVeryLong()
    {
        var longName = string.Join(' ', Enumerable.Repeat("Quantum Mega Core", 8)).Substring(0, 120);
        longName.Length.Should().Be(120);
        var f = DeviceIdentity.BuildFingerprint("Linux", "Ubuntu", "X64", longName, 8, 16, 32L << 30, null);

        var slug = DeviceIdentity.BuildSlug(f);

        slug.Length.Should().BeLessThanOrEqualTo(63);
        slug.Should().StartWith("linux-x64-quantum-mega-core").And.EndWith("-16c-32gb");
        slug.Should().NotContain("--");
        DeviceIdentity.IsValidDeviceName(slug).Should().BeTrue();
    }

    [Fact]
    public void BuildSlug_ShouldNotLeaveDoubleDash_WhenTruncationLandsOnDash()
    {
        // Choose a cpu whose cut point falls just after a dash.
        var cpu = new string('a', 30) + "-" + new string('b', 40);
        var f = new DeviceFingerprint("linux", "x", "x64", cpu, 1, 4, 16, null);
        var slug = DeviceIdentity.BuildSlug(f);

        slug.Length.Should().BeLessThanOrEqualTo(63);
        slug.Should().NotContain("--");
        slug.Should().EndWith("-4c-16gb");
        DeviceIdentity.IsValidDeviceName(slug).Should().BeTrue();
    }
}
