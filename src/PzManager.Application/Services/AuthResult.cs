using PzManager.Domain.Devices;
using PzManager.Domain.Security;

namespace PzManager.Application.Services;

public sealed record AuthResult(
    DeviceId DeviceId,
    Role Role,
    string[] Capabilities,
    bool IsNewDevice);

