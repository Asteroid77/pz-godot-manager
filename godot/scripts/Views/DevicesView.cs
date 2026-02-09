using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace PzManager
{

[Tool]
public partial class DevicesView : Control
{
	private Button? _refresh;
	private Label? _status;
	private ItemList? _list;

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

		var title = new Label { Text = "设备列表" };
		title.AddThemeFontSizeOverride("font_size", 18);
		root.AddChild(title);

		var row = new HBoxContainer();
		_refresh = new Button { Text = "刷新" };
		_refresh.Pressed += async () => await RefreshAsync();
		row.AddChild(_refresh);
		root.AddChild(row);

		_status = new Label { Text = "" };
		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		root.AddChild(_status);

		_list = new ItemList
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		root.AddChild(_list);

		AddChild(root);
	}

	public async Task RefreshAsync()
	{
		if (Engine.IsEditorHint())
		{
			if (_status is not null)
			{
				_status.Text = "编辑器预览模式";
			}

			return;
		}

		if (_refresh is null || _status is null || _list is null)
		{
			return;
		}

		_refresh.Disabled = true;
		try
		{
			var client = AppState.Instance.GetClient();
			var ok = await client.DevicesListAsync(CancellationToken.None);

			_list.Clear();
			foreach (var d in ok.Devices)
			{
				var line = $"{d.DeviceId}  role={d.Role}  name={d.Name ?? ""}  revoked={d.Revoked}";
				if (!string.IsNullOrWhiteSpace(d.Note))
				{
					line += $"  note={d.Note}";
				}

				_list.AddItem(line);
			}

			_status.Text = $"共 {ok.Devices.Length} 台设备";
		}
		catch (Exception ex)
		{
			_status.Text = $"失败：{ex.Message}";
		}
		finally
		{
			_refresh.Disabled = false;
    }
}
}
}
