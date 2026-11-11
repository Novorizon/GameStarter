# GameStarter

独立 Unity 2022.3.62f1 启动工程。GameEntry 配置开关并顺序初始化，GameHotUpdate 执行更新步骤，HotGame 组织业务。依赖 UnityPackages `v0.1.0-preview.35` 对应的固定提交 `5bf37118c412ce6f188340fe75661e54c0d91515`，包含 Story、Localization 和 Diagnostics。工程使用固定 Git 来源，不依赖相邻仓库或本地 file: 路径。

Novorizon SDK 包由 manifest 固定 Git 提交，不在 `Packages` 下重复嵌入；嵌入包会优先于 Git 依赖，验证脚本会拒绝这类覆盖。HybridCLR 按模块配置保留嵌入安装。

## 从哪里开始

- [Unity 云端编译与验收](Documentation/UnityValidation.zh-CN.md)：从 GitHub 获取、导入全部示例、Editor/Player 脚本编译、Play、IL2CPP 和补丁。
- [Demo 入口](Documentation/Demos.zh-CN.md)：示例前置条件和预期结果。
- [更新站部署](Deployment/README.md)：Nginx、目录与上传顺序。
- [验证范围](VALIDATION.md)：模型回归与原生验收分别记录。
- [SDK 协作](Documentation/SDKCoordination.zh-CN.md)：包维护分工、接口需求和固定提交交付。
- [塔防业务原型](Documentation/TowerDefense/README.zh-CN.md)：当前玩法、业务表、逻辑/表现分层、账号与结算契约、UI 与地图方案。
- [Meshy 模型制作清单](Documentation/Game/MeshyBrief.zh-CN.md)：模型优先级、尺寸、坐标轴、动作和挂点。
- [业务接入规划](Documentation/Game/ClientPlan.zh-CN.md)：Cube 塔防参考、剧情模块、实际服务器核对、卡牌等级和演出设计。
- [海岛营地大厅](Documentation/Game/IslandCamp.zh-CN.md)：漂流序章、帐篷/围栏/庭院/家园建设、任务领取与塔防补给。
- [塔防本轮交付记录](Documentation/TowerDefense/Delivery.zh-CN.md)：实际 Unity 画面/通关结果、已实现边界和仍待补验的项目。

## 本地与云端交接

使用 GitHub 上的同一开发分支交接。离开当前电脑或云端任务前，先保存 Unity 场景、Prefab 和配置，再 commit 并 push；另一端接手时先确认分支与提交，再拉取最新内容。关闭本地电脑后，未推送的修改不会自动出现在云端。

两端使用 Unity 2022.3.62f1，测试入口为 `Assets/Scenes/Bootstrap.unity`。接手先读 `AGENTS.md` 和[海岛营地约定](Documentation/Game/IslandCamp.zh-CN.md)：当前画面是大海岛中的营地区域，探索和全岛小地图暂不开发；3D 继续占位，正式模型由用户通过 Meshy 提供。SDK 固定版本与维护分工按仓库约定处理。

Unity 安装、许可证和 VPN 按各自运行环境确认；玩家本地存档不随 Git 同步。代码与项目资源通过 GitHub 接续，不依赖两个环境共享磁盘。

## 启动与业务

打开 Assets/Scenes/Bootstrap.unity 后 Play，先显示启动进度，再进入海岛营地大厅；首次进入播放漂流序章。打开“海岛手记”领取补给，逐步建设营地；点击“出征 · 守卫哨站”进入塔防，完成后返回营地刷新材料、任务和战绩。

Player 的热更路径如下；Editor 使用已编译程序集，默认启用 YooAsset 编辑器模拟模式。

```text
Unity 场景中的 GameEntry
  → await ResourceManager.InitializePackageAsync：仅初始化资源包文件系统
  → HotUpdateManager.Initialize：仅配置热更 Manager
  → await GameHotUpdate.RunAsync
      → HotUpdateManager.StartAsync 选择发行、下载资源、校验和加载代码
      → HotGame 初始化公共 Manager、读取 Luban 表、调用 ClientFlow
      → ClientFlow 播放首次序章并打开海岛营地，出征进入塔防，结算后返回营地
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

资源包初始化只是文件系统 warmup，不会选资源版本、加载版本清单或下载资源。实际下载在后面的热更步骤中阻塞等待该版本全部缺失资源，完成校验后才进入 `HotGame`。启动界面显示已完成步骤的进度；有下载时显示 YooAsset 实际字节进度。启动背景及已知控件的布局、颜色和文案通过资源更新缓存，下次启动使用；首次启动使用独立的内置图片、JSON 和 TMP 字体。


`HotGame` 是热更业务入口：初始化公共 Manager、加载 Luban 表，然后调用 `ClientFlow.StartAsync`；退出调用模块的 Dispose。`ClientFlow` 协调营地和塔防，档案/任务/建设由 `JourneyProgress` 与 `CampProgress` 处理，视图只发命令和展示。入口不包含 UI 组件、任务计算或玩家存档逻辑。详见[业务模块与数据规划](Documentation/Game/ClientPlan.zh-CN.md)。

`GameStartupOptions` 现在只传输入资源和初始 Map，不包含 Manager、运行状态或模块启动回调。场景/UI 加载器直接在 `HotGame` 装配；两个更新基础 Manager 由 Entry 释放，其余由 HotGame 集中释放。Editor 使用已编译程序集；关闭 Initialize Hot Update 也使用编译源入口，业务初始化顺序相同。普通编译源 Player 使用 Native Validation → Build Local Source Player 菜单。

命令行退出和表结构探针集中在 `GameEntry.NativeValidation.cs`、`HotGame.NativeValidation.cs`。`Game.Startup` 中的共享契约、更新步骤和启动展示必须配套主包基线；已有 IL2CPP 主包必须重建，不能只发布业务 DLL。本次已通过实际 Unity Player 脚本编译和两轮 Editor 模拟启动/退出；两轮之间重新打开 Bootstrap，不声明同一入口跨停止保留。详见[本轮报告](Documentation/Validation/EntryStartup/)。

## 海岛营地与塔防

塔防是这个项目在 `Assets/HotUpdate/TowerDefense` 下实现的业务示例，不是 UnityPackages 通用框架新增的固定玩法。`HotGame.StartGameAsync` 加载 Luban 后，通过[业务流程](Assets/HotUpdate/Client/ClientFlow.cs)打开海岛营地并按出征操作创建战斗会话和表现；换其他游戏时替换该业务入口，继续复用资源、UI、数据和网络能力。

首次打开可执行 `Tools → Novorizon → GameStarter → Prepare Startup Font` 同步启动及公共 TMP 字体。打开 `Assets/Scenes/Bootstrap.unity` 并 Play，日志先出现 `PLAYER_PAGE_READY`，出征后出现 `TOWER_DEFENSE_READY forest-diorama-1`。也可用 `Create Prototype Scene` 创建独立场景，它仍通过 AOT GameEntry 进入同一业务流程。

当前加载 14 张表：8 张战斗配置、主题/旅程任务/等级 3 张、营地等级/营地任务 2 张和原 Example。营地有四个建设阶段，任务与塔防提供本地材料；头像打开玩家成长/旅程档案，出征准备展示关卡、波次和补给，确认后加载战场；关卡有 6 波、7 个固定建造位置、3 塔型、4 怪物型和 7 张强化卡。先选塔卡，才显示可建区域；部署后点击已有塔查看实际射程和升级。卡牌显示表中的品级与图标，抽取仍使用原单池权重。局内预告下一波敌人和奖励，手动/后台/失焦/返回确认暂停相互独立；新出征使用新种子，重开保留原种子。

地图是 imagegen 生成的低多边形台地式 2D 预渲染图，包含砂岩道路、池塘、木桥和七个固定建造位置；18 个路线点和建造点随 Luban 表一起发布。固定 55° 正交相机呈现 3D 石塔、蓝顶基地和怪物，UI 使用松绿/铜金九宫格面板和 3×3 图标图集；所有文字使用 TMP。图片与动态 SDF 字体通过 YooAsset 租约加载，按 maps/ui/fonts 分类；字体许可随包保留在 `Assets/StreamingAssets/ThirdPartyLicenses/Noto-CJK.txt`。3D 当前仅保留简单几何体占位，正式模型由用户通过 Meshy 提供；移动真机和最终性能尚未验收。

当前玩法、输入链路、布局截图和真实 Unity 验收范围见[交付记录](Documentation/TowerDefense/Delivery.zh-CN.md)。原生表现由 `Game.Editor.TowerDefenseValidation.Run` 验证；带 `--novorizon-phone-layout` 可检查 19.5:9 编辑器横屏布局。当前没有真实登录或发奖后端，Editor 图形验收不代表手机真机、IL2CPP 或声音已通过。

## Luban 与设置

| 内容 | 路径 |
| --- | --- |
| Excel 与表定义 | Config/Luban/Data、Config/Luban/Defines |
| 热更表代码 | Assets/HotUpdate/Generated/Data，Game.Data.Generated 程序集 |
| YooAsset 数据 | Assets/GameAssets/Data/Luban |
| 音量/按键设置 | Assets/HotUpdate/GameSettings.cs |

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
python3 Tools~/sync-packages.py --ref 5bf37118c412ce6f188340fe75661e54c0d91515
python3 Tools~/verify.py --dotnet /path/to/dotnet
```

固定 Git 标签不会自动更新。模型回归不代替 Unity 导入、图形或 IL2CPP；原生结果按验收环境生成的日志和报告核对。导入包示例只用于验证副本，不提交回模板。

DLL 和 AOT 补充元数据现已纳入 YooAsset 同一资源版本，默认先统一下载全部缺失资源，再校验并加载代码。构建、职责与上传顺序见 [统一下载](Documentation/UnifiedDownloads.zh-CN.md)，原 UnityWebRequest 实现保存在 [备份分支](Documentation/UnityWebRequestBackup.zh-CN.md)。
