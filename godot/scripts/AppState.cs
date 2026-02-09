using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using PzManager.Client;
using PzManager.Client.Persistence;
using PzManager.Transport.Protocol;

namespace PzManager
{

public partial class AppState : Node
{
	public static AppState Instance { get; private set; } = null!;

	private ManagerWsClient? _client;
	private PairingBundleV1? _bundle;

	public bool HasManagerConnection => _client is not null;
	public AuthOkV1? Auth => _client?.Auth;
	public PairingBundleV1? Bundle => _bundle;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (ReferenceEquals(Instance, this))
		{
			Instance = null!;
		}
	}

	public async Task<AuthOkV1> ConnectFromBundleAsync(string rawBundle, string? deviceName = null, CancellationToken cancellationToken = default)
	{
		if (!PairingBundleCodec.TryDecode(rawBundle, out var bundle, out var decodeError))
		{
			throw new InvalidOperationException($"invalid bundle: {decodeError}");
		}

		await DisconnectAsync();

		var dataDir = ProjectSettings.GlobalizePath("user://pzmanager-client");
		var keyPath = Path.Combine(dataDir, "device-key.json");
		var key = DeviceKeyFileStore.LoadOrCreate(keyPath);

		var name = deviceName;
		if (string.IsNullOrWhiteSpace(name))
		{
			name = $"{OS.GetName()}-{OS.GetUniqueId()}";
		}

		var options = new ManagerWsClientOptions(new Uri(bundle!.ManagerUrl), key, name, bundle.PairingCode);
		_client = await ManagerWsClient.ConnectAsync(options, cancellationToken);
		_bundle = bundle;
		return _client.Auth;
	}

	public ManagerWsClient GetClient()
	{
		if (_client is null)
		{
			throw new InvalidOperationException("not connected");
		}

		return _client;
	}

	public async Task DisconnectAsync()
	{
		if (_client is null)
		{
			_bundle = null;
			return;
		}

		try
		{
			await _client.DisposeAsync();
		}
		finally
		{
			_client = null;
			_bundle = null;
		}
	}
}
}
