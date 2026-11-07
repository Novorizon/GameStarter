# GameStarter

独立 Unity 2022.3.62f1 启动工程。GameEntry 配置开关并顺序初始化，GameHotUpdate 执行更新步骤，HotGame 组织业务。依赖 UnityPackages 固定提交 `5cc7dffd5eec36dad86b63aaec19838a191fa8cf`；包元数据预备版本 preview.29 尚未发布标签。工程使用固定 Git 来源，不依赖相邻仓库或本地 file: 路径。

## 从哪里开始

- [Unity 云端编译与验收](Documentation/UnityValidation.zh-CN.md)：从 GitHub 获取、导入全部示例、Editor/Player 脚本编译、Play、IL2CPP 和补丁。
- [Demo 入口](Documentation/Demos.zh-CN.md)：示例前置条件和预期结果。
- [更新站部署](Deployment/README.md)：Nginx、目录与上传顺序。
- [验证范围](VALIDATION.md)：模型回归与原生验收分别记录。
- [SDK 协作](Documentation/SDKCoordination.zh-CN.md)：包维护分工、接口需求和固定提交交付。
- [塔防业务原型](Documentation/TowerDefense/README.zh-CN.md)：本分支的玩法、9 张业务表、逻辑/表现分层、账号与结算契约、UI 与地图方案。
- [塔防本轮交付记录](Documentation/TowerDefense/Delivery.zh-CN.md)：实际 Unity 画面/通关结果、已实现边界和仍待补验的项目。

## 启动与业务

打开 Assets/Scenes/Bootstrap.unity 后 Play，日志包含 LUBAN_EXAMPLE、HOT_UPDATE_READY、GAME_STARTER_READY。

Player 的热更路径如下；Editor 使用已编译程序集，默认启用 YooAsset 编辑器模拟模式。

```text
Unity 场景中的 GameEntry
  → await ResourceManager.InitializePackageAsync：仅初始化资源包文件系统
  → HotUpdateManager.Initialize：仅配置热更 Manager
  → await GameHotUpdate.RunAsync
      → HotUpdateManager.StartAsync 选择发行、下载资源、校验和加载代码
      → HotGame 初始化其余 Manager、读取 Luban 表、进入具体业务
```

`GameEntry` 是场景入口和启动顺序的 owner：在 Inspector 配置输入、初始 Map、热更与资源模式开关，持有启动 CTS、task 和 State，并直接初始化资源与热更两个 Manager。先等待资源文件系统初始化成功，再配置 `HotUpdateManager`，最后调用 `GameHotUpdate.RunAsync` 开始具体更新。资源包初始化失败就停止，不提前进入热更步骤。

| GameEntry 开关 | 默认值 | 作用 |
| --- | --- | --- |
| `initializeHotUpdate` | `true` | Player 使用热更代码入口；关闭时使用编译源入口。Editor 仍使用已编译程序集 |
| `resourceMode` | `Game.ResourceMode.Online` | Player 热更模式下在线检查已配置的可信发行，准备对应资源；可改为 `Offline` 只使用匹配的内置发行 |
| `simulateResourcesInEditor` | `true` | Editor 优先使用资源模拟；关闭后执行真实资源文件系统配置，内置/远端来源还取决于模式和所选发行上下文。Player 不使用此模拟开关 |

`Offline` 直接选择匹配的内置发行，禁止远端检查，并跳过联网发行缓存，不将合法联网缓存标记为损坏；切回 `Online` 后这些缓存仍可使用。`Online` 保留可信公钥、签名和精确版本要求，准备失败仍可成对回退到内置发行。资源模式不代表账号服务器已经登录，也不会自动连接业务服务器。

`GameHotUpdate` 只放具体的 settings、资源模式参数工厂、资源版本准备、DLL 字节读取和进度接线。它不初始化 Manager、不创建启动 CTS，也不做中央 Release；`RunAsync` 调用已经配置好的 `HotUpdateManager.StartAsync` 执行发行选择、下载、校验和代码加载。

`GameEntry` 退出时先取消启动，再按 `HotUpdateManager → ResourceManager` 顺序释放，并转发应用暂停/焦点事件及保留验收探针。`HotUpdateManager` 关闭 `HotGame`，后者集中释放其业务与其余 Manager；资源下载、日常租约仍由 `ResourceManager` 管理。

资源包初始化只是文件系统 warmup，不会选资源版本、加载版本清单或下载资源。实际下载在后面的热更步骤中阻塞等待该版本全部缺失资源，完成校验后才进入 `HotGame`。当前仅有 `StartupMessage/DownloadProgress` 数据，没有可见下载进度条；进度 UI 仍是后续计划，不算本轮已实现功能。


`HotGame` 是真正的游戏业务入口，位于 `Assets/HotUpdate`：初始化公共 Manager、加载 Luban 表，再直接调用塔防等业务模块。新增业务就在这里显式调用初始化和关闭，不需要额外的 Session 或模块注册器。无生产使用的 `GameSession`、`IStartupModule` 和 `StartupModule` 已移除。

`GameStartupOptions` 现在只传输入资源和初始 Map，不包含 Manager、运行状态或模块启动回调。场景/UI 加载器直接在 `HotGame` 装配；两个更新基础 Manager 由 Entry 释放，其余由 HotGame 集中释放。Editor 使用已编译程序集；关闭 Initialize Hot Update 也使用编译源入口，业务初始化顺序相同。普通编译源 Player 使用 Native Validation → Build Local Source Player 菜单。

命令行退出和表结构探针仍集中在 `GameEntry.NativeValidation.cs`、`HotGame.NativeValidation.cs`。本次 `Game.Startup` 中 `GameStartupOptions` 构造签名变化，并把更新/资源流程移到新的 AOT 类型 `GameHotUpdate`；已有 IL2CPP 主包必须重建基线，不能只发布业务 DLL。本次已通过实际 Unity Player 脚本编译和两轮 Editor 模拟启动/退出；两轮之间重新打开 Bootstrap，不声明同一入口跨停止保留。详见[本轮报告](Documentation/Validation/EntryStartup/)。

## 本分支的塔防业务原型

塔防是这个项目在 `Assets/HotUpdate/TowerDefense` 下实现的业务示例，不是 UnityPackages 通用框架新增的固定玩法。`HotGame.StartGameAsync` 加载 Luban 后，通过[塔防业务装配](Assets/HotUpdate/TowerDefense/Presentation/HotGame.TowerDefense.cs)创建纯 C# 战斗会话和表现；换其他游戏时替换该业务入口，继续复用资源、UI、数据和网络能力。

首次打开执行 `Tools → Novorizon → Tower Defense → Prepare TMP Font`，等待 `TOWER_DEFENSE_TMP_FONT_READY`；需要的 TMP Essentials 会由该配置流程导入。随后打开 `Assets/Scenes/Bootstrap.unity` 并 Play，日志应出现 `TOWER_DEFENSE_READY prototype-1` 和原有 Ready 标记。也可用 `Create Prototype Scene` 创建独立场景，它仍通过 AOT GameEntry 进入同一业务流程。

当前真实表内容为 6 波、7 建造点、3 塔型、4 怪物型、7 张卡，使用 9 张新增业务表，加原示例共加载 10 张表。选建造点和塔型、升级、开始波次，在波间三选一，守到通关或基地归零。主流程使用真实表，5 波的 `DemoConfiguration` 只用于独立领域测试 fallback。

地图是 imagegen 生成的 2D 底图，塔、基地和 NPC 暂用 3D primitives；所有文字使用 TMP。图片与动态 SDF 字体通过 YooAsset 租约加载，按 maps/ui/fonts 分类；字体许可随包保留在 `Assets/StreamingAssets/ThirdPartyLicenses/Noto-CJK.txt`。PC 与手机横屏、触控共同入口已配置，移动真机和最终 3D 美术尚未验收。

塔防初次接入时，已在真实 Unity 2022.3.62f1 / Linux / OpenGLCore 的 llvmpipe 软件渲染环境跑通 6 波胜利、胜利后重开、第 2 波无塔防守失败及失败后重开，验证实际 Unity Button `onClick.Invoke` 事件、重复操作拒绝、TMP 文字及停止 Play 清理，保留 4 张实际渲染截图；当时 72 项完整模型、12 项领域、13 项账号协调回归通过。该记录不代表本次入口/热更职责重构已经通过验证，也不是设备鼠标/触摸点击验收。当前没有真实登录或发奖后端；手机真机、IL2CPP 和声音仍未执行原生验收。具体历史指标、日志与补验状态见[交付记录](Documentation/TowerDefense/Delivery.zh-CN.md)。

## Luban 与设置

| 内容 | 路径 |
| --- | --- |
| Excel 与表定义 | Config/Luban/Data、Config/Luban/Defines |
| 热更表代码 | Assets/HotUpdate/Generated/Data，Game.Data.Generated 程序集 |
| YooAsset 数据 | Assets/GameAssets/Data/Luban |
| 设置保存/恢复 | Assets/HotUpdate/GameSettings.cs |

生成表关闭 autoReferenced，AOT 不引用 cfg.Tables。表结构可在现有 AOT 能力与程序集拓扑范围内随补丁更新。表已通过 YooAsset 字节加载器接好，不需逐表注册：

```csharp
var row = DataManager.Instance.Get<cfg.Tables>().TbExample.Get(1);
GameSettings.Instance.SetMasterVolume(0.8f);
GameSettings.Instance.SetGroupVolume(AudioGroup.Music, 0.5f);
await GameSettings.Instance.RebindAsync("Move", 0);
```

修改表后执行 Tools → Novorizon → GameStarter → Generate Tables，并等 Unity 编译完成。设置在音频/输入初始化后恢复，修改、后台、失焦和退出时保存；自动保存不覆盖无效存档，明确修改设置后才替换。

## 构建与发行

Tools → Novorizon → GameStarter：

1. Generate Tables：使用 .NET 8 和官方 Luban CLI 生成表。
2. Build Resources：构建当前平台的 YooAsset 资源。
3. Build New Player：资源、公钥、HybridCLR 原生运行时、基线和 IL2CPP 主包。
4. Export Signed Update：编译兼容 DLL、构建资源、签名配套清单、整理发布目录。

首次主包构建在工程外生成 RSA-3072 私钥，公钥资产进入 Assets/Resources。已有公钥时需使用匹配私钥；可通过 NOVORIZON_SIGNING_KEY_PATH 指定工程外的私钥文件，不自动替换发行密钥。

输出在 Artifacts/Publish/{assets,manifests}，先上传包含代码与普通资源的 assets，最后发布清单；旧主包仍需对应基线和资源。旧 WR 备份流程的 patches 目录继续按原版本维护。下载根为 https://15000.top/game-updates/（443），游戏 TCP 为 15000.top:22370，统一配置在 GameReleaseConfiguration。模板不自动连接业务服务器。

签名清单绑定 DLL 哈希、基线和资源版本。资源准备失败时，在加载代码前切回配套内置发行；加载代码后不在同一进程替换发行。AOT、契约和程序集拓扑变化需重建主包。

## 更新与维护

```sh
python3 Tools~/sync-packages.py --ref 5cc7dffd5eec36dad86b63aaec19838a191fa8cf
python3 Tools~/verify.py --dotnet /path/to/dotnet
```

固定 Git 标签不会自动更新。模型回归不代替 Unity 导入、图形或 IL2CPP；原生结果按验收环境生成的日志和报告核对。导入包示例只用于验证副本，不提交回模板。

DLL 和 AOT 补充元数据现已纳入 YooAsset 同一资源版本，默认先统一下载全部缺失资源，再校验并加载代码。构建、职责与上传顺序见 [统一下载](Documentation/UnifiedDownloads.zh-CN.md)，原 UnityWebRequest 实现保存在 [备份分支](Documentation/UnityWebRequestBackup.zh-CN.md)。
