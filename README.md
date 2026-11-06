# GameStarter

独立 Unity 启动工程，建议 Unity 2022.3.62f1。当前工程版本为 `v0.1.0-preview.23`，依赖 UnityPackages `v0.1.0-preview.22`。通过 Git/UPM 引用 `com.novorizon.framework.all` 和完整依赖目录，不需要在旁边放 UnityPackages 源码仓库。

## 打开和运行

1. 安装 Git，并确保 Unity Hub/Editor 能找到 Git。
2. 用 Unity Hub 打开工程，等待 UPM 下载包和脚本编译。
3. 打开 `Assets/Scenes/Bootstrap.unity`，进入 Play。正常启动会输出 `LUBAN_EXAMPLE 1 示例配置`、`HOT_UPDATE_READY` 和 `GAME_STARTER_READY`。
4. 聚合安装器会处理官方依赖、HybridCLR 嵌入及默认 Configure；使用 Tools/Novorizon/Optional Modules 维护可选模块来源。

默认启用 GameEntry Inspector 中的 Initialize Hot Update。关闭后走 AOT 启动分支，HotGame 不会创建。不使用自动 Bootstrap 宿主或启动 JSON。

## 启动函数与业务位置

| 函数 | GameEntry | HotGame |
| --- | --- | --- |
| InitializeAsync | 准备资源并选择是否加载热更；关闭热更时执行后续启动 | 准备公共系统，再启动业务，完成后 ReportReady |
| InitializeManagersAsync | 只供关闭热更的分支使用 | 默认热更分支在这里显式初始化普通 Manager |
| StartGameAsync | 关闭热更时的业务启动位置 | 开启热更时的业务启动位置 |
| Shutdown / ReleaseManagers | 清理 AOT 分支或关闭热更入口，最后关闭资源层 | 清理热更业务和本入口初始化的 Manager |

GameEntry.OnStart/OnExit 只是 Unity 单例生命周期接线。开启热更后的实际顺序是：

```text
GameEntry.InitializeAsync
  -> InitializeResourcesAsync（等待 YooAsset DefaultPackage）
  -> HotUpdateManager.InitializeAsync(settings, options, token)
  -> HotGame.InitializeAsync
     -> InitializeManagersAsync
     -> StartGameAsync：LoadTablesAsync → 可选模块 → 业务
     -> ReportReady
  -> GAME_STARTER_READY
```

事件、定时器、本地存储、输入、场景、音频、Luban 数据表、UI、GPU 动画、ECS 和网络的初始化调用，已经移到 HotGame.InitializeManagersAsync。Manager 的实现仍来自各个 package；只有装配调用和项目业务处于热更程序集。GameEntry 的同名方法作为关闭热更时的启动分支，不会和 HotGame 重复执行。

两种启动分支都已在 StartGameAsync 加载 cfg.Tables，并示范查询 TbExample。具体业务类放在 Assets/HotUpdate 的其他目录，由这里组织调用。模板没有业务摄像机或首场景，后续在 StartGameAsync 添加自己的玩法与场景入口。

HotGame 是普通 C# 对象，不挂到 GameObject 上。IGameModule 是 AOT/热更共用的入口约定，只规定 InitializeAsync 和 Shutdown；HotUpdateManager 通过它创建、启动及关闭热更入口，AOT 无需引用 HotGame 的具体类型。HotGame 不得反向等待 GameEntry.WaitUntilReadyAsync，因为 GameEntry 正在等待 HotGame 完成。

## 启动参数与资源

Game.Startup 是项目的共享 AOT 契约程序集，包含 GameStartupOptions；它保存 Inspector 中的输入配置、资源适配器及可选模块启动回调，不拥有 Manager 或额外运行期。GameEntry 将它作为启动上下文传给封装，HotGame 通过可选 IGameContextHost 读取。旧 IGameHost/IGameModule 方法保持不变。Game.Startup/link.xml 保留这些热更端会访问的 AOT 成员。

模板固定初始化 YooAsset DefaultPackage，GameEntry 等待资源系统就绪，再开始热更加载；场景、UI、数据表读取已经接入对应适配器。数据表通过独立 YooAsset 租约加载，不走 Resources。输入使用 Inspector 中的 Input Actions，留空时读取 Resources/InputActions，默认 Map 为 Gameplay。工程已设置 Active Input Handling Both，示例包含键盘和手柄动作。

网络只在 Resources/NetworkSettings 存在时 Initialize，连接仍由游戏业务显式调用 ConnectAsync。音频由业务场景的摄像机提供 AudioListener。框架自动驱动负责各 Manager 的 Update，项目入口不重复推进。

## 可选启动模块与 Session

原有 StartupModule 列表保留，默认为空。GameSession 只负责该列表的顺序初始化、Tick、取消和逆序清理；一次入口运行只有一个，不是登录 Session，也不随切场景或进入战斗创建。只有下一次 Play 或关闭入口后重新创建入口，才会有下一次运行的对象。

热更模式中，HotGame 在普通 Manager 就绪后，通过 GameStartupOptions 中的项目回调初始化可选 AOT 模块，再进入业务。退出时先关闭这些模块，再关闭 HotGame 的业务和 Manager，最后关闭资源层。热更业务可以直接写在 HotGame，不必新增 StartupModule。

Framework 保证同次运行中 OnStart/OnExit 各执行一次。主动关闭可调用 GameEntry.Release()、已取得实例的 Dispose() 或 ShutdownAsync()。Manager 清理使用 Release，不为清理创建新实例；异步释放由对应库内部完成。入口不维护 ManagerLifetime、借用标记或跨服务初始化失败回滚。

## 包和程序集

当前包标签为 `v0.1.0-preview.22`：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.22
```

UPM 不支持 package.json 的 Git 传递依赖，因此工程 manifest 同时声明聚合包及全部依赖来源。维护目录时运行 `python3 Tools~/sync-packages.py --ref <已发布标签>`；正常打开工程不需要运行 Python。固定标签不会随 GitHub 新提交自动升级。

Game.HotUpdate.asmdef 已显式引用启动所需的公共库及 Game.Startup。新增业务使用其他库时，继续添加实际需要的程序集引用。AOT 的 GameStarter.Runtime 不引用 Game.HotUpdate。Framework 内置的 Source Generator 负责关闭 Domain Reload 时的单例静态恢复；关闭 Scene Reload 时不强制覆盖实例配置。

Luban 统一使用 com.novorizon.data 中的 DataManager，旧 ConfigManager、通用 JSON 表 API 和 Core 动态对象容器已删除。GameEntry 和 HotGame 已统一初始化 DataManager 的单一字节加载器；无需分别提供文本和二进制加载函数。

## 已接入的 Luban 表流程

工程已包含可运行的示例，无需再手写加载函数、工厂或程序集引用：

| 位置 | 内容 |
| --- | --- |
| Config/Luban/Data/example.xlsx | 可编辑的示例 Excel |
| Config/Luban/Defines/example.xml | 表定义 |
| Assets/Generated/Data | 官方 Luban CLI 生成的 cfg.Tables、记录类型与 Game.Data.Generated 程序集 |
| Assets/GameAssets/Data/Luban | 二进制表与 manifest.json，位于 Resources 之外 |
| Assets/Game/Startup/GameDataPaths.cs | 生成目录、采集目录、地址和包名的统一配置 |
| Assets/Game/Editor/GameDataSetup.cs | 自动配置、生成、寻址规则、资源打包及主包构建接线 |

GameEntry 和 HotGame 都已写好以下流程，函数名对应：

```text
InitializeManagersAsync
  -> DataManager.Initialize(LoadDataBytesAsync)
StartGameAsync
  -> LoadTablesAsync
     -> DataManager.LoadAsync<cfg.Tables>(CreateTables, manifestLocation, token)
        -> YooAsset 读取清单和所有 .bytes
        -> CreateTables(readTable) 返回 new cfg.Tables(readTable)
        -> DataManager 保存完整 Tables
  -> 初始化可选模块
  -> 查询示例表，进入项目业务
```

CreateTables 中返回的对象就是交给 DataManager 保存的表。以后业务直接查询：

```csharp
var example = DataManager.Instance.Get<cfg.Tables>().TbExample.Get(1);
```

新增表时添加自己的 Luban 定义和 Excel，然后执行 **Tools → Novorizon → GameStarter → 1. Generate Tables**。现有工厂会创建重新生成的 Tables，不需要逐张注册或增加加载代码。生成器、加载器、业务查询与所有相关 asmdef 均已接线；替换掉示例表时，相应修改 StartGameAsync 中的示例查询。

打开工程后自动检查 DefaultPackage 的 GameStarterData 采集组，保留其他组；只采集 .bytes 与 JSON，使用 Data/Luban/manifest、Data/Luban/tbexample 等地址。Editor 使用 YooAsset 模拟模式读取工程资源，首次 Play 不需要构建 Bundle，也不需要安装 .NET CLI。修改表后生成需要 Git 和 .NET 8 SDK。源文件改动后尚未生成时会阻止 Play/构建，避免误用旧表。

源 Excel 不进入运行时包，官方 Luban/YooAsset 源码也不复制到本工程。玩家属性等动态数据由业务自身维护。生成类型目前属于共享 AOT 程序集 Game.Data.Generated，供两种启动分支使用；数据值可以随资源包更新，生成类型或结构发生变化时需要重新构建 Player。

## HybridCLR 构建

Editor 使用 Unity 已编译的热更程序集。IL2CPP Player 由封装准备版本、补充 AOT 元数据并加载 DLL。

1. 进入 Play 验证启动日志。
2. 选择目标平台，使用 **Tools → Novorizon → GameStarter → 3. Build New Player**。该菜单先将 YooAsset 资源构建到 StreamingAssets/yoo，再调用 HybridCLR 主包构建，安装原生运行时并收集 DLL/AOT 元数据。
3. 修改热更代码后使用 Hot Update → 3. Export Patch 导出补丁。修改 AOT 代码、共享契约或程序集依赖时必须重建 Player。

只需单独打资源时使用 GameStarter → 2. Build Resources。普通 Unity/HybridCLR 构建入口也会检查资源是否为当前目标和当前数据版本，未准备好时明确提示；不要在 Player 的预处理回调中嵌套构建资源。默认 Player 使用 YooAsset 离线资源模式，远程资源地址和发布由项目另行配置。

远程地址、渠道、公钥等通过 C# UpdateSettings 传入。构建菜单通过 GameBuild.SettingsFactory 使用同一配置，不需要编辑启动 JSON。详见 [HybridCLR 封装指南](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.22/Packages/com.novorizon.framework.hybridclr/README.md)。

## 开发验证

需要 Python 3.12+、.NET SDK 8+、Git：

```sh
python3 Tools~/verify.py --dotnet /path/to/dotnet
python3 Tools~/export.py --output /tmp/GameStarter.zip
```

验证器按 manifest 固定版本下载 Framework，分别编译 Core、Unity 单例、共享启动参数、AOT 和热更程序集。热更编译关闭传递项目引用，并核对与生产 asmdef 的直接依赖一致；测试 Manager API 模型按实际程序集拆分；DataManager、Luban 运行时、生成的表以及导出器使用实际源码，验证读取真实二进制数据。YooAsset 寻址/过滤接口和枚举来自工程固定的官方版本，原生 Bundle 构建使用边界模型。

受控模型覆盖初始化顺序、热更接管普通 Manager、参数传递、重复启动、退出取消、清理顺序和包安装。模型不代替真实 Unity 导入、Play、设备、渲染或 IL2CPP 验收。

Visual Studio 跳转：等待 Unity 完成包解析和编译，在 Edit → Preferences → External Tools 选实际 Visual Studio，Regenerate project files 后从 Unity 打开项目。`.sln`、`.csproj` 由 Unity 生成，不随工程提交。
