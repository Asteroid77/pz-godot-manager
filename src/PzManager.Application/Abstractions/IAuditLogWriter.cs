using PzManager.Application.Audit;

namespace PzManager.Application.Abstractions;

public interface IAuditLogWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

