using PzManager.Application.Abstractions;
using PzManager.Application.Errors;
using PzManager.Application.Services;
using PzManager.Domain.Devices;
using PzManager.Domain.Security;
using Xunit;

namespace PzManager.Application.Tests.Services;

public sealed class DeviceAdminServiceTests
{
    [Fact]
    public async Task RevokeAsync_MarksDeviceRevoked()
    {
        var repo = new InMemoryDeviceRepository();
        var service = new DeviceAdminService(repo);

        var id = new DeviceId("dev1");
        await repo.UpsertAsync(
            new Device(
                Id: id,
                PublicKey: "pk",
                Role: Role.Readonly,
                Name: "n",
                Note: null,
                Revoked: false,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                LastSeenUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);

        var updated = await service.RevokeAsync(id, note: "bye", CancellationToken.None);
        Assert.True(updated.Revoked);
        Assert.Equal("bye", updated.Note);

        var stored = await repo.FindByIdAsync(id, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.True(stored!.Revoked);
    }

    [Fact]
    public async Task RevokeAsync_UnknownDevice_Throws()
    {
        var repo = new InMemoryDeviceRepository();
        var service = new DeviceAdminService(repo);

        var ex = await Assert.ThrowsAsync<AppException>(() => service.RevokeAsync(new DeviceId("missing"), note: null, CancellationToken.None));
        Assert.Equal(AppErrorCodes.BadRequest, ex.Code);
    }

    [Fact]
    public async Task RevokeAsync_LastAdmin_ThrowsForbidden()
    {
        var repo = new InMemoryDeviceRepository();
        var service = new DeviceAdminService(repo);

        var id = new DeviceId("admin1");
        await repo.UpsertAsync(
            new Device(
                Id: id,
                PublicKey: "pk",
                Role: Role.Admin,
                Name: "n",
                Note: null,
                Revoked: false,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                LastSeenUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AppException>(() => service.RevokeAsync(id, note: null, CancellationToken.None));
        Assert.Equal(AppErrorCodes.Forbidden, ex.Code);
    }

    [Fact]
    public async Task UpdateAsync_DowngradeLastAdmin_ThrowsForbidden()
    {
        var repo = new InMemoryDeviceRepository();
        var service = new DeviceAdminService(repo);

        var id = new DeviceId("admin1");
        await repo.UpsertAsync(
            new Device(
                Id: id,
                PublicKey: "pk",
                Role: Role.Admin,
                Name: "n",
                Note: null,
                Revoked: false,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                LastSeenUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AppException>(
            () => service.UpdateAsync(id, Role.Readonly, note: null, CancellationToken.None));
        Assert.Equal(AppErrorCodes.Forbidden, ex.Code);
    }

    [Fact]
    public async Task UpdateAsync_ChangesRoleAndNote()
    {
        var repo = new InMemoryDeviceRepository();
        var service = new DeviceAdminService(repo);

        var admin = new DeviceId("admin1");
        var user = new DeviceId("user1");

        await repo.UpsertAsync(
            new Device(
                Id: admin,
                PublicKey: "pk",
                Role: Role.Admin,
                Name: "admin",
                Note: null,
                Revoked: false,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                LastSeenUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);

        await repo.UpsertAsync(
            new Device(
                Id: user,
                PublicKey: "pk",
                Role: Role.Readonly,
                Name: "u",
                Note: null,
                Revoked: false,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                LastSeenUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);

        var updated = await service.UpdateAsync(user, Role.Ops, note: "ops box", CancellationToken.None);
        Assert.Equal(Role.Ops, updated.Role);
        Assert.Equal("ops box", updated.Note);
    }

    private sealed class InMemoryDeviceRepository : IDeviceRepository
    {
        private readonly Dictionary<string, Device> _byId = new(StringComparer.Ordinal);

        public Task<Device?> FindByIdAsync(DeviceId id, CancellationToken cancellationToken)
        {
            _byId.TryGetValue(id.Value, out var device);
            return Task.FromResult(device);
        }

        public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Device>>(_byId.Values.ToArray());
        }

        public Task UpsertAsync(Device device, CancellationToken cancellationToken)
        {
            _byId[device.Id.Value] = device;
            return Task.CompletedTask;
        }
    }
}
