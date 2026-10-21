using FluentAssertions;
using Pixelbadger.Toolkit.Benchmarks.Results;

namespace Pixelbadger.Toolkit.Tests.Benchmarks;

public class ResultStoreTests : TempDirTestBase
{
    [Fact]
    public void RunFileName_ShouldUseTimestampTierAndShortSha_WhenClean()
    {
        var name = ResultStore.RunFileName(new DateTime(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc), "standard", "0123456789abcdef", false);

        name.Should().Be("20261020T010203Z-standard-0123456.json");
    }

    [Fact]
    public void RunFileName_ShouldAppendDirty_WhenDirty()
    {
        var name = ResultStore.RunFileName(new DateTime(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc), "slow", "abcdef1234", true);

        name.Should().Be("20261020T010203Z-slow-abcdef1-dirty.json");
    }

    [Fact]
    public void RunFileName_ShouldUseNogit_WhenShaMissing()
    {
        var name = ResultStore.RunFileName(new DateTime(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc), "standard", null, null);

        name.Should().Be("20261020T010203Z-standard-nogit.json");
    }

    [Fact]
    public void RunRelativePath_ShouldUseForwardSlashes()
    {
        ResultStore.RunRelativePath("dev", "8.2.0", "f.json").Should().Be("dev/8.2.0/f.json");
    }

    [Theory]
    [InlineData("8.2.0+abc123", "8.2.0")]
    [InlineData("8.2.0", "8.2.0")]
    [InlineData("8.2.0-beta.1+sha.5", "8.2.0-beta.1")]
    public void CleanVersion_ShouldRemovePlusSuffix(string input, string expected)
    {
        ResultStore.CleanVersion(input).Should().Be(expected);
    }

    [Fact]
    public void SaveRun_ShouldWriteDeviceJsonAndRunFileAtLayoutPath_WhenFirstRun()
    {
        var device = BenchmarkTestData.Device();
        var run = BenchmarkTestData.Run(dirty: true);

        var relative = ResultStore.SaveRun(Root, device, run);

        relative.Should().Be($"{device.Slug}/8.2.0/20261020T123045Z-standard-0123456-dirty.json");
        File.Exists(Path.Combine(Root, relative)).Should().BeTrue();
        File.Exists(ResultStore.DevicePath(Root, device.Slug)).Should().BeTrue();
        Directory.GetFiles(Root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void SaveRun_ShouldRoundTripRunAndDevice_ThroughLoadAll()
    {
        var device = BenchmarkTestData.Device();
        var run = BenchmarkTestData.Run(results: new[]
        {
            BenchmarkTestData.Result("B", "M2", "N=1"),
            BenchmarkTestData.Result("A", "M1"),
            BenchmarkTestData.Result("B", "M2", "N=0", success: false),
        });

        ResultStore.SaveRun(Root, device, run);
        var loaded = ResultStore.LoadAll(Root);

        loaded.Should().ContainSingle();
        loaded[0].Device.Should().Be(device);
        loaded[0].DeviceSlug.Should().Be(device.Slug);
        loaded[0].Version.Should().Be("8.2.0");
        loaded[0].Run.Should().BeEquivalentTo(ResultsJson.Normalize(run), o => o.WithStrictOrdering());
        loaded[0].Run.Results.Select(r => $"{r.Class}.{r.Method}({r.Parameters})")
            .Should().Equal("A.M1()", "B.M2(N=0)", "B.M2(N=1)");
    }

    [Fact]
    public void ResultsJson_ShouldWriteCamelCaseNullsAndZTimestamp()
    {
        var json = ResultsJson.Serialize(BenchmarkTestData.Run(sha: null, dirty: null, results: BenchmarkTestData.Result() with { AllocatedBytes = null }));

        json.Should().Contain("\"schemaVersion\": 1");
        json.Should().Contain("\"gitSha\": null");
        json.Should().Contain("\"gitDirty\": null");
        json.Should().Contain("\"allocatedBytes\": null");
        json.Should().Contain("\"capturedAtUtc\": \"2026-10-20T12:30:45Z\"");
        json.Should().Contain("\"stdDevNs\"");
    }

    [Fact]
    public void SaveRun_ShouldThrowAndLeaveFirstFileByteIdentical_WhenFilenameCollides()
    {
        var device = BenchmarkTestData.Device();
        var run = BenchmarkTestData.Run();
        var relative = ResultStore.SaveRun(Root, device, run);
        var path = Path.Combine(Root, relative);
        var before = File.ReadAllBytes(path);

        var act = () => ResultStore.SaveRun(Root, device, run with { TotalSeconds = 99 });

        act.Should().Throw<RunFileExistsException>();
        File.ReadAllBytes(path).Should().Equal(before);
        Directory.GetFiles(Root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void SaveRun_ShouldAddSecondFile_WhenTimestampDiffers()
    {
        var device = BenchmarkTestData.Device();
        var first = ResultStore.SaveRun(Root, device, BenchmarkTestData.Run());
        var before = File.ReadAllBytes(Path.Combine(Root, first));

        ResultStore.SaveRun(Root, device, BenchmarkTestData.Run(at: new DateTime(2026, 10, 20, 12, 30, 46, DateTimeKind.Utc)));

        Directory.GetFiles(Path.Combine(Root, device.Slug, "8.2.0"), "*.json").Should().HaveCount(2);
        File.ReadAllBytes(Path.Combine(Root, first)).Should().Equal(before);
    }

    [Fact]
    public void CheckDevice_ShouldPassAndWriteNothing_WhenNoDeviceJson()
    {
        ResultStore.CheckDevice(Root, BenchmarkTestData.Device());

        Directory.GetFileSystemEntries(Root).Should().BeEmpty();
    }

    [Fact]
    public void CheckDevice_ShouldPass_WhenFingerprintMatchesIgnoringOsDescription()
    {
        var device = BenchmarkTestData.Device();
        ResultStore.SaveRun(Root, device, BenchmarkTestData.Run());
        var current = device with { Fingerprint = device.Fingerprint with { OsDescription = "Ubuntu 26.04" } };

        var act = () => ResultStore.CheckDevice(Root, current);

        act.Should().NotThrow();
    }

    [Fact]
    public void CheckDevice_ShouldThrowNamingDifferingFields_WhenFingerprintMismatches()
    {
        var device = BenchmarkTestData.Device();
        ResultStore.SaveRun(Root, device, BenchmarkTestData.Run());
        var changed = BenchmarkTestData.Device(logicalCores: 8);

        var act = () => ResultStore.CheckDevice(Root, changed);

        var ex = act.Should().Throw<DeviceMismatchException>().Which;
        ex.Fields.Should().Equal("logicalCores");
        ex.Message.Should().Contain("logicalCores").And.Contain("--device");
    }

    [Fact]
    public void SaveRun_ShouldNotModifyDeviceJson_OnSecondRun()
    {
        var device = BenchmarkTestData.Device();
        ResultStore.SaveRun(Root, device, BenchmarkTestData.Run());
        var before = File.ReadAllBytes(ResultStore.DevicePath(Root, device.Slug));

        ResultStore.SaveRun(Root, device with { Fingerprint = device.Fingerprint with { OsDescription = "Changed" } },
            BenchmarkTestData.Run(at: new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)));

        File.ReadAllBytes(ResultStore.DevicePath(Root, device.Slug)).Should().Equal(before);
    }

    [Fact]
    public void LoadAll_ShouldOrderOrdinalByRelativePath()
    {
        var a = BenchmarkTestData.Device("zzz-device");
        var b = BenchmarkTestData.Device("aaa-device");
        BenchmarkTestData.Write(Root, a, BenchmarkTestData.Run(version: "8.2.0"));
        BenchmarkTestData.Write(Root, b, BenchmarkTestData.Run(version: "8.10.0"));
        BenchmarkTestData.Write(Root, b, BenchmarkTestData.Run(version: "8.2.0"));

        ResultStore.LoadAll(Root).Select(r => r.RelativePath.Split('/')[0] + "/" + r.Version)
            .Should().Equal("aaa-device/8.10.0", "aaa-device/8.2.0", "zzz-device/8.2.0");
    }

    [Fact]
    public void LoadAll_ShouldReturnEmpty_WhenDirectoryMissing()
    {
        ResultStore.LoadAll(Path.Combine(Root, "nope")).Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_ShouldThrowNamingFile_WhenRunFileIsCorrupt()
    {
        var path = BenchmarkTestData.Write(Root, BenchmarkTestData.Device(), BenchmarkTestData.Run());
        File.WriteAllText(path, "{ not json");

        var act = () => ResultStore.LoadAll(Root);

        act.Should().Throw<InvalidDataException>().WithMessage($"*{path}*");
    }

    [Fact]
    public void LoadAll_ShouldThrow_WhenRequiredFieldMissing()
    {
        var path = BenchmarkTestData.Write(Root, BenchmarkTestData.Device(), BenchmarkTestData.Run());
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"toolkitVersion\": \"8.2.0\",", ""));

        var act = () => ResultStore.LoadAll(Root);

        act.Should().Throw<InvalidDataException>().WithMessage($"*{path}*");
    }

    [Fact]
    public void LoadAll_ShouldThrow_WhenDeviceJsonMissing()
    {
        var device = BenchmarkTestData.Device();
        BenchmarkTestData.Write(Root, device, BenchmarkTestData.Run());
        File.Delete(ResultStore.DevicePath(Root, device.Slug));

        var act = () => ResultStore.LoadAll(Root);

        act.Should().Throw<InvalidDataException>().WithMessage("*device.json*");
    }

    [Fact]
    public void EnsureWritable_ShouldCreateDirectory_AndLeaveNoProbe()
    {
        var dir = Path.Combine(Root, "new", "results");

        ResultStore.EnsureWritable(dir);

        Directory.Exists(dir).Should().BeTrue();
        Directory.GetFileSystemEntries(dir).Should().BeEmpty();
    }

    [Fact]
    public void EnsureWritable_ShouldThrow_WhenPathIsAFile()
    {
        var file = Path.Combine(Root, "afile");
        File.WriteAllText(file, "x");

        var act = () => ResultStore.EnsureWritable(file);

        act.Should().Throw<IOException>();
    }
}
