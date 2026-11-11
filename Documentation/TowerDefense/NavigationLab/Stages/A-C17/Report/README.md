# 导航候选对照

有限共同输入的冻结 .NET 模型证据汇总；不代表全面普适、商业游戏原版源码、Unity/IL2CPP/设备验收。

证据核对：Core/Oracle DLL 字节一致；Runner 实际编译源、csproj、清单记录的编译属性与 SDK 记录的规范身份一致；协议、套件、案例集合及同名输入一致；每份原始 DLL 均独立与冻结构建绑定，所有 PASS 已完成新实例回放。

Runner DLL 的原始 SHA 可以不同：生成 AssemblyInfo 的唯一 RepositoryRoot 属性指向各自 snapshot，规范化仅限该精确属性，其余源码字节不变。该根路径会被运行时读取，故还必须验证各冻结源与逐例输入一致；这不等同于 DLL 字节相同或无条件语义等价。

只列 PASS / FAIL / UNSUPPORTED；InvalidInput 或证据不完整会拒绝生成。未进行性能排名。

| 标签 / 算法 | PASS | FAIL | UNSUPPORTED | 隔离及暂停 |
| --- | ---: | ---: | ---: | --- |
| A / A-Sipp | 41 | 0 | 8 | 通过 |
| C17 / C-SupCom2-FlowTiles | 47 | 2 | 0 | 通过 |

共同 Runner 编译输入身份：`e17b632f65d9b62990907f10726b84af8d6fa3747c1aa6d20bc0dbd260195544`。各原始 Runner DLL hash 与根路径例外明细见 comparison.json。

每格格式为“状态 · 活体数 · 待入队数 · 攻击数”。容量/不可达场景的 PASS 按其原始验收条件判断，并不表示所有请求都已入场。

| 案例 | A | C17 |
| --- | --- | --- |
| open-arena | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 |
| mixed-speed-approach | PASS · 活64 · 队0 · 攻16 | PASS · 活64 · 队0 · 攻16 |
| large-radius-turning-clearance | PASS · 活48 · 队0 · 攻12 | PASS · 活48 · 队0 · 攻12 |
| single-cell-throat | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 |
| u-obstacle | PASS · 活56 · 队0 · 攻16 | PASS · 活56 · 队0 · 攻16 |
| right-angle-corridor | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| cross-junction-parking-lanes | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 |
| cross-junction-original-4m | PASS · 活72 · 队0 · 攻12 | PASS · 活72 · 队0 · 攻12 |
| cross-junction-original-5m | PASS · 活72 · 队0 · 攻16 | PASS · 活72 · 队0 · 攻16 |
| shared-spawn-parking-lanes | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 |
| shared-spawn-original-5m | PASS · 活72 · 队0 · 攻16 | PASS · 活72 · 队0 · 攻16 |
| three-hundred-units-parking-lanes | PASS · 活300 · 队0 · 攻6 | PASS · 活300 · 队0 · 攻6 |
| three-hundred-units-original-12m | PASS · 活300 · 队0 · 攻16 | PASS · 活300 · 队0 · 攻16 |
| forest-single-small | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 |
| forest-single-medium | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 |
| forest-single-large | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 |
| forest-wave-1 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 |
| forest-wave-2 | PASS · 活16 · 队0 · 攻13 | PASS · 活16 · 队0 · 攻13 |
| forest-wave-3 | PASS · 活18 · 队0 · 攻14 | PASS · 活18 · 队0 · 攻14 |
| forest-wave-4 | PASS · 活25 · 队0 · 攻15 | FAIL · 活25 · 队0 · 攻14 |
| forest-wave-5 | PASS · 活30 · 队0 · 攻15 | PASS · 活30 · 队0 · 攻15 |
| forest-wave-6 | PASS · 活29 · 队0 · 攻15 | PASS · 活29 · 队0 · 攻15 |
| forest-full44-large | PASS · 活44 · 队0 · 攻18 | PASS · 活44 · 队0 · 攻18 |
| forest-full45-medium | PASS · 活45 · 队0 · 攻12 | PASS · 活45 · 队0 · 攻12 |
| forest-full44-two-late-small | PASS · 活46 · 队0 · 攻20 | PASS · 活46 · 队0 · 攻20 |
| static-unreachable-retains-pending | PASS · 活0 · 队12 · 攻0 | PASS · 活0 · 队12 · 攻0 |
| physical-and-endpoint-overflow-200 | PASS · 活39 · 队161 · 攻12 | PASS · 活39 · 队161 · 攻12 |
| capacity-death-refills-original-queue | PASS · 活39 · 队148 · 攻12 | FAIL · 活35 · 队152 · 攻10 |
| moving-noncenter-death-and-reinforcement | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 |
| settled-batch-death-and-reinforcement | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 |
| old-waiters-refill-without-newcomers | PASS · 活42 · 队0 · 攻16 | PASS · 活42 · 队0 · 攻16 |
| ranged84-before-short1-fairness | PASS · 活85 · 队0 · 攻84 | PASS · 活85 · 队0 · 攻84 |
| short16-before-ranged84-fairness | PASS · 活100 · 队0 · 攻84 | PASS · 活100 · 队0 · 攻84 |
| dynamic-route-block-and-open | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活24 · 队0 · 攻16 |
| target-relocation-before-arrival | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活24 · 队0 · 攻16 |
| target-relocation-after-settlement | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活24 · 队0 · 攻16 |
| opposing-flow-wide-arena | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活8 · 队0 · 攻2 |
| large-body-wide-approach | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活1 · 队0 · 攻1 |
| large-body-sparse-group | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活8 · 队0 · 攻8 |
| large-small-sparse-group | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活11 · 队0 · 攻11 |
| fast-per-tick-approach | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活4 · 队0 · 攻4 |
| random-multigate-maze-11 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-97 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-713 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-20261111 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-1701 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-991 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| large-portal-grid-128 | PASS · 活128 · 队0 · 攻128 | PASS · 活128 · 队0 · 攻128 |
| large-portal-grid-256 | PASS · 活256 · 队0 · 攻256 | PASS · 活256 · 队0 · 攻256 |

## 失败与不支持

- **A / dynamic-route-block-and-open / Unsupported**：Factory does not declare DynamicTerrain
- **A / target-relocation-before-arrival / Unsupported**：Factory does not declare MovingTarget
- **A / target-relocation-after-settlement / Unsupported**：Factory does not declare MovingTarget
- **A / opposing-flow-wide-arena / Unsupported**：Factory does not declare MultipleTargets, AssignedGoals
- **A / large-body-wide-approach / Unsupported**：Factory does not declare LargeBodies
- **A / large-body-sparse-group / Unsupported**：Factory does not declare LargeBodies
- **A / large-small-sparse-group / Unsupported**：Factory does not declare LargeBodies
- **A / fast-per-tick-approach / Unsupported**：Factory does not declare FastPerTick
- **C17 / forest-wave-4 / Failed**：Deadline: alive=25, pending=0, moving=1, attack=14, failure=None
- **C17 / capacity-death-refills-original-queue / Failed**：Deadline: alive=35, pending=152, moving=2, attack=10, failure=SpawnOccupiedOrUnreachable

## 最终态图

图按实际出生半径绘制身体，只应用 Commands 中已执行的环境变更。图不替代连续碰撞、守恒和攻击条件验收。

- [A / forest-wave-4](diagrams/A-559aead0--forest-wave-4.svg)
- [C17 / forest-wave-4](diagrams/C17-aa482ed1--forest-wave-4.svg)
- [A / capacity-death-refills-original-queue](diagrams/A-559aead0--capacity-death-refills-original-queue.svg)
- [C17 / capacity-death-refills-original-queue](diagrams/C17-aa482ed1--capacity-death-refills-original-queue.svg)
- [A / forest-full44-large](diagrams/A-559aead0--forest-full44-large.svg)
- [C17 / forest-full44-large](diagrams/C17-aa482ed1--forest-full44-large.svg)

## 来源与复现

comparison.json 保留逐例计数、完整最终态、输入/原摘要/完整案例/trace hash、factory 身份及候选构建来源。inputs/ 是核验后的原始输入副本。

原始大体积 trace 不复制。复现仍需原报告目录、对应冻结构建清单及其源文件/DLL；下列 SHA 可核对本次汇总使用的确切摘要。计时含宿主机调度影响，仅作诊断。

- **A**：`/workspace/navigation-comparison/results/a-v2-final-stress/summary.json`；SHA-256 `cb58d6840a2196b91cfcc3f9bae8de945958f2c9c981e76a0c286e6a845a9889`；factory `NavigationLab.Candidates.ASipp.SippFactory`。
- **C17**：`/workspace/navigation-comparison/results/c-final-v2-stress49/summary.json`；SHA-256 `dffeba83d503fd31448fc74914bca7c7b77d1171fcd6d3b71b77b74bb3f14e0d`；factory `NavigationLab.CSupCom2.Factory`。
