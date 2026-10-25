using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class DfrotzGameEngineTests : IDisposable
{
    private const string Noise = ">Please enter a filename [autosave.qzl]: \n\nOk.\n\n>\n\nAre you sure you want to quit? \n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _gameFile;
    private readonly IfSaveStore _store;
    private readonly Mock<IIfProcessRunner> _runner = new();
    private readonly List<(string File, List<string> Args, string Stdin, TimeSpan Timeout)> _calls = [];

    public DfrotzGameEngineTests()
    {
        Directory.CreateDirectory(_root);
        _gameFile = Path.Combine(_root, "story.z8");
        File.WriteAllBytes(_gameFile, [7, 7, 7]);
        _store = IfSaveStore.Open(_gameFile, Path.Combine(_root, "saves"));
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private DfrotzGameEngine Engine(TimeSpan? timeout = null) => new(_runner.Object, "/opt/dfrotz", _gameFile, _store, timeout);

    /// <summary>Scripts the runner; the handler may write the temp save named in stdin.</summary>
    private void Script(Func<string, IfProcessResult> handler)
    {
        _runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((string file, IReadOnlyList<string> args, string stdin, TimeSpan timeout, CancellationToken _) =>
            {
                _calls.Add((file, args.ToList(), stdin, timeout));
                return Task.FromResult(handler(stdin));
            });
    }

    /// <summary>The file name typed at the save prompt (relative to the game directory).</summary>
    private static string TypedName(string stdin)
    {
        var lines = stdin.Split('\n');
        var i = Array.IndexOf(lines, "save");
        return lines[i + 1];
    }

    /// <summary>Absolute path of the temp save the typed name resolves to under -R.</summary>
    private string TempPath(string stdin) => Path.Combine(_store.GameDirectory, TypedName(stdin));

    [Fact]
    public async Task RunTurnAsync_ShouldPassExpectedArgumentsAndStdin()
    {
        File.WriteAllText(_store.AutosavePath, "old");
        Script(stdin => { File.WriteAllText(TempPath(stdin), "new"); return new IfProcessResult(0, "Room.\n" + Noise, false); });

        await Engine().RunTurnAsync("open door", default);

        var call = _calls.Single();
        call.File.Should().Be("/opt/dfrotz");
        call.Args.Should().Equal("-m", "-p", "-q", "-w", "80", "-R", _store.GameDirectory, "-L", _store.AutosavePath, _gameFile);
        var typed = TypedName(call.Stdin);
        typed.Should().Be(Path.GetFileName(typed));
        call.Stdin.Should().Be($"open door\nsave\n{typed}\nquit\ny\n");
        call.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunTurnAsync_ShouldTypeOnlyShortFileName_AtSavePrompt()
    {
        File.WriteAllText(_store.AutosavePath, "old");
        Script(stdin => { File.WriteAllText(TempPath(stdin), "new"); return new IfProcessResult(0, "Room.\n", false); });

        var result = await Engine().RunTurnAsync("look", default);

        result.Status.Should().Be(IfTurnStatus.Saved);
        var typed = TypedName(_calls.Single().Stdin);
        typed.Should().NotContain("/").And.NotContain("\\");
        typed.Length.Should().BeLessThanOrEqualTo(16);
        typed.Should().MatchRegex("^t[0-9a-f]{8}\\.qzl$");
    }

    [Fact]
    public async Task StartNewGameAsync_ShouldTypeOnlyShortFileName_WhenGameDirectoryPathIsLong()
    {
        var longRoot = Path.Combine(_root, new string('d', 80), new string('e', 80));
        Directory.CreateDirectory(longRoot);
        using var store = IfSaveStore.Open(_gameFile, longRoot);
        store.GameDirectory.Length.Should().BeGreaterThan(150);
        var engine = new DfrotzGameEngine(_runner.Object, "/opt/dfrotz", _gameFile, store);
        Script(stdin => { File.WriteAllText(Path.Combine(store.GameDirectory, TypedName(stdin)), "new"); return new IfProcessResult(0, "Intro.\n", false); });

        var result = await engine.StartNewGameAsync(default);

        result.Status.Should().Be(IfTurnStatus.Saved);
        var call = _calls.Single();
        call.Args.Should().Contain(["-R", store.GameDirectory]);
        var typed = TypedName(call.Stdin);
        typed.Should().Be(Path.GetFileName(typed));
        call.Stdin.Split('\n').Max(l => l.Length).Should().BeLessThanOrEqualTo(16);
        File.Exists(store.AutosavePath).Should().BeTrue();
    }

    [Fact]
    public async Task StartNewGameAsync_ShouldNotPassLoadFlag_AndSendEmptyLineThenLook()
    {
        Script(stdin => { File.WriteAllText(TempPath(stdin), "new"); return new IfProcessResult(0, "Intro.\n" + Noise, false); });

        var result = await Engine().StartNewGameAsync(default);

        var call = _calls.Single();
        call.Args.Should().NotContain("-L");
        call.Args.Should().Equal("-m", "-p", "-q", "-w", "80", "-R", _store.GameDirectory, _gameFile);
        call.Stdin.Should().StartWith("\nlook\nsave\n").And.EndWith("\nquit\ny\n");
        result.Status.Should().Be(IfTurnStatus.Saved);
        result.Output.Should().Be("Intro.");
        _store.HasAutosave.Should().BeTrue();
    }

    [Fact]
    public async Task RunTurnAsync_ShouldCommitAutosaveAtomically_WhenTempSavedNonEmpty()
    {
        File.WriteAllText(_store.AutosavePath, "old");
        Script(stdin => { File.WriteAllText(TempPath(stdin), "new"); return new IfProcessResult(0, "Hello.\n" + Noise, false); });

        var result = await Engine().RunTurnAsync("look", default);

        result.Should().Be(new IfTurnResult("Hello.", IfTurnStatus.Saved));
        File.ReadAllText(_store.AutosavePath).Should().Be("new");
        Directory.GetFiles(_store.GameDirectory, "t*.qzl").Should().BeEmpty();
    }

    [Fact]
    public async Task RunTurnAsync_ShouldReturnNotSaved_AndKeepAutosaveByteIdentical_WhenNoTempFile()
    {
        var original = new byte[] { 1, 2, 3, 250 };
        File.WriteAllBytes(_store.AutosavePath, original);
        Script(_ => new IfProcessResult(0, "*** You have died ***\n", false));

        var result = await Engine().RunTurnAsync("jump", default);

        result.Status.Should().Be(IfTurnStatus.NotSaved);
        result.Output.Should().Be("*** You have died ***");
        File.ReadAllBytes(_store.AutosavePath).Should().Equal(original);
    }

    [Fact]
    public async Task RunTurnAsync_ShouldReturnNotSaved_AndDiscardTemp_WhenTempFileEmpty()
    {
        File.WriteAllText(_store.AutosavePath, "old");
        Script(stdin => { File.WriteAllBytes(TempPath(stdin), []); return new IfProcessResult(0, "Text\n", false); });

        var result = await Engine().RunTurnAsync("x", default);

        result.Status.Should().Be(IfTurnStatus.NotSaved);
        File.ReadAllText(_store.AutosavePath).Should().Be("old");
        Directory.GetFiles(_store.GameDirectory, "t*.qzl").Should().BeEmpty();
    }

    [Fact]
    public async Task RunTurnAsync_ShouldReturnTimedOut_AndKeepAutosave_WhenRunnerTimesOut()
    {
        File.WriteAllText(_store.AutosavePath, "old");
        Script(stdin => { File.WriteAllText(TempPath(stdin), "partial"); return new IfProcessResult(-1, "partial output", true); });

        var result = await Engine(TimeSpan.FromSeconds(3)).RunTurnAsync("x", default);

        result.Should().Be(new IfTurnResult("The interpreter timed out.", IfTurnStatus.TimedOut));
        File.ReadAllText(_store.AutosavePath).Should().Be("old");
        Directory.GetFiles(_store.GameDirectory, "t*.qzl").Should().BeEmpty();
        _calls.Single().Timeout.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RunTurnAsync_ShouldReturnFailed_WhenNonZeroExitAndNoOutput()
    {
        File.WriteAllText(_store.AutosavePath, "old");
        Script(_ => new IfProcessResult(2, "", false));

        var result = await Engine().RunTurnAsync("x", default);

        result.Should().Be(new IfTurnResult("The interpreter failed (exit code 2).", IfTurnStatus.Failed));
        File.ReadAllText(_store.AutosavePath).Should().Be("old");
    }

    [Fact]
    public async Task RunTurnAsync_ShouldReturnNotSaved_WhenNonZeroExitButHasOutput()
    {
        Script(_ => new IfProcessResult(1, "Some text\n", false));

        var result = await Engine().RunTurnAsync("x", default);

        result.Status.Should().Be(IfTurnStatus.NotSaved);
        result.Output.Should().Be("Some text");
    }

    [Fact]
    public async Task RunTurnAsync_ShouldReturnSaved_EvenWhenExitCodeNonZero_IfTempSaved()
    {
        Script(stdin => { File.WriteAllText(TempPath(stdin), "s"); return new IfProcessResult(1, "Text\n", false); });

        var result = await Engine().RunTurnAsync("x", default);

        result.Status.Should().Be(IfTurnStatus.Saved);
    }

    [Fact]
    public async Task RunTurnAsync_ShouldDiscardTemp_AndRethrow_WhenCancelled()
    {
        File.WriteAllText(_store.AutosavePath, "old");
        _runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> _, string stdin, TimeSpan _, CancellationToken _) =>
            {
                File.WriteAllText(TempPath(stdin), "half");
                throw new OperationCanceledException();
            });

        var act = () => Engine().RunTurnAsync("x", default);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(_store.AutosavePath).Should().Be("old");
        Directory.GetFiles(_store.GameDirectory, "t*.qzl").Should().BeEmpty();
    }

    [Fact]
    public async Task RunTurnAsync_ShouldPassCancellationTokenToRunner()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        _runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> _, string _, TimeSpan _, CancellationToken ct) =>
            {
                seen = ct;
                return Task.FromResult(new IfProcessResult(0, "x", false));
            });

        await Engine().RunTurnAsync("x", cts.Token);

        seen.Should().Be(cts.Token);
    }

    [Fact]
    public async Task RunTurnAsync_ShouldNotValidateCommands()
    {
        Script(_ => new IfProcessResult(0, "x\n", false));

        await Engine().RunTurnAsync("quit", default);

        _calls.Should().ContainSingle();
    }
}
