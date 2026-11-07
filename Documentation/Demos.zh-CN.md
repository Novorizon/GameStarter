# Demo 入口

GameStarter 的主示例是 Assets/Scenes/Bootstrap.unity：GameEntry 配置开关并持有启动状态/取消，直接等待 ResourceManager 的包文件系统初始化成功，再配置 HotUpdateManager，最后调用 GameHotUpdate.RunAsync 进入发行选择、下载和代码加载。随后 HotGame 初始化其余系统、恢复设置、加载 Luban 表并进入业务。Editor 使用已编译程序集。

| 内容 | 代码/操作 | 预期 |
| --- | --- | --- |
| 输入 | Assets/Resources/InputActions.inputactions | Gameplay Map 的 Move/Confirm；重绑定由 GameSettings 保存 |
| Luban | Config/Luban、Assets/HotUpdate/Generated/Data、Assets/GameAssets/Data/Luban | YooAsset 读取真实 bytes，日志出现 LUBAN_EXAMPLE |
| 设置 | Assets/HotUpdate/GameSettings.cs | 音量与绑定跨启动恢复，失焦/后台/退出保存 |
| 资源与热更 | GameEntry 顺序初始化两个 Manager；GameHotUpdate 放具体参数/准备/代码读取接线 | 包 warmup 不下载；显式 Start 后才选择发行、下载和加载。代码与资源版本配套，准备失败可回到匹配的内置发行 |
| 启动进度 | StartupScreen、StartupMessage、DownloadProgress | 无下载显示启动步骤；有下载显示字节进度，准备完成且业务就绪后退出启动展示 |
| 业务接入 | HotGame.StartGameAsync 直接调用业务模块，退出时显式关闭 | 不需要 Session 或模块注册器；其余 Manager 在 HotGame 初始化/释放，资源与热更两个 Manager 由 GameEntry 收尾 |
| 塔防原型 | Assets/Scenes/Bootstrap.unity，或 Tools → Novorizon → Tower Defense → Create Prototype Scene | 走相同入口和真实表；玩法、已测/未测边界见[塔防交付记录](TowerDefense/Delivery.zh-CN.md) |
| 海岛营地 | Bootstrap → 首次漂流序章 → 海岛营地大厅 | 手记领奖、四阶段建设、出征与返回；使用 SDK StoryPlayer，地图/剧情/TMP 资源走 YooAsset |

GameStartupOptions 仅传输入资源与初始 Map。SDK 与热更程序集依赖需要匹配的 IL2CPP 主包基线；当前实际 Player 脚本编译与两轮 Editor 模拟启动/退出已通过，报告见[本轮验收](Validation/EntryStartup/)，不作为 HTTP 或真实离线包下载验收。

包示例的完整目录见 [UnityPackages Demo 索引](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.35/Documentation/Samples.zh-CN.md)。云端通过 ImportSamples 导入后统一编译，操作见 [Unity 验收](UnityValidation.zh-CN.md)。

Framework Demo 和 UI Demo 可从 Tools → Novorizon → Demos 创建独立场景；ECS QuickStart 和 RTree 在空场景挂示例组件。资源 Demo 需要 DefaultPackage 配置，GPU 示例需要配套烘焙资源。不要把演示控制器直接挂到正式 Bootstrap 场景中。
