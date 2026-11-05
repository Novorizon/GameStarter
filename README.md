# GameStarter

独立的 Unity 基础启动工程，建议 Unity 2022.3.62f1。默认通过 Git/UPM 安装 **Framework**，其他库由编辑器窗口按需安装。下载或克隆本工程即可打开，不需要在旁边放置 UnityPackages 仓库。

## 使用

1. 使用 Unity Hub 打开本工程，等待 Unity Package Manager 下载 Framework 及 Unity 基础模块，并完成脚本编译。Git URL 安装需要本机可用 Git 和网络连接。
2. 打开 `Assets/Scenes/Bootstrap.unity`，进入 Play Mode，空入口输出 `GAME_STARTER_READY`。
3. 使用 **Tools/Novorizon/Optional Modules** 安装 UI、Ability（含编辑器）、ECS、HybridCLR、RTree、GpuAnimator 或 YooAsset 适配。可选模块清单也使用已发布的 Git 地址，不使用包仓库的相对路径。

本项目只依赖包的公开程序集。Framework 提供单例、线程、事件、计时和池等基础能力；GameEntry 使用其 MonoSingleton，通过项目自己的 StartupModule 初始化、更新及清理具体系统。

`Assets/Game/Runtime` 中的启动流程是：`GameEntry` 创建 `GameSession` → 按列表顺序等待模块 `InitializeAsync` → 全部成功后逐帧调用 `Tick` → 退出或失败时逆序调用 `Shutdown`。`IStartupModule` 定义这三个方法，`StartupModule` 是可挂载到场景的空实现基类。`StartupState` 定义在 `GameSession.cs` 中，供入口查询启动状态。

添加项目逻辑时，继承 `StartupModule`，重写需要的初始化、更新和清理方法，将组件挂到 Bootstrap 场景的 GameObject 上，再拖入 GameEntry 的 **Startup Modules** 列表。被依赖的模块排在前面；异步初始化应响应取消信号，清理方法也应处理初始化只完成一部分的情况。默认列表为空，直接进入就绪状态。

要一次安装全部库，也可在 Package Manager 添加：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.6
```

HybridCLR 安装项会处理官方依赖、嵌入及默认 Configure；实际 DLL/AOT 收集、原生安装和 Player 构建使用封装菜单。Odin 插件由项目自行提供，缺少 Odin 不影响 Ability 运行时。

## 开发验证

Python 3.12+、.NET SDK 8+、Git：

```sh
python3 Tools~/verify.py --dotnet /path/to/dotnet
python3 Tools~/export.py --output /tmp/GameStarter.zip
```

验证器从项目清单里的同一个 Git 版本获取 Framework 源码到临时目录，编译真实 Core/MonoSingleton 并运行 21 组生命周期及安装 API 模型。它不会从父目录寻找包仓库。Unity Editor、UPM 服务和 IL2CPP 使用原生流程单独验收；模型验证不代表已完成原生验收。

GpuAnimator 包名为 `com.novorizon.gpuanimator`，可选模块安装器会同时移除旧的 `com.guanjinbiao.gpuanimator` 依赖。

更新依赖目录使用 `Tools~/sync-packages.py --ref <已发布标签>`。该工具从公开 Git 地址读取版本目录，同时更新 Framework 清单和可选模块来源。正常打开工程不需要运行 Python 工具。
