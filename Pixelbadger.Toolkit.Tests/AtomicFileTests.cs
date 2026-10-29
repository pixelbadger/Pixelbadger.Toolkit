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
    public void Replace_ShouldUseTempNameInSameDirectory_WhileWriting()
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
}
