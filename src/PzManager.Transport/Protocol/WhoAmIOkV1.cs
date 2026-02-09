namespace PzManager.Transport.Protocol;

public sealed record WhoAmIOkV1(
    string DeviceId,
    string Role,
    string? DeviceName);

