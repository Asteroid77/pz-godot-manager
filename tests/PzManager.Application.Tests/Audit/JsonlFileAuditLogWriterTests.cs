using System.Text.Json;
using PzManager.Application.Audit;
using PzManager.Infrastructure.Audit;
using Xunit;

namespace PzManager.Application.Tests.Audit;

public sealed class JsonlFileAuditLogWriterTests
{
    [Fact]
    public async Task WriteAsync_AppendsJsonLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pz-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "audit.jsonl");

        var writer = new JsonlFileAuditLogWriter(path);
        await writer.WriteAsync(
            new AuditEvent(
                TimeUtc: new DateTimeOffset(2026, 2, 6, 0, 0, 0, TimeSpan.Zero),
                ActorDeviceId: "dev1",
                ActorRole: "admin",
                Action: "pairing.create",
                TargetId: null,
                Data: new Dictionary<string, string> { ["role"] = "admin" }),
            CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Single(lines);

        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("pairing.create", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal("dev1", doc.RootElement.GetProperty("actorDeviceId").GetString());
        Assert.Equal("admin", doc.RootElement.GetProperty("actorRole").GetString());
    }
}

