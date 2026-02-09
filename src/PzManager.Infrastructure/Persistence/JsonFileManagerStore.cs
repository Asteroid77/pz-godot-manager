using System.Text.Json;
using PzManager.Application.Abstractions;
using PzManager.Domain.Devices;
using PzManager.Domain.Pairing;
using PzManager.Domain.Security;

namespace PzManager.Infrastructure.Persistence;

public sealed class JsonFileManagerStore : IDeviceRepository, IPairingInvitationRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json;
    private readonly string _statePath;

    public JsonFileManagerStore(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("dataDirectory required", nameof(dataDirectory));
        }

        Directory.CreateDirectory(dataDirectory);
        _statePath = Path.Combine(dataDirectory, "state.json");
        _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
    }

    public async Task<Device?> FindByIdAsync(DeviceId id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var row = state.Devices.FirstOrDefault(d => d.Id == id.Value);
            return row is null ? null : ToDevice(row);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<IReadOnlyList<Device>> IDeviceRepository.ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Devices.Select(ToDevice).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(Device device, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var existingIndex = state.Devices.FindIndex(d => d.Id == device.Id.Value);
            var row = new DeviceRow
            {
                Id = device.Id.Value,
                PublicKey = device.PublicKey,
                Role = device.Role.ToString(),
                Name = device.Name,
                Note = device.Note,
                Revoked = device.Revoked,
                CreatedAtUtc = device.CreatedAtUtc,
                LastSeenUtc = device.LastSeenUtc,
            };

            if (existingIndex >= 0)
            {
                state.Devices[existingIndex] = row;
            }
            else
            {
                state.Devices.Add(row);
            }

            await SaveStateAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PairingInvitation?> FindByCodeAsync(PairingCode code, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var row = state.Pairings.FirstOrDefault(p => p.Code.Equals(code.Value, StringComparison.OrdinalIgnoreCase));
            return row is null ? null : ToInvitation(row);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<IReadOnlyList<PairingInvitation>> IPairingInvitationRepository.ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Pairings.Select(ToInvitation).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(PairingInvitation invitation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var existingIndex = state.Pairings.FindIndex(p => p.Code.Equals(invitation.Code.Value, StringComparison.OrdinalIgnoreCase));
            var row = new PairingRow
            {
                Code = invitation.Code.Value,
                Role = invitation.Role.ToString(),
                CreatedAtUtc = invitation.CreatedAtUtc,
                ExpiresAtUtc = invitation.ExpiresAtUtc,
                ConsumedAtUtc = invitation.ConsumedAtUtc,
            };

            if (existingIndex >= 0)
            {
                state.Pairings[existingIndex] = row;
            }
            else
            {
                state.Pairings.Add(row);
            }

            await SaveStateAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<State> LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
        {
            return new State();
        }

        await using var fs = File.OpenRead(_statePath);
        var state = await JsonSerializer.DeserializeAsync<State>(fs, _json, cancellationToken);
        return state ?? new State();
    }

    private async Task SaveStateAsync(State state, CancellationToken cancellationToken)
    {
        var tempPath = _statePath + ".tmp";
        await using (var fs = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(fs, state, _json, cancellationToken);
        }

        File.Move(tempPath, _statePath, overwrite: true);
    }

    private static Device ToDevice(DeviceRow row)
    {
        var role = Enum.TryParse<Role>(row.Role, ignoreCase: true, out var parsed) ? parsed : Role.Readonly;
        return new Device(
            Id: new DeviceId(row.Id),
            PublicKey: row.PublicKey,
            Role: role,
            Name: row.Name,
            Note: row.Note,
            Revoked: row.Revoked,
            CreatedAtUtc: row.CreatedAtUtc,
            LastSeenUtc: row.LastSeenUtc);
    }

    private static PairingInvitation ToInvitation(PairingRow row)
    {
        var role = Enum.TryParse<Role>(row.Role, ignoreCase: true, out var parsed) ? parsed : Role.Readonly;
        return new PairingInvitation(
            Code: new PairingCode(row.Code),
            Role: role,
            CreatedAtUtc: row.CreatedAtUtc,
            ExpiresAtUtc: row.ExpiresAtUtc,
            ConsumedAtUtc: row.ConsumedAtUtc);
    }

    private sealed class State
    {
        public int SchemaVersion { get; set; } = 1;

        public List<DeviceRow> Devices { get; set; } = [];

        public List<PairingRow> Pairings { get; set; } = [];
    }

    private sealed class DeviceRow
    {
        public string Id { get; set; } = "";

        public string PublicKey { get; set; } = "";

        public string Role { get; set; } = "";

        public string? Name { get; set; }

        public string? Note { get; set; }

        public bool Revoked { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public DateTimeOffset LastSeenUtc { get; set; }
    }

    private sealed class PairingRow
    {
        public string Code { get; set; } = "";

        public string Role { get; set; } = "";

        public DateTimeOffset CreatedAtUtc { get; set; }

        public DateTimeOffset ExpiresAtUtc { get; set; }

        public DateTimeOffset? ConsumedAtUtc { get; set; }
    }
}
