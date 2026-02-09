using PzManager.Domain.Devices;

namespace PzManager.Application.Abstractions;

public interface IDeviceRepository
{
    Task<Device?> FindByIdAsync(DeviceId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(Device device, CancellationToken cancellationToken);
}

