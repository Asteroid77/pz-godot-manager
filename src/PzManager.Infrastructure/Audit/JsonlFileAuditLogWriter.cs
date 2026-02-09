using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PzManager.Application.Abstractions;
using PzManager.Application.Audit;

namespace PzManager.Infrastructure.Audit;

public sealed class JsonlFileAuditLogWriter : IAuditLogWriter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly JsonSerializerOptions _json;

    public JsonlFileAuditLogWriter(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("path required", nameof(path));
        }

        _path = path;
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(auditEvent, _json) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fs = new FileStream(
                _path,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous);

            fs.Seek(0, SeekOrigin.End);
            await fs.WriteAsync(bytes, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

