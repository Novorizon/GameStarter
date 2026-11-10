# 寻路方案对照

这些分支保留实际运行的替代方案与失败，不把论文或库的名称当作验收结果。最终生产与 Unity 结果见[导航验收](Verification.zh-CN.md)；旧报告只能证明其记录的冻结源码，不能自动继承到后续候选。所有分支均未合并 `main`。

## 相同目标集合与规则变更分开比较

| 实验 | 已执行的结果 | 证据边界 |
| --- | --- | --- |
| 旧优先预约 | 原 4m 十字场景只准入 52/72 | 独立见证证明剩余请求可行，因此是算法失败，不能计为容量通过 |
| 独立撤离、逆序插入见证 | 同一地图、原 72 目标、原半径/速度/入口，全部 72 实际抵达 | 证明存在合法执行序列，不等于在线生产算法已经执行它 |
| 协同预约修复 | 原 72 目标集全部抵达；满员后的单点、批量、途中死亡恢复分别验证 | 锁定原 72 终点；与允许更多合法内侧目标的实验分开，不宣称一般完备性 |
| 官方 RVO2 + 全局路线 + 显式让行 | 4 种出生顺序 × 5 种目标顺序，共 20 组全部 72/72，12 攻击、60 候场，稳定 200 原 tick | 5ms 子步检查连续几何；独立实验，未接入正式战斗生命周期、Unity 与移动设备 |
| 只细化网格 | 同一 19×19 粗图对应世界的 200 请求仅 177 抵达 | 保留的失败：增加可选目标未自动解决在线调度 |
| 细网格 + 协同修复 | 四种不同 ID 请求顺序均 200/200，24 攻击、176 候场；96 次逐攻击格死亡补位通过 | 1m 改为 500mm、25ms 子步；目标数从 72 增为 272、攻击位从 12 增为 24，不能冒充原目标集合不变的通过 |
| B2 底座上的局部临停 + 保守回退 | 44/45 饱和、双速晚到46、70轮静态死亡补位、30组过程死亡恢复通过 | 独立提交 `23fca5c`；未包含后续 B3 攻击份额机制，需重新验证集成版本 |

“原 4m 十字 72”与“19×19 单口 200”是两个不同人工场景。后者细化保持同一世界尺寸、障碍、身体半径和每秒速度，但改变了离散站位规则。实际 Forest 本来就使用 500mm 格，最大身体半径为 250mm；不能根据这项实验直接再缩格，也不能据此声称真实地图可以容纳 200 人。

ORCA 对照保留基础控制器的失败：只有 BFS 方向与局部避让时，3000 步内仅 65 个入场、14 个抵达。通过的候选还加入可行的路线连接、动态改道与明确让行，不能归纳成“装上 RVO2 就解决围攻”。官方 RVO2-CS 只在实验工程编译，未加入正式 Unity 运行时依赖。

## 真实地图的独立见证

同一份 `ForestNavigation.bytes` 的 SHA-256 为 `ad5dd54b722c97779ad6b0f6e22548d3288ed380c8d758b7615d1e6894165256`。

- 半径 250、射程 550：可实际容纳 44 人，其中 18 个攻击位置。旧的“只允许攻击外沿”规则漏掉了 4 个合法内侧位置。
- 半径 200、射程 450：45 人，12 个攻击位置。
- 44 个大体型已满后，两个半径 180 的单位仍能进入大体型无法占据的 256、257 格。见证让 4 个原有单位实际撤到临停通道，两个小体型从原出生点进入，再让原单位合法返场，最终 46 人、20 攻击、26 候场。
- 实际六波与 burst30 的最大兼容攻击人数为 8、13、14、15、15、15、15，并分别有全部活体抵达的动态见证。这里计算共享目标的匹配，不能把各 profile 的候选数相加；例如仅一名 Boss 有 18 个可选格，不代表整波能同时安排 18 个攻击者。

这些见证没有删除阻挡者、改变速度、扩大地图，也没有把通道临停计作完成。它们证明相应需求可行；生产算法仍须实际执行同样的入场、补位与围攻验收。

## 局部临停为何需要完整回退

B2 原保守布局要求门到出生点的静态路线不能作临停、临停不得相邻，并要求放置后所有无关通行格仍保持全连通。真实地图中，这些筛选使两名 Big 的完整出/返路径达到 124/130 条边，绕到地图另一段道路。

独立 `23fca5c` 候选优先使用近处临停：保留 Small 路线保护与身体净空，允许相邻且不重叠的格，再用完整 SIPP 预约验证全部出行、入场和返场。四名 Big 可在基地西侧 `382–385` 格临停，完整出/返分别仅 4/8/12/16 条边，最终仍回到各自原 Goal，Small 精确占 256/257。

165 速 Small 在 tick 4000 请求的同一输入，B2 全部到位为 tick 9880，独立局部候选为 5228。新增的 5880 tick 降为 1228 tick，按业务 50ms/tick 即 **294 秒降为 61.4 秒**；这不是整场从第 0 tick 开始用时的降幅。第二名 Small 仍等待首轮事务结束后从原 S 实际行走，没有预先创建或传送。

直接删掉三项筛选的裸局部版本虽然通过初始 46 与静态死亡补位，过程死亡仅 6/30：死亡后的贪心临停布局可能无法执行兼容重分配。交付的独立候选因此保留局部失败后的原子回滚，再尝试保守布局，共用原有预算；最终才通过该版本的 70 轮静态死亡与 30 组过程死亡。两种版本分开存档，不能省略回退后沿用通过报告。

这些时间和通过数字属于 B2 底座的独立对照。B3 已移植局部优先与保守回退机制，其最终时间和通过数量以本轮冻结报告为准。

## B3 的机制与验收身份

最终完整 Domain 指纹为 `074502ae2c712a92094d4b851c451afba25ac300c063f78e28b605b767a72c2f`，共 15 份 C# 源码。纯导航模型编译其中 11 份 Configuration/Navigation 文件，并另链接地图解析器和检查器；不能把完整源快照范围写成每个模型都执行全部业务。本页不替代本轮 56、33、70、76 等矩阵和 Unity 的对应报告。

B3 使用全部合法攻击候选、外沿优先排序、空位链、局部多门与完整事务回退。布局分析和各段搜索共用 200000 展开预算，失败不推进时钟、移动身体或保留虚假目标/交通修订。已经在场的恢复事务允许重新开门，不再把所有未来空位的永久静态通路当作本次恢复义务；当前实际参与者的路线仍须完整预约。

大混合阵容还需要稳定分配：84 名长射程单位占满 84 个攻击格后，短射程新人不能让总攻击数变成 85；可行目标是让一名选择更多的长射程单位退到合法候场，得到 83 长程 + 1 短程。反向顺序中，已建立的短射程攻击份额在死亡补兵后也应恢复。

因此 profile 只记录历史真实攻击人数，恢复目标按当前活体需求限幅；活体归零重置，有合法目标的新 profile 至少争取 1 个入攻位置。只有攻击候选集合严格包含请求者的占有者可为这种恢复退让；比较排除不能作为最终 Goal 的出生入口。历史份额不是硬上限，普通空攻击位仍可填充，也不为已经死亡的单位空留位置。这些规则与需求缩减、归零、新波次、过程死亡和完整失败输入重放一起验收。

## 分支与复现

| 分支 | 内容 |
| --- | --- |
| [`exp/navigation-feasibility-witness`](https://github.com/Novorizon/GameStarter/tree/exp/navigation-feasibility-witness/Verification~/NavigationFeasibility) | 原72、Forest饱和、晚到异质46、真实波次最大兼容攻击人数的独立见证 |
| [`AdmissionTransactionAudit`](https://github.com/Novorizon/GameStarter/tree/exp/navigation-feasibility-witness/Verification~/AdmissionTransactionAudit) | 预算分类、失败缓存、精确回滚、实际释放唤醒与事务隔离审计 |
| [`experiment/navigation-refined-grid`](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-refined-grid/Experiment~/RefinedGrid) | 粗细网格对照、早期失败、协同候选源快照及四种真实请求顺序 |
| [`experiment/navigation-orca`](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-orca/Experiments~/OrcaComparison) | 固定官方 RVO2-CS 源码、许可证、控制器对照及20组矩阵 |
| [`experiment/navigation-local-yield`](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-local-yield/Experiment~/LocalYield) | `23fca5c` 独立局部临停、被拒绝的裸局部版本及完整回退证据 |
| [`experiment/navigation-b7-performance`](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-b7-performance) | 仅静态几何连通区缓存；保持逐 tick 物理轨迹、搜索序列与事务结果 |
| [`experiment/navigation-all-legal-slots`](https://github.com/Novorizon/GameStarter/tree/experiment/navigation-all-legal-slots/Experiment~/AllLegalSlots) | 合法内侧攻击位、多门、兼容目标重分配与后续候选机制 |
| [`test/navigation-completeness-challenge`](https://github.com/Novorizon/GameStarter/tree/test/navigation-completeness-challenge/Verification~/TowerDefenseNavigationChallenges) | 不能靠拒绝请求完成的严格挑战、队列/身份守恒及独立容量证书 |
| [`test/navigation-gate-lifecycle`](https://github.com/Novorizon/GameStarter/tree/test/navigation-gate-lifecycle/Verification~/NavigationGateLifecycle) | 真实多门途中死亡、补兵、份额交换与再次补位审计 |

各实验记录对应源码、输入与二进制指纹。使用其他提交的通过报告，或仅改 seed 标签而未改变输入，都不能算新覆盖。正式方案保持整数领域、明确预约、实际攻击状态与战斗生命周期；替代分支提供交叉验证，不直接增加游戏运行依赖。
