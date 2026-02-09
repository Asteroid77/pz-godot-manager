using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using PzManager.Application.Abstractions;
using PzManager.Application.Errors;
using PzManager.Application.Services;
using PzManager.Domain.Devices;
using PzManager.Domain.Pairing;
using PzManager.Domain.Security;
using PzManager.Infrastructure.Crypto;
using Xunit;

namespace PzManager.Application.Tests.Services;

public sealed class DeviceAuthServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_NewDevice_WithValidPairing_RegistersAndConsumes()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 2, 6, 0, 0, 0, TimeSpan.Zero));
        var devices = new InMemoryDeviceRepository();
        var pairings = new InMemoryPairingInvitationRepository();
        var verifier = new BouncyCastleEd25519SignatureVerifier();

        var auth = new DeviceAuthService(clock, devices, pairings, verifier);

        var keypair = GenerateEd25519KeyPair();
        var publicKey = ((Ed25519PublicKeyParameters)keypair.Public).GetEncoded();

        var nonce = new byte[32];
        new SecureRandom().NextBytes(nonce);

        var signature = SignEd25519((Ed25519PrivateKeyParameters)keypair.Private, nonce);

        var code = new PairingCode("A1B2C3D4E5F6A7B8");
        var invitation = new PairingInvitation(
            Code: code,
            Role: Role.Admin,
            CreatedAtUtc: clock.UtcNow,
            ExpiresAtUtc: clock.UtcNow.AddMinutes(10),
            ConsumedAtUtc: null);
        await pairings.UpsertAsync(invitation, CancellationToken.None);

        var result = await auth.AuthenticateAsync(
            new AuthenticateDeviceCommand(
                PublicKey: publicKey,
                Signature: signature,
                Nonce: nonce,
                PairingCode: code.Value,
                DeviceName: "dev1"),
            CancellationToken.None);

        Assert.True(result.IsNewDevice);
        Assert.Equal(Role.Admin, result.Role);
        Assert.Contains(Capabilities.PairingCreate, result.Capabilities);

        var deviceId = DeviceId.FromEd25519PublicKey(publicKey);
        var stored = await devices.FindByIdAsync(deviceId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("dev1", stored!.Name);

        var storedInvitation = await pairings.FindByCodeAsync(code, CancellationToken.None);
        Assert.NotNull(storedInvitation);
        Assert.True(storedInvitation!.IsConsumed);
    }

    [Fact]
    public async Task AuthenticateAsync_ExistingDevice_DoesNotRequirePairing()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 2, 6, 0, 0, 0, TimeSpan.Zero));
        var devices = new InMemoryDeviceRepository();
        var pairings = new InMemoryPairingInvitationRepository();
        var verifier = new BouncyCastleEd25519SignatureVerifier();

        var auth = new DeviceAuthService(clock, devices, pairings, verifier);

        var keypair = GenerateEd25519KeyPair();
        var publicKey = ((Ed25519PublicKeyParameters)keypair.Public).GetEncoded();

        var nonce1 = new byte[32];
        new SecureRandom().NextBytes(nonce1);
        var sig1 = SignEd25519((Ed25519PrivateKeyParameters)keypair.Private, nonce1);

        var code = new PairingCode("A1B2C3D4E5F6A7B8");
        await pairings.UpsertAsync(
            new PairingInvitation(code, Role.Readonly, clock.UtcNow, clock.UtcNow.AddMinutes(10), ConsumedAtUtc: null),
            CancellationToken.None);

        _ = await auth.AuthenticateAsync(
            new AuthenticateDeviceCommand(publicKey, sig1, nonce1, PairingCode: code.Value, DeviceName: "dev1"),
            CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(1));

        var nonce2 = new byte[32];
        new SecureRandom().NextBytes(nonce2);
        var sig2 = SignEd25519((Ed25519PrivateKeyParameters)keypair.Private, nonce2);

        var result = await auth.AuthenticateAsync(
            new AuthenticateDeviceCommand(publicKey, sig2, nonce2, PairingCode: null, DeviceName: "dev1"),
            CancellationToken.None);

        Assert.False(result.IsNewDevice);
        Assert.Equal(Role.Readonly, result.Role);
    }

    [Fact]
    public async Task AuthenticateAsync_InvalidSignature_Throws()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 2, 6, 0, 0, 0, TimeSpan.Zero));
        var devices = new InMemoryDeviceRepository();
        var pairings = new InMemoryPairingInvitationRepository();
        var verifier = new BouncyCastleEd25519SignatureVerifier();

        var auth = new DeviceAuthService(clock, devices, pairings, verifier);

        var keypair = GenerateEd25519KeyPair();
        var publicKey = ((Ed25519PublicKeyParameters)keypair.Public).GetEncoded();

        var nonce = new byte[32];
        new SecureRandom().NextBytes(nonce);

        var signature = new byte[64];
        new SecureRandom().NextBytes(signature);

        var ex = await Assert.ThrowsAsync<AppException>(
            () => auth.AuthenticateAsync(new AuthenticateDeviceCommand(publicKey, signature, nonce, PairingCode: null, DeviceName: null), CancellationToken.None));

        Assert.Equal(AppErrorCodes.InvalidSignature, ex.Code);
    }

    private static AsymmetricCipherKeyPair GenerateEd25519KeyPair()
    {
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        return gen.GenerateKeyPair();
    }

    private static byte[] SignEd25519(Ed25519PrivateKeyParameters privateKey, byte[] message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
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

    private sealed class InMemoryPairingInvitationRepository : IPairingInvitationRepository
    {
        private readonly Dictionary<string, PairingInvitation> _byCode = new(StringComparer.OrdinalIgnoreCase);

        public Task<PairingInvitation?> FindByCodeAsync(PairingCode code, CancellationToken cancellationToken)
        {
            _byCode.TryGetValue(code.Value, out var inv);
            return Task.FromResult(inv);
        }

        public Task<IReadOnlyList<PairingInvitation>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<PairingInvitation>>(_byCode.Values.ToArray());
        }

        public Task UpsertAsync(PairingInvitation invitation, CancellationToken cancellationToken)
        {
            _byCode[invitation.Code.Value] = invitation;
            return Task.CompletedTask;
        }
    }
}

