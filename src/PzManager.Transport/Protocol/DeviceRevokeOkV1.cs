namespace PzManager.Transport.Protocol;

public sealed record DeviceRevokeOkV1(
    string DeviceId,
    bool Revoked);

