# 寻路方案对照

这些分支保留实际运行的替代方案与失败，不把论文或库的名称当作验收结果。正式实现和 Unity 结果以[当前验收](Verification.zh-CN.md)为准；所有分支均未合并 `main`。

## 相同目标集合与规则变更分开比较

| 实验 | 已执行的结果 | 证据边界 |
| --- | --- | --- |
| 旧优先预约 | 原 4m 十字场景只准入 52/72 | 独立见证证明剩余请求可行，因此是算法失败，不能计为容量通过 |
| 独立撤离、逆序插入见证 | 同一地图、原 72 目标、原半径/速度/入口，全部 72 实际抵达 | 证明存在合法执行序列，不等于在线生产算法已经执行它 |
| 协同预约修复 | 原 72 目标集全部抵达；满员后的单点、批量、途中死亡恢复另测 | 保持原离散模型；仍须通过真实业务地图和 Unity，不宣称一般完备性 |
| 官方 RVO2 + 全局路线 + 显式让行 | 4 种出生顺序 × 5 种目标顺序，共 20 组全部 72/72，12 攻击、60 候场，稳定 200 原 tick | 5ms 子步检查连续几何；仅为独立实验，尚未接入正式战斗生命周期、Unity 与移动设备 |
| 只细化网格 | 同一 19×19 粗图对应世界的 200 请求仅 177 抵达 | 这是保留的失败，增加可选目标并未自动解决在线调度 |
| 细网格 + 协同修复 | 四种不同 ID 请求顺序均 200/200，24 攻击、176 候场；96 次逐攻击格死亡补位通过 | 1m 改为 500mm、25ms 子步；目标数从 72 增为 272，攻击位从 12 增为 24，不能冒充原目标集合不变的通过 |

表中“原 4m 十字 72”与“19×19 单口 200”是两个不同人工场景。后者细化保持同一世界尺寸、障碍、身体半径和每秒速度，但改变了离散站位规则。实际 Forest 本来就使用 500mm 格，最大身体半径为 250mm；不能根据这项实验直接再缩格，也不能据此声称真实地图可以容纳 200 人。

ORCA 对照也保留基础控制器的失败：只有 BFS 方向与局部避让时，3000 步内仅 65 个入场、14 个抵达。最终方案还加入可行的路线连接、动态改道与明确的临时让行。这个结果说明需要全局路线与群体调度，不能归纳成“装上 RVO2 就解决围攻”。

## 真实地图的独立容量见证

同一份 `ForestNavigation.bytes` 的 SHA-256 为 `ad5dd54b722c97779ad6b0f6e22548d3288ed380c8d758b7615d1e6894165256`。

- 半径 250、射程 550：全部合法驻留格可以容纳 44 人，其中 18 个实际攻击位置。旧的“只允许攻击外沿”规则漏掉了 4 个合法内侧位置。
- 半径 200、射程 450：45 人，12 个实际攻击位置。
- 44 个大体型已满后，两个半径 180 的单位仍能进入大体型无法占据的 256、257 格。见证让 4 个原有单位实际撤到临停通道，两个小体型从原出生点进入，最后 4 个单位沿合法预约回到原位置，最终 46 人、20 攻击、26 候场。

最后一项没有删除大体型、改变速度、扩大地图或让临停单位在通道里冒充完成。它证明晚到请求可行；生产算法必须独立实际完成，才能在当前验收中计为通过。

## 分支与复现

| 分支 | 内容 |
| --- | --- |
| [`exp/navigation-feasibility-witness`](https://github.com/Novorizon/GameStarter/tree/exp/navigation-feasibility-witness/Verification~/NavigationFeasibility) | 原 72 目标、实际 Forest 饱和与晚到异质 46 的独立见证、动作时序和连续几何报告 |
| [`experiment/navigation-refined-grid`](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-refined-grid/Experiment~/RefinedGrid) | 粗细网格对照、早期失败、协同候选源码快照与四种真实请求顺序 |
| [`experiment/navigation-orca`](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-orca/Experiments~/OrcaComparison) | 固定官方 RVO2-CS 源码、许可证、控制器对照及 20 组矩阵 |
| [`test/navigation-completeness-challenge`](https://github.com/Novorizon/GameStarter/tree/test/navigation-completeness-challenge/Verification~/TowerDefenseNavigationChallenges) | 不能通过拒绝请求完成的严格挑战、队列与身份守恒、独立容量证书 |
| [`test/navigation-gate-lifecycle`](https://github.com/Novorizon/GameStarter/tree/test/navigation-gate-lifecycle/Verification~/NavigationGateLifecycle) | 让路事务活动期间，参与者在边中途死亡、补兵和再次补位的独立审计 |

各实验记录对应源码与二进制指纹。使用其他提交的通过报告，或仅改变 seed 标签但没有改变输入，均不能算新一轮覆盖。正式方案继续保留整数领域、明确预约、实际攻击状态与已接入的战斗生命周期；替代分支用于交叉验证和后续选择，不直接增加游戏运行依赖。
