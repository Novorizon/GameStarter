# E：Warcraft III 风格公开行为的独立复现

本分支为 `experiment/navigation-e-warcraft3`，基线为 `8c0dd50`。**它不是已获证实的暴雪原版寻路算法实现**。目前能核实的是官方公开的指令、碰撞与堵路行为；未取得原引擎完整寻路源码、设计文档或可复现实机轨迹。

此实验独立实现“离散通行图和体型净空 + 全局 A* + 动态局部重搜 + 等待/友军让路”的组合，并以统一 NavigationLab 测试器判断其结果。A*、局部重搜的具体规则与预算是本实验的选择，不能因为分支名称含 Warcraft III 就声称原版使用相同细节。

## 资料与证据等级

| 来源 | 能确认的内容 | 不能据此确认的内容 |
| --- | --- | --- |
| [Blizzard：Unit Commands](https://classic.battle.net/war3/basics/unitcommands.shtml) | Move/Stop/Hold Position/Attack/Attack-Move/Patrol 的公开行为；水与树木等物理屏障可以阻挡单位 | 内部图搜索算法、网格分辨率、碰撞求解器及调度次序 |
| [Blizzard：Unit Control](https://classic.battle.net/war3/basics/unitcontrol.shtml) | 单位围同一目标寻找攻击机会，友军/敌军混杂可能阻路，部分单位会绕行寻找空隙；官方建议玩家更换容易到达的目标 | 自动解开所有包围或堵塞的完备保证 |
| [Blizzard：Siege Engine](https://classic.battle.net/war3/human/units/siegeengine.shtml) | 大型单位更难通行，可被其它单位包围堵路 | 原版内部碰撞半径及寻路代价公式 |
| [Blizzard：Revisiting the Warcraft III Editor](https://news.blizzard.com/en-gb/article/23395649/revisiting-the-warcraft-iii-editor) | 地图几何、单位/建筑由编辑器管理；地面通行空间与瓶颈是官方地图制作关注点 | 具体寻路实现或自动修复不合理地图的能力 |
| [Warsmash：CPathfindingProcessor](https://github.com/Retera/WarsmashModEngine/blob/main/core/src/com/etheller/warsmash/viewer5/handlers/w3x/simulation/pathing/CPathfindingProcessor.java) | 一个公开 Warcraft III 重实现使用 A* 风格搜索，并分别检查静态通行与动态碰撞 | 暴雪原引擎使用同一算法；该文件作者明确将其描述为自己的初稿 |
| [Warsmash：CBehaviorMove](https://github.com/Retera/WarsmashModEngine/blob/main/core/src/com/etheller/warsmash/viewer5/handlers/w3x/simulation/behaviors/CBehaviorMove.java) | 该重实现把行走执行、碰撞与重新寻路放在移动行为层 | 原版对同类堵塞的内部状态机与重搜阈值 |

读取时间：2026-11-11 UTC。Warsmash 仅作为公开重实现的身份与架构对照，不作为暴雪算法的证据。其许可证为 AGPL-3.0；本分支不复制、编译或链接其源文件。候选 C# 搜索、几何与调度代码独立编写，使用本项目许可。

## 实验实现选择

1. 地图的不可通行格、基地圆和单位半径共同决定静态净空。不能只检查格心，移动整段都必须合法。
2. 每个单位以独立 A* 搜索全局路线；动态单位与静态地形分别处理，不把友军永久变成地形墙。
3. 路线前方被动态身体挡住时先等待，超出可观察阈值后执行有预算的局部重搜。沿有效后续路点重新接回原路线。
4. 友军让路属于独立、可开关的实验扩展：身体实际步行到合法临时位置，通行后返场；不使用时空预约、SIPP、ORCA、传送或关闭碰撞。
5. 围攻目标需真实满足半径、攻击距离与路径可达性；最终到位和临停分开计数。没有合法驻留位的请求留在外部队列。
6. 记录全局/局部搜索、等待、碰撞停止、让路次数及失败，保留初版失败与修正版结果。任何待生成或未到位单位都不能被当作完成。

## 测试状态

公共 Core/Runner 正在由导航实验主分支建立。候选会使用相同输入、独立连续几何检查器与相同成功条件；正式命令、源码指纹及结果在可运行后补入本页。当前资料核对和设计不算测试通过。
