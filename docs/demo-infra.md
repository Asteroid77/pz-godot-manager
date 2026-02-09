# 基础设施 Demo（连接 / 匹配 / 授权 / 权限）

目标：在不引入 Godot UI 的前提下，用 `Manager + Client CLI` 跑通一条最小闭环：

- 服务端启动 → 输出 bootstrap 配对包（`PZMB1:`）
- 客户端粘贴配对包 → 首次注册设备（ed25519 本地生成）→ 拿到 role + capabilities
- 管理员创建设备配对包（预设 role）→ 第二台设备注册
- 用“只读设备”触发 `forbidden`，验证 RBAC 生效
- （可选）管理员撤销设备，验证撤销立即生效

## 前置条件

- .NET 8 SDK（`dotnet`）

Ubuntu 24.04 安装示例：

```bash
sudo apt update
sudo apt install -y dotnet-sdk-8.0
dotnet --version
```

如果你不想安装 `dotnet`，也可以直接用 docker 运行 demo（需要本机可用 `docker`）：

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget-packages \
  -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -lc 'bash scripts/demo-infra.sh'
```

## 方式 A：一键脚本

Linux/macOS：

```bash
bash "scripts/demo-infra.sh"
```

或（如果你当前就在 `scripts/` 目录下）：

```bash
bash "./demo-infra.sh"
```

Windows PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File "scripts/demo-infra.ps1"
```

脚本会创建一个新的 demo 数据目录（默认在 `./demo-data/infra-<timestamp>/`），并打印关键输出与验证点。

> 不建议用 `sudo` 跑 demo：会导致产出目录/缓存变成 root 所有，反而更麻烦。

## 方式 B：手工跑通（更贴近后续 Godot UI 的 UX）

1) 启动 Manager

```bash
dotnet run --project "./src/PzManager.Manager/PzManager.Manager.csproj" -c Release
```

启动日志会打印类似：

- `Bootstrap pairing bundle (PZMB1, edit managerUrl if needed): PZMB1:...`

2) 用配对包完成首次注册（模拟“第一台管理员设备”）

```bash
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- \
  --data-dir "./demo-admin" --device-name "demo-admin" --pairing-bundle "<PZMB1:...>" auth
```

3) 管理员创建“只读设备”的配对包（预设权限）

```bash
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- \
  --data-dir "./demo-admin" --url "ws://127.0.0.1:27100/ws" \
  pairing create --role readonly --ttl 600 --print-bundle --bundle-url "ws://127.0.0.1:27100/ws"
```

4) 第二台设备用配对包注册（模拟“只读运维/朋友”）

```bash
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- \
  --data-dir "./demo-ro" --device-name "demo-ro" --pairing-bundle "<PZMB1:...>" auth
```

5) 触发权限校验（应返回 `forbidden`）

```bash
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- \
  --data-dir "./demo-ro" --url "ws://127.0.0.1:27100/ws" devices list
```

## 备注

- “配对包（Pairing Bundle）”只做一次性授权注册：**不从配对包派生设备密钥**。
- 设备密钥由客户端本地随机生成并保存；服务端存公钥 + 元信息。
- 后续 Godot UI 直接复用同一套 `PZMB1:` 字符串输入（可扩展为二维码）。 
