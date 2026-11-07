# GameStarter 与 UnityPackages 的协作

[UnityPackages 对话](codex://threads/01a1b12e-ff35-762a-b973-67981ef710ad?hostId=durable) 维护通用包、包版本、依赖闭包、聚合目录及发行；本对话维护 GameStarter、具体业务和 Unity 接入验收。GameStarter 有需求时先交接，不同时在 SDK 中实现另一份修改。

## 交接方法

有实际问题或接口需求时，以 SDK 仓库的 Issue 或 PR 作为持久需求记录。需求尚不明确时先提供下面的信息，避免仅说“改一下初始化”。双方确认接口语义后，SDK 维护方交付 PR 与固定 SHA；GameStarter 再升级、验证并反馈结果。

应用具备跨对话发送工具时，可以直接传递这些链接；目前本对话能读取指定对话，但没有向现有对话发消息的接口。读取不代表对方已收到、认可或开始处理。若采用需求队列，应先让两个对话约定在开工和收尾时读取未关闭项；仅建立 Issue 不会自动唤醒另一个对话。

```text
现象与重现：具体触发条件、输入、日志；哪些是已验证事实。
当前输入：GameStarter 提交、SDK SHA、Unity 版本、平台和资源模式。
期望行为：先后顺序、错误和取消如何处理；哪些工作不得提前执行。
所需接口：签名、参数、返回值、状态语义及资源归属。
兼容要求：旧接口如何处理，是否影响 AOT/主包基线、数据或发行格式。
验收条件：模型回归与真实 Unity 分开说明，需要哪些通过证据。
交付：SDK PR、固定 SHA、变更说明、测试结果和未验证项。
```

等待 SDK 时，本项目继续处理不依赖新接口的业务、界面、数据和接入准备。接口尚未交付的部分明确标注等待，不靠测试桩或临时分叉宣称已完成。

## 已完成的接入基线

以下能力已在 [UnityPackages PR #15](https://github.com/Novorizon/UnityPackages/pull/15) 和 [GameStarter PR #18](https://github.com/Novorizon/GameStarter/pull/18) 合并。它们是后续优化应保留的接入约定，不是待开发需求：

| 阶段 | 接口与约束 |
| --- | --- |
| 资源系统初始化 | `ResourceManager.InitializePackageAsync` 仅建立文件系统，不查询版本、读取资源清单或下载内容 |
| 热更管理器初始化 | `HotUpdateManager.Initialize` 仅保存和校验配置，不下载、不创建 HotGame |
| 更新执行 | `GameHotUpdate.RunAsync` 调用 `HotUpdateManager.StartAsync`，执行发行选择、资源准备、代码加载与业务启动 |
| 内容准备 | `ResourceManager.PrepareContentAsync` 准备精确资源版本；旧 `InitializeAsync` 保持兼容 |
| 离线选择 | 使用配套内置发行，不损坏、拒绝或删除合法联网缓存；切回联网后缓存仍可用 |
| 退出 | GameEntry 取消本次启动，并按 HotUpdateManager、ResourceManager 的顺序释放；旧回调不得影响新一轮启动 |

当前接入固定 SDK 提交 `a3e531d0ed3ac11748324bc655f1b29cc3113821`，对应合并提交 `6779ede4628f6a1bd6cccabd3eabce237c8e80d0`，两者源码树一致。后续以本项目 manifest 中的固定输入为准；预备版本名不等于已发布标签。

GameStarter 的业务 UI 使用 TMP，图片、字体和数据通过 YooAsset 管理。未来接入多语言等包时，需要核对 TMP 绑定与字体资源的适配；不能仅因为 SDK 提供 Legacy Text 示例就把业务 UI 改回 Legacy Text。

## 单例结构的待讨论项

用户询问为何 HotUpdateManager 仍继承 ConfigurableSingleton。当前创建代码固定使用无参 new，没有自定义创建工厂；但 Dispose 还调用基类的 ReleaseInstance(this)，在清理完成后清空属于本实例的引用。因此“有配置参数”不是必须使用该基类的理由，也不能仅改继承声明就认定行为相同。

建议 SDK 维护方评估能否统一为普通 Singleton。范围先限 HotUpdateManager，不顺带移除其他服务实际使用的工厂能力。若调整，应保留配置初始化与执行分离、取消、重复释放、直接 Dispose 后的访问约定、旧实例不影响新实例，以及关闭 Domain Reload 后的编辑器重置行为；说明是否需要重建 AOT 主包。

此项是待沟通的结构评估，不是已经发送、对方已经接收或已经批准实现的 SDK 修改。

## 避免环境与版本冲突

同一路径名称不证明两个对话位于同一运行实例；本地笔记也不保证另一个对话可见。交付以 Git 提交、Issue 和 PR 链接为准。

同一环境中并行开发时，各自使用独立 checkout/worktree 和分支。Unity 的 Library、资源导入、构建与许可证进程按实际运行实例处理，不共用同一个正在打开的 Unity 项目目录。一个仓库的分支切换不能作为另一个对话的通知方式。

包版本、聚合目录和新发行标签由 SDK 维护方统一安排，已发布标签不移动。GameStarter 不自行发布补丁包标签；升级时记录 SDK SHA、必要的主包重建要求以及实际验收范围。
