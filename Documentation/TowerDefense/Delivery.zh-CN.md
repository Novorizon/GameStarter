# 当前客户端流程与验收

打开 `Assets/Scenes/Bootstrap.unity`：启动界面 → 玩家页面 → 出征 → 塔防 → 返回营地。SDK 固定提交以项目 manifest 为准。当前地图为 `forest-diorama-1`，6 波、7 个固定建造位置、18 个路线点、3 塔型、4 怪物型、7 张强化卡。

## 已实现

- AOT 独立启动展示：普通启动使用步骤进度，有更新时使用 YooAsset 实际下载字节。`GameEntry` 只调用展示和 Manager API。
- 启动图片、已知控件布局/文案/颜色走 YooAsset，成功加载后缓存供下次启动；首次启动有独立内置图片、布局和 TMP 字体。新增控件或行为仍须匹配 AOT 代码。
- 热更 `ClientFlow` 协调玩家页面和战斗，`HotGame` 只调用模块。玩家页面显示头像、名称、等级、经验、战绩和三项旅程目标。
- `JourneyProgress` 负责本地目标/等级和统计，保存成功后发布状态，同一局不会重复结算。界面明确标识本地档案；没有伪造服务器货币或正式领奖。
- 12 张真实 Luban 表：8 张战斗、主题/任务/等级 3 张配置和 Example。具体结构与后续分组见[客户端与数据规划](../Game/ClientPlan.zh-CN.md)。
- 地图是低多边形台地式 2D 预渲染图，保留统一格尺的砂岩路、桥梁、池塘和台地；平时不显示特殊建造格，选择塔卡后才描出未占用区域。
- 塔、基地和怪物为共享网格的程序化 3D 低模。点塔退出建造并显示来自实际战斗数值的射程圈，升级和强化后同步变化。
- 松绿/铜金面板、3×3 图标、守卫头像与森林城堡背景；文字全部 TMP。公共 UI/字体位于 `Assets/GameAssets/Common`，地图位于 TowerDefense，按 ui/fonts/maps 分组。

## 当前验证

| 项目 | 证据 |
| --- | --- |
| 项目回归 | 73 项；含真实 Luban 解码、关卡可通关、任务等级、重复结算与保存失败重试 |
| 领域 / 账号协调 | 12 / 13 项；账号使用测试后端 |
| Unity 脚本 | Unity 2022.3.62f1；8 Demo，11 Editor / 8 Player 程序集 |
| 桌面实际渲染 | Linux / llvmpipe OpenGLCore，1440×789 |
| 宽屏布局 | 同一 Editor 在 1440×665（约 19.5:9）渲染与操作验收，不是真机测试 |
| 点击与建造 | 七个区域均未被 HUD 遮挡；真实 Input System 注入鼠标事件点选，塔卡回调与实际扣费/落点通过；闲置隐藏、建造时显示区域通过 |
| 射程 | 点击另一座已建塔后，检查圆圈中心和半径与该塔领域状态一致 |
| 战斗和玩家页 | 6 波胜利，126 击杀、0 漏怪、2178 tick、13 次升级、5 次选卡；胜败重开、旅程写入与返回页面刷新通过 |
| 字体 | 战斗 TMP 46 / Legacy Text 0；玩家页面和启动展示也使用独立 TMP 字体 |
| 启动展示 | 两轮真实 Play，普通启动/下载控件分支以及下一次启动读取缓存布局分别验证；下载控件使用25/100MiB的验证输入，不作为真实 HTTP 证据 |
| 生命周期 | 进入/返回/重开及停止 Play 清理通过，业务模块使用独立于初始化操作令牌的生命周期 |

资源包实际输出以[资源报告](Validation/resource-build.json)为准，启动/界面代码改变后必须重建匹配的 AOT 主包基线，图片、表和热更代码需要配套发行。

## 画面与报告

[玩家页面](player-page.png) · [部署与射程](planning.png) · [战斗](battle.png) · [三选一卡](three-cards.png) · [返回后的任务](player-page-returned.png) · [宽屏建造模式](phone-deployment-mode.png) · [宽屏玩家页](phone-player-page.png)。全部是实际 Unity 渲染帧。

[桌面](Validation/prototype.json)、[宽屏](Validation/phone-layout.json)、[编译](Validation/player-compile.json)、[程序集](Validation/sample-assemblies.json)、[资源包](Validation/resource-build.json)、[输入范围](Validation/inputs.json)。启动生命周期报告见项目的 [EntryStartup](../Validation/EntryStartup/)。

主界面/战斗验收入口为 `Game.Editor.TowerDefenseValidation.Run`；带 `--novorizon-phone-layout` 检查宽屏。启动与缓存验收入口为 `Game.Editor.GameEntryValidation.Run`。这些 Play 验收不传 `-quit`，由完成回调退出。

## 规划与边界

[Meshy 清单](../Game/MeshyBrief.zh-CN.md)说明模型优先级、尺寸、坐标轴、动作和挂点。[业务规划](../Game/ClientPlan.zh-CN.md)说明正式账号/任务协议、合理表分组、近战/远程 NPC 状态机与避让。固定路线阶段先考虑队列间距和走廊内有限偏移，当前没有引入 RVO 或动态堵路。

正式服务登录、服务器经济/任务领奖、IL2CPP、硬件 GPU、手机触控/性能、声音、正式角色动画和 LOD 尚未作通过声明。当前炮塔仍为单体即时命中，台地/阶梯是图像表现，不运行 3D 地形导航。
