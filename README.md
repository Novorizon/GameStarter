# GameStarter

独立的 Unity 基础启动工程，建议 Unity 2022.3.62f1。默认通过 Git/UPM 引用 **com.novorizon.framework.all**，安装整套库。下载或克隆本工程即可打开，不需要在旁边放置 UnityPackages 仓库。

## 使用

1. 使用 Unity Hub 打开本工程，等待 Unity Package Manager 下载聚合包、各库及 Unity 基础模块，并完成脚本编译。Git URL 安装需要本机可用 Git 和网络连接。
2. 打开 `Assets/Scenes/Bootstrap.unity`，进入 Play Mode，入口准备好公共 Manager 后输出 `GAME_STARTER_READY`。
3. 聚合安装器会完成整套包的安装及 HybridCLR 默认配置。保留 **Tools/Novorizon/Optional Modules** 窗口用于按模块重试或维护安装来源。

本项目只依赖包的公开程序集。Framework 提供单例、线程、事件、计时和池等基础能力；GameEntry 使用其 MonoSingleton，并在 InitializeManagersAsync 中集中准备公共 Manager。StartupModule 与 GameSession 继续负责额外启动模块的初始化、更新和清理。

`Assets/Game/Runtime` 中的启动流程是：`GameEntry.OnStart` 创建 `GameSession` → `InitializeManagersAsync` 准备公共 Manager → 按列表顺序等待模块 `InitializeAsync` → 全部成功后逐帧调用 `Tick` → 退出或失败时逆序调用 `Shutdown`。`IStartupModule` 定义这三个方法，`StartupModule` 是可挂载到场景的空实现基类。`StartupState` 定义在 `GameSession.cs` 中，供入口查询启动状态。

`GameEntry.OnStart()` 是 Framework 调用的启动入口；它创建的 `GameSession` 是普通 C# 对象，负责执行启动模块列表，每次从启动到退出对应一个对象。

添加项目逻辑时，继承 `StartupModule`，重写需要的初始化、更新和清理方法，将组件挂到 Bootstrap 场景的 GameObject 上，再拖入 GameEntry 的 **Startup Modules** 列表。被依赖的模块排在前面；异步初始化应响应取消信号，清理方法也应处理初始化只完成一部分的情况。默认列表为空，公共 Manager 准备完成后进入就绪状态。

`StartupModule` 是项目自定义的抽象组件基类，本类不能直接挂载，场景中添加的是具体子类组件。Inspector 数组用于保存这些组件的引用和执行顺序，各模块的序列化参数在对应组件中配置；如果项目使用普通 C# 初始化对象，也可以在代码中通过 `IStartupModule` 组织流程。

项目默认引用的聚合包地址：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.7
```

UPM 不支持 package.json 中的 Git 传递依赖，因此工程 manifest 同时声明 all 和它的依赖来源，让首次导入即可解析运行时程序集。所有来源由同一份聚合目录生成，维护时运行 sync-packages.py；源码由 UPM 获取，不随工程复制。

公共 Manager 的准备顺序写在 `GameEntry.InitializeManagersAsync`：取得事件和定时器服务 → 准备资源管理器 → UI → ECS EntityManager/WorldManager → 有 NetworkSettings 时初始化网络。Framework、UI、ECS 的自动驱动和运行退出清理仍由各库负责，GameEntry 不重复调用其 Update。

空模板不附带 YooAsset 资源包；项目配置好 DefaultPackage 的采集和构建后，在 GameEntry 上开启 **Initialize Yoo Asset**，入口会等待 ResourceManager.InitializeAsync 成功后继续。网络需要在 Resources 中创建 NetworkSettings 并填写项目配置；入口只做 Initialize，实际联网由项目流程调用 ConnectAsync。

HybridCLR 安装项会处理官方依赖、嵌入及默认 Configure；实际 DLL/AOT 收集、原生安装和 Player 构建使用封装菜单。Odin 插件由项目自行提供，缺少 Odin 不影响 Ability 运行时。

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

验证器从项目清单里的同一个 Git 版本获取 Framework 源码到临时目录，编译真实 Core/MonoSingleton 并运行 26 组生命周期、Manager 准备及安装 API 模型。它不会从父目录寻找包仓库。Unity Editor、UPM 服务和 IL2CPP 使用原生流程单独验收；模型验证不代表已完成原生验收。

GpuAnimator 包名为 `com.novorizon.gpuanimator`，可选模块安装器会同时移除旧的 `com.guanjinbiao.gpuanimator` 依赖。

更新依赖目录使用 `Tools~/sync-packages.py --ref <已发布标签>`。该工具从公开 Git 地址读取版本目录，同时更新聚合包、完整依赖清单和模块来源。正常打开工程不需要运行 Python 工具。
