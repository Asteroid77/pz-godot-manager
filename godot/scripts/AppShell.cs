using System;
using Godot;
using PzManager.Domain.Security;
using PzManager.Transport.Protocol;

namespace PzManager
{

[Tool]
public partial class AppShell : Control
{
    private VBoxContainer? _nav;
    private Control? _contentHost;
    private Control? _viewHost;
    private Label? _status;

    private Button? _navConnect;
    private Button? _navDevices;
    private Button? _navPairing;

    private ConnectView? _connectView;
    private DevicesView? _devicesView;
    private PairingCreateView? _pairingView;

    public override void _Ready()
    {
        if (GetChildCount() > 0)
        {
            return;
        }

        Name = "AppShell";

        var root = new HBoxContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };

        _nav = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(260, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };

        var title = new Label { Text = "PzManager" };
        title.AddThemeFontSizeOverride("font_size", 20);
        _nav.AddChild(title);

        _status = new Label { Text = "未连接" };
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _nav.AddChild(_status);

        _nav.AddChild(new HSeparator());

        _navConnect = new Button { Text = "连接" };
        _navConnect.Pressed += () => ShowView(GetConnectView());
        _nav.AddChild(_navConnect);

        _navDevices = new Button { Text = "设备列表" };
        _navDevices.Pressed += async () =>
        {
            ShowView(GetDevicesView());
            await GetDevicesView().RefreshAsync();
        };
        _nav.AddChild(_navDevices);

        _navPairing = new Button { Text = "创建配对包" };
        _navPairing.Pressed += () => ShowView(GetPairingView());
        _nav.AddChild(_navPairing);

        _nav.AddChild(new VSeparator());

        var disconnect = new Button { Text = "断开连接" };
        disconnect.Pressed += async () =>
        {
            if (Engine.IsEditorHint())
            {
                return;
            }

            await AppState.Instance.DisconnectAsync();
            UpdateNavVisibility();
            ShowView(GetConnectView());
        };
        _nav.AddChild(disconnect);

        _contentHost = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };

        _viewHost = new Control
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _contentHost.AddChild(_viewHost);

        root.AddChild(_nav);
        root.AddChild(_contentHost);
        AddChild(root);

        UpdateNavVisibility();
        ShowView(GetConnectView());
    }

    private ConnectView GetConnectView()
    {
        _connectView ??= new ConnectView();
        _connectView.Connected -= OnConnected;
        _connectView.Connected += OnConnected;
        return _connectView;
    }

    private DevicesView GetDevicesView()
    {
        _devicesView ??= new DevicesView();
        return _devicesView;
    }

    private PairingCreateView GetPairingView()
    {
        _pairingView ??= new PairingCreateView();
        return _pairingView;
    }

    private void ShowView(Control view)
    {
        if (_viewHost is null)
        {
            return;
        }

        if (view.GetParent() is null)
        {
            _viewHost.AddChild(view);
            view.AnchorRight = 1;
            view.AnchorBottom = 1;
            view.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            view.SizeFlagsVertical = SizeFlags.ExpandFill;
        }
        else if (!ReferenceEquals(view.GetParent(), _viewHost))
        {
            view.GetParent().RemoveChild(view);
            _viewHost.AddChild(view);
        }

        foreach (var child in _viewHost.GetChildren())
        {
            if (child is Control c)
            {
                c.Visible = ReferenceEquals(c, view);
            }
        }
    }

    private async void OnConnected(AuthOkV1 _)
    {
        if (Engine.IsEditorHint())
        {
            return;
        }

        UpdateNavVisibility();
        ShowView(GetDevicesView());
        await GetDevicesView().RefreshAsync();
    }

    private void UpdateNavVisibility()
    {
        if (Engine.IsEditorHint())
        {
            if (_status is not null)
            {
                _status.Text = "编辑器预览（运行后显示真实连接状态）";
            }

            if (_navDevices is not null)
            {
                _navDevices.Visible = true;
            }

            if (_navPairing is not null)
            {
                _navPairing.Visible = true;
            }

            return;
        }

        if (AppState.Instance is null)
        {
            if (_status is not null)
            {
                _status.Text = "未连接";
            }

            if (_navDevices is not null)
            {
                _navDevices.Visible = false;
            }

            if (_navPairing is not null)
            {
                _navPairing.Visible = false;
            }

            return;
        }

        var auth = AppState.Instance.Auth;
        if (_status is not null)
        {
            _status.Text = auth is null
                ? "未连接"
                : $"已连接\nrole={auth.Role}\ndevice={auth.DeviceId}";
        }

        var caps = auth?.Capabilities ?? Array.Empty<string>();
        var canDevices = Array.IndexOf(caps, Capabilities.DevicesRead) >= 0;
        var canPairing = Array.IndexOf(caps, Capabilities.PairingCreate) >= 0;

        if (_navDevices is not null)
        {
            _navDevices.Visible = auth is not null && canDevices;
        }

        if (_navPairing is not null)
        {
            _navPairing.Visible = auth is not null && canPairing;
        }
    }
}
}
