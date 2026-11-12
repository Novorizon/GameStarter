# GameStarter Windows Unity 导航测试

2026-11-12（北京时间） · Unity 2022.3.62f1 · Windows Direct3D 11 · `8e3ac68c536d9543b74edb13ce0b65b7e5d75dfe`

原仓库位于 `experiment/navigation-unity-playground`。矩阵使用同提交的独立工作树和临时驱动，调用原生 Unity 菜单和原窗口使用的控制器。每次开始均使用交付版的哈希验证。7 个 DLL 和 49 个输入的磁盘 SHA-256 全部匹配。

## Play 入口与操作

- 菜单 `Tools/GameStarter/Navigation Playground` 由 Unity `EditorApplication.ExecuteMenuItem` 调用，返回 `True`，找到 1 个窗口。桌面截屏超时，菜单调用及运行画面改由 Unity 本机记录。
- 交付版 Smoke 结果为 **Passed**。六方案均通过 `forest-single-medium`。暂停、单步 50ms、恢复、停止、重新开始、相机切换、退出并再次进入 Play 均执行。单步快照从 4464 到 4474，正好 10 个 5ms 物理步。
- A 对 `dynamic-route-block-and-open` 返回 **Unsupported**（DynamicTerrain）；控制检查主动停止时返回 **Stopped**。
- Windows 编辑器实际 Play 的专用场景是 `Assets/NavigationPlayground/Scenes/NavigationPlayground.unity`。测试范围包括下列 17 个输入、六个单只起跑检查，以及一项动态障碍能力声明检查。

[Smoke 日志](smoke-editor.log) · [Smoke 结构化报告](smoke.json) · [Unity 相机截图](smoke.png)

## 无塔场景矩阵

**102 次矩阵运行** · Passed 72 / Failed 29 / Unsupported 1 / Stopped 0。另有 8 次目标倍速 12× 的复测，8/8 次在终态、逻辑刻数与计数上和首轮一致。首轮目标倍速为 128×；底层仍按 5ms 步进并执行连续校验，复杂场景实际倍速受计算量限制。

P = Passed，F = Failed，U = Unsupported，S = Stopped。每格数字为终态存活 / 攻击 / 等待 / 移动。

| 输入 | A | B0 | B1 | C33 | D12 | E |
| --- | --- | --- | --- | --- | --- | --- |
| `forest-wave-1` | P 8/8/0/0 | P 8/8/0/0 | P 8/8/0/0 | P 8/8/0/0 | P 8/8/0/0 | P 8/8/0/0 |
| `forest-wave-2` | P 16/13/3/0 | P 16/13/3/0 | P 16/13/3/0 | P 16/13/3/0 | P 16/13/3/0 | P 16/13/3/0 |
| `forest-wave-3` | P 18/14/4/0 | P 18/14/4/0 | P 18/14/4/0 | P 18/14/4/0 | F 18/13/4/1 | F 18/14/2/2 |
| `forest-wave-4` | P 25/15/10/0 | P 25/15/10/0 | P 25/15/10/0 | P 25/15/10/0 | F 25/13/7/5 | F 25/15/5/5 |
| `forest-wave-5` | P 30/15/15/0 | P 30/15/15/0 | P 30/15/15/0 | P 30/15/15/0 | F 30/9/6/15 | F 30/11/9/10 |
| `forest-wave-6` | P 29/15/14/0 | P 29/15/14/0 | P 29/15/14/0 | P 29/15/14/0 | F 29/14/14/1 | F 29/14/8/7 |
| `forest-full44-large` | P 44/18/26/0 | F 44/9/8/27 | F 44/6/5/33 | F 44/17/25/2 | F 44/17/0/27 | P 44/18/26/0 |
| `forest-full45-medium` | P 45/12/33/0 | F 45/10/29/6 | P 45/12/33/0 | F 45/12/31/2 | P 45/12/33/0 | F 45/12/30/3 |
| `forest-full44-two-late-small` | P 46/20/26/0 | F 44/9/6/29 | F 44/6/4/34 | P 46/20/26/0 | F 44/17/0/27 | F 46/18/26/2 |
| `single-cell-throat` | P 48/16/32/0 | P 48/16/32/0 | P 48/16/32/0 | P 48/16/32/0 | F 48/3/0/45 | F 48/9/0/39 |
| `cross-junction-original-4m` | P 72/12/60/0 | F 72/11/58/3 | P 72/12/60/0 | P 72/12/60/0 | F 72/12/58/2 | P 72/12/60/0 |
| `mixed-speed-approach` | P 64/16/48/0 | P 64/16/48/0 | P 64/16/48/0 | P 64/16/48/0 | P 64/16/48/0 | P 64/16/48/0 |
| `large-small-sparse-group` | U | P 11/11/0/0 | P 11/11/0/0 | P 11/11/0/0 | P 11/11/0/0 | P 11/11/0/0 |
| `moving-noncenter-death-and-reinforcement` | P 24/16/8/0 | F 24/15/8/1 | P 24/16/8/0 | P 24/16/8/0 | P 24/16/8/0 | P 24/16/8/0 |
| `capacity-death-refills-original-queue` | P 39/12/27/0 | F 33/4/8/21 | F 39/7/11/21 | P 39/12/27/0 | F 31/12/17/2 | F 18/11/5/2 |
| `old-waiters-refill-without-newcomers` | P 42/16/26/0 | P 42/16/26/0 | P 42/16/26/0 | P 42/16/26/0 | F 42/10/26/6 | P 42/16/26/0 |
| `static-unreachable-retains-pending` | P 0/0/0/0 | P 0/0/0/0 | P 0/0/0/0 | P 0/0/0/0 | P 0/0/0/0 | P 0/0/0/0 |

| 方案 | Passed | Failed | Unsupported | Stopped |
| --- | ---: | ---: | ---: | ---: |
| A | 16 | 0 | 1 | 0 |
| B0 | 11 | 6 | 0 | 0 |
| B1 | 14 | 3 | 0 | 0 |
| C33 | 15 | 2 | 0 | 0 |
| D12 | 7 | 10 | 0 | 0 |
| E | 9 | 8 | 0 | 0 |

## 异常与复现

在 Unity 中通过 Tools → GameStarter → Navigation Playground 选择表中的方案和输入，点击开始。保持无塔，等待窗口显示终态。下表包含首次报出的终态失败原因和最后快照。逻辑 tick 为 50ms。

- 26 个达到逻辑时限的失败在最后快照仍有移动单位。最后一帧不能区分持续振荡、绕圈、寻路缓慢或动态争用；检查点状态保存在 JSONL。
- 连续几何或速度校验报错的终态数为 0。各 Passed 用例已通过每个 5ms 步的持续校验，并满足输入规定的最终稳定期。
- 0 个快照出现额外账面核对异常。账面检查同时核对 `issued = admitted + pending` 和 `admitted = live + deaths`。Pending 含原定在后续逻辑刻出生的单位；CSV 的 `futureInitial` 单列这些计划。
- `forest-full44-two-late-small` 有 3 个结果停在初始 44 只的稳定检查。后续两只还未到计划出生时间。E 的该用例可对照 CSV 中的最终出生与移动数。
- 在 `static-unreachable-retains-pending` 负对照中，六方案都保留 12 个待出生请求，场上为零。
- 在 `capacity-death-refills-original-queue` 中，A 和 C33 各有 13 次移除，终态均为 39 只存活、12 只攻击。B0、B1、D12、E 的死亡事件均未触发，失败发生在补员阶段之前。
- 第三波额外轨迹复现两种未到位。D12 的 3 号单位截止前 50 秒采样位移为零；E 的 15 和 16 号单位在约半米宽的区域反复移动，累计位移约 37 米，目标格固定。 [第三波轨迹与双视角截图](wave3-trace.zh-CN.md)

| 方案 | 输入 | 逻辑 tick | 实际 / 预期存活 | 攻击 / 等待 / 移动 | 出生 / 待出生 / 死亡 | 原因 | 证据 |
| --- | --- | ---: | --- | --- | --- | --- | --- |
| D12 | `forest-wave-3` | 7000 | 18 / 18 | 13 / 4 / 1 | 18 / 0 / 0 | Simulation deadline: live=18, pending=0, moving=1, failure=None | [画面](matrix/D12-forest-wave-3-matrix-final.png) |
| E | `forest-wave-3` | 7000 | 18 / 18 | 14 / 2 / 2 | 18 / 0 / 0 | Simulation deadline: live=18, pending=0, moving=2, failure=None | [画面](matrix/E-forest-wave-3-matrix-final.png) |
| D12 | `forest-wave-4` | 7000 | 25 / 25 | 13 / 7 / 5 | 25 / 0 / 0 | Simulation deadline: live=25, pending=0, moving=5, failure=None | [画面](matrix/D12-forest-wave-4-matrix-final.png) |
| E | `forest-wave-4` | 7000 | 25 / 25 | 15 / 5 / 5 | 25 / 0 / 0 | Simulation deadline: live=25, pending=0, moving=5, failure=None | [画面](shard-3/E-forest-wave-4-matrix-final.png) |
| D12 | `forest-wave-5` | 7000 | 30 / 30 | 9 / 6 / 15 | 30 / 0 / 0 | Simulation deadline: live=30, pending=0, moving=15, failure=None | [画面](shard-2/D12-forest-wave-5-matrix-final.png) |
| E | `forest-wave-5` | 7000 | 30 / 30 | 11 / 9 / 10 | 30 / 0 / 0 | Simulation deadline: live=30, pending=0, moving=10, failure=None | [画面](shard-1/E-forest-wave-5-matrix-final.png) |
| D12 | `forest-wave-6` | 7000 | 29 / 29 | 14 / 14 / 1 | 29 / 0 / 0 | Simulation deadline: live=29, pending=0, moving=1, failure=None | [画面](shard-1/D12-forest-wave-6-matrix-final.png) |
| E | `forest-wave-6` | 7000 | 29 / 29 | 14 / 8 / 7 | 29 / 0 / 0 | Simulation deadline: live=29, pending=0, moving=7, failure=None | [画面](shard-4/E-forest-wave-6-matrix-final.png) |
| B0 | `forest-full44-large` | 12000 | 44 / 44 | 9 / 8 / 27 | 44 / 0 / 0 | Simulation deadline: live=44, pending=0, moving=27, failure=None | [画面](shard-3/B0-forest-full44-large-matrix-final.png) |
| B1 | `forest-full44-large` | 12000 | 44 / 44 | 6 / 5 / 33 | 44 / 0 / 0 | Simulation deadline: live=44, pending=0, moving=33, failure=None | [画面](shard-3/B1-forest-full44-large-matrix-final.png) |
| C33 | `forest-full44-large` | 12000 | 44 / 44 | 17 / 25 / 2 | 44 / 0 / 0 | Simulation deadline: live=44, pending=0, moving=2, failure=None | [画面](shard-4/C33-forest-full44-large-matrix-final.png) |
| D12 | `forest-full44-large` | 12000 | 44 / 44 | 17 / 0 / 27 | 44 / 0 / 0 | Simulation deadline: live=44, pending=0, moving=27, failure=None | [画面](shard-4/D12-forest-full44-large-matrix-final.png) |
| B0 | `forest-full45-medium` | 12000 | 45 / 45 | 10 / 29 / 6 | 45 / 0 / 0 | Simulation deadline: live=45, pending=0, moving=6, failure=None | [画面](overflow-4/B0-forest-full45-medium-matrix-final.png) |
| C33 | `forest-full45-medium` | 12000 | 45 / 45 | 12 / 31 / 2 | 45 / 0 / 0 | Simulation deadline: live=45, pending=0, moving=2, failure=None | [画面](overflow-4/C33-forest-full45-medium-matrix-final.png) |
| E | `forest-full45-medium` | 12000 | 45 / 45 | 12 / 30 / 3 | 45 / 0 / 0 | Simulation deadline: live=45, pending=0, moving=3, failure=None | [画面](shard-1/E-forest-full45-medium-matrix-final.png) |
| B0 | `forest-full44-two-late-small` | 3999 | 44 / 46 | 9 / 6 / 29 | 44 / 2 / 0 | System.InvalidOperationException: Initial crowd did not physically settle before late arrivals. | [画面](overflow-2/B0-forest-full44-two-late-small-matrix-final.png) |
| B1 | `forest-full44-two-late-small` | 3999 | 44 / 46 | 6 / 4 / 34 | 44 / 2 / 0 | System.InvalidOperationException: Initial crowd did not physically settle before late arrivals. | [画面](overflow-2/B1-forest-full44-two-late-small-matrix-final.png) |
| D12 | `forest-full44-two-late-small` | 3999 | 44 / 46 | 17 / 0 / 27 | 44 / 2 / 0 | System.InvalidOperationException: Initial crowd did not physically settle before late arrivals. | [画面](shard-3/D12-forest-full44-two-late-small-matrix-final.png) |
| E | `forest-full44-two-late-small` | 12000 | 46 / 46 | 18 / 26 / 2 | 46 / 0 / 0 | Simulation deadline: live=46, pending=0, moving=2, failure=None | [画面](shard-3/E-forest-full44-two-late-small-matrix-final.png) |
| D12 | `single-cell-throat` | 12000 | 48 / 48 | 3 / 0 / 45 | 48 / 0 / 0 | Simulation deadline: live=48, pending=0, moving=45, failure=None | [画面](shard-1/D12-single-cell-throat-matrix-final.png) |
| E | `single-cell-throat` | 12000 | 48 / 48 | 9 / 0 / 39 | 48 / 0 / 0 | Simulation deadline: live=48, pending=0, moving=39, failure=None | [画面](overflow-2/E-single-cell-throat-matrix-final.png) |
| B0 | `cross-junction-original-4m` | 12000 | 72 / 72 | 11 / 58 / 3 | 72 / 0 / 0 | Simulation deadline: live=72, pending=0, moving=3, failure=None | [画面](overflow-4/B0-cross-junction-original-4m-matrix-final.png) |
| D12 | `cross-junction-original-4m` | 12000 | 72 / 72 | 12 / 58 / 2 | 72 / 0 / 0 | Simulation deadline: live=72, pending=0, moving=2, failure=None | [画面](overflow-4/D12-cross-junction-original-4m-matrix-final.png) |
| B0 | `moving-noncenter-death-and-reinforcement` | 12000 | 24 / 24 | 15 / 8 / 1 | 25 / 0 / 1 | Simulation deadline: live=24, pending=0, moving=1, failure=None | [画面](overflow-1/B0-moving-noncenter-death-and-reinforcement-matrix-final.png) |
| B0 | `capacity-death-refills-original-queue` | 8000 | 33 / 39 | 4 / 8 / 21 | 33 / 167 / 0 | Simulation deadline: live=33, pending=167, moving=21, failure=SpawnOccupied | [画面](overflow-1/B0-capacity-death-refills-original-queue-matrix-final.png) |
| B1 | `capacity-death-refills-original-queue` | 8000 | 39 / 39 | 7 / 11 / 21 | 39 / 161 / 0 | Simulation deadline: live=39, pending=161, moving=21, failure=SpawnOccupied | [画面](overflow-4/B1-capacity-death-refills-original-queue-matrix-final.png) |
| D12 | `capacity-death-refills-original-queue` | 8000 | 31 / 39 | 12 / 17 / 2 | 31 / 169 / 0 | Simulation deadline: live=31, pending=169, moving=2, failure=StaticGoalUnreachable | [画面](overflow-1/D12-capacity-death-refills-original-queue-matrix-final.png) |
| E | `capacity-death-refills-original-queue` | 8000 | 18 / 39 | 11 / 5 / 2 | 18 / 182 / 0 | Simulation deadline: live=18, pending=182, moving=2, failure=SpawnOccupied | [画面](overflow-4/E-capacity-death-refills-original-queue-matrix-final.png) |
| D12 | `old-waiters-refill-without-newcomers` | 12000 | 42 / 42 | 10 / 26 / 6 | 48 / 0 / 6 | Simulation deadline: live=42, pending=0, moving=6, failure=None | [画面](tail-2/D12-old-waiters-refill-without-newcomers-matrix-final.png) |

## 复测与视角证据

| 方案 | 输入 | 状态 | 移动过程 | 终态 |
| --- | --- | --- | --- | --- |
| A | `forest-full44-large` | Passed | [斜视移动](overflow-4/A-forest-full44-large-repeat-moving.png) | [画面](overflow-4/A-forest-full44-large-repeat-final.png) |
| B1 | `forest-full45-medium` | Passed | [斜视移动](overflow-1/B1-forest-full45-medium-repeat-moving.png) | [画面](overflow-1/B1-forest-full45-medium-repeat-final.png) |
| C33 | `forest-full44-two-late-small` | Passed | [斜视移动](overflow-1/C33-forest-full44-two-late-small-repeat-moving.png) | [画面](overflow-1/C33-forest-full44-two-late-small-repeat-final.png) |
| D12 | `forest-wave-3` | Failed | [斜视移动](shard-3/D12-forest-wave-3-repeat-moving.png) | [画面](shard-3/D12-forest-wave-3-repeat-final.png) |
| E | `forest-full44-large` | Passed | [斜视移动](shard-3/E-forest-full44-large-repeat-moving.png) | [画面](shard-3/E-forest-full44-large-repeat-final.png) |
| B0 | `single-cell-throat` | Passed | [斜视移动](shard-3/B0-single-cell-throat-repeat-moving.png) | [画面](shard-3/B0-single-cell-throat-repeat-final.png) |
| A | `capacity-death-refills-original-queue` | Passed | [斜视移动](tail-2/A-capacity-death-refills-original-queue-repeat-moving.png) | [画面](tail-2/A-capacity-death-refills-original-queue-repeat-final.png) |
| E | `old-waiters-refill-without-newcomers` | Passed | [斜视移动](overflow-4/E-old-waiters-refill-without-newcomers-repeat-moving.png) | [画面](overflow-4/E-old-waiters-refill-without-newcomers-repeat-final.png) |

[逐案 CSV](Windows-Play-results.csv) · [合并后的快照和结果 JSONL](matrix-merged/events.jsonl) · [首批 Unity 日志](matrix-editor.log) · [测试输入清单](jobs.csv) · [临时驱动源码](WindowsPlayMatrix.cs)

长耗时用例由独立工作树并行执行。部分早期进程在交接后停止；汇总仅纳入每个已完成的计划项。各进程使用同一提交和原冻结绑定。墙钟耗时仅用于本次观察。 [分片 1](shard-1-editor.log) · [分片 2](shard-2-editor.log) · [分片 3](shard-3-editor.log) · [分片 4](shard-4-editor.log) · [接续 1](overflow-1-editor.log) · [接续 2](overflow-2-editor.log) · [接续 4](overflow-4-editor.log) · [尾段 2](tail-2-editor.log)

这些结果适用于交付分支的 Windows Unity Editor 专用场景。生产 Bootstrap、Windows Player、手机、IL2CPP 以及余下输入属于其他验收范围。
