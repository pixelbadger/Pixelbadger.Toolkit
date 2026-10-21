using FluentAssertions;
using Pixelbadger.Toolkit.Benchmarks.Results;

namespace Pixelbadger.Toolkit.Tests.Benchmarks;

public class ResultsValidatorTests : TempDirTestBase
{
    private readonly DeviceRecord _device = BenchmarkTestData.Device();

    [Fact]
    public void Validate_ShouldReturnNoProblems_WhenTreeIsClean()
    {
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run(tier: "slow", dirty: true, job: "short", at: new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)));
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run(sha: null, dirty: null, at: new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc)));

        ResultsValidator.Validate(Root).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldReturnNoProblems_WhenDirectoryIsEmpty()
    {
        ResultsValidator.Validate(Root).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldReportMissingDirectory()
    {
        ResultsValidator.Validate(Path.Combine(Root, "missing")).Should().ContainSingle().Which.Should().Contain("not found");
    }

    [Fact]
    public void Validate_ShouldFlagCorruptJson_WithRelativePath()
    {
        var path = BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.WriteAllText(path, "{ nope");

        var problems = ResultsValidator.Validate(Root);

        problems.Should().ContainSingle().Which.Should().StartWith($"{_device.Slug}/8.2.0/{Path.GetFileName(path)}:");
    }

    [Fact]
    public void Validate_ShouldFlagMissingRequiredField()
    {
        var path = BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"totalSeconds\": 12.3,", ""));

        ResultsValidator.Validate(Root).Should().ContainSingle().Which.Should().StartWith($"{_device.Slug}/8.2.0/{Path.GetFileName(path)}:");
    }

    [Fact]
    public void Validate_ShouldFlagVersionFolderMismatch()
    {
        var path = BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        var wrong = Path.Combine(Root, _device.Slug, "9.9.9");
        Directory.CreateDirectory(wrong);
        File.Move(path, Path.Combine(wrong, Path.GetFileName(path)));
        Directory.Delete(Path.GetDirectoryName(path)!);

        ResultsValidator.Validate(Root).Should().ContainSingle()
            .Which.Should().StartWith($"{_device.Slug}/9.9.9/{Path.GetFileName(path)}:").And.Contain("does not match folder");
    }

    [Fact]
    public void Validate_ShouldFlagMisnamedFile()
    {
        var path = BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, "results.json"));

        ResultsValidator.Validate(Root).Should().ContainSingle()
            .Which.Should().StartWith($"{_device.Slug}/8.2.0/results.json:").And.Contain("file name");
    }

    [Fact]
    public void Validate_ShouldFlagFileNameThatDisagreesWithContent()
    {
        var path = BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        var renamed = Path.Combine(Path.GetDirectoryName(path)!, "20200101T000000Z-slow-abcdef0-dirty.json");
        File.Move(path, renamed);

        var problems = ResultsValidator.Validate(Root);

        problems.Should().OnlyContain(p => p.StartsWith($"{_device.Slug}/8.2.0/20200101T000000Z-slow-abcdef0-dirty.json:"));
        problems.Should().Contain(p => p.Contains("timestamp"));
        problems.Should().Contain(p => p.Contains("tier"));
        problems.Should().Contain(p => p.Contains("sha"));
        problems.Should().Contain(p => p.Contains("dirty"));
    }

    [Fact]
    public void Validate_ShouldFlagMissingDeviceJson()
    {
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.Delete(ResultStore.DevicePath(Root, _device.Slug));

        ResultsValidator.Validate(Root).Should().ContainSingle()
            .Which.Should().StartWith($"{_device.Slug}/device.json:").And.Contain("missing");
    }

    [Fact]
    public void Validate_ShouldFlagDeviceSlugMismatch()
    {
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.WriteAllText(ResultStore.DevicePath(Root, _device.Slug), ResultsJson.Serialize(_device with { Slug = "other-device" }));

        ResultsValidator.Validate(Root).Should().ContainSingle()
            .Which.Should().StartWith($"{_device.Slug}/device.json:").And.Contain("other-device");
    }

    [Fact]
    public void Validate_ShouldFlagCorruptDeviceJson()
    {
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.WriteAllText(ResultStore.DevicePath(Root, _device.Slug), "[]");

        ResultsValidator.Validate(Root).Should().ContainSingle().Which.Should().StartWith($"{_device.Slug}/device.json:");
    }

    [Fact]
    public void Validate_ShouldFlagUnsupportedSchemaVersion()
    {
        var path = BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"));

        ResultsValidator.Validate(Root).Should().ContainSingle().Which.Should().Contain("schemaVersion 2");
    }

    [Fact]
    public void Validate_ShouldFlagInvalidTierAndJob()
    {
        var run = BenchmarkTestData.Run(tier: "weird", job: "turbo");
        var path = BenchmarkTestData.Write(Root, _device, run);

        var problems = ResultsValidator.Validate(Root);

        problems.Should().Contain(p => p.Contains("tier 'weird'"));
        problems.Should().Contain(p => p.Contains("job name 'turbo'"));
        problems.Should().OnlyContain(p => p.Contains(Path.GetFileName(path)) || p.Contains("20261020T123045Z"));
    }

    [Fact]
    public void Validate_ShouldFlagNegativeStatistic()
    {
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run(results: BenchmarkTestData.Result(mean: -1)));

        ResultsValidator.Validate(Root).Should().ContainSingle().Which.Should().Contain("negative or non-finite");
    }

    [Fact]
    public void Validate_ShouldFlagStrayFiles()
    {
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.WriteAllText(Path.Combine(Root, "stray.txt"), "x");
        File.WriteAllText(Path.Combine(Root, _device.Slug, "notes.md"), "x");
        File.WriteAllText(Path.Combine(Root, _device.Slug, "8.2.0", "notes.md"), "x");

        var problems = ResultsValidator.Validate(Root);

        problems.Should().HaveCount(3);
        problems.Should().Contain(p => p.StartsWith("stray.txt:"));
        problems.Should().Contain(p => p.StartsWith($"{_device.Slug}/notes.md:"));
        problems.Should().Contain(p => p.StartsWith($"{_device.Slug}/8.2.0/notes.md:"));
    }

    [Fact]
    public void Validate_ShouldFlagStrayTempFiles_AnywhereUnderResultsDir()
    {
        BenchmarkTestData.Write(Root, _device, BenchmarkTestData.Run());
        File.WriteAllText(Path.Combine(Root, _device.Slug, "8.2.0", "x.json.abc.tmp"), "partial");
        File.WriteAllText(Path.Combine(Root, _device.Slug, "device.json.def.tmp"), "partial");
        File.WriteAllText(Path.Combine(Root, "top.tmp"), "partial");

        var problems = ResultsValidator.Validate(Root);

        problems.Should().BeEquivalentTo(new[]
        {
            "top.tmp: stray temp file from an interrupted run; delete it",
            $"{_device.Slug}/device.json.def.tmp: stray temp file from an interrupted run; delete it",
            $"{_device.Slug}/8.2.0/x.json.abc.tmp: stray temp file from an interrupted run; delete it",
        });
    }
}
