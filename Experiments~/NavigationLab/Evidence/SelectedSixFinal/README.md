# 导航候选对照

有限共同输入的冻结 .NET 模型证据汇总；不代表全面普适、商业游戏原版源码、Unity/IL2CPP/设备验收。

证据核对：Core/Oracle DLL 字节一致；Runner 实际编译源、csproj、清单记录的编译属性与 SDK 记录的规范身份一致；协议、套件、案例集合及同名输入一致；每份原始 DLL 均独立与冻结构建绑定，所有 PASS 已完成新实例回放。

Runner DLL 的原始 SHA 可以不同：生成 AssemblyInfo 的唯一 RepositoryRoot 属性指向各自 snapshot，规范化仅限该精确属性，其余源码字节不变。该根路径会被运行时读取，故还必须验证各冻结源与逐例输入一致；这不等同于 DLL 字节相同或无条件语义等价。

只列 PASS / FAIL / UNSUPPORTED；InvalidInput 或证据不完整会拒绝生成。未进行性能排名。

| 标签 / 算法 | PASS | FAIL | UNSUPPORTED | 隔离及暂停 |
| --- | ---: | ---: | ---: | --- |
| A / A-Sipp | 41 | 0 | 8 | 通过 |
| B0 / B0-Orca | 28 | 21 | 0 | 通过 |
| B1 / B1-OrcaYield | 45 | 4 | 0 | 通过 |
| C33 / C-SupCom2-FlowTiles | 46 | 3 | 0 | 通过 |
| D12 / D12-aoe4-with-route-progress-and-slot-exchange | 26 | 23 | 0 | 通过 |
| E / E4-Warcraft3-Inspired-RefinedConnections | 37 | 12 | 0 | 通过 |

共同 Runner 编译输入身份：`e17b632f65d9b62990907f10726b84af8d6fa3747c1aa6d20bc0dbd260195544`。各原始 Runner DLL hash 与根路径例外明细见 comparison.json。

每格格式为“状态 · 活体数 · 待入队数 · 攻击数”。容量/不可达场景的 PASS 按其原始验收条件判断，并不表示所有请求都已入场。

| 案例 | A | B0 | B1 | C33 | D12 | E |
| --- | --- | --- | --- | --- | --- | --- |
| open-arena | PASS · 活48 · 队0 · 攻16 | FAIL · 活48 · 队0 · 攻15 | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 |
| mixed-speed-approach | PASS · 活64 · 队0 · 攻16 | PASS · 活64 · 队0 · 攻16 | PASS · 活64 · 队0 · 攻16 | PASS · 活64 · 队0 · 攻16 | PASS · 活64 · 队0 · 攻16 | PASS · 活64 · 队0 · 攻16 |
| large-radius-turning-clearance | PASS · 活48 · 队0 · 攻12 | FAIL · 活48 · 队0 · 攻8 | PASS · 活48 · 队0 · 攻12 | PASS · 活48 · 队0 · 攻12 | FAIL · 活48 · 队0 · 攻12 | FAIL · 活48 · 队0 · 攻9 |
| single-cell-throat | PASS · 活48 · 队0 · 攻16 | FAIL · 活48 · 队0 · 攻15 | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 | FAIL · 活48 · 队0 · 攻3 | FAIL · 活48 · 队0 · 攻9 |
| u-obstacle | PASS · 活56 · 队0 · 攻16 | PASS · 活56 · 队0 · 攻16 | PASS · 活56 · 队0 · 攻16 | PASS · 活56 · 队0 · 攻16 | FAIL · 活56 · 队0 · 攻14 | PASS · 活56 · 队0 · 攻16 |
| right-angle-corridor | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| cross-junction-parking-lanes | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 | FAIL · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 |
| cross-junction-original-4m | PASS · 活72 · 队0 · 攻12 | FAIL · 活72 · 队0 · 攻11 | PASS · 活72 · 队0 · 攻12 | PASS · 活72 · 队0 · 攻12 | FAIL · 活72 · 队0 · 攻12 | PASS · 活72 · 队0 · 攻12 |
| cross-junction-original-5m | PASS · 活72 · 队0 · 攻16 | FAIL · 活72 · 队0 · 攻16 | PASS · 活72 · 队0 · 攻16 | PASS · 活72 · 队0 · 攻16 | PASS · 活72 · 队0 · 攻16 | PASS · 活72 · 队0 · 攻16 |
| shared-spawn-parking-lanes | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 | PASS · 活72 · 队0 · 攻6 |
| shared-spawn-original-5m | PASS · 活72 · 队0 · 攻16 | FAIL · 活72 · 队0 · 攻15 | PASS · 活72 · 队0 · 攻16 | PASS · 活72 · 队0 · 攻16 | FAIL · 活72 · 队0 · 攻16 | PASS · 活72 · 队0 · 攻16 |
| three-hundred-units-parking-lanes | PASS · 活300 · 队0 · 攻6 | FAIL · 活300 · 队0 · 攻6 | PASS · 活300 · 队0 · 攻6 | PASS · 活300 · 队0 · 攻6 | FAIL · 活300 · 队0 · 攻6 | PASS · 活300 · 队0 · 攻6 |
| three-hundred-units-original-12m | PASS · 活300 · 队0 · 攻16 | FAIL · 活300 · 队0 · 攻16 | PASS · 活300 · 队0 · 攻16 | PASS · 活300 · 队0 · 攻16 | FAIL · 活300 · 队0 · 攻15 | FAIL · 活300 · 队0 · 攻16 |
| forest-single-small | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 |
| forest-single-medium | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 |
| forest-single-large | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 |
| forest-wave-1 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 |
| forest-wave-2 | PASS · 活16 · 队0 · 攻13 | PASS · 活16 · 队0 · 攻13 | PASS · 活16 · 队0 · 攻13 | PASS · 活16 · 队0 · 攻13 | PASS · 活16 · 队0 · 攻13 | PASS · 活16 · 队0 · 攻13 |
| forest-wave-3 | PASS · 活18 · 队0 · 攻14 | PASS · 活18 · 队0 · 攻14 | PASS · 活18 · 队0 · 攻14 | PASS · 活18 · 队0 · 攻14 | FAIL · 活18 · 队0 · 攻11 | FAIL · 活18 · 队0 · 攻14 |
| forest-wave-4 | PASS · 活25 · 队0 · 攻15 | PASS · 活25 · 队0 · 攻15 | PASS · 活25 · 队0 · 攻15 | PASS · 活25 · 队0 · 攻15 | FAIL · 活25 · 队0 · 攻13 | FAIL · 活25 · 队0 · 攻15 |
| forest-wave-5 | PASS · 活30 · 队0 · 攻15 | PASS · 活30 · 队0 · 攻15 | PASS · 活30 · 队0 · 攻15 | PASS · 活30 · 队0 · 攻15 | FAIL · 活30 · 队0 · 攻12 | FAIL · 活30 · 队0 · 攻11 |
| forest-wave-6 | PASS · 活29 · 队0 · 攻15 | PASS · 活29 · 队0 · 攻15 | PASS · 活29 · 队0 · 攻15 | PASS · 活29 · 队0 · 攻15 | FAIL · 活29 · 队0 · 攻13 | FAIL · 活29 · 队0 · 攻14 |
| forest-full44-large | PASS · 活44 · 队0 · 攻18 | FAIL · 活44 · 队0 · 攻7 | FAIL · 活44 · 队0 · 攻7 | FAIL · 活44 · 队0 · 攻17 | FAIL · 活44 · 队0 · 攻17 | PASS · 活44 · 队0 · 攻18 |
| forest-full45-medium | PASS · 活45 · 队0 · 攻12 | FAIL · 活45 · 队0 · 攻9 | PASS · 活45 · 队0 · 攻12 | FAIL · 活45 · 队0 · 攻12 | PASS · 活45 · 队0 · 攻12 | FAIL · 活45 · 队0 · 攻12 |
| forest-full44-two-late-small | PASS · 活46 · 队0 · 攻20 | FAIL · 活44 · 队2 · 攻8 | FAIL · 活44 · 队2 · 攻1 | PASS · 活46 · 队0 · 攻20 | FAIL · 活44 · 队2 · 攻17 | FAIL · 活46 · 队0 · 攻18 |
| static-unreachable-retains-pending | PASS · 活0 · 队12 · 攻0 | PASS · 活0 · 队12 · 攻0 | PASS · 活0 · 队12 · 攻0 | PASS · 活0 · 队12 · 攻0 | PASS · 活0 · 队12 · 攻0 | PASS · 活0 · 队12 · 攻0 |
| physical-and-endpoint-overflow-200 | PASS · 活39 · 队161 · 攻12 | FAIL · 活25 · 队175 · 攻6 | FAIL · 活39 · 队161 · 攻4 | PASS · 活39 · 队161 · 攻12 | FAIL · 活31 · 队169 · 攻12 | FAIL · 活18 · 队182 · 攻10 |
| capacity-death-refills-original-queue | PASS · 活39 · 队148 · 攻12 | FAIL · 活27 · 队173 · 攻6 | FAIL · 活39 · 队161 · 攻2 | PASS · 活39 · 队148 · 攻12 | FAIL · 活31 · 队169 · 攻12 | FAIL · 活18 · 队182 · 攻11 |
| moving-noncenter-death-and-reinforcement | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 |
| settled-batch-death-and-reinforcement | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 | PASS · 活48 · 队0 · 攻16 | FAIL · 活42 · 队6 · 攻10 | PASS · 活48 · 队0 · 攻16 |
| old-waiters-refill-without-newcomers | PASS · 活42 · 队0 · 攻16 | FAIL · 活42 · 队0 · 攻15 | PASS · 活42 · 队0 · 攻16 | PASS · 活42 · 队0 · 攻16 | FAIL · 活42 · 队0 · 攻10 | PASS · 活42 · 队0 · 攻16 |
| ranged84-before-short1-fairness | PASS · 活85 · 队0 · 攻84 | FAIL · 活84 · 队1 · 攻83 | PASS · 活85 · 队0 · 攻84 | FAIL · 活85 · 队0 · 攻82 | FAIL · 活84 · 队1 · 攻79 | FAIL · 活85 · 队0 · 攻84 |
| short16-before-ranged84-fairness | PASS · 活100 · 队0 · 攻84 | FAIL · 活100 · 队0 · 攻83 | PASS · 活100 · 队0 · 攻84 | PASS · 活100 · 队0 · 攻84 | FAIL · 活67 · 队33 · 攻56 | PASS · 活100 · 队0 · 攻84 |
| dynamic-route-block-and-open | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 |
| target-relocation-before-arrival | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 |
| target-relocation-after-settlement | UNSUPPORTED · 活0 · 队0 · 攻0 | FAIL · 活24 · 队0 · 攻15 | PASS · 活24 · 队0 · 攻16 | PASS · 活24 · 队0 · 攻16 | FAIL · 活24 · 队0 · 攻14 | PASS · 活24 · 队0 · 攻16 |
| opposing-flow-wide-arena | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活8 · 队0 · 攻2 | PASS · 活8 · 队0 · 攻2 | PASS · 活8 · 队0 · 攻2 | PASS · 活8 · 队0 · 攻2 | PASS · 活8 · 队0 · 攻2 |
| large-body-wide-approach | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 | PASS · 活1 · 队0 · 攻1 |
| large-body-sparse-group | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 | PASS · 活8 · 队0 · 攻8 |
| large-small-sparse-group | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活11 · 队0 · 攻11 | PASS · 活11 · 队0 · 攻11 | PASS · 活11 · 队0 · 攻11 | PASS · 活11 · 队0 · 攻11 | PASS · 活11 · 队0 · 攻11 |
| fast-per-tick-approach | UNSUPPORTED · 活0 · 队0 · 攻0 | PASS · 活4 · 队0 · 攻4 | PASS · 活4 · 队0 · 攻4 | PASS · 活4 · 队0 · 攻4 | PASS · 活4 · 队0 · 攻4 | PASS · 活4 · 队0 · 攻4 |
| random-multigate-maze-11 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-97 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-713 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-20261111 | PASS · 活32 · 队0 · 攻16 | FAIL · 活32 · 队0 · 攻15 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-1701 | PASS · 活32 · 队0 · 攻16 | FAIL · 活32 · 队0 · 攻15 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| random-multigate-maze-991 | PASS · 活32 · 队0 · 攻16 | FAIL · 活32 · 队0 · 攻14 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 | PASS · 活32 · 队0 · 攻16 |
| large-portal-grid-128 | PASS · 活128 · 队0 · 攻128 | PASS · 活128 · 队0 · 攻128 | PASS · 活128 · 队0 · 攻128 | PASS · 活128 · 队0 · 攻128 | FAIL · 活128 · 队0 · 攻127 | PASS · 活128 · 队0 · 攻128 |
| large-portal-grid-256 | PASS · 活256 · 队0 · 攻256 | FAIL · 活256 · 队0 · 攻249 | PASS · 活256 · 队0 · 攻256 | PASS · 活256 · 队0 · 攻256 | FAIL · 活256 · 队0 · 攻233 | PASS · 活256 · 队0 · 攻256 |

## 失败与不支持

- **A / dynamic-route-block-and-open / Unsupported**：Factory does not declare DynamicTerrain
- **A / target-relocation-before-arrival / Unsupported**：Factory does not declare MovingTarget
- **A / target-relocation-after-settlement / Unsupported**：Factory does not declare MovingTarget
- **A / opposing-flow-wide-arena / Unsupported**：Factory does not declare MultipleTargets, AssignedGoals
- **A / large-body-wide-approach / Unsupported**：Factory does not declare LargeBodies
- **A / large-body-sparse-group / Unsupported**：Factory does not declare LargeBodies
- **A / large-small-sparse-group / Unsupported**：Factory does not declare LargeBodies
- **A / fast-per-tick-approach / Unsupported**：Factory does not declare FastPerTick
- **B0 / open-arena / Failed**：Deadline: alive=48, pending=0, moving=1, attack=15, failure=None
- **B0 / large-radius-turning-clearance / Failed**：Deadline: alive=48, pending=0, moving=5, attack=8, failure=None
- **B0 / single-cell-throat / Failed**：Deadline: alive=48, pending=0, moving=1, attack=15, failure=None
- **B0 / cross-junction-original-4m / Failed**：Deadline: alive=72, pending=0, moving=1, attack=11, failure=None
- **B0 / cross-junction-original-5m / Failed**：Deadline: alive=72, pending=0, moving=1, attack=16, failure=None
- **B0 / shared-spawn-original-5m / Failed**：Deadline: alive=72, pending=0, moving=2, attack=15, failure=None
- **B0 / three-hundred-units-parking-lanes / Failed**：Deadline: alive=300, pending=0, moving=1, attack=6, failure=None
- **B0 / three-hundred-units-original-12m / Failed**：Deadline: alive=300, pending=0, moving=13, attack=16, failure=None
- **B0 / forest-full44-large / Failed**：Deadline: alive=44, pending=0, moving=34, attack=7, failure=None
- **B0 / forest-full45-medium / Failed**：Deadline: alive=45, pending=0, moving=12, attack=9, failure=None
- **B0 / forest-full44-two-late-small / Failed**：Initial crowd did not physically settle before late arrivals.
- **B0 / physical-and-endpoint-overflow-200 / Failed**：Deadline: alive=25, pending=175, moving=14, attack=6, failure=SpawnOccupied
- **B0 / capacity-death-refills-original-queue / Failed**：Deadline: alive=27, pending=173, moving=17, attack=6, failure=SpawnOccupied
- **B0 / old-waiters-refill-without-newcomers / Failed**：Deadline: alive=42, pending=0, moving=1, attack=15, failure=None
- **B0 / ranged84-before-short1-fairness / Failed**：Initial crowd did not physically settle before late arrivals.
- **B0 / short16-before-ranged84-fairness / Failed**：Deadline: alive=100, pending=0, moving=1, attack=83, failure=None
- **B0 / target-relocation-after-settlement / Failed**：Deadline: alive=24, pending=0, moving=1, attack=15, failure=None
- **B0 / random-multigate-maze-20261111 / Failed**：Deadline: alive=32, pending=0, moving=1, attack=15, failure=None
- **B0 / random-multigate-maze-1701 / Failed**：Deadline: alive=32, pending=0, moving=1, attack=15, failure=None
- **B0 / random-multigate-maze-991 / Failed**：Deadline: alive=32, pending=0, moving=2, attack=14, failure=None
- **B0 / large-portal-grid-256 / Failed**：Deadline: alive=256, pending=0, moving=7, attack=249, failure=None
- **B1 / forest-full44-large / Failed**：Deadline: alive=44, pending=0, moving=35, attack=7, failure=None
- **B1 / forest-full44-two-late-small / Failed**：Initial crowd did not physically settle before late arrivals.
- **B1 / physical-and-endpoint-overflow-200 / Failed**：Deadline: alive=39, pending=161, moving=30, attack=4, failure=SpawnOccupied
- **B1 / capacity-death-refills-original-queue / Failed**：Deadline: alive=39, pending=161, moving=34, attack=2, failure=SpawnOccupied
- **C33 / forest-full44-large / Failed**：Deadline: alive=44, pending=0, moving=2, attack=17, failure=None
- **C33 / forest-full45-medium / Failed**：Deadline: alive=45, pending=0, moving=2, attack=12, failure=None
- **C33 / ranged84-before-short1-fairness / Failed**：Deadline: alive=85, pending=0, moving=2, attack=82, failure=None
- **D12 / large-radius-turning-clearance / Failed**：Deadline: alive=48, pending=0, moving=1, attack=12, failure=None
- **D12 / single-cell-throat / Failed**：Deadline: alive=48, pending=0, moving=45, attack=3, failure=None
- **D12 / u-obstacle / Failed**：Deadline: alive=56, pending=0, moving=2, attack=14, failure=None
- **D12 / cross-junction-parking-lanes / Failed**：Deadline: alive=72, pending=0, moving=5, attack=6, failure=None
- **D12 / cross-junction-original-4m / Failed**：Deadline: alive=72, pending=0, moving=2, attack=12, failure=None
- **D12 / shared-spawn-original-5m / Failed**：Deadline: alive=72, pending=0, moving=1, attack=16, failure=None
- **D12 / three-hundred-units-parking-lanes / Failed**：Deadline: alive=300, pending=0, moving=1, attack=6, failure=None
- **D12 / three-hundred-units-original-12m / Failed**：Deadline: alive=300, pending=0, moving=71, attack=15, failure=None
- **D12 / forest-wave-3 / Failed**：Deadline: alive=18, pending=0, moving=4, attack=11, failure=None
- **D12 / forest-wave-4 / Failed**：Deadline: alive=25, pending=0, moving=5, attack=13, failure=None
- **D12 / forest-wave-5 / Failed**：Deadline: alive=30, pending=0, moving=9, attack=12, failure=None
- **D12 / forest-wave-6 / Failed**：Deadline: alive=29, pending=0, moving=3, attack=13, failure=None
- **D12 / forest-full44-large / Failed**：Deadline: alive=44, pending=0, moving=27, attack=17, failure=None
- **D12 / forest-full44-two-late-small / Failed**：Initial crowd did not physically settle before late arrivals.
- **D12 / physical-and-endpoint-overflow-200 / Failed**：Deadline: alive=31, pending=169, moving=2, attack=12, failure=StaticGoalUnreachable
- **D12 / capacity-death-refills-original-queue / Failed**：Deadline: alive=31, pending=169, moving=2, attack=12, failure=StaticGoalUnreachable
- **D12 / settled-batch-death-and-reinforcement / Failed**：Deadline: alive=42, pending=6, moving=6, attack=10, failure=StaticGoalUnreachable
- **D12 / old-waiters-refill-without-newcomers / Failed**：Deadline: alive=42, pending=0, moving=6, attack=10, failure=None
- **D12 / ranged84-before-short1-fairness / Failed**：Initial crowd did not physically settle before late arrivals.
- **D12 / short16-before-ranged84-fairness / Failed**：Deadline: alive=67, pending=33, moving=11, attack=56, failure=SpawnOccupied
- **D12 / target-relocation-after-settlement / Failed**：Deadline: alive=24, pending=0, moving=2, attack=14, failure=None
- **D12 / large-portal-grid-128 / Failed**：Deadline: alive=128, pending=0, moving=1, attack=127, failure=None
- **D12 / large-portal-grid-256 / Failed**：Deadline: alive=256, pending=0, moving=23, attack=233, failure=None
- **E / large-radius-turning-clearance / Failed**：Deadline: alive=48, pending=0, moving=10, attack=9, failure=None
- **E / single-cell-throat / Failed**：Deadline: alive=48, pending=0, moving=39, attack=9, failure=None
- **E / three-hundred-units-original-12m / Failed**：Deadline: alive=300, pending=0, moving=2, attack=16, failure=None
- **E / forest-wave-3 / Failed**：Deadline: alive=18, pending=0, moving=2, attack=14, failure=None
- **E / forest-wave-4 / Failed**：Deadline: alive=25, pending=0, moving=5, attack=15, failure=None
- **E / forest-wave-5 / Failed**：Deadline: alive=30, pending=0, moving=10, attack=11, failure=None
- **E / forest-wave-6 / Failed**：Deadline: alive=29, pending=0, moving=7, attack=14, failure=None
- **E / forest-full45-medium / Failed**：Deadline: alive=45, pending=0, moving=3, attack=12, failure=None
- **E / forest-full44-two-late-small / Failed**：Deadline: alive=46, pending=0, moving=2, attack=18, failure=None
- **E / physical-and-endpoint-overflow-200 / Failed**：Deadline: alive=18, pending=182, moving=3, attack=10, failure=SpawnOccupied
- **E / capacity-death-refills-original-queue / Failed**：Deadline: alive=18, pending=182, moving=2, attack=11, failure=SpawnOccupied
- **E / ranged84-before-short1-fairness / Failed**：Deadline: alive=85, pending=0, moving=0, attack=84, failure=None

## 最终态图

图按实际出生半径绘制身体，只应用 Commands 中已执行的环境变更。图不替代连续碰撞、守恒和攻击条件验收。

- [A / forest-full44-large](diagrams/A-559aead0--forest-full44-large.svg)
- [B0 / forest-full44-large](diagrams/B0-05816a15--forest-full44-large.svg)
- [B1 / forest-full44-large](diagrams/B1-5b950e77--forest-full44-large.svg)
- [C33 / forest-full44-large](diagrams/C33-a39ed58c--forest-full44-large.svg)
- [D12 / forest-full44-large](diagrams/D12-8c53e7b8--forest-full44-large.svg)
- [E / forest-full44-large](diagrams/E-a9f51566--forest-full44-large.svg)
- [A / forest-full45-medium](diagrams/A-559aead0--forest-full45-medium.svg)
- [B0 / forest-full45-medium](diagrams/B0-05816a15--forest-full45-medium.svg)
- [B1 / forest-full45-medium](diagrams/B1-5b950e77--forest-full45-medium.svg)
- [C33 / forest-full45-medium](diagrams/C33-a39ed58c--forest-full45-medium.svg)
- [D12 / forest-full45-medium](diagrams/D12-8c53e7b8--forest-full45-medium.svg)
- [E / forest-full45-medium](diagrams/E-a9f51566--forest-full45-medium.svg)
- [A / forest-full44-two-late-small](diagrams/A-559aead0--forest-full44-two-late-small.svg)
- [B0 / forest-full44-two-late-small](diagrams/B0-05816a15--forest-full44-two-late-small.svg)
- [B1 / forest-full44-two-late-small](diagrams/B1-5b950e77--forest-full44-two-late-small.svg)
- [C33 / forest-full44-two-late-small](diagrams/C33-a39ed58c--forest-full44-two-late-small.svg)
- [D12 / forest-full44-two-late-small](diagrams/D12-8c53e7b8--forest-full44-two-late-small.svg)
- [E / forest-full44-two-late-small](diagrams/E-a9f51566--forest-full44-two-late-small.svg)
- [A / ranged84-before-short1-fairness](diagrams/A-559aead0--ranged84-before-short1-fairness.svg)
- [B0 / ranged84-before-short1-fairness](diagrams/B0-05816a15--ranged84-before-short1-fairness.svg)
- [B1 / ranged84-before-short1-fairness](diagrams/B1-5b950e77--ranged84-before-short1-fairness.svg)
- [C33 / ranged84-before-short1-fairness](diagrams/C33-a39ed58c--ranged84-before-short1-fairness.svg)
- [D12 / ranged84-before-short1-fairness](diagrams/D12-8c53e7b8--ranged84-before-short1-fairness.svg)
- [E / ranged84-before-short1-fairness](diagrams/E-a9f51566--ranged84-before-short1-fairness.svg)

## 来源与复现

comparison.json 保留逐例计数、完整最终态、输入/原摘要/完整案例/trace hash、factory 身份及候选构建来源。inputs/ 是核验后的原始输入副本。

原始大体积 trace 不复制。复现仍需原报告目录、对应冻结构建清单及其源文件/DLL；下列 SHA 可核对本次汇总使用的确切摘要。计时含宿主机调度影响，仅作诊断。

- **A**：`/workspace/navigation-comparison/results/a-v2-final-stress/summary.json`；SHA-256 `cb58d6840a2196b91cfcc3f9bae8de945958f2c9c981e76a0c286e6a845a9889`；factory `NavigationLab.Candidates.ASipp.SippFactory`。
- **B0**：`/workspace/navigation-comparison/results/b0-v2-final-stress/summary.json`；SHA-256 `1b0462c69c48bd3508fe0e6bf426fbaf6f8c7795e6d979fcff0ca6bdc653c460`；factory `NavigationLab.Candidates.BOrca.OrcaFactory`。
- **B1**：`/workspace/navigation-comparison/results/b1-v2-final-stress/summary.json`；SHA-256 `362909c45de54cb3888392e1f5b48c06e4e02fffc71aaa12f43354e74b70c96f`；factory `NavigationLab.Candidates.BOrca.OrcaYieldFactory`。
- **C33**：`/workspace/navigation-comparison/results/c-final2-v2-stress49/summary.json`；SHA-256 `c36c045fe4d6d62c863a34e66a1288e6a8ac7479e916bc78cde76acfb0cd4649`；factory `NavigationLab.CSupCom2.Factory`。
- **D12**：`/workspace/navigation-comparison/results/d-final-stress/summary.json`；SHA-256 `a1bdc5212408003d7bb13c712b848faa24b335f82e2f44b14c173b886ec81248`；factory `NavigationLab.DAoe4.Aoe4ProgressExchangeFactory`。
- **E**：`/workspace/navigation-comparison/results/e-final-goal-safe-stress49/summary.json`；SHA-256 `351a856caf5cf31c9f0d422c94a7e5aaac8a58cfc469187a6de9a5b67cb20b49`；factory `NavigationLab.Candidates.EWarcraft3.WarcraftRefinedFactory`。
