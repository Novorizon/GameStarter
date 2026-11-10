# 当前客户端流程与验收

打开 `Assets/Scenes/Bootstrap.unity`：启动界面 → 首次漂流序章 → 海岛营地大厅 → 出征准备/确认 → 塔防 → 返回营地。SDK 使用 `v0.1.0-preview.35` 对应固定 SHA `5bf37118c412ce6f188340fe75661e54c0d91515`；聚合包默认配置 URP 14，项目材质与资源依赖配套适配。

## 已实现

- 海岛大厅：独立固定场景、玩家摘要、木材/石料、营地建设和海岛手记；替代原城堡背景玩家页，GameEntry/HotGame 仍只装配和调用业务模块。
- 玩家档案：头像入口查看探索经验条、称号、战绩与长期旅程目标，目标经验随战报自动结算；与手记的材料领取分开显示。
- 出征准备：预览实际关卡、6 波敌人、塔型/费用、初始金币/基地耐久与营地胜负补给；确认后才加载战场，取消不创建战斗。新出征生成种子，局内重试保留种子。准备失败可在当前页面重试。
- 波次与暂停：准备页/局内共用配置投影，预告下一波或当前进攻；手动、后台、失焦和返回确认独立，取消弹窗不会误清除其他暂停。
- 建设四级：临时帐篷 → 围栏营地 → 石砌庭院 → 海岛家园。材料、胜利条件与任务奖励来自 Luban，建设后更新真实 3D 外观。
- SDK StoryPlayer 驱动三段漂流抵达对白，TMP 呈现；正文以 YooAsset story 资源发布。完成或主动跳过记录已抵达，取消/失败不冒充完成，奖励在任务页另行领取。抵达保存未成功时可在当前大厅重试序章。
- 本地建设、任务领取、战斗统计和补给使用同一份档案原子提交；失败不先扣材料/发奖励，同一当前战报不重复入账。旧档案保留战绩并初始化营地，真实 JsonUtility 的缺字段行为有单独验证。补给锁定出征时营地等级，保存失败后先建设营地再重试也不改变原战报奖励。
- 14 张真实 Luban 表：8 张战斗、主题/旅程任务/等级 3 张、营地等级/任务 2 张及 Example。结构与合理分组见[客户端规划](../Game/ClientPlan.zh-CN.md)。
- 固定通行区域塔防：6 波、7 建造位置、18 个参考路线点、3 塔型、4 怪物型、7 强化卡。敌人按显式逻辑格寻路、预约避让，在基地周围取得攻击/候场位置；闲置隐藏建造标记，选塔后显示可建位置，点塔显示实际领域射程。
- 卡牌基础品级/颜色与表内图标已接通，抽牌仍为单池权重。飞入、粒子和主题扩展未在本轮制作。
- AOT 启动展示继续保留：无下载显示步骤，有下载显示实际字节；图片/已知布局/文案缓存供下次启动。资源下载完成仍等业务就绪才退出展示。
- imagegen 海岛底图、低模塔防地图、公共面板/图标/头像；文字全部 TMP。公共图像/字体和营地 maps/story/rendering 分类由 YooAsset 管理；运行时材质所需 Shader 有实际资源依赖。

## 当前验证

| 项目 | 证据 |
| --- | --- |
| 项目回归 | 97 项；含真实 Luban/导航解码、迁移、建设/任务、结算原子提交、奖励等级锁定、暂停叠加和 8 种种子实际导航策略通关 |
| 导航 | 完整压力76/76、严格完整性56/56、过程死亡33/33；真实Unity10组无塔与满员围攻，逐ID核对到位、队列与死亡；详见导航验收 |
| 领域 / 账号协调 | 12 / 13 项；12 项为旧折线参考检查，账号使用测试后端，不代表导航或正式登录/结算 |
| Unity 脚本 | Unity 2022.3.62f1；9 Demo，13 Editor / 9 Player 程序集，包含新增 Story 示例 |
| 包安装 | 聚合包安装、HybridCLR 嵌入、URP/热更配置完成；报告见 packages.json |
| 原生流程 | Linux / llvmpipe OpenGLCore；1440×789 桌面与 1440×665 宽屏，报告明确实际渲染管线 |
| 序章与营地 | 实际 SDK 三段对白、手记按钮、四级建造/外观、两场完整塔防胜利后返营地；测试恢复原有档案及存储备份 |
| 迁移 | 真实 Unity JsonUtility 读取缺营地字段的旧 JSON；战绩保留/初始材料/新格式标记与损坏新格式拒绝 |
| 战斗 | 真实 6 波关卡的两场完整胜利、失败重开/胜利重开；每场种子、击杀/漏怪、tick、升级与选卡次数见对应 JSON，随机种子变化不要求固定战斗 tick |
| 输入/射程 | 部署/射程使用实际 Input System 鼠标事件；档案、准备和取消走按钮路径；末波用实际 ScrollRect 组件滚入视口；焦点/挂起通过 Unity SendMessage 驱动，不冒充设备事件 |
| 字体 | 战斗 TMP 47 / Legacy Text 0；营地/任务/剧情/启动展示也全部为 TMP |
| 启动 | 两轮 Play 的就绪和退出、实际启动控件与缓存布局；25/100 MiB 下载控件验证输入不作为 HTTP 下载证据 |

本分支桌面完整流程和无塔围攻已重新执行；既有宽屏画面不是本次新增的真机或导航性能证明。当前资源输出为 [27 个 YooAsset 主资源](Navigation/resource-build.json)，含独立 `navigation` 分类。具体结果、复现命令、容量与性能边界见[导航验收](Navigation/Verification.zh-CN.md)。SDK 和程序集引用变化需要重建匹配主包基线，图片、表和热更 DLL 要配套发行。

## 实际画面与报告

[玩家档案](player-profile.png) · [出征准备](expedition-preparation.png) · [手机档案](phone-player-profile.png) · [手机出征](phone-expedition-preparation.png) · [漂流序章](arrival-story.png) · [帐篷营地](camp-tent.png) · [海岛手记](camp-missions.png) · [围栏](camp-fence.png) · [庭院](camp-courtyard.png) · [家园](camp-home.png) · [塔防](battle.png) · [品级卡牌](three-cards.png)。这些为 Unity 实际渲染帧。

[桌面完整流程](Navigation/unity-gameplay.json)、[无塔围攻](Navigation/unity-navigation.json)、[围攻画面](Navigation/base-surround.png)、[F3 覆盖](Navigation/base-surround-debug.png)、[脚本编译](Navigation/player-compile.json)、[程序集](Navigation/sample-assemblies.json)、[既有宽屏](Validation/phone-layout.json)、[包安装](Validation/packages.json)、[资源包](Navigation/resource-build.json)、[输入范围](Validation/inputs.json)。启动生命周期报告见 [EntryStartup](../Validation/EntryStartup/)。

验收入口：`Game.Editor.TowerDefenseValidation.Run`，加 `--novorizon-phone-layout` 检查宽屏；`Game.Editor.GameEntryValidation.Run` 验证启动与退出。这些 Play 验收不加 `-quit`。

## 规划与范围

[海岛说明](../Game/IslandCamp.zh-CN.md)记录模块、数据、存档与玩法规则。[Meshy 清单](../Game/MeshyBrief.zh-CN.md)包含帐篷/围栏/庭院/小屋和战斗模型制作要求；当前仅保留 Cube/简单几何体占位，正式模型、动画与 LOD 由用户通过 Meshy 提供。

[网络核对](../Game/ServerIntegration.zh-CN.md)已补用户的成功交叉验证与本实例 VPN disabled 的实时证据。本项目仍未执行正式服务器登录/经济/领奖，当前材料是本地玩法。IL2CPP、手机设备/性能、硬件 GPU、声音与完整媒体播放未作为通过声明。地图台地是 2D 表现，当前未引入 RVO 或动态堵路。
