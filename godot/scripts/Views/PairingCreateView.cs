using System;
using System.Threading;
using Godot;
using PzManager.Transport.Protocol;

namespace PzManager
{

[Tool]
public partial class PairingCreateView : Control
{
	private OptionButton? _role;
	private SpinBox? _ttl;
	private Button? _create;
	private LineEdit? _bundleOut;
	private Label? _status;

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

		var title = new Label { Text = "创建配对包" };
		title.AddThemeFontSizeOverride("font_size", 18);
		root.AddChild(title);
		root.AddChild(new HSeparator());

		root.AddChild(new Label { Text = "角色（role）" });
		_role = new OptionButton();
		foreach (var r in new[] { "player", "readonly", "gm", "ops", "admin" })
		{
			_role.AddItem(r);
		}

		_role.Selected = 1;
		root.AddChild(_role);

		root.AddChild(new Label { Text = "TTL（秒）" });
		_ttl = new SpinBox { MinValue = 30, MaxValue = 86400, Step = 30, Value = 600 };
		root.AddChild(_ttl);

		_create = new Button { Text = "生成" };
		_create.Pressed += OnCreatePressed;
		root.AddChild(_create);

		root.AddChild(new Label { Text = "输出配对包（可复制/可做二维码）" });
		_bundleOut = new LineEdit { Editable = false };
		root.AddChild(_bundleOut);

		var copy = new Button { Text = "复制到剪贴板" };
		copy.Pressed += () =>
		{
			if (_bundleOut is not null)
			{
				DisplayServer.ClipboardSet(_bundleOut.Text ?? "");
			}
		};
		root.AddChild(copy);

		_status = new Label { Text = "" };
		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		root.AddChild(_status);

		AddChild(root);
	}

	private async void OnCreatePressed()
	{
		if (Engine.IsEditorHint())
		{
			if (_status is not null)
			{
				_status.Text = "编辑器预览模式";
			}

			return;
		}

		if (_role is null || _ttl is null || _create is null || _bundleOut is null || _status is null)
		{
			return;
		}

		_create.Disabled = true;
		try
		{
			var bundle = AppState.Instance.Bundle;
			if (bundle is null)
			{
				_status.Text = "未连接";
				return;
			}

			var role = _role.GetItemText(_role.Selected);
			var ttl = (int)_ttl.Value;

			var client = AppState.Instance.GetClient();
			var ok = await client.PairingCreateAsync(role, ttl, CancellationToken.None);

			var outBundle = new PairingBundleV1(
				ManagerUrl: bundle.ManagerUrl,
				PairingCode: ok.PairingCode,
				Role: role,
				ExpiresAtUtc: ok.ExpiresAtUtc);

			_bundleOut.Text = PairingBundleCodec.Encode(outBundle);
			_status.Text = $"已生成，过期时间：{ok.ExpiresAtUtc:O}";
		}
		catch (Exception ex)
		{
			_status.Text = $"失败：{ex.Message}";
		}
		finally
		{
			_create.Disabled = false;
    }
}
}
}
