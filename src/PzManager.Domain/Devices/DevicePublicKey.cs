namespace PzManager.Domain.Devices;

public readonly record struct DevicePublicKey(byte[] Bytes)
{
    public static DevicePublicKey FromEd25519Bytes(byte[] bytes)
    {
        if (bytes.Length != 32)
        {
            throw new ArgumentException("ed25519 public key must be 32 bytes", nameof(bytes));
        }

        return new DevicePublicKey(bytes);
    }

    public string ToBase64() => Convert.ToBase64String(Bytes);
}

