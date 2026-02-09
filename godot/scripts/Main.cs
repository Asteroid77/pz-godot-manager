using System.Text.Json;
using System;
using System.IO;
using System.Threading;
using Godot;
using PzManager.Client;
using PzManager.Client.Persistence;
using PzManager.Transport.Protocol;

namespace PzManager.GodotClient
{

public partial class Main : Control
{
	private LineEdit? _bundleInput;
	private Button? _connectButton;
	private Label? _statusLabel;

	public override void _Ready()
	{
		Name = "Main";

		var root = new VBoxContainer
		{
			AnchorRight = 1,
			AnchorBottom = 1,
			OffsetLeft = 24,
			OffsetTop = 24,
			OffsetRight = -24,
			OffsetBottom = -24,
		};

		var title = new Label { Text = "PzManager（基础设施 Demo）" };
		title.AddThemeFontSizeOverride("font_size", 22);
		root.AddChild(title);

		root.AddChild(new HSeparator());

		root.AddChild(new Label { Text = "粘贴配对包（PZMB1:...）：" });
		_bundleInput = new LineEdit
		{
			PlaceholderText = "PZMB1:...",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		root.AddChild(_bundleInput);

		_connectButton = new Button { Text = "连接并鉴权" };
		_connectButton.Pressed += OnConnectPressed;
		root.AddChild(_connectButton);

		_statusLabel = new Label
		{
			Text = "状态：未连接",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		root.AddChild(_statusLabel);

		AddChild(root);
	}

	private async void OnConnectPressed()
	{
		if (_bundleInput is null || _connectButton is null || _statusLabel is null)
		{
			return;
		}

		_connectButton.Disabled = true;
		try
		{
			var raw = _bundleInput.Text?.Trim();
			if (!PairingBundleCodec.TryDecode(raw, out var bundle, out var decodeError))
			{
				_statusLabel.Text = $"状态：配对包无效：{decodeError}";
				return;
			}

			var dataDir = ProjectSettings.GlobalizePath("user://pzmanager-client");
			var keyPath = Path.Combine(dataDir, "device-key.json");
			var key = DeviceKeyFileStore.LoadOrCreate(keyPath);

			var deviceName = $"{OS.GetName()}-{OS.GetUniqueId()}";
			var options = new ManagerWsClientOptions(new Uri(bundle!.ManagerUrl), key, deviceName, bundle.PairingCode);

			await using var client = await ManagerWsClient.ConnectAsync(options, CancellationToken.None);
			var json = JsonSerializer.Serialize(client.Auth, new JsonSerializerOptions { WriteIndented = true });
			_statusLabel.Text = $"状态：已连接\n\n{json}";
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"状态：失败：{ex.Message}";
		}
		finally
		{
			_connectButton.Disabled = false;
    }
}
}
}
