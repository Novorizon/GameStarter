# Demo 入口

GameStarter 的主示例是 Assets/Scenes/Bootstrap.unity：Play 后由 GameEntry 准备资源，HotGame 初始化系统、恢复设置并加载 Luban 表。业务从 HotGame.StartGameAsync 接入。

| 内容 | 代码/操作 | 预期 |
| --- | --- | --- |
| 输入 | Assets/Resources/InputActions.inputactions | Gameplay Map 的 Move/Confirm；重绑定由 GameSettings 保存 |
| Luban | Config/Luban、Assets/HotUpdate/Generated/Data、Assets/GameAssets/Data/Luban | YooAsset 读取真实 bytes，日志出现 LUBAN_EXAMPLE |
| 设置 | Assets/HotUpdate/GameSettings.cs | 音量与绑定跨启动恢复，失焦/后台/退出保存 |
| 资源与热更 | GameReleaseConfiguration、GameEntry、HotGame | 代码和资源版本配套，资源失败后回到内置发行 |
| 扩展模块 | StartupModule，可选，默认空列表 | 只在项目确有需要时添加；普通 Manager 在 HotGame 初始化 |

包示例的完整目录见 [UnityPackages Demo 索引](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.25/Documentation/Samples.zh-CN.md)。云端通过 ImportSamples 导入后统一编译，操作见 [Unity 验收](UnityValidation.zh-CN.md)。

Framework Demo 和 UI Demo 可从 Tools → Novorizon → Demos 创建独立场景；ECS QuickStart 和 RTree 在空场景挂示例组件。资源 Demo 需要 DefaultPackage 配置，GPU 示例需要配套烘焙资源。不要把演示控制器直接挂到正式 Bootstrap 场景中。
