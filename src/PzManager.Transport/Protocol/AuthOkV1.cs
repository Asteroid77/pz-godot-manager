namespace PzManager.Transport.Protocol;

public sealed record AuthOkV1(
    string DeviceId,
    string Role,
    string[] Capabilities,
    bool IsNewDevice);

