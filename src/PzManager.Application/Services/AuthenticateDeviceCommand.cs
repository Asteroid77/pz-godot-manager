namespace PzManager.Application.Services;

public sealed record AuthenticateDeviceCommand(
    byte[] PublicKey,
    byte[] Signature,
    byte[] Nonce,
    string? PairingCode,
    string? DeviceName);

