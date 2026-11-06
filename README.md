# GameStarter

独立的 Unity 基础启动工程，建议 Unity 2022.3.62f1。默认通过 Git/UPM 引用 **com.novorizon.framework.all**，安装整套库。下载或克隆本工程即可打开，不需要在旁边放置 UnityPackages 仓库。

## 使用

1. 使用 Unity Hub 打开本工程，等待 Unity Package Manager 下载聚合包、各库及 Unity 基础模块，并完成脚本编译。Git URL 安装需要本机可用 Git 和网络连接。
2. 打开 `Assets/Scenes/Bootstrap.unity`，进入 Play Mode，入口准备好公共 Manager 后输出 `GAME_STARTER_READY`，随后热更入口输出 `HOT_UPDATE_READY 1.0.0`。
3. 聚合安装器会完成整套包的安装及 HybridCLR 默认配置。保留 **Tools/Novorizon/Optional Modules** 窗口用于按模块重试或维护安装来源。

本项目只依赖包的公开程序集。Framework 提供单例、线程、事件、计时和池等基础能力；GameEntry 使用其 MonoSingleton，并在 InitializeManagersAsync 中集中准备公共 Manager。StartupModule 与 GameSession 继续负责额外启动模块的初始化、更新和清理。

`Assets/Game/Runtime` 中的启动流程是：`GameEntry.OnStart` 创建 `GameSession` → `InitializeManagersAsync` 准备公共 Manager → 按列表顺序等待模块 `InitializeAsync` → 全部成功后逐帧调用 `Tick` → 退出或失败时逆序调用 `Shutdown`。`IStartupModule` 定义这三个方法，`StartupModule` 是可挂载到场景的空实现基类。`StartupState` 定义在 `GameSession.cs` 中，供入口查询启动状态。

`GameEntry.OnStart()` 是 Framework 调用的启动入口；它创建的 `GameSession` 是普通 C# 对象，负责执行启动模块列表，每次从启动到退出对应一个对象。

添加项目逻辑时，继承 `StartupModule`，重写需要的初始化、更新和清理方法，将组件挂到 Bootstrap 场景的 GameObject 上，再拖入 GameEntry 的 **Startup Modules** 列表。被依赖的模块排在前面；异步初始化应响应取消信号，清理方法也应处理初始化只完成一部分的情况。默认列表为空，公共 Manager 准备完成后进入就绪状态。

`StartupModule` 是项目自定义的抽象组件基类，本类不能直接挂载，场景中添加的是具体子类组件。Inspector 数组用于保存这些组件的引用和执行顺序，各模块的序列化参数在对应组件中配置；如果项目使用普通 C# 初始化对象，也可以在代码中通过 `IStartupModule` 组织流程。

项目默认引用的聚合包地址：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.20
```

UPM 不支持 package.json 中的 Git 传递依赖，因此工程 manifest 同时声明 all 和它的依赖来源，让首次导入即可解析运行时程序集。所有来源由同一份聚合目录生成，维护时运行 sync-packages.py；源码由 UPM 获取，不随工程复制。

公共 Manager 的准备顺序写在 `GameEntry.InitializeManagersAsync`：初始化事件和定时器服务 → 准备资源管理器 → 运行时数据与本地存储 → 输入 → 场景 → 音频 → 配置 → UI → GPU 动画调度器 → ECS EntityManager/WorldManager → 有 NetworkSettings 时初始化网络。Framework、UI、ECS 的自动驱动和运行退出清理仍由各库负责，GameEntry 不重复调用其 Update。

空模板不附带 YooAsset 资源包；项目配置好 DefaultPackage 的采集和构建后，在 GameEntry 上开启 **Initialize Yoo Asset**，入口会等待 ResourceManager.InitializeAsync 成功后继续。网络需要在 Resources 中创建 NetworkSettings 并填写项目配置；入口只做 Initialize，实际联网由项目流程调用 ConnectAsync。

HybridCLR 安装项会处理官方依赖、嵌入及默认 Configure；实际 DLL/AOT 收集、原生安装和 Player 构建使用封装菜单。Odin 插件由项目自行提供，缺少 Odin 不影响 Ability 运行时。

## HybridCLR 的使用

项目已经包含热更新程序集和入口，打开 Bootstrap 场景即可在 Editor 验证启动：

- `Assets/Game/Runtime/GameEntry.cs`：AOT 入口，初始化公共 Manager 和启动模块。
- `Assets/HotUpdate/HotGame.cs`：热更入口，实现封装提供的 `IGameModule`；在基础服务就绪后由 GameEntry 启动，执行项目热更初始化并调用 `ReportReady`。

GameEntry 在基础 Manager 和可选启动模块就绪后，直接调用 `HotUpdateManager.Instance.InitializeAsync(new UpdateSettings(), token)`。没有自动启动宿主，也没有启动 JSON；是否启用由 GameEntry Inspector 的 Initialize Hot Update 开关决定，默认开启。配置直接通过 C# 传入，默认使用 Unity 应用 ID、Game.HotUpdate 程序集和 Game.HotUpdate.HotGame 类型。Editor 使用 Unity 已编译的 Game.HotUpdate 程序集；IL2CPP Player 由封装准备版本、补充 AOT 元数据并加载热更 DLL。GameStarter 不再额外实现 DLL 加载逻辑。

1. 编辑器验证：进入 Play，依次查看 `HOT_UPDATE_READY 1.0.0`、封装的 `STARTUP_OK` 和 `GAME_STARTER_READY` 日志。
2. 构建主包：选择目标平台，使用 **Tools → Novorizon → Hot Update → 2. Build New Player**。封装自动安装匹配原生运行时、收集热更 DLL/AOT 元数据并准备内置版本。需要对应 Unity 平台模块和 IL2CPP 工具链。
3. 导出补丁：修改 `Assets/HotUpdate` 中的代码，使用 **3. Export Patch**。修改 AOT 层 GameEntry、共享契约或程序集依赖后，需要重新构建主包。

项目的可更新初始化逻辑写在 HotGame.InitializeAsync，资源和订阅清理写在 Shutdown。AOT 程序集不直接引用 Game.HotUpdate；热更程序集可以引用 AOT 的公开接口。远程更新需要项目自己的发布地址和签名公钥，设置字段及发布工具见 [HybridCLR 封装指南](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.20/Packages/com.novorizon.framework.hybridclr/README.md)。

## Visual Studio 代码跳转

工程清单声明了 Unity 官方 `com.unity.ide.visualstudio` 2.0.22，由 Package Manager 安装，用于生成 Visual Studio 的解决方案和项目文件。

1. 等待 Unity 完成包解析和脚本编译；Console 的依赖错误需要先处理。
2. 在 **Edit → Preferences → External Tools** 中将 **External Script Editor** 设为实际安装的 Visual Studio，点击 **Regenerate project files**。
3. 从 Unity 双击脚本或使用 **Assets → Open C# Project** 打开生成的解决方案，再使用 F12 跳转。

`StartupModule` 的定义位于 [Assets/Game/Runtime/StartupModule.cs](Assets/Game/Runtime/StartupModule.cs)，和 GameEntry 同属 `GameStarter.Runtime` 程序集。`.sln`、`.csproj` 由各自的 Unity 编辑器生成，不随工程源码提交。

## 开发验证

Python 3.12+、.NET SDK 8+、Git：

```sh
python3 Tools~/verify.py --dotnet /path/to/dotnet
python3 Tools~/export.py --output /tmp/GameStarter.zip
```

验证器从项目清单里的同一个 Git 版本获取 Framework 源码到临时目录，编译真实 Core/MonoSingleton、HybridCLR 封装的 IGameModule 契约，并将 AOT 和热更代码编译成独立程序集，运行 47 组生命周期、Manager 准备、热更入口及安装 API 模型。它不会从父目录寻找包仓库。Unity Editor、UPM 服务和 IL2CPP 使用原生流程单独验收；模型验证不代表已完成原生验收。

GpuAnimator 包名为 `com.novorizon.gpuanimator`，可选模块安装器会同时移除旧的 `com.guanjinbiao.gpuanimator` 依赖。

更新依赖目录使用 `Tools~/sync-packages.py --ref <已发布标签>`。该工具从公开 Git 地址读取版本目录，同时更新聚合包、完整依赖清单和模块来源。正常打开工程不需要运行 Python 工具。

包采用固定版本标签，不会随 GitHub 的新提交自动升级。维护依赖时运行 `python3 Tools~/sync-packages.py --ref v0.1.0-preview.20`；该命令同步聚合包及全部来源，Unity 随后按清单重新解析。当前入口包含 UIManager 和 GpuAnimatorManager，播放器会自行注册到 GPU 调度器，入口不重复推进动画。此次 AOT 变化需要重新构建 HybridCLR 主包。


新增服务在 GameEntry 中集中初始化。DataManager 只持有项目定义的运行时对象，LocalStorageManager 默认写入 persistentDataPath/Storage；ConfigManager 只准备加载来源，不假定存在任何业务表。默认场景来自 Build Settings，默认配置来自 Resources；开启 Initialize Yoo Asset 后分别使用资源包场景加载器和 TextAsset 配置来源。

播放音频时由游戏场景的摄像机提供 AudioListener；空 Bootstrap 场景只启动框架，不创建业务摄像机或加载音频。音频与配置的独立包也包含在 framework.all 中。API 示例及取消、所有权、存储迁移规则见 [基础服务说明](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.20/Documentation/GameServices.zh-CN.md)。

## 启动与退出

GameEntry.OnStart 直接调用各 Manager 的初始化方法；基础服务就绪后加载并初始化 HotGame，完成前状态保持 Starting。OnExit 先 Release 热更管理器，再按顺序释放其他 Manager。StartupModule 与 GameSession 保留，用于项目额外模块；入口不维护 ManagerLifetime、借用标记或跨 Manager 的失败回滚。

Framework 保证同次运行中 OnStart/OnExit 各执行一次。主动关闭可调用 GameEntry.Release() 或已取得实例的 Dispose()；退出钩子也会在 Unity 销毁、退出时执行。各 Manager 的 Release 不创建新实例，重复清理和清理异常由各自内部处理。

ShutdownAsync 保留兼容调用，发起退出后返回，不再汇总等待所有服务。UI 原生销毁及资源租约释放在 UI 包内部完成；ResourceManager 自己等待未完成获取和旧关闭操作。需要等待具体服务时调用对应的 ShutdownAsync。

开启 YooAsset 后，UI 和配置读取继续使用独立租约。相关规则见 [生命周期说明](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.20/Documentation/LifecycleAndOwnership.zh-CN.md)。AOT 生命周期接口变化需要重新构建 HybridCLR 主包。

## 输入、Luban 与音频

GameEntry 在存储之后初始化 InputManager，使用 Inspector 指定的 Input Actions，留空时读取 Resources/InputActions；默认启用 Gameplay。示例包含键盘 WASD/回车与手柄摇杆/确认，项目可编辑 .inputactions 定义自己的动作。工程 Active Input Handling 已设 Both，保留现有 UI 模块输入。Manager 由 GameEntry 直接初始化，在 OnExit 中统一调用 Release；订阅与资源回收在各 Manager 内部处理。

使用 Novorizon → Config → Luban → Create Project/Generate 创建 Excel 配置工程并生成代码与数据。首次需要 Git 和 .NET 8 SDK，工具缓存到 Library。生成的 Tables 由项目按需要调用 ConfigManager.LoadLubanAsync 加载；开启 YooAsset 时需配置与导出清单一致的资源地址。GameEntry 已提供文本和二进制租约加载器。

音频可用 PlayMusic 自动交叉渐变、句柄 FadeTo/FadeOut 控制声音，以及 SetMixerGroup 路由到项目 AudioMixer。输入、Luban 和音频的 API 示例见对应包 README。新增 AOT 程序集依赖后，需要重建 HybridCLR 主包。

HotGame.cs 是类定义。GameEntry 显式启动加载后，由 HotUpdateManager 取得程序集，通过 Activator.CreateInstance 创建对象并调用 InitializeAsync。HotGame 是普通 C# 对象，不挂场景；场景只需 GameEntry。HotGame 不再等待 GameEntry.WaitUntilReadyAsync，因此入口可以 await 热更初始化，不会互相等待。

修改远程地址、渠道、公钥等参数时，运行时传入项目的 UpdateSettings。构建菜单使用同一配置：在项目 Editor 初始化代码中将 GameBuild.SettingsFactory 绑定到共享的 C# 配置工厂，示例见封装指南。生成的发布清单与下载缓存由工具内部维护，不需要编辑启动 JSON。升级旧工程时删除旧启动配置和 Bootstrap 宿主组件；启动流程属于 AOT 改动，需要重新构建主包。

GameEntry 已使用 Framework 内置的静态重置生成器，声明为 partial；不需要手写 sessionOwner 的初值恢复。进入 Play 时由 Framework 判断 Domain Reload 选项，关闭时先退出旧生命周期再恢复静态声明。关闭 Scene Reload 时保留场景组件及其配置，OnStart/OnExit 管理运行资源。


### HotGame 中的业务启动位置

当前代码仍由 GameEntry.InitializeManagersAsync 初始化公共 Manager；HotGame.InitializeAsync 只做就绪确认，尚未包含游戏业务，也没有自动加载业务首场景。

| 内容 | 当前入口 | 如果需要热更装配逻辑 |
| --- | --- | --- |
| YooAsset 等用于取得补丁的资源系统 | GameEntry | 保留在 GameEntry，加载热更程序集前完成 |
| HybridCLR/AOT 元数据、热更 DLL 加载 | GameEntry 调用 HotUpdateManager | 保留在 GameEntry |
| 音频、UI、输入、网络、ECS 等公共 Manager 的初始化调用 | GameEntry.InitializeManagersAsync | 移入 HotGame 自己的 InitializeManagersAsync |
| 项目的表注册、玩法系统、登录界面、首场景 | 项目自行添加 | 由 HotGame.StartGameAsync 组织，从 InitializeAsync 等待完成 |
| 热更业务资源和订阅释放 | HotGame.Shutdown | 和移入的初始化成对迁移，先于 AOT 资源层退出 |

HotGame 是入口，具体业务类可以放在 Assets/HotUpdate 下的其他目录，不需要把所有实现塞进同一个类。可以在 HotGame.InitializeAsync 内按顺序等待 InitializeManagersAsync(token)、StartGameAsync(token)，最后调用 host.ReportReady。这里的两个私有方法由项目在 HotGame 中实现或委托给业务类；无需再创建启动模块或场景挂载组件。迁移某个 Manager 的调用后，要删除 GameEntry 中原有的同一调用，并配对安排释放。

Game.HotUpdate.asmdef 当前只引用热更契约程序集。移动调用或编写业务时，在这个 asmdef 中添加实际使用的 AOT 公共程序集引用，例如 Game.Framework.Core、Game.Framework.Unity、Game.Framework.UI；不要让 AOT 的 GameStarter.Runtime 反向引用 Game.HotUpdate。若关闭 Initialize Hot Update，HotGame 不会创建，非热更业务启动可写在 GameEntry 的业务启动方法中。

ConfigManager 目前仍存在并负责 JSON/文本和 Luban 表加载；DataManager 当前仍是类型注册容器。此前讨论的 Luban 职责迁移尚未实施，本文不将尚未存在的 DataManager 表 API 当作已实现接口。
