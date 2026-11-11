# GameServer 接入核对与改动清单

## GameStarter 当前接入

营地加载后，可选启动游戏服务器会话。旅人档案展示登录返回的玩家 ID 和服务器旧进度，刷新按钮用于重新读取或断线后手动连接。本地营地、战绩与奖励继续使用自己的档案。资源热更新的 Online/Offline 开关与这条业务连接独立。

当前使用 [GameServer 97538a1 的协议](https://github.com/Novorizon/GameServer/blob/97538a12b7907fb8c7f2a6f723936ade9928d74d/protos/game.proto)。客户端根据协议生成所需 Protobuf 类型，经 SDK 配置 AES-GCM、首次鉴权和鉴权后心跳。只调用 Login 和 ProgressRead。此前独立 Windows Unity 探针在阿里云压测端点通过了这两项与心跳，当前游戏入口的接线另行记录验收。

从启动 Unity 或 Player 的进程环境注入以下参数。缺少服务器配置时，游戏可继续使用本地档案。

| 环境变量 | 用途 |
| --- | --- |
| `GAMESTARTER_SERVER_HOST` | 游戏 TCP 域名或 IP。现有阿里云压测端点为 `15000.top` |
| `GAMESTARTER_SERVER_PORT` | TCP 端口，默认 `22370` |
| `GAMESTARTER_SERVER_DEVICE_ID` | 与专用游戏账号和 JWT 匹配的设备标识 |
| `GAMESTARTER_SERVER_ACCESS_TOKEN` | 由可信认证方签发的短期 JWT。游戏在每次连接鉴权时向提供方取票，当前提供方读取进程环境 |
| `GAMESTARTER_SERVER_AES_KEY_BASE64` | 对应实例的 32 字节通信密钥，Base64 编码 |

正式令牌签发、刷新和撤销等待认证服务契约。客户端发布物、Luban 配置和日志均不保存令牌或密钥。

## Luban 与业务字段

本次沿用 14 张 Luban 表。Luban 保存静态规则、稳定 ID、文案和资源地址。玩家当前状态、票据和回执走 Protobuf。服务器用已认证会话定位玩家。

| 业务 | Luban 静态配置 | 通信字段与状态 |
| --- | --- | --- |
| 已接入登录与旧进度 | 无需加表 | Login 请求 `device_id`、`access_token`，返回 `device_id`、`player_id`。ProgressRead 返回 `progress`、`version`，用于独立展示 |
| 正式玩家档案 | 沿用 `PlayerLevel`、`CampLevel`、`TdStage` 的稳定 ID。材料类型在服务器经济定案后再配置 | 待定案的快照包含 `player_id`、`profile_revision`、`camp_level`、`experience`、余额条目和已解锁关卡 ID |
| 六波出征与结算 | 沿用 `TdStage.id/configVersion` 及路线、塔、敌人、波次和卡牌规则。双方需冻结同版配置 | 开局提交操作 ID、关卡 ID、配置版本。票据返回局 ID、种子和有效期。结算提交局 ID、幂等键及版本化命令日志，查询返回终态与回执 |
| 营地与海岛手记 | 沿用 `CampLevel.id` 与 `CampTask.id` 作为规则引用。正式消耗与奖励由服务器维护 | 升级与领取发送目标 ID、操作 ID 和期望的档案版本。服务端返回结果与新版本，客户端根据回执更新 |

正式快照、六波结算和手记领取尚无可用的固定消息号。下一步先冻结快照字段，再用六波黄金样本核对种子、tick、命令顺序和期望结果。完整战斗日志和奖励规则随对应契约落实。

## 早期云环境连通性记录

| 检查 | 结果 | 能证明什么 |
| --- | --- | --- |
| 15000.top DNS | 解析为 139.196.76.229 | 当前域名与提供的公网 IP 指向一致 |
| HTTPS 网站 | 经受管 HTTP 代理访问 `https://15000.top/` 返回 200；HTTP 入口转 HTTPS 后也为 200 | 网站可以通过这条路径访问，不代表游戏 TCP 可用 |
| 公网游戏 TCP | 获得网络权限后，139.196.76.229:22370 与 15000.top:22370 都返回 ECONNREFUSED | TCP 尚未建立，没到登录或 JWT 校验；不能仅据此断言整台主机不可达、进程未启动或防火墙必然有错 |
| 云端 VPN | 实时代理状态 configured=false、state=disabled；TCP CONNECT 代理 proxy:8088 入口连接拒绝 | 当前运行实例无法使用 Tailscale 备用路径；没有实际到达 100.106.202.63:22370，不能据此判断尾网服务器端口状态 |
| 登录与结算 | 未执行 | 无有效连接、通信配置或测试凭证，不能宣称账号/结算通过 |
| 用户提供的交叉验证 | 服务器通过 systemd 监听 0.0.0.0:22370；Windows/主机 6 公网收到 TCPA，另一云环境经 Tailscale CONNECT 200 收到完整握手 | 服务器已有正常监听和可用路径的证据；本对话的失败应继续定位云环境路径 |

默认沙盒最初返回 Operation not permitted，重试取得网络权限后才得到上述 TCP 结果。云环境的普通 HTTP 通过受管代理，公网原始 TCP 与 Tailscale 有各自的路由/策略限制。北京时间 01:01 再次读取本实例实时状态，VPN 仍为 configured=false / disabled；proxy:8088 在代理入口处拒绝，尚未到达尾网服务器。用户截图中的完整登录/心跳是在服务器本机执行，云端部分验证 TCPA 握手，二者不混为一次云端账号验收。

当时的连通性阻塞位于原云实例的 VPN 路径。按[官方环境文档](https://developers.openai.com/codex/environments/cloud-environments#private-networking-vpn)，入口为 Settings → Codex Cloud → Environments → 对应环境的 … → Edit；在 Tailscale 旁的铅笔中设置连接，auth key 启用 Reusable 与 Ephemeral，确认路由/DNS/ACL，保存并 Republish。VPN 变更需要替换运行环境或新建任务，发布不会自动更新旧任务。新实例需实际取得 CONNECT 200 + TCPA 握手。已发布配置、运行实例、未提交文件和许可证状态分别管理。

以下仅作为新部署/排障参考，不是认定当前服务器仍使用默认值，也不要求在已有成功证据时再重启或改放行规则。服务器的[默认配置](https://github.com/Novorizon/GameServer/blob/97538a12b7907fb8c7f2a6f723936ade9928d74d/src/GameServer.Host/appsettings.json)为 `ListenAddress=127.0.0.1`、`Port=1236`，地址和端口是两个字段。运维侧可只读检查：

```sh
ss -ltnp 'sport = :22370'
```

应能看到对外接口的 22370 监听。新实例若仍使用默认值，可在服务启动环境中分别配置以下两项并按部署方式重启；不把端口拼进 ListenAddress：

```sh
export GAMESERVER_Networking__ListenAddress=0.0.0.0
export GAMESERVER_Networking__Port=22370
```

同时检查云安全组、主机防火墙对 TCP 22370 的允许规则；若经 Docker 发布，还需检查端口映射。网站的 HTTP 反向代理不能自动转发原生游戏 TCP。只有确实要经域名代理时才另配 TCP 转发，不默认要求新增 Nginx stream。

可在另一台可访问公网的电脑执行 `Test-NetConnection 15000.top -Port 22370`（PowerShell）或 `nc -vz -w 5 15000.top 22370`，与云端结果对照。若采用备用尾网，需要在当前 Codex 环境配置中启用并确认 Tailscale ready、允许到 100.106.202.63:22370 的路由/ACL；其他对话的 VPN 配置不构成本实例就绪证据。不在执行器里另装 Tailscale 绕过受管路径。

## 已有协议可复用的部分

源码[game.proto](https://github.com/Novorizon/GameServer/blob/97538a12b7907fb8c7f2a6f723936ade9928d74d/protos/game.proto)包含心跳、登录、整数进度读写、原始存档读写、Facebook 绑定和服务端通知。它没有正式玩家档案、塔防开局、结算或任务领取消息。

| 层 | 核对结果 |
| --- | --- |
| TCP 包头 | GameServer 与当前固定 SDK 均使用 GWS1/v2、28 字节头；消息号/请求号/结果码/长度字段一致 |
| 加密 | 支持 None/AesGcm，默认 AesGcm；TCPA/v1 hello 和方向密钥协议已有文档及客户端实现，部署模式/通信主密钥仍须匹配 |
| 登录 | LoginRequest 是 device_id + access_token；AccountService 验 JWT 的 device/player 与已有账号一致，不自动创建账号或签发 token |
| JWT | 由可信服务端发行；现有验证检查 issuer、audience、sub、device、iat、exp。JWT 签名密钥留在可信服务端，不进入客户端或 Git |
| 心跳 | 消息 1 需要先鉴权，不能拿未登录心跳当匿名健康检查 |
| 客户端接线 | 使用真实 Protobuf 生成类和 NetworkManager 鉴权回调；首次 Initialize 前配置，不在已初始化后补写登录回调 |

压测实例的部署版本、AES-GCM 模式与帧长已核对。启动时需要该环境的通信参数和匹配的 device/JWT。正式身份服务的签发、刷新和撤销契约仍待定。

## 服务器需要增加或调整的业务

下列是待开发契约，不是已经部署的消息号；具体 .proto/错误码应由服务端维护者分配，不随意复用示例编号。

| 优先级 | 功能 | 最小请求/返回与约束 |
| --- | --- | --- |
| P0 | 游客/账号认证与刷新 | 接现有可信认证服务，或新增认证入口；创建/映射账号后签发短期 TCP JWT 和可撤销刷新凭证，支持过期/封禁/重登 |
| P0 | 玩家快照 | playerId、昵称/头像 ID、等级/经验、解锁、正式货币和 profileRevision；客户端本地旅程不能覆盖服务器经济 |
| P0 | BeginRun | 操作 ID、stageId、配置/规则版本；返回 runId、seed、到期时间、锁定配置/阵容；同操作重试返回原 ticket |
| P0 | SubmitRun / QueryRun | 固定 runId/幂等键、命令日志与摘要；服务器校验 ticket/归属/版本和结果，事务内仅发一次奖；超时后可查原结果 |
| P0 | Run 终态与恢复 | Active/Settled/Abandoned/Expired、未完成局查询、放弃策略；重连和重启后不能因响应丢失重复发奖 |
| P1 | 任务读取与领取 | 任务 ID/周期/目标、状态/奖励预览；服务端统计和校验，按玩家+任务+周期唯一领取，事务返回新档案版本 |
| P1 | 章节/剧情标记 | 已看/已完成与版本；叙事跳过规则明确，正式解锁/任务进展不能只相信客户端 Story Completed |
| P1 | 错误码与时间 | 区分认证失效、配置过期、重复/冲突、限流和维护，提供服务端时间；客户端据此恢复而非一律重连 |

建议把登录后的玩家快照归入 Player 模块，塔防 ticket/结算归入 BattleRun，任务/领奖归入 Mission；原有参数化仓储、会话管理和请求限流继续使用，不重写 TCP 基础设施。数据库至少保存玩家档案、battle_run（含唯一 runId/操作 ID/规则版本/终态）、结算/奖励流水与任务进度/领取唯一键；由迁移和事务维护，不使用一张无限扩展 JSON 代替全部权威记录。

需要收紧的现有示例接口是 ProgressWrite 与 SaveWrite：它们允许客户端写通用进度/原始存档，不应直接作为货币、任务完成、关卡解锁或战斗胜利的权威入口。可以限定为非经济偏好/允许的断点数据、增加 schema/版本/大小校验；若产品不用则不注册对应模块。Facebook 绑定可以按实际渠道需要启用，不必因为本游戏暂不用就删除通用能力。

单人首版不逐攻击、逐怪联网。正式奖励若要求可信复演，需要日志本体、明确命令格式/版本和服务器一致实现；只传 seed、成绩或 SHA256 无法证明合法胜利。没有复演前可以联调协议与事务，但不能宣称已经具备正式防作弊。

## 客户端接入顺序

1. 完成可信认证服务接入，补齐账号切换和令牌续期。
2. 接入权威玩家快照，在联网页面展示正式档案，同时保留明确标识的离线档案。
3. 冻结六波命令和复演样例，再接开局票据、结算日志及按局查询。页面根据回执显示待确认或正式奖励。
4. 接营地升级、手记领取和影响解锁的剧情标记，分别处理幂等、过期与响应丢失。

网络业务由客户端会话和业务适配层负责，GameEntry 与 HotGame 继续负责启动和装配。
