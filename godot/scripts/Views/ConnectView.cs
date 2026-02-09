using System;
using System.Threading;
using Godot;
using PzManager.Transport.Protocol;

namespace PzManager
{

[Tool]
public partial class ConnectView : Control
{
	private LineEdit? _bundleInput;
	private LineEdit? _deviceName;
	private Button? _connect;
	private Label? _status;

	public event Action<AuthOkV1>? Connected;

	public override void _Ready()
	{
		if (GetChildCount() > 0)
		{
			return;
		}

		var root = new VBoxContainer
		{
			AnchorRight = 1,
			AnchorBottom = 1,
			OffsetLeft = 24,
			OffsetTop = 24,
			OffsetRight = -24,
			OffsetBottom = -24,
		};

		var title = new Label { Text = "连接" };
		title.AddThemeFontSizeOverride("font_size", 18);
		root.AddChild(title);
		root.AddChild(new HSeparator());

		root.AddChild(new Label { Text = "配对包（PZMB1:...）" });
		_bundleInput = new LineEdit { PlaceholderText = "PZMB1:..." };
		root.AddChild(_bundleInput);

		root.AddChild(new Label { Text = "设备名（可选）" });
		_deviceName = new LineEdit { PlaceholderText = "例如：my-macbook / ops-laptop" };
		root.AddChild(_deviceName);

		_connect = new Button { Text = "连接并鉴权" };
		_connect.Pressed += OnConnectPressed;
		root.AddChild(_connect);

		_status = new Label { Text = "状态：未连接" };
		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		root.AddChild(_status);

		AddChild(root);
	}

	private async void OnConnectPressed()
	{
		if (Engine.IsEditorHint())
		{
			if (_status is not null)
			{
				_status.Text = "状态：编辑器预览模式";
			}

			return;
		}

		if (_bundleInput is null || _connect is null || _status is null)
		{
			return;
		}

		var raw = _bundleInput.Text?.Trim();
		if (string.IsNullOrWhiteSpace(raw))
		{
			_status.Text = "状态：请输入配对包";
			return;
		}

		_connect.Disabled = true;
		try
		{
			var auth = await AppState.Instance.ConnectFromBundleAsync(raw, _deviceName?.Text, CancellationToken.None);
			_status.Text = $"状态：已连接 role={auth.Role}";
			Connected?.Invoke(auth);
		}
		catch (Exception ex)
		{
			_status.Text = $"状态：失败：{ex.Message}";
		}
		finally
		{
			_connect.Disabled = false;
    }
}
}
}
