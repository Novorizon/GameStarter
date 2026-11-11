# 导航方案实现与对照结论

本地直接运行六个冻结方案，请使用独立分支 `experiment/navigation-unity-playground`，见[本地 Unity 可视化测试](LocalUnity.zh-CN.md)。原实验分支的 Bootstrap 默认仍使用 A。

六份选定报告的共同49项已完整结束，使用同一套原始输入；每项固定地图、圆盘身体、速度上限、合法驻留位置及每5ms连续扫掠检查。下面按整版实现比较。容量与不可达用例按各自条件验收，PASS不表示所有请求都入场。

| 候选 | 通过／失败／未支持 | Forest44 | Forest45 | 先稳44再晚到2 | 原72三例 | 原300两例 |
| --- | --- | --- | --- | --- | --- | --- |
| A冻结SIPP | 41／0／8 | 通过 | 通过 | 通过 | 3／3 | 2／2 |
| B0：ORCA＋重寻路＋简化 | 28／21／0 | 失败，34人移动 | 失败，12人移动 | 初始44未稳 | 0／3 | 0／2 |
| B1：B0＋项目协调 | 45／4／0 | 失败，35人移动 | 通过 | 初始44未稳 | 3／3 | 2／2 |
| C33：分区流场 | 46／3／0 | 失败，2人移动 | 失败，2人移动 | 通过 | 3／3 | 2／2 |
| D12：分层与分段流场 | 26／23／0 | 失败，27人移动 | 通过 | 初始44未稳 | 1／3 | 0／2 |
| E：全局A*与局部重搜 | 37／12／0 | 通过 | 失败，3人移动 | 初始44已稳，晚到2人未到位 | 3／3 | 1／2 |

72三例指原4m交叉口、5m交叉口和5m同入口；300两例为parking-lanes与original-12m。表内分数只是这些原输入的通过数，未增加覆盖。Forest44与45的体型配置不同；晚到用例还预注册后续配置，不能因44人数相同便视为相同输入。

B使用固定提交的官方RVO2-CS速度求解器；全局路线、执行精度护栏、终点及协调属于项目适配。C依据Emerson公开的分区门户与流场架构，D依据Cheng的AoE4官方GDC分层搜索、重叠LOS/FMM架构；围攻调度并非两份资料公开的原版代码。E只借鉴Warcraft III公开阻挡行为，采用独立A*、局部精细接回与让路，不能称为已还原暴雪内部算法。

局部安全、静态可达和全员到位是不同要求。C33三失败是44、45及先远程后近战的公平性；不能借旧C17的47／2补成绩。E公平性失败时85人全部静止，84攻击、1候场，但短射程份额不足，不能称12项全部“卡住”。B1和D12晚到失败发生在初始44稳定检查，后两人尚未到计划出生时间，不能据此认定其补位失败。A的8项未支持涉及地形/目标变化、多目标对向流、大体型和快速移动，未支持不计通过。

六份完整报告的通过项均完成全新实例重放，源码/DLL绑定及隔离、暂停检查有效。Core/Oracle实际DLL相同；Runner只允许核验过的快照根元数据差异。选定DLL的Linux Unity/Mono子集为B0/B1/C33/D12各2通过1失败、E三通过；E有软件渲染，均不等于49项Unity、IL2CPP或手机验收。大体型终点三例另列，D12已实际补跑选定9e61，不能套旧D11结果。

当前业务保留A；新候选的到位失败说明全局路径与局部避障之外，还需可靠的站位分配、射程服务和拥堵恢复。后续应按反例区分离散图漏路、终点互锁及调度公平性，再验证整版回归；若约束下仍无法稳定满足需求，再讨论逻辑格与地图同步设计。现有证据不支持任意地图普适、商业原版失败或设备性能排名。

D12最后两个大图均跑满15000逻辑tick（150000微步）：128人全部入场，127攻击/1移动；256人全部入场，233攻击/23移动，均失败。容量死亡用例未达到初始39活体要求，死亡阶段未触发；另两个死亡用例确实执行了6次移除，随后补位未完成，不能把它们统一写成“死亡未执行”。旧候场补位数按死亡前轨迹与最终状态独立推导；失败报告的默认计数不作为实测值。

D256做了两次仅线程栈诊断，工具调用合计约1.030秒；无heap/core、崩溃信号或重启。该扰动可能进入wall/API计时，详见D分支RuntimeDiagnostics，不用于性能排名。原始DLL、输入与逻辑时限保持冻结。

两个精确缓存辅助分支均只运行四例，分别为2通过/2失败，输入、命令、物理轨迹与最终状态均逐字等于原9e。格边缓存和路线结果缓存都有局部成本下降及其他用例无收益/反向耗时，未据此扩大为49项成绩，未选为正式D。路线缓存虽消除了Forest44约99.41%的重复路线搜索，该例整体分配和耗时收益仍很小；减少A*不等于解决到位或整体成本。

[完整49项逐例对照、来源指纹与最终态图](../../../Experiments~/NavigationLab/Evidence/SelectedSixFinal/README.md)记录本表的正式来源；[版本与分支清单](branches.json)固定实际factory、源码快照、DLL和证据提交。[D失败条件分类与死亡边界证据](../../../Experiments~/NavigationLab/Evidence/SelectedSixFinal/Classification/README.md)另列，分类描述观察到的阶段，不推断未证明的根因。

| 实现分支 | 内容 |
| --- | --- |
| [A](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-a-sipp) | 冻结原实现 |
| [B](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-b-orca) | B0与B1独立工厂 |
| [C](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-c-supcom2) | 公开SupCom2架构独立实现 |
| [D](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-d-aoe4) | 公开AoE4架构独立实现 |
| [E](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-e-warcraft3) | Warcraft III公开行为风格独立实现 |
| [格边缓存辅助](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-d-aoe4-cache) | 四例等价性及成本试验 |
| [路线缓存辅助](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-d-aoe4-routecache) | 四例等价性及成本试验 |
