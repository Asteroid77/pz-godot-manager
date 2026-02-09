using PzManager.Client.Crypto;

namespace PzManager.Client;

public sealed record ManagerWsClientOptions(
    Uri Uri,
    Ed25519KeyPair DeviceKey,
    string? DeviceName = null,
    string? PairingCode = null);

