# E：Warcraft III 风格公开行为的独立复现

当前工作分支为 `experiment/navigation-e-warcraft3-refined`；原 E1b 分支、冻结 DLL 与未完成长测保留。新增 `WarcraftRefinedFactory`（E4）只用于明确选择的精细连接实验，不能把旧版报告改名为 E4。

本分支为 `experiment/navigation-e-warcraft3`，基线为 `8c0dd50`。**它不是已获证实的暴雪原版寻路算法实现**。目前能核实的是官方公开的指令、碰撞与堵路行为；未取得原引擎完整寻路源码、设计文档或可复现实机轨迹。

此实验独立实现“离散通行图和体型净空 + 全局 A* + 动态局部重搜 + 等待/友军让路”的组合，并以统一 NavigationLab 测试器判断其结果。A*、局部重搜的具体规则与预算是本实验的选择，不能因为分支名称含 Warcraft III 就声称原版使用相同细节。

## 资料与证据等级

| 来源 | 能确认的内容 | 不能据此确认的内容 |
| --- | --- | --- |
| [Blizzard：Unit Commands](https://classic.battle.net/war3/basics/unitcommands.shtml) | Move/Stop/Hold Position/Attack/Attack-Move/Patrol 的公开行为；水与树木等物理屏障可以阻挡单位 | 内部图搜索算法、网格分辨率、碰撞求解器及调度次序 |
| [Blizzard：Unit Control](https://classic.battle.net/war3/basics/unitcontrol.shtml) | 单位围同一目标寻找攻击机会，友军/敌军混杂可能阻路，部分单位会绕行寻找空隙；官方建议玩家更换容易到达的目标 | 自动解开所有包围或堵塞的完备保证 |
| [Blizzard：Siege Engine](https://classic.battle.net/war3/human/units/siegeengine.shtml) | 大型单位更难通行，可被其它单位包围堵路 | 原版内部碰撞半径及寻路代价公式 |
| [Blizzard：Revisiting the Warcraft III Editor](https://news.blizzard.com/en-gb/article/23395649/revisiting-the-warcraft-iii-editor) | 地图几何、单位/建筑由编辑器管理；地面通行空间与瓶颈是官方地图制作关注点 | 具体寻路实现或自动修复不合理地图的能力 |
| [Warsmash：CPathfindingProcessor](https://github.com/Retera/WarsmashModEngine/blob/f9e0aeed4be372d6016519d0e97b384aa873f374/core/src/com/etheller/warsmash/viewer5/handlers/w3x/simulation/pathing/CPathfindingProcessor.java) | 一个公开 Warcraft III 重实现使用 A* 风格搜索，并分别检查静态通行与动态碰撞 | 暴雪原引擎使用同一算法；该文件作者明确将其描述为自己的初稿 |
| [Warsmash：CBehaviorMove](https://github.com/Retera/WarsmashModEngine/blob/f9e0aeed4be372d6016519d0e97b384aa873f374/core/src/com/etheller/warsmash/viewer5/handlers/w3x/simulation/behaviors/CBehaviorMove.java) | 该重实现把行走执行、碰撞与重新寻路放在移动行为层 | 原版对同类堵塞的内部状态机与重搜阈值 |

读取时间：2026-11-11 UTC。Warsmash 仅作为公开重实现的身份与架构对照，不作为暴雪算法的证据。固定参考提交为 `f9e0aeed4be372d6016519d0e97b384aa873f374`，三个文件的blob SHA均已逐一核对。其许可证为 AGPL-3.0；本分支不复制、编译或链接其源文件。候选 C# 搜索、几何与调度代码独立编写，使用本项目许可。

## 实验实现选择

1. 地图的不可通行格、基地圆和单位半径共同决定静态净空。不能只检查格心，移动整段都必须合法。
2. 每个单位以独立 A* 搜索全局路线；动态单位与静态地形分别处理，不把友军永久变成地形墙。
3. 路线前方被动态身体挡住时先等待，超出可观察阈值后执行有预算的局部重搜。沿有效后续路点重新接回原路线。
4. 友军让路属于独立、可开关的实验扩展：身体实际步行到合法临时位置，通行后返场；不使用时空预约、SIPP、ORCA、传送或关闭碰撞。
5. 围攻目标需真实满足半径、攻击距离与路径可达性；最终到位和临停分开计数。没有合法驻留位的请求留在外部队列。
6. 记录全局/局部搜索、等待、碰撞停止、让路次数及失败，保留初版失败与修正版结果。任何待生成或未到位单位都不能被当作完成。

## 测试状态

公共 Core/Oracle/Runner 已同步。候选使用相同输入、独立连续几何检查器与相同成功条件；来源核对本身不算测试通过。公开来源与自选策略的机器可读界定在 `Evidence/source-review.json`。

本分支只通过实验 factory 运行 E；没有将 `Bootstrap`、业务 `FightSession` 或生产导航切换到 E。直接启动原游戏仍运行保留的 A 实现。

## 早期真实 Forest 排错记录

以下是早期 debug，**不是统一跨算法矩阵或 Unity 验收**。所有输入直接解析同一 `ForestNavigation.bytes`，每个5ms子步使用公共独立 `ContinuousOracle` 检查身体/障碍/基地扫掠、速度、真实攻击状态及出生/拒绝身份。成功还需全员实际到位、最大兼容攻击人数达到独立匹配结果，并稳定200个50ms逻辑tick。

| 版本 | Medium8 / Big12 | Big44 | Medium45 | tick4000释放Small2（先检查Big44是否稳定） |
| --- | --- | --- | --- | --- |
| 初版非分组让路 | 仅2/1人到位，其余堵住 | 未作为通过 | 未作为通过 | 未作为通过 |
| 修正连续碰撞停步优先级（E1） | 8/8、12/12通过 | 44/44、18攻26候场通过 | 45出生、8仍Moving，失败 | 46出生、22仍Moving；前44其实也未稳定，失败 |
| 请求者占有让路组、分阶段返场（E2） | 未重计覆盖 | 44出生、38仍Moving，失败 | 45出生、28仍Moving，失败 | 初始44也未稳定，失败 |

首个确定的实现错误是：仅按等待时长决定谁停步，可能先停前车，再因后车将追撞而停后车，导致整队互锁。E1先检查一方独自行走、另一方停住时哪种选择安全，再使用等待优先级；相同Forest小/中阵容因此恢复，Big12实际发生两次友军让路和返场。

复查早期日志还发现：预注册Small的射程改变了Big的目标优先级，导致E1前44人本就未稳定。后续smoke显式检查tick3999前提，并把专用格排序只依赖空间净空，不能把这轮结果冒称“44先稳后晚到”通过。

E2试图冻结无关交通，为单个请求者逐个让路，结果制造更多阻塞；该变体被拒绝，默认factory恢复较优E1。E2仅保留为 `WarcraftGroupedYieldFactory`，不能使用其失败结果冒充默认方案结果，也不能因Big44通过就把默认E1称为普适。

原始日志和来源指纹：`Evidence/EarlyForest`、`Evidence/SoloPriority`、`Evidence/GroupedYield`。初轮报告还暴露单体最小间距Infinity与Point2派生属性递归的JSON写入错误，日志保留；后续报告使用nullable间距与显式X/Y，并未改变几何检查。

早期smoke独立运行示例（共享Core/Oracle同步到本分支后）：

```bash
dotnet run --project Experiments~/NavigationLab/Candidates/E-Warcraft3/Smoke/NavigationLab.EWarcraft3.Smoke.csproj -c Release -- /path/to/GameStarter big44 /tmp/E-Forest
```

`--baseline`关闭显式友军让路，`--grouped`选择被拒绝的E2，`--backoff`选择E3。报告中的factory ID区分实际变体；同一`name`不代表不同输入，不能拿重跑数量当新覆盖。

## 默认版本与被拒绝的退让扩展

默认 `NavigationLab.Candidates.EWarcraft3.WarcraftFactory` 使用较稳的空间重寻路与非分组友军让路（E1b）。它按空间净空而非其它单位的射程决定专用格优先级；预注册Small不再改变Big的整体填充顺序。当前存在的普适性失败不会被版本名称或参数改变掩盖。

- `WarcraftBaselineFactory`：E0，仅空间A*、动态局部重搜和连续碰撞停步，不主动让路。
- `WarcraftGroupedYieldFactory`：被拒绝的E2，冻结无关交通的让路组/返场实验。
- `WarcraftRequesterBackoffFactory`：被拒绝的E3，空间专用位置的请求者先退让、等待门单位移动后再进入。它仍未使晚到46完成。

E3早期无条件backoff破坏了初始44人稳定；限定到空间专用Goal后，初始44人确实稳定，但最后2名Small仍未到位。`Evidence/RequesterBackoff`与`Evidence/ConstrainedBackoff`保存失败。两目录均有`candidate-source.zip`及`source-snapshot.json`，归档的运行时源码逐字SHA与原报告一致，可以在独立worktree复现，不用当前默认源冒充旧变体。

所有临停、backoff、Goal排序和碰撞优先级都是本实验自行选择的控制策略，尚无证据证明暴雪原版使用相同策略。此处测试的仅是统一模型中的2D地面圆盘、固定速度上限、格心驻留/攻击目标；未涵盖原版单位转向、加速度、海空移动、阵营阻挡规则、技能位移或战争迷雾。

## 绑定来源的旧协议公共7项smoke

共享harness `c882538`、默认运行时源`8a0a3cd`已通过freeze-run构建绑定、独立oracle与全新实例重放。结果为 **6 Passed / 1 Failed / 0 Unsupported**：open48、Forest8、非格心途中死亡补兵、动态堵路/重开、大体型r700、快速2200mm/tick通过，6项通过均重放一致；原72场景全部实际生成，但最终11人Moving、仅9攻击，deadline12000，明确失败。

这7项是旧协议 `c882538` 下来源绑定的smoke，不计为最终共同 v2 矩阵。暂停/实例隔离、源与DLL无漂移均通过；`formalEvidenceReady=true`表示来源完整，并不表示`success=true`（本轮success为false）。`Evidence/BoundSmoke7`保留summary、编译项、buildmanifest、实际输入、日志与压缩逐步轨迹；其中冻结DLL SHA为`ba4fbaaa6445087f1e80095c34368ebcaba2f75b43ea363d6d6f332d65a0c543`，不能与未PathMap的live构建DLL指纹混用。后续同一公共full/stress仍须单独验证并记录真实失败。


## 选择性的真实 Unity Play / Mono 复验

`Evidence/SelectedUnityMono`保存旧E1b的相同冻结DLL（`ba4fbaaa…c543`）的真实Unity 2022.3.62f1复验：

- Forest第1波8/8全攻击，8120微步后满足200稳定区间；实际OpenGLCore/llvmpipe软件渲染通过画面检查并保存PNG。
- 24人初始到位后移动目标，确实执行1次WorldChange并重分配24个Goal，最终16攻8候场、200稳定区间通过；使用Null图形。
- 48人“旧候场补位”用例在初始阶段仍有1人Moving，0次死亡、0稳定区间，失败。补位事件未触发，不能宣称这轮验证了旧候场补位。

此轮旧驱动的deadline guard比共同模型最多多9微步（45ms），失败报告止于120010微步；已在索引中保留限制，未把它算成相同协议的49项完整重跑。后续驱动`d91e168`已统一deadline，但没有用新版驱动冒充这轮记录。这里只确认选定场景能在同DLL的Unity Mono中执行，不代表生产后端切换、IL2CPP、手机性能或真实硬件GPU验收。


## E4：动态粗图漏路的局部精细连接修正

独立审计在旧版U反例的同一末态中发现：原8个静态合法格心连接都被驻留单位占据，但原半径、原Goal和55个固定他体之间存在连续合法通道。同旧DLL反射确认动态Find返回null；独立Oracle验证一条通道可在36微步到位，再稳定200区间。这是本实现离散图的遗漏，不是已证明的Warcraft原算法极限，也不能因此改小单位或放大地图。

E4在动态粗图搜索失败、最终目标当前没有被身体占据时，才启用当前位置周围最多6个原格范围的1/4格连接图。该图按相同静态扫掠和动态身体检测找出可达原格心，再把每条实际细路径前缀作为原A*的多源起点；原地形、驻留Goal、碰撞半径和速度不变。所有连接均独立检查整段，未硬编码审计路径或场景名称，也没有增加时间预约。细节点数、搜索预算和失败单独计数。

此修正仍是有预算的局部图，不能由单个反例修好就宣布普适。先验证相同末态、完整U、直角走廊与Forest44/45/46，随后再决定是否替换整体实验版本。当前新增策略的实际结果会另列，旧E1b失败保持原身份。


### E4-v2 的已完成关键回归（整体矩阵尚未开始）

- 同原U失败末态：实际新Find生成14路点，保持55他体固定、原Goal588/r250/速度1000，40微步到位，再200稳定区间。共享Oracle验证3,144,680次身体对扫掠，最小间隙0、漂移0；独立审阅确认代码未读见证路线、未硬编码地图/ID。
- 完整U56：16攻40候场，tick351后满足200稳定区间，全新实例重放一致。
- 完整直角32：16攻16候场，tick323后满足200稳定区间，全新实例重放一致。
- Forest44：44实际生成、18攻26候场，tick1618后满足200稳定区间，全新实例重放一致。
- Forest45：45实际生成、12攻30候场、3仍Moving，真正deadline12000失败。原尺寸/地图/人数/条件没有放宽；残余原因继续独立排查，不将它直接称为原Warcraft算法或空间策略极限。

同态查询和完整原始输入执行是两类不同证据。E4-v1与v2的U只是同一物理场景的源码回归，不算增加覆盖。`Evidence/RefinedFailedStateQuery`、`RefinedRegressions/V2-U`与`V2-RightAngle`保存来源绑定记录；`V2-Forest-Partial`仅保存已闭合的44/45个例，明确不是完整组或49项矩阵，晚到46在此阶段备份后闭合，完整组见`V2-Forest`。上述U/直角完整报告均已用严格比较工具校验输入、Commands、压缩报告/轨迹、实际DLL、编译清单、源稳定与重放。E4还未执行对应Unity/Mono复验，旧E1b的Mono记录不能改名使用。


### 选定整体版本与旧长测的中止

选定E4-v2（运行时代码`a5d592f`，冻结DLL `7f4cdb6b660ab3280276239034bd763d084b5a28d7411021cc30cdf4fb11869f`）作为后续整体E版本。关键5个完整输入中，U56、直角32、Forest44通过并完成全新实例重放；Forest45剩3Moving、晚到46剩2Moving，在真正deadline12000失败。晚到46的初始44稳定前提已实际核验，不把前提未到位的结果当作晚到测试。

`RefinedRegressions/V2-Forest`为完整三例组，已严格校验所有原始证据。较早的`V2-Forest-Partial`仅是当时44/45闭合后的历史阶段备份，不另计覆盖。失败case没有执行成功case那种重放，`ReplayVerified=false`不能据此另算一次“重放不一致”。

Forest45后续独立检查固定其它44实际身体（包括另2个Moving）：15/19的25mm搜索未找到单体路，14的原Goal被15身体占据；15拟议竖直路线实际穿过原地图阻挡格，初步离散猜测被否定。此结果不证明几何无解，也不推出必须挪42个驻留者。未根据未证实猜测增加切线策略或放宽几何，完整审阅保存在`RefinedForest45Audit`。

被替代E1b的最终v2长测已明确中止：12项闭合，5通过、7失败；活动300密集场景只读取到94083微步的落盘下界，SIGTERM中止，不是deadline失败，也没有形成49或41项整体结果。`E1bFinalV2Partial`保存12个闭合报告/完整闭合轨迹/输入、原3C#运行时和4DLL压缩归档、源稳定后验与中止标记；不完整活动轨迹仅在原外部目录保留并记录SHA。之后以选定E4单独运行完整共同stress49和成功case重放，不混合两个版本拼分。
