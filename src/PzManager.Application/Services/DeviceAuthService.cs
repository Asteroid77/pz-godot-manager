using PzManager.Application.Abstractions;
using PzManager.Application.Errors;
using PzManager.Application.Security;
using PzManager.Domain.Devices;
using PzManager.Domain.Pairing;

namespace PzManager.Application.Services;

public sealed class DeviceAuthService
{
    private const int Ed25519PublicKeyBytes = 32;
    private const int Ed25519SignatureBytes = 64;

    private readonly IClock _clock;
    private readonly IDeviceRepository _devices;
    private readonly IPairingInvitationRepository _pairings;
    private readonly ISignatureVerifier _signatureVerifier;

    public DeviceAuthService(
        IClock clock,
        IDeviceRepository devices,
        IPairingInvitationRepository pairings,
        ISignatureVerifier signatureVerifier)
    {
        _clock = clock;
        _devices = devices;
        _pairings = pairings;
        _signatureVerifier = signatureVerifier;
    }

    public async Task<AuthResult> AuthenticateAsync(AuthenticateDeviceCommand command, CancellationToken cancellationToken)
    {
        if (command.PublicKey.Length != Ed25519PublicKeyBytes)
        {
            throw new AppException(AppErrorCodes.BadRequest, "invalid publicKey length");
        }

        if (command.Signature.Length != Ed25519SignatureBytes)
        {
            throw new AppException(AppErrorCodes.BadRequest, "invalid signature length");
        }

        if (command.Nonce.Length == 0)
        {
            throw new AppException(AppErrorCodes.BadRequest, "missing nonce");
        }

        if (!_signatureVerifier.VerifyEd25519(command.PublicKey, command.Nonce, command.Signature))
        {
            throw new AppException(AppErrorCodes.InvalidSignature, "invalid signature");
        }

        var now = _clock.UtcNow;
        var deviceId = DeviceId.FromEd25519PublicKey(command.PublicKey);
        var existing = await _devices.FindByIdAsync(deviceId, cancellationToken);

        if (existing is not null)
        {
            if (existing.Revoked)
            {
                throw new AppException(AppErrorCodes.Unauthorized, "device revoked");
            }

            var updated = existing with
            {
                Name = command.DeviceName ?? existing.Name,
                LastSeenUtc = now,
            };

            await _devices.UpsertAsync(updated, cancellationToken);
            var capabilities = RoleCapabilityPolicy.CapabilitiesFor(updated.Role);
            return new AuthResult(updated.Id, updated.Role, capabilities, IsNewDevice: false);
        }

        if (!PairingCode.TryParse(command.PairingCode, out var pairingCode))
        {
            throw new AppException(AppErrorCodes.PairingInvalid, "pairing code required");
        }

        var invitation = await _pairings.FindByCodeAsync(pairingCode, cancellationToken);
        if (invitation is null)
        {
            throw new AppException(AppErrorCodes.PairingInvalid, "pairing code not found");
        }

        if (invitation.IsConsumed)
        {
            throw new AppException(AppErrorCodes.PairingConsumed, "pairing code consumed");
        }

        if (invitation.IsExpired(now))
        {
            throw new AppException(AppErrorCodes.PairingExpired, "pairing code expired");
        }

        var device = new Device(
            Id: deviceId,
            PublicKey: Convert.ToBase64String(command.PublicKey),
            Role: invitation.Role,
            Name: command.DeviceName,
            Note: null,
            Revoked: false,
            CreatedAtUtc: now,
            LastSeenUtc: now);

        await _devices.UpsertAsync(device, cancellationToken);
        await _pairings.UpsertAsync(invitation with { ConsumedAtUtc = now }, cancellationToken);

        var deviceCapabilities = RoleCapabilityPolicy.CapabilitiesFor(device.Role);
        return new AuthResult(device.Id, device.Role, deviceCapabilities, IsNewDevice: true);
    }
}

