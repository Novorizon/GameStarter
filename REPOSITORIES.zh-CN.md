# 仓库布局

基础组件库保留 **Framework** 名称。所有模块面向 Unity，不再按 Core/Unity 拆分产品；程序集可按现有职责组织。

| 仓库 | 内容 | 处理方式 |
| --- | --- | --- |
| Framework | 单例、线程调度、事件、定时器、通用池、网络等基础能力；YooAsset 适配仍为可选包 | 保留现有仓库；不加入只服务某个 ECS 或玩法的抽象 |
| HybridCLR 封装 | 项目配置、原生安装、完整 Player 构建、补丁导出和加载 | 独立仓库，继续依赖 Framework 与官方 HybridCLR；当前代码在 HybridCLR-Demo |
| Ability | 技能运行时及其编辑器 | 从 Cube 提取；运行时和 Odin 编辑器为两个包，放在同一个 Ability 仓库 |
| RTree | 六种空间索引、共用几何与查询结构 | 保留独立仓库和一个包；每种树各自一个目录，没有 Grid |
| GpuAnimator | 动画烘焙、资源注入、播放和回收 | 保留独立仓库；Framework 池集成为可选 Sample |
| UI | 界面生命周期、导航、层级、提示、资源加载接口 | 建议独立仓库与独立包，单向依赖 Framework；UI 编辑器工具暂不另开仓库 |
| GameStarter | Unity 工程、GameEntry、模块安装窗口、项目级接线 | 新的独立工程仓库；依赖 Framework，可安装其他包；不是基础库的大包 |
| ECS-Framework | 实体、系统、专用池、生命周期 | 保留既有独立仓库，单向使用 Framework 的通用能力，不关联 Ability，也不引入 IUnit |

用户列出的五个库，加 UI 和启动工程，共 **7 个仓库**；将已有 ECS 算入则为 **8 个**。不建议合并这些仓库。Cube 作为游戏项目继续保留，不计入框架库数量；演示工程、服务器项目也不计入上表的库数量。

## 当前已落地与后续迁移

UI 当前已是独立 UPM 包，暂时仍托管在 Framework 仓库的 `Framework/UI`。Ability 两个包当前仍托管在 Cube。将它们移至独立远端仓库时保留包 ID、公开命名空间和程序集名称，只替换安装目录中的 Git 源，并验证新仓库的同版归档一致性。

GameStarter 已提供本地工程源码与可复现导出；本轮没有创建新的 GitHub 仓库，也没有把 UI/Ability 的旧路径删除。安装目录使用现有仓库的已提交版本，避免引用尚不存在的远端地址。

## 依赖边界

- Framework 不依赖 UI、ECS、Ability、RTree、GpuAnimator 或 HybridCLR。
- UI、HybridCLR 封装、ECS 单向依赖 Framework。Ability/RTree 的算法与行为按各自实际需要使用基础库，不为了统一目录强加依赖。
- Ability 与 ECS 彼此独立；项目需要共同使用时，在项目代码中接线。
- Framework 不实现 ECS 专用实体、查询、帧序或池策略；真正通用的单例、线程、计时、引用池等继续由基础库提供。
- GameStarter 只负责项目入口和装配；安装可选包不会自动创建战斗、网络连接或业务系统。

第三方依赖的许可与通知保留在各包中。RTree 继续遵循原 LGPL-2.1-or-later；本工程没有变更其许可。Odin 商业依赖不随项目分发。
