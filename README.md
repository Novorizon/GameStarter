# GameStarter

面向 Unity 的基础启动工程，默认仅依赖 **Novorizon Framework**。基础库继续使用 `Framework` 名称、原包 ID 与命名空间。

建议 Unity **2022.3.62f1**。工程不包含玩法、账号、存档、地图、战斗或服务器连接流程。

## 使用

1. 使用 Unity Hub 打开工程，等待 Package Manager 和脚本编译完成。
2. 打开 `Assets/Scenes/Bootstrap.unity` 并进入 Play Mode。空项目应输出 `GAME_STARTER_READY`；默认场景只有入口，没有演示画面。
3. 通过 **Tools → Novorizon → Optional Modules** 选择模块并安装。依赖会先安装，安装进度可跨脚本重编译恢复。版本固定到本轮整理后的提交，不跟随分支最新内容。
4. 需要示例时，从 Package Manager 导入相应包的 Samples。安装一个包不会自动启动其游戏系统。

下载版附带 `Packages/OptionalArchives~`，默认从经过 SHA-256 校验的本地归档安装；源码工程缺少归档时使用目录中固定提交的 Git URL。工程清单保留动画、物理、UI、图像转换、AssetBundle、网络请求等所需 Unity 内置模块。官方 uGUI、Mathematics 等传递依赖仍由 Unity 的注册表下载，因此附带归档不等于完全离线工程。

可选项包括 UI、HybridCLR 封装、Ability 运行时、Ability Odin 编辑器、RTree、GpuAnimator、ECS。Ability 编辑器需要另行导入有授权的 Odin；GpuAnimator 保持现有 Built-in 渲染管线支持范围。

## 入口与生命周期

`GameEntry` 继承 Framework 的 `MonoSingleton<GameEntry>`，启动模块按 Inspector 中的顺序初始化，全部成功后才开始更新；退出或失败时按相反顺序清理已经开始初始化的模块，包含初始化失败的那个模块。单个模块清理异常不会阻断其他模块的清理。正常更新不复制模块列表。

项目自己的系统通过继承 `StartupModule` 接入，将组件添加到 GameEntry 根对象，并填入 `startupModules`。基础实现为空；只覆写项目需要的回调：

```csharp
using System.Threading;
using System.Threading.Tasks;
using Game;

public sealed class ProjectServices : StartupModule
{
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // 初始化项目自己的服务。异步操作需要观察 cancellationToken。
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
    }

    public override void Tick(float deltaTime, float unscaledDeltaTime) { }
    public override void Shutdown() { }
}
```

模块应留在主线程上使用 Unity API，响应取消，并允许部分初始化后清理。关闭时会发出取消并立即执行清理，不会无限等待不响应取消的异步操作。模块需要在每次异步返回后检查取消，避免退出后继续创建资源。需要在关闭阶段保存或等待异步工作时，应由项目定义显式的退出流程，在销毁 GameEntry 之前完成。

沿用 Cube `65794c791ab1f9ab59d2833bf79f867480f42411` 中 GameEntry 的初始化、逐帧更新、关闭清理结构。Cube 中的具体游戏管理器、空的 BootFlowManager/GameModeManager，以及游戏模式枚举没有复制进来。公共单例、定时器、对象池、事件等能力继续由 Framework 提供。

## HybridCLR

安装项自动处理 Framework、官方 HybridCLR 和封装包；官方 9.0.3 包会嵌入 `Packages/com.code-philosophy.hybridclr`，保留其 `Data~` 和原始 `.meta` 文件，以满足该版本原生安装器的路径要求。随后调用封装的 `GameBuild.Configure`，生成默认配置、热更 asmdef 和入口。实际原生运行时安装由 **Build New Player** 统一处理。

配置生成后，热更业务入口是 `Game.HotUpdate.GameModule`，遵循封装包的 `Game.Framework.HotUpdate.IGameModule`。GameStarter 的 `StartupModule` 用于 AOT 启动服务；默认列表为空，两者不会重复初始化业务。AOT 的 GameStarter 不引用热更程序集；共享契约应放在 AOT 程序集中，再由热更代码单向引用。

使用 **Tools → Novorizon → Hot Update** 中的 Build New Player / Export Update。首次构建需要 Unity 对应平台的 IL2CPP 模块及原生工具链。不要直接收集编译输出目录里的旧 DLL 制作新补丁；失败的编译不产生可发布的新版本，客户端仍可运行已经验证和发布的旧版本。

## 安装失败

UPM 报错、归档校验失败或版本不符时停止队列，保留已安装的包，并在窗口与 Console 显示原因。处理网络或编译问题后重新选择安装即可，已满足版本的依赖会跳过。队列通过 SessionState 跨 domain reload 保存；关闭整个 Editor 后不自动续装，可以重新安装。初始 Framework 未安装成功时，先在 Package Manager/Console 修复基础依赖解析，安装窗口需要项目编译成功后才可使用。

HybridCLR 编辑器程序集加载后才调用配置；配置入口应保持幂等。安装器不会覆盖已有的 Resources 配置、热更代码或场景。

## 验证与导出

Python 3.12+、.NET SDK 8+ 可运行源码与 API 模型验证：

```sh
python3 Tools~/verify.py --framework /path/to/com.novorizon.framework
# 附带包归档的下载版可自动从 Framework 归档读取真实源码：
python3 Tools~/verify.py
```

验证使用真实 Framework Core 和 MonoSingleton 源码，Unity/UPM 使用测试模型；**不代表已在 Unity Editor、IL2CPP 或设备上验证**。细节及待验证项见 [VALIDATION.md](VALIDATION.md)。

```sh
python3 Tools~/export.py --output /tmp/GameStarter-source.zip
python3 Tools~/export.py --output /tmp/GameStarter-with-packages.zip --archives /path/to/archives
```

仓库拆分建议及现状见 [REPOSITORIES.zh-CN.md](REPOSITORIES.zh-CN.md)。
