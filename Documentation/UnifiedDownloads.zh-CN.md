# YooAsset 统一下载

DLL、AOT 补充元数据、Luban 数据及其他游戏资源使用同一个 DefaultPackage 的精确资源版本。默认通过 YooAsset 下载这个版本全部缺失的文件，完成后读取代码字节，按签名发行清单校验，再交给 HybridCLR/Assembly.Load。缓存中已有且有效的文件不重复下载；当前不按 code 标签单独安排第一批下载。

上述下载/加载顺序描述 Player 的在线热更路径；Editor 使用已编译程序集，并由单独的 `simulateResourcesInEditor` 开关决定是否模拟资源。默认开启模拟，不在 Editor 默认流程中下载或加载远端 DLL。

## 入口与模块职责

GameEntry 负责 Inspector 场景参数和模式开关，持有本次启动的 CTS、task 和 State，并直接按顺序初始化资源与热更两个 Manager。具体流程是：

1. `await ResourceManager.Instance.InitializePackageAsync(initialParameters, token)`：只初始化 YooAsset 包的文件系统，不选版本、不加载版本清单、不下载。失败时不进入后续热更步骤。
2. `HotUpdateManager.Instance.Initialize(settings, inputOptions, token)`：只配置 Manager，不启动发行选择或加载游戏。
3. `await GameHotUpdate.RunAsync(token)`：调用已经配置的 `HotUpdateManager.StartAsync`，执行发行选择、资源准备/下载、字节校验、元数据与程序集加载，再进入 `HotGame`。

GameHotUpdate 只负责具体 settings、资源模式参数工厂、prepareContentAsync、loadCodeAsync 和进度接线。prepareContentAsync 在选定发行后准备精确资源版本并等待全部缺失资源下载完成；loadCodeAsync 通过 ResourceManager 的 TextAsset 租约读取 `HotUpdate/<baselineId>/<assembly>.dll.bytes`。读取结束立即归还租约，取得的托管字节仍用于后续校验和加载。

GameHotUpdate 不初始化两个 Manager、不拥有启动 CTS，也不做中央 Release。退出由 GameEntry 先取消启动，再按 `HotUpdateManager → ResourceManager` 释放：先停止 HotGame 与业务对象，再结束资源管理。HotGame 初始化其余公共 Manager、读取数据表，通过 ClientFlow 打开玩家页面和进入战斗。

SDK 的旧 `HotUpdateManager.InitializeAsync` 保留兼容调用；本工程采用上述“资源文件系统 warmup → 热更配置 → 显式 Start”顺序，避免把初始化与实际更新混在同一步。

GameStartupOptions 只携带输入资源和初始 Map。启动共享契约、更新流程和启动展示都属于 AOT，必须与主包基线一起构建；已有 IL2CPP 主包不能只替换业务 DLL。本次脚本编译及两轮 Editor 模拟启动/退出已通过，详见[本轮报告](Validation/EntryStartup/)。

## 离线、联网与 Editor 模拟

`GameEntry.resourceMode` 使用 `Game.ResourceMode`，默认 `Online`；`simulateResourcesInEditor` 默认 `true`，与资源离线/联网模式分开配置。

| 配置 | 执行方式 | 版本约束 |
| --- | --- | --- |
| `Offline` | 直接选择内置发行，准备内置资源文件系统 | 不读取、拒绝或删除已缓存的联网发行；切回联网模式后缓存仍可使用 |
| `Online` | Player 热更路径中按可信公钥与签名配置检查远端发行，准备其绑定的精确资源版本 | 更新资源准备失败仍成对退回内置代码/资源，不能拿新代码配旧资源 |
| Editor 模拟开启 | 使用 YooAsset 编辑器模拟文件系统，读取工程内资产；程序集使用 Editor 已编译版本 | 不把这种结果当作真实下载、离线内置文件或 Player DLL 加载通过 |
| Editor 模拟关闭 | 执行真实资源文件系统准备，按资源模式和发行上下文选择内置或远端来源 | Editor 代码仍是已编译程序集；此开关不会把 Editor 变成 IL2CPP Player |

模块中的 `ConfigureResourceModeAsync(content, token)` 返回所需的 `InitializeParameters`，负责文件系统模式选择和回退前的旧包处理；离线模式在发行选择阶段就跳过联网缓存，防止将模式限制误记为损坏发行。准备函数再设置精确资源版本和统一下载策略；GameEntry 不需要理解 YooAsset 的 Host/Offline 参数类型。

这些开关控制代码/资源更新来源，不控制玩家账号登录或服务器结算。无论模式如何，代码与资源仍由同一已验证发行配对，已有 AOT 元数据/程序集加载后不尝试进程内换版。本次已验证模拟模式下的入口配置、初始化顺序和退出；实际离线 bundle 与 HTTP 下载没有重跑，SDK 独立回归覆盖模式与缓存选择。

## 构建及发布

1. 新主包：先准备供 HybridCLR 生成阶段使用的资源，再生成 DLL/AOT 基线。GameBuild.BuildContent 回调把这些代码文件暂存为 `.dll.bytes`，重新构建最终 YooAsset 内置资源，绑定精确版本，最后构建 Player。
2. 补丁：先编译热更程序集、验证 AOT/桥接兼容，再暂存 DLL 和该主包归档中的 AOT 元数据，构建新的 YooAsset 资源版本，最后导出签名发行清单。
3. 暂存目录 `Assets/GameAssets/HotUpdate` 由工具生成，源码仓库仅保留目录和采集规则。每个代码文件单独打包，保留同名文件 GUID，支持增量下载。AOT 元数据的内容固定在主包基线中，更新不得替换。
4. `Artifacts/Publish/assets/` 包含资源包及版本清单；`manifests/` 包含签名发行入口。先完整上传 assets，再以临时文件重命名发布 manifests 中的 release.json。新流程不发布独立 patches DLL。

发行入口 release.json 仍是通过 HTTP 读取的小型控制清单；DLL、AOT 元数据和普通资源的下载及缓存均由 YooAsset 负责。代码缓存只保存发行清单和启动健康记录，不再额外缓存一份下载的 DLL。

## 失败与兼容

选中远程发行后，资源准备或任何代码哈希校验失败，会在加载程序集之前退回匹配的内置资源及代码。取消不会改成回退；加载过元数据或程序集后不会尝试在同一进程替换程序集。

新基线声明 codeInResources，缺少资源代码提供方时直接失败，不静默改用独立 DLL 下载。本次 AOT 接口、构建顺序和发行格式有变，已有游戏需要重建主包。旧流程见 [WR 备份](UnityWebRequestBackup.zh-CN.md)。

## 下载进度与界面

GameEntry 调用 AOT 模块 `StartupScreen.Open`，在资源初始化前创建可见启动界面；通过 `IStartupProgressSource` 读取快照。无下载时展示实际启动步骤进度，有下载时展示 YooAsset 已完成/总字节和百分比。资源准备阶段等待全部缺失资源，随后继续校验代码、加载 DLL 并初始化 HotGame。

下载完成后仍展示代码校验与业务初始化步骤；只有业务准备完成才淡出启动界面，进入热更 ClientFlow 的玩家页面。背景和已知控件的布局/颜色/文案通过 YooAsset 更新并缓存，下次启动读取；第一次启动使用独立内置图片、JSON 和 TMP 字体。新增控件或交互行为需匹配 AOT 实现。更新算法没有写入玩家页面或战斗模块。

## 入口职责重构的本轮验收

73 项工程模型、12 项领域和 13 项账号会话回归通过。真实 Unity 2022.3.62f1 的 Player 脚本编译通过；8 个 Demo 的 11 个 Editor、8 个 Player 程序集逐项检查，包含 Framework Demo。

Linux / OpenGLCore 软件渲染下完成 Online、Offline 两轮入口配置，均使用 Editor 资源模拟，实际加载表、TMP 界面并验证停止后的 Manager/视图清理。Unity 停止时会销毁 DontDestroyOnLoad 入口，因此轮间显式重新打开 Bootstrap；没有把结果写成“同一 Entry 跨停止保留”。原始报告见 [EntryStartup](Validation/EntryStartup/)。未重新执行真实离线 bundle、HTTP、IL2CPP 或手机验收。

启动进度展示与下一次启动读取缓存布局已在真实 Unity 中验证；下载控件使用 25/100 MiB 的验证输入，没有冒充真实 HTTP 下载。资源文件系统初始阶段响应 Entry 的取消 token；发行准备超时预算从后续 StartAsync 阶段开始。当前资源已单独构建，具体输出与业务范围见[客户端交付记录](TowerDefense/Delivery.zh-CN.md)。
