# PZ Godot Manager

目标：用 Godot(C#) 做统一的 Manager（控制面）与客户端 UI，替代现有 Go/JS 面板；Linux 侧用 Docker 运行 PZ，Windows 侧用 Dedicated Server `.bat` 运行。

## 代码结构（DDD）

见 `plan.md` 与 `src/` 下的分层工程。

## 运行 Manager Server（WebSocket）

默认监听 `127.0.0.1:27100`（只通过 Tailscale/ZeroTier/SSH 隧道访问，避免明文 WS 上公网）。

```bash
dotnet run --project "./src/PzManager.Manager/PzManager.Manager.csproj" -c Release
```

环境变量：

- `PZ_MANAGER_BIND`：监听地址（默认 `127.0.0.1`）
- `PZ_MANAGER_PORT`：监听端口（默认 `27100`）
- `PZ_MANAGER_DATA_DIR`：数据目录（默认 `./data`，保存 `state.json`）
- `PZ_MANAGER_BOOTSTRAP_TTL_SECONDS`：首次启动管理员配对码 TTL（默认 `1800`）

PZ Runner（M2）环境变量：

- `PZ_GAME_RUNNER_MODE`：`none` / `docker-compose` / `bat`（默认 `none`）
- `PZ_DOCKER_PROJECT_DIR`：docker compose 项目目录（含 `.env`/相对卷），`docker-compose` 模式必填
- `PZ_DOCKER_COMPOSE_FILE`：compose 文件路径（默认 `${PZ_DOCKER_PROJECT_DIR}/docker-compose.yml`）
- `PZ_DOCKER_SERVICE`：服务名（默认 `pz-server`）
- `PZ_BAT_PATH`：Windows Dedicated Server 启动 `.bat` 路径（`bat` 模式必填）
- `PZ_BAT_LOG_FILE`：Windows `.bat` 模式日志文件路径（默认 `./data/pz-server.log`，用于 `logs.tail/logs.follow`）

接口：

- `GET /healthz`
- `WS /ws`（首包需 `auth`；无设备时会在启动日志打印一次性管理员配对码）
  - `server.status/server.start/server.stop/server.restart`（需要 `server_restart` 能力）
  - `logs.tail`（需要 `logs_read` 能力）
  - `logs.follow` / `logs.follow.stop`（需要 `logs_read` 能力；`logs.follow.ok` 会持续推送日志行，支持断线续接参数 `since/offset`，服务端回传 `nextSince/nextOffset`）

## 运行 Client CLI（最小可用客户端）

> 用于在没有 Godot UI 的情况下先跑通配对/重启/日志 follow（支持 `--resume` 续接）。

```bash
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- whoami
```

示例：

```bash
# 首次配对（需要服务端打印/生成的 pairing code）
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- --pairing-code "ABCD-EFGH" whoami

# 生成可复制的“配对包（Pairing Bundle）”（建议分享给客户端；可做成二维码）
# - bundle 内包含 manager url + pairing code + role + expiresAt
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- \
  --url "ws://127.0.0.1:27100/ws" pairing create --role admin --ttl 600 \
  --print-bundle --bundle-url "ws://127.0.0.1:27100/ws"

# 客户端用配对包一键连接并完成首次注册（会在本地生成/保存设备 ed25519 密钥对）
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- \
  --pairing-bundle "<PZMB1:...>" whoami

# 跟随日志（写入 ./data-client/logs.cursor.json；下次可 --resume）
dotnet run --project "./src/PzManager.Client.Cli/PzManager.Client.Cli.csproj" -c Release -- logs follow --resume
```

## 基础设施 Demo（连接/匹配/授权/权限）

见 `docs/demo-infra.md`（提供 Linux/macOS bash + Windows PowerShell 一键演示脚本）。

## Godot 客户端（4.6 Standard.NET）

- 用 Godot 4.6-stable（Standard.NET）打开 `godot/project.godot` 运行。
- 通用 UI 最小闭环（先做基础设施，不做游戏服务）：
  - 连接：粘贴 `PZMB1:` → 鉴权成功后显示 `role/capabilities`
  - 设备列表：需要 `devices_read`
  - 创建配对包：需要 `pairing_create`（输出新的 `PZMB1:` 可分享给其它设备）
- `.godot/` 是 Godot 自动生成的项目缓存目录，需要对当前用户可写；如果提示“项目数据文件夹 .godot 缺失/重启编辑器”，优先检查权限并可直接删除后重开：
  - `sudo chown -R "$USER":"$USER" "./godot/.godot"` 或 `sudo rm -rf "./godot/.godot"`

## 本地预检（push 前）

本仓库约定在 `git push` 前执行本地门禁（AST/AI Search）。建议启用仓库级 hooks：

```bash
./scripts/install-githooks.sh
```

然后每次 push 会自动运行 `./scripts/prepush.sh`（需要本机可用 `codex` CLI）。
