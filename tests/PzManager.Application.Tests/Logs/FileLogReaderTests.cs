using PzManager.Infrastructure.Logs;
using Xunit;

namespace PzManager.Application.Tests.Logs;

public sealed class FileLogReaderTests
{
    [Fact]
    public void FindTailStartOffset_ReturnsExpectedOffsets()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "test.log");
            File.WriteAllText(path, "a\nb\nc\n");

            Assert.Equal(4, FileLogReader.FindTailStartOffset(path, tailLines: 1));
            Assert.Equal(2, FileLogReader.FindTailStartOffset(path, tailLines: 2));
            Assert.Equal(0, FileLogReader.FindTailStartOffset(path, tailLines: 3));
            Assert.Equal(0, FileLogReader.FindTailStartOffset(path, tailLines: 99));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Tail_ReturnsLastLines()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "test.log");
            File.WriteAllText(path, "a\nb\nc\n");

            var lines = FileLogReader.Tail(path, maxLines: 2);
            Assert.Equal(new[] { "b", "c" }, lines);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task FollowAsync_YieldsLinesWithOffsets_AndWaitsForAppend()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "test.log");
            File.WriteAllText(path, "a\nb\n");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var e = FileLogReader.FollowAsync(path, startOffset: 0, cts.Token).GetAsyncEnumerator(cts.Token);

            Assert.True(await e.MoveNextAsync());
            Assert.Equal("a", e.Current.Text);
            Assert.Equal(2, e.Current.Offset);

            Assert.True(await e.MoveNextAsync());
            Assert.Equal("b", e.Current.Text);
            Assert.Equal(4, e.Current.Offset);

            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var e2 = FileLogReader.FollowAsync(path, startOffset: 4, cts2.Token).GetAsyncEnumerator(cts2.Token);

            _ = Task.Run(async () =>
            {
                await Task.Delay(150, cts2.Token);
                await File.AppendAllTextAsync(path, "c\n", cts2.Token);
            }, cts2.Token);

            Assert.True(await e2.MoveNextAsync());
            Assert.Equal("c", e2.Current.Text);
            Assert.Equal(6, e2.Current.Offset);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pzmanager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }
}

