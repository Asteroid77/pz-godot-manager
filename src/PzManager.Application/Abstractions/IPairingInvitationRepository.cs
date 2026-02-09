using PzManager.Domain.Pairing;

namespace PzManager.Application.Abstractions;

public interface IPairingInvitationRepository
{
    Task<PairingInvitation?> FindByCodeAsync(PairingCode code, CancellationToken cancellationToken);

    Task<IReadOnlyList<PairingInvitation>> ListAsync(CancellationToken cancellationToken);

    Task UpsertAsync(PairingInvitation invitation, CancellationToken cancellationToken);
}

