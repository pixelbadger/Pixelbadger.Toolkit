using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public AtomicFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Replace_ShouldReplaceContent_WhenWriteSucceeds()
    {
        var path = Path.Combine(_dir, "a.opus");
        File.WriteAllBytes(path, [1, 2, 3]);
        AtomicFile.Replace(path, s => s.Write([9, 8]));
        File.ReadAllBytes(path).Should().Equal(9, 8);
        Directory.GetFileSystemEntries(_dir).Should().HaveCount(1);
    }

    [Fact]
    public void Replace_ShouldLeaveOriginalAndNoTempFile_WhenWriteThrows()
    {
        var path = Path.Combine(_dir, "a.opus");
        File.WriteAllBytes(path, [1, 2, 3]);
        var act = () => AtomicFile.Replace(path, s => { s.Write([7, 7, 7, 7]); throw new InvalidOperationException("boom"); });
        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
        File.ReadAllBytes(path).Should().Equal(1, 2, 3);
        Directory.GetFileSystemEntries(_dir).Should().ContainSingle().Which.Should().Be(path);
        Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Replace_ShouldCreateFile_WhenPathDoesNotExist()
    {
        var path = Path.Combine(_dir, "new.opus");
        AtomicFile.Replace(path, s => s.Write([5]));
        File.ReadAllBytes(path).Should().Equal(5);
        Directory.GetFileSystemEntries(_dir).Should().HaveCount(1);
    }

    [Fact]
    public void Replace_ShouldNotCreateFile_WhenWriteThrowsAndPathDoesNotExist()
    {
        var path = Path.Combine(_dir, "new.opus");
        var act = () => AtomicFile.Replace(path, _ => throw new IOException("fail"));
        act.Should().Throw<IOException>();
        Directory.GetFileSystemEntries(_dir).Should().BeEmpty();
    }

    [Fact]
    public void Replace_ShouldUseTempNameInSameDirectory_WhenWriting()
    {
        var path = Path.Combine(_dir, "a.opus");
        string? tempName = null;
        AtomicFile.Replace(path, s =>
        {
            tempName = Directory.GetFiles(_dir).Single();
            s.WriteByte(1);
        });
        Path.GetFileName(tempName).Should().StartWith(".a.opus.").And.EndWith(".tmp");
    }

    [Fact]
    public void Replace_ShouldEditLinkTargetAndKeepLinkAndMode_WhenPathIsSymlinkToPrivateFile()
    {
        var target = Path.Combine(_dir, "real.opus");
        var link = Path.Combine(_dir, "link.opus");
        File.WriteAllBytes(target, [1, 2, 3]);
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.CreateSymbolicLink(link, "real.opus");

        AtomicFile.Replace(link, s => s.Write([9, 8]));

        new FileInfo(link).LinkTarget.Should().Be("real.opus");
        File.ReadAllBytes(target).Should().Equal(9, 8);
        File.GetUnixFileMode(target).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.GetFileSystemEntries(_dir).Should().HaveCount(2);
    }

    [Fact]
    public void Replace_ShouldKeepMode_WhenPlainFileHasRestrictedMode()
    {
        var path = Path.Combine(_dir, "a.opus");
        File.WriteAllBytes(path, [1]);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        AtomicFile.Replace(path, s => s.Write([2]));

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
    }

    [Fact]
    public void Replace_ShouldLeaveTargetUntouched_WhenWriteThrowsThroughSymlink()
    {
        var target = Path.Combine(_dir, "real.opus");
        var link = Path.Combine(_dir, "link.opus");
        File.WriteAllBytes(target, [1, 2, 3]);
        File.CreateSymbolicLink(link, target);

        var act = () => AtomicFile.Replace(link, _ => throw new IOException("fail"));

        act.Should().Throw<IOException>();
        File.ReadAllBytes(target).Should().Equal(1, 2, 3);
        new FileInfo(link).LinkTarget.Should().Be(target);
        Directory.GetFileSystemEntries(_dir).Should().HaveCount(2);
    }
}
