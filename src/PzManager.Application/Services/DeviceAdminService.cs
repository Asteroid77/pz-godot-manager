using PzManager.Application.Abstractions;
using PzManager.Application.Errors;
using PzManager.Domain.Devices;
using PzManager.Domain.Security;

namespace PzManager.Application.Services;

public sealed class DeviceAdminService
{
    private readonly IDeviceRepository _devices;

    public DeviceAdminService(IDeviceRepository devices)
    {
        _devices = devices;
    }

    public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken)
    {
        return _devices.ListAsync(cancellationToken);
    }

    public async Task<Device> RevokeAsync(DeviceId deviceId, string? note, CancellationToken cancellationToken)
    {
        var existing = await _devices.FindByIdAsync(deviceId, cancellationToken);
        if (existing is null)
        {
            throw new AppException(AppErrorCodes.BadRequest, "device not found");
        }

        if (existing.Revoked)
        {
            return existing;
        }

        await EnsureNotLastAdminAsync(existing, newRole: null, willRevoke: true, cancellationToken);

        var updated = existing with { Revoked = true, Note = note ?? existing.Note };
        await _devices.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<Device> UpdateAsync(DeviceId deviceId, Role? role, string? note, CancellationToken cancellationToken)
    {
        var existing = await _devices.FindByIdAsync(deviceId, cancellationToken);
        if (existing is null)
        {
            throw new AppException(AppErrorCodes.BadRequest, "device not found");
        }

        var nextRole = role ?? existing.Role;
        var nextNote = note ?? existing.Note;

        await EnsureNotLastAdminAsync(existing, newRole: nextRole, willRevoke: false, cancellationToken);

        if (nextRole == existing.Role && nextNote == existing.Note)
        {
            return existing;
        }

        var updated = existing with { Role = nextRole, Note = nextNote };
        await _devices.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    private async Task EnsureNotLastAdminAsync(Device existing, Role? newRole, bool willRevoke, CancellationToken cancellationToken)
    {
        if (existing.Revoked)
        {
            return;
        }

        var isRemovingAdmin = existing.Role == Role.Admin && (willRevoke || (newRole is not null && newRole.Value != Role.Admin));
        if (!isRemovingAdmin)
        {
            return;
        }

        var all = await _devices.ListAsync(cancellationToken);
        var activeAdminsExcludingSelf = all.Count(d => d.Role == Role.Admin && !d.Revoked && d.Id != existing.Id);
        if (activeAdminsExcludingSelf == 0)
        {
            throw new AppException(AppErrorCodes.Forbidden, "cannot remove last admin device");
        }
    }
}
