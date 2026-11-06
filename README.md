# GameStarter

独立 Unity 启动工程，建议 Unity 2022.3.62f1。工程版本 `v0.1.0-preview.25`，依赖 UnityPackages `v0.1.0-preview.23`。通过 Git/UPM 获取 framework.all 及依赖，不使用相邻仓库路径。

## 启动与业务代码

打开 Assets/Scenes/Bootstrap.unity 后进入 Play。默认日志包含 LUBAN_EXAMPLE、HOT_UPDATE_READY、STARTUP_OK、GAME_STARTER_READY。

GameEntry 是 AOT 入口，负责选择发行和准备资源；HotGame 是游戏入口，负责 Manager、数据表和业务初始化。启动顺序：

```text
GameEntry.InitializeAsync
  → HotUpdateManager 选择并验证发行清单
  → PrepareResourcesAsync：清单指定的 YooAsset 版本、下载和资源准备
  → 装载 AOT 元数据和热更 DLL
  → HotGame.InitializeAsync
     → InitializeManagersAsync：存储、输入、音频、设置恢复、UI/ECS 等
     → StartGameAsync：LoadTablesAsync → 可选模块 → 业务
     → ReportReady
```

资源准备发生在任何热更程序集加载之前。签名清单同时绑定 DLL 哈希、基线和资源包版本。新版资源准备失败时，在装载托管代码前回到配套的内置 DLL 与资源；装载代码后不在同一进程切换发行。

Editor 使用 Unity 已编译的游戏程序集及 YooAsset 模拟模式。关闭 Initialize Hot Update 可选择编译源模式；普通 AOT 主包使用 Native Validation → Build Local Source Player 菜单，游戏源码正常编入主包。两种模式通过同一个 IGameModule 入口启动，GameEntry 不直接引用热更表类型。可选 StartupModule 列表和 GameSession 保留，默认为空；业务可以直接写在 HotGame.StartGameAsync。

## 服务器与端口

| 内容 | HTTPS 地址前缀 | 服务器目录 |
| --- | --- | --- |
| 版本清单 | https://15000.top/game-updates/manifests/ | /srv/gameserver/updates/manifests/ |
| DLL 补丁 | https://15000.top/game-updates/patches/ | /srv/gameserver/updates/patches/ |
| YooAsset 资源 | https://15000.top/game-updates/assets/ | /srv/gameserver/updates/assets/ |

HTTPS 使用默认 443，网址不必写 :443。游戏 TCP 为 15000.top:22370，与下载端口独立。地址统一写在 GameReleaseConfiguration；TCP 协议、认证和连接时机仍由具体游戏配置，不自动连接。

清单按 appId/platform/channel/baselineId 分开。例如：

```text
manifests/com.novorizon.gamestarter/windows-x64/development/{baselineId}/release.json
patches/com.novorizon.gamestarter/windows-x64/development/{baselineId}/{payloadHash}/Game.HotUpdate.dll.bytes
assets/com.novorizon.gamestarter/windows-x64/DefaultPackage/...
```

Nginx 配置片段见 Deployment/nginx-game-updates.conf，放进现有 15000.top 的 HTTPS server 块。它保留现有证书和网站配置，不需要额外的下载服务端程序。

## Luban 数据和表结构热更

源表：Config/Luban/Data/example.xlsx；定义：Config/Luban/Defines/example.xml。生成代码在 Assets/HotUpdate/Generated/Data，独立程序集为 Game.Data.Generated；二进制和清单在 Assets/GameAssets/Data/Luban，通过 YooAsset 加载。

Game.Data.Generated 与 Game.HotUpdate 都属于热更程序集，生成程序集关闭 autoReferenced，AOT 不引用 cfg.Tables。新增表、修改字段后重新生成并导出配套更新，可以随 DLL 和资源更新。增加 AOT 依赖、原生桥接需求或改变程序集依赖顺序仍需新主包；不是所有 AOT API 变化都能通过补丁完成。

HotGame 已写好 CreateTables、LoadTablesAsync、字节加载器和查询示例：

```csharp
var row = DataManager.Instance.Get<cfg.Tables>().TbExample.Get(1);
```

无需逐张注册。修改表后执行 Tools → Novorizon → GameStarter → 1. Generate Tables。生成、采集、寻址和 asmdef 均已配置；替换示例表时同步修改示例查询。

## 设置保存与恢复

HotGame 在音频和输入初始化后调用 GameSettings.Instance.Initialize。默认保存主音量、Music/Sound/Voice/UI 分组音量和输入重绑定；使用 LocalStorageManager 的版本化存档。

```csharp
GameSettings.Instance.SetMasterVolume(0.8f);
GameSettings.Instance.SetGroupVolume(AudioGroup.Music, 0.5f);
await GameSettings.Instance.RebindAsync("Move", bindingIndex: 0);
```

修改时立即保存，失焦、进入后台和退出时再次保存当前状态。重启时恢复；无效设置会报告错误，自动保存不会覆盖坏存档，明确修改设置后才写入替代值。设置类位于热更业务目录，不是通用配置表管理器。

## 构建和发布

需要 Git；生成表需要 .NET 8 SDK。Editor 首次 Play 可使用已提交的示例生成物。常用菜单位于 Tools → Novorizon → GameStarter：

1. Generate Tables：生成所有表，并检查源文件是否与生成物一致。
2. Build Resources：构建当前平台 YooAsset 资源。
3. Build New Player：准备资源、公钥，安装匹配的 HybridCLR 原生运行时、生成基线并构建主包。
4. Export Signed Update：构建新资源、编译并检查热更 DLL、生成配套清单、签名并整理发布目录。

首次主包构建会生成 RSA-3072 签名密钥。公钥保存为 Assets/Resources/GameUpdateKey.asset 并随主包分发，建议纳入项目版本管理。私钥位于当前用户本地应用数据目录的 Novorizon/Signing/appId/，在 Unity 工程之外；也可通过 NOVORIZON_SIGNING_KEY_PATH 指定已有私钥。妥善备份私钥，不上传到服务器或代码仓库。已有公钥而缺失私钥时不会自动替换密钥。

发行输出为 Artifacts/Publish/{assets,patches,manifests}/。上传 assets、patches 完成后，最后发布 manifests。不可覆盖已有哈希 DLL 或带版本的资源；新的发行应使用新版本。服务器保留旧基线及仍在用的资源，旧客户端才能继续取得匹配内容。

默认运行时若尚无主包公钥，不启用远程 DLL 更新；构建主包时生成公钥后启用指定 HTTPS 清单源。远端不可用时使用经过验证的本地发行或主包内置发行。

## 本机 Unity 原生验收

当前云环境没有 Unity。自动回归结果不能替代原生验收。建议在独立验证副本中安装 Unity 2022.3.62f1 和 Windows IL2CPP 模块后执行：

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe"
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase Editor
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase Player
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase SchemaPatch
# 上传 Artifacts/Publish 的 assets、patches、manifests 后，用原来的 Player 验证新表：
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase PatchPlayer
```

先关闭同一工程的 Unity Editor。Editor 阶段验证实际包导入、模拟资源和启动；Player 阶段构建并运行 IL2CPP 主包；SchemaPatch 在验证副本中新增 TbNativePatchProbe 表及类型，重新生成和导出；PatchPlayer 使用原主包从服务器获取发行并检查新表存在，日志应包含 NATIVE_SCHEMA_READY TbNativePatchProbe 和 NATIVE_PLAYER_READY。

报告和日志位于 Artifacts/NativeValidation。SchemaPatch 会修改验证副本中的表定义，使用独立副本执行，不直接用于正在开发的业务工程。

## 开发验证与升级

```sh
python3 Tools~/verify.py --dotnet /path/to/dotnet
python3 Tools~/sync-packages.py --ref v0.1.0-preview.23
```

固定 Git 标签不会自动升级。验证按清单从远端获取包，在边界模型下编译实际 DataManager、Luban 运行时和表；原生验证入口单独由 Unity 编译执行。详细范围见 VALIDATION.md。

本次调整 AOT 启动契约，并将生成表移入热更程序集。已有主包需要重新构建一次，之后在既有基线和程序集拓扑范围内发布更新。
