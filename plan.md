# PZ Godot Manager 计划（plan.md）

> 状态枚举：`pending` / `in_progress` / `completed`  
> 约定：`[x]` 仅用于 `completed`；其余用 `[ ]`。

## 目标与边界（已确认）

- [x] (`completed`) 控制面/游戏面分离：控制面为 **Manager WebSocket 单端口**；游戏面为 **PZ UDP 16261/16262**。
- [x] (`completed`) “一体化软件”形态：同一套安装包同时包含 **Manager（服务端）** 与 **Client（客户端）**；同一台机器既可以开服也可以作为客户端连接其它服主（可选：同进程/同 UI 内切换）。
- [ ] (`pending`) 平台化（多游戏）：
  - 通用模块/通用 UI：连接、匹配（配对包）、设备身份、会话、授权/权限（RBAC/能力）、审计日志、基础运维框架（日志/Runner/配置 IO）
  - 业务模块（Game Modules）：每个游戏以模块方式集成到同一套 Manager/Client UI 中；PZ 作为第一个模块落地，后续可扩展到更多游戏
- [x] (`completed`) 玩家 **不强制** 加入 Tailscale：有公网时可仅通过 **SSH 端口转发** 访问 Manager。
- [x] (`completed`) Manager 明文 `ws://` **不直接暴露公网**：公网访问必须走 **Tailscale/ZeroTier/SSH 隧道** 等加密传输层。
- [x] (`completed`) 不集成 DDNS/公网更新（如 ddns-go）：由用户自行折腾。
- [x] (`completed`) 项目采用 DDD 分层架构：按领域拆模块，并保持依赖方向（Domain → Application → Infrastructure/Interface Adapters）。
- [x] (`completed`) 发布需有业内最佳实践 CI/CD（GitHub Actions）：传统门禁（单测/Lint/构建）+ 版本化发布 + 可复现构建与产物校验；版本号变更自动打 tag 并创建 Release。
- [x] (`completed`) AST 与 AI Search 验收不放在 GitHub CI：作为本地 push 前预检（调用本机 Codex，低思维）执行并产出报告。
- [ ] (`pending`) 支持三平台（Windows/Linux/macOS）：
  - CI 需要三平台构建验证（避免路径/权限/大小写差异）
  - Release 产物需覆盖三平台（后续随 Godot UI 导出一并落地）

## 里程碑（Milestones）

- [ ] (`pending`) M0：仓库落地（Godot 4.6-stable（Standard.NET）/C# 工程骨架 + 基础构建/发布脚本）
- [x] (`completed`) M1：Manager Server（WS 服务端 + 最小鉴权 + 设备管理数据落盘）
- [x] (`completed`) M1.1：基础设施 Demo（连接/匹配/授权/权限）跑通（先 CLI，后 Godot UI）
- [ ] (`pending`) M2：PZ 运行适配（后置：Linux Docker + Windows .bat/进程）
- [ ] (`pending`) M3：配置管理（Server.ini + SandboxVars.lua 读写 + 最小校验）
- [ ] (`in_progress`) M4：客户端 UI（Notion 风格基础组件 + 权限分区 + 基础页面）
- [ ] (`pending`) M5：可运维性（审计日志/备份/回滚/崩溃恢复 + 文档）

## 任务清单（Backlog）

### A. 项目骨架与工程化

- [x] (`completed`) 初始化 Godot 4.x C# 工程（Client/Manager 同仓库目录规划）
- [ ] (`pending`) 抽出 `Core` 纯 C# 库：协议/鉴权/RBAC/配置解析/运行适配统一放这里
- [x] (`completed`) DDD 目录结构与依赖规则（分层/模块边界/命名约定）
- [x] (`completed`) CI（GitHub Actions/质量门禁）：格式化/静态检查(Lint)/单元测试/覆盖率报告/构建矩阵
- [x] (`completed`) CD（GitHub Actions/发布流水线）：版本号策略 + 产物打包/签名(可选) + 版本号变更自动打 tag + GitHub Release 自动化
- [x] (`completed`) Client CLI（过渡期）：跑通配对/重启/日志 `tail/follow(续接)`，为 Godot UI 提供参考实现
- [ ] (`pending`) 发布产物策略：Windows/Linux（Client GUI + Manager Headless/GUI）

### B. 连接模式（传输层）与用户引导

- [ ] (`pending`) 连接模式矩阵与 UX：
  - Server：`Public(游戏端口公网)` / `Overlay(Tailscale/ZeroTier)` / `Local`
  - Client：`Public+SSH Tunnel` / `Overlay` / `Local`
- [x] (`completed`) “匹配码/邀请包（Pairing Bundle）”规范（用于复制/二维码）：
  - 载荷：`manager_url` + `pairing_code` + `role` + `expires_at` +（可选）`overlay_kind`/`overlay_join_token`/`overlay_hint`
  - 目标：客户端拿到一段字符串即可完成“加入 overlay（可选）→ 连接 Manager → 完成配对”
  - 安全：不从匹配码**派生**设备密钥；设备密钥由客户端本地随机生成，仅用匹配码做一次性注册授权
- [ ] (`pending`) Tailscale 引导（不保存管理员账号密码）：
  - 服务端提示如何创建一次性 AuthKey（建议短 TTL + tag）
  - 客户端检测 `tailscale` 可用性、执行加入、失败诊断与降级提示
- [ ] (`pending`) ZeroTier 备选引导：
  - 服务端提示创建网络/审批成员
  - 客户端检测 `zerotier-cli` 可用性、执行加入、失败诊断
- [ ] (`pending`) SSH 隧道模式（公网无 TLS 的默认安全方案）：
  - Manager 仅监听 `127.0.0.1:<port>`（或仅监听 overlay 网卡）
  - 客户端一键启动 `ssh -N -L <localport>:127.0.0.1:<port> user@host`
  - 失败诊断（端口占用/权限/密钥/known_hosts/网络不可达）

### C. 设备绑定、鉴权与权限（应用层）

- [x] (`completed`) M1.0 Manager WS MVP：`/ws` + `hello/auth/ping/whoami/pairing.create/devices.list/devices.revoke/devices.update` + `data/state.json` 落盘 + `data/audit.jsonl` 审计落盘（部分事件） + 首次启动 bootstrap 管理员配对码
- [x] (`completed`) 设备身份（替代 MAC 绑定）：
  - 客户端首次启动生成 ed25519 密钥对（本地持久化）
  - 服务端仅存公钥 + 设备元信息（备注/最后在线/撤销状态）
- [x] (`completed`) 配对流程：
  - 服务端生成一次性配对码（短 TTL、一次性消费）
  - 客户端使用配对码注册设备公钥并换取会话
- [ ] (`pending`) 配对码与权限预设：
  - 服务端生成配对码时直接绑定 `role`（最小 RBAC 入口）
  - 允许服主在发放前预览该 role 的能力清单（避免“发错权限”）
- [x] (`completed`) 会话与重放防护（最小可用）：
  - WS 连接握手 challenge/response（nonce + 签名）
- [ ] (`pending`) 断线重连策略（后续增强）：
  - 连接重试/退避、日志 follow 游标续接等（CLI 已实现 `logs.follow --resume`；Godot UI 待做）
- [x] (`completed`) 简易 RBAC（服务端）：
  - 角色：`player` / `readonly` / `gm` / `ops` / `admin`（可调整）
  - 能力：`game_connect` / `config_read` / `config_write` / `logs_read` / `server_restart` / `admin_cmd` 等
- [ ] (`pending`) UI 权限分区（Godot）：
  - UI 按能力分区展示（没有能力则隐藏或只读）
- [ ] (`in_progress`) 审计日志（谁在何时做了什么）：重启/配置改动/管理命令/密钥撤销（已覆盖 `pairing.create` / `device.register` / `devices.revoke` / `devices.update`）

### D. PZ 运行与监控适配（Server host）

- [x] (`completed`) M2.0 PZ Runner MVP：`IServerRunner` + `Docker Compose`/`.bat` Runner + `server.*` WS 命令 + 审计
- [x] (`completed`) 统一接口 `IServerRunner`：
  - `Start/Stop/Restart/Status`
- [ ] (`in_progress`) Linux：Docker 方案
  - 启动/停止/重启/状态：docker compose Runner（环境变量配置）
  - 日志：支持 `logs.tail` + `logs.follow`（流式；可 stop）
- [ ] (`in_progress`) Windows：Dedicated Server `.bat` 方案
  - 启动/停止/重启/状态：`.bat` Runner（PID 文件模式）
  - 日志：stdout/stderr 重定向到文件（`PZ_BAT_LOG_FILE`），支持 `logs.tail` + `logs.follow`（offset）
- [ ] (`pending`) “游戏端口暴露”策略落地（公网/overlay）并做自检提示（防火墙/端口占用）

### E. 配置/模组/管理能力（最小闭环 → 逐步增强）

- [ ] (`pending`) Server.ini 读写（保留注释/格式可延后）
- [ ] (`pending`) SandboxVars.lua 读写（最小语法支持 + 生成器）
- [ ] (`pending`) 配置备份/回滚（保存前自动备份，提供一键恢复）
- [ ] (`in_progress`) 日志查看（已支持 `logs.tail` + `logs.follow`（WS 推送，since/offset 游标）；断线重连待做）
- [ ] (`pending`) 游戏内 Admin 命令：
  - 先明确接口（RCON？STDIN？文件/Socket？）再实现
- [ ] (`pending`) 模组管理（可选，后置）：
  - 本地 Workshop 扫描（mod.info）
  - Workshop 元信息查询与缓存（网络错误容错）

### F. UI（Notion 风格）与性能

- [ ] (`in_progress`) Godot AppShell 导航框架（`godot/scripts/AppShell.cs`，待本地可见性验收）
- [ ] (`in_progress`) 最小闭环页面：Connect / Devices / PairingCreate（`godot/scripts/Views/*`，待本地可见性验收）
- [ ] (`in_progress`) AppState 持久连接接入（`godot/scripts/AppState.cs` + `project.godot` autoload，待本地可见性验收）
- [ ] (`pending`) Design System：字体/字号/色板/圆角/阴影/分割线（Theme + StyleBox）
- [ ] (`pending`) 组件库：按钮/输入/下拉/Tab/Modal/Toast/Sidebar/表格/折叠面板
- [ ] (`pending`) Sidebar 权限分区：按 capabilities 分组入口 + 空态提示
- [ ] (`pending`) 大表单性能：虚拟列表/增量渲染/搜索过滤（避免一次性创建数百 Control）
- [ ] (`pending`) 多语言（可选，后置）：优先做 UI 文案；游戏翻译文件解析可后置

### G. 本地质量门禁与自动验收（AST + AI Search，早于 CI）

- [x] (`completed`) 验收规范（Acceptance Spec）：用 `acceptance/` 的 Markdown/YAML 描述关键流程与授权矩阵，作为自动验收输入
- [x] (`completed`) AST 架构测试：基于静态分析校验依赖方向（Domain 不依赖 Infrastructure/Transport/Godot）
- [ ] (`pending`) AST 协议/权限测试：强制每个 WS Handler 显式声明能力需求，并与 UI 分区一致
- [x] (`completed`) AI Search 验收工具：按 Acceptance Spec 做 repo 语义检索/对照，输出可读验收报告
- [x] (`completed`) 本地预检入口（git hook/命令）：push 前必跑（单测子集 + AST + 验收报告）；允许配置“Codex gpt-5.2 low thinking”模式
- [ ] (`pending`) CI 与本地门禁分工：CI 只跑传统门禁（Lint/单测/构建/发布），不跑 AI Search/AST；必要时 CI 仅校验“验收报告文件存在且版本匹配”

### H. 平台化与业务模块（多游戏）

- [ ] (`pending`) 平台模块边界（Platform/Core）定义：
  - `Transport`：协议与序列化（跨端复用）
  - `Identity/Auth`：设备密钥、配对包、会话、重放防护
  - `AuthZ`：RBAC/能力模型 + UI 分区约束
  - `Ops`：日志/Runner/配置 IO 的统一抽象（由具体游戏模块实现适配）
- [ ] (`pending`) 业务模块接口（Game Module）最小规范：
  - 模块元信息（id/name/version）
  - 能力声明（capabilities → UI 分区/服务端授权）
  - Runner/配置/日志适配接口（可选；按需实现）
  - UI 注册（侧边栏入口/页面工厂），保持“同一套 UI 框架”
- [ ] (`pending`) PZ 模块化收口：把目前 PZ Runner/配置/日志相关内容逐步收敛为 `PZ` 模块实现（平台仅保留抽象）

### I. 基础设施 Demo（连接/匹配/授权/权限）

- [x] (`completed`) Demo：服务端启动后打印 bootstrap 配对包（PZMB1），客户端可直接粘贴完成首次注册
- [x] (`completed`) Demo：最小演示脚本（Linux/macOS bash + Windows PowerShell）：
  - 生成 admin 设备 → 创建 readonly 配对包 → 注册 readonly 设备 → 验证 forbidden（无 `devices_read` / `pairing_create`）

## 完成定义（DoD）

> 约定：任何条目要标记为 `completed`，至少满足以下条件（按需要裁剪，但不能跳过“单元测试”与“验收”）。

- 单元测试：新增/修改的核心逻辑有对应单元测试（失败用例与边界用例优先）
- DDD/架构：AST 架构测试通过（不破坏分层与依赖方向）
- 自动验收：`acceptance/` 的验收项通过；AI Search/AST 在 push 前本地预检执行并产出报告（允许在未配置 AI 时退化为“代码搜索 + AST”）
- 可运维性：涉及权限/重启/配置写入等高风险动作有审计记录
- 文档：对外使用方式/配置项/风险提示与实现保持一致

## 风险与原则（持续校验）

- 明文 WS 不上公网：公网访问必须走加密隧道/overlay（或未来引入 wss + pinning）。
- AuthKey 不是“退出保证”：设备撤销要有服务端兜底（应用层立刻生效；网络层需要管理员禁用设备或 ACL 限权）。
- 最小闭环优先：先做“连上 → 鉴权 → 重启 → 读写配置 → 看日志”，再做模组/i18n/更新等增强项。
