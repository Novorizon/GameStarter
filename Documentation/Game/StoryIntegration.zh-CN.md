# 剧情包接入规划

已按用户授权升级至 [UnityPackages PR #24](https://github.com/Novorizon/UnityPackages/pull/24)、`v0.1.0-preview.35` 固定提交 `5bf37118c412ce6f188340fe75661e54c0d91515`，接口见 [Story README](https://github.com/Novorizon/UnityPackages/blob/5bf37118c412ce6f188340fe75661e54c0d91515/Packages/com.novorizon.story/README.md)。海岛首次抵达使用 SDK StoryPlayer、项目 CampArrivalView 与 YooAsset 中的三段故事 JSON；当前 TMP 呈现支持继续/跳过，取消和失败不会完成抵达标记。后续接入规划如下。

## 可用于当前游戏的内容

| 场景 | 触发位置 | 规则 |
| --- | --- | --- |
| 首次进入营地 | 玩家档案准备后，由业务决定是否播放 | 已实现漂流上岛、搭帐篷和建设目标的三段对白；完成或主动跳过记录已抵达，补给另行领取 |
| 首次进入关卡 | 战场资源准备完成、尚未允许开始第一波 | 介绍路线/基地/塔；对白结束再开放操作，不让战斗与对白争抢输入 |
| 建造/选卡教学 | 稳定目标按钮出现时，由业务引导模块触发 | Story 可播放说明，按钮高亮/点击等待由业务负责；不能用固定秒数猜测玩家完成动作 |
| 精英或章节介绍 | 备战阶段或清波安全边界 | 单人阻塞对白暂停领域；非阻塞短提示不覆盖选卡窗口 |
| 战后对话 | 战斗终态后、结算展示流程中 | 叙事完成不等于服务器已经发奖；结果与正式奖励分别处理 |

Say、Choice、背景/立绘、语音、视频、等待、Command、稳定节点/内容版本、取消/跳过/失败结果已由包提供。首批只需要 TMP 对白、头像/立绘和简单分支；不必为此增加 Timeline、复杂条件语言或新的剧情单例。

## 模块与 API 边界

由热更业务中的独立剧情协调对象提供 `PlayAsync(storyId, context, token)` 一类 API；这里只约定职责，未新增运行时接口。ClientFlow 在营地/战斗切换的业务边界调用它，GameEntry 不处理剧情，HotGame 只负责业务模块的装配与释放。

- 内容适配：Luban 的触发配置引用 StorySequenceAsset 地址，Sequence 内节点列表保存完整段落；不为每句台词、每个选项独立建全局表。
- 资源适配：项目实现 IStoryAssets，把 YooAsset 本次 AcquireAsync 租约交给 StoryAssetLease 释放回调。序列/Settings/舞台 Prefab 与立绘走 ui/story 标签，字体走 fonts，配音走 audio；有资源后再配置采集，不预先建空目录。所有正式剧情文字使用 TMP 和明确的字体。
- 窗口适配：StoryStage 放入项目拥有的剧情 Popup；关闭等待 StoryPlayer/Launcher 的停止与清理，不清掉共享 UI 或资源。
- 业务命令：IStoryCommands 只派发明确注册的动作，例如展示教学目标、通知章节标记；未知命令失败。正式奖励请求仍走任务/结算服务，不能允许文本参数直接修改账号余额。
- 文字：使用本地化 key；中文字体、长文本、语言切换和真机阅读区域由项目验收。

## 暂停、跳过与存档

暂停应由业务协调多个原因：手动暂停、后台、选卡、阻塞剧情。关闭一个窗口只释放自己的暂停原因，不能把另一个原因一起恢复；不要由剧情包直接修改全局 Time.timeScale。领域停止，舞台打字、UI 和允许的媒体使用独立展示时钟。

Completed、Skipped、Cancelled、Failed 分别处理。第一次播放的完成标记与奖励不能由“窗口已关闭”推断。Command 的内容位置标识还要结合业务逻辑运行 ID 做幂等；服务器领奖按玩家/任务/领取 ID 落库。

检查点使用 sequenceId/version/nodeId/选择历史，并转换为项目可序列化 DTO；不直接用 JsonUtility 序列化只有属性的 StoryCheckpoint。首版只在能够重建立绘/背景的章节边界保存，不承诺任意对白、语音或视频帧恢复；内容换版需要迁移或从章节入口重播。

## 对包的优化建议与交接

当前核心足够，不要求先扩展才能接入。实际内容制作的一个缺口是：Inspector 的 Validate Story 当前调用 Build 校验节点图，资源 ID、资源类型、本地化 key 与业务命令还需运行时发现错误。

已建立 [UnityPackages Issue #23](https://github.com/Novorizon/UnityPackages/issues/23)，建议提供可选的内容引用收集/预检，项目可接外部资源和文字解析器；未检查的项目明确标注，不初始化 Manager、不联网、不执行业务命令。此项不要求 SDK 依赖 YooAsset，也不将任务、奖励或暂停规则塞入剧情核心。

当前没有向现有 Codex 对话发送消息的工具；Issue 是持久交接记录，不表示包维护对话已收到或开始实现。包方交付稳定 PR/SHA 后，本项目再单独升级并验证；不自动追踪其正在进行的优化分支。

接入验收应覆盖：第一次/再次进入、取消/跳过/资源失败、退出时迟到租约、选卡与剧情互斥、多个暂停原因、存档恢复/换版、无重复发奖，以及真实 Unity 的 TMP/触控/资源释放。源码评估不替代这些验证。
