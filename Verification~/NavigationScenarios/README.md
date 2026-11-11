# 塔防导航定向验证

在 GameStarter 根目录运行。验证在独立的 .NET 进程中执行，直接编译战斗领域代码，使用正式 Luban 字节表和地图解析器。

```powershell
dotnet run --project 'Verification~/NavigationScenarios/NavigationScenarios.csproj' -c Release -- all 'Artifacts/NavigationScenarios' 12345 --repeat
```

可用 `--list` 查看场景，也可用逗号连接几个场景名替换 `all`。`--repeat` 用相同种子重跑并比对每个模拟刻的位置、状态和伤害哈希。控制台返回非零表示至少一个场景失败。输出目录内的 CSV 每十刻记录单位位置和状态，额外记录攻击和连续两秒无进展的样本。事件文件记录首次失败、补位耗时和容量诊断。

加上 `--time` 可记录 `FightSession.Step()` 的 p50、p95、p99 与最长耗时。计时范围包含战斗步进和该步的快照更新，诊断和文件写入在计时范围之外。耗时受机器负载与首次 JIT 影响，用于比较同一环境下的版本。

| 场景 | 验证内容 |
| --- | --- |
| `forest-road` | 真实地图和 126 只敌人的出兵配置。把基地近区入口临时设成出口，单独检查弯道、过桥和主路吞吐。 |
| `forest-defeat` | 测试内把基地设为 20 耐久，无塔时按正常攻击规则打到战败。 |
| `forest-saturation` | 无塔且不移除敌人，跑到 180 秒。检查攻击与候补人数、实体重叠、入口前各体型的可达空位和反事实让位后是否还有兼容空位。 |
| `forest-hold-90`、`forest-hold-180`、`forest-hold-270`、`forest-hold-heavy-leading` | 用直角旋转和重甲先到复验同一组无减员围站条件。 |
| `crossing-hold`、`wide-three-hold`、`wide-three-delayed-hold` | 十字口、三入口和西路晚 30 秒出兵。全程不移除敌人。要求每只都到达攻击位或候补位。 |
| `forest-surround` | 第 1700 刻至少 6 名已到位的候补分布在至少 3 侧。入口队列、行进中的候补和正在攻击的敌人单独记录。 |
| `forest-vacancies` | 稳定后逐个移除 25 个已命中基地的攻击者，测量不同方向补位后首次命中的时间。区分能直接走到的空位与需要绕行或被包围的空位。 |
| `forest-drain` / `forest-rotate180` | 反复释放已命中基地的攻击者，直到每只敌人都实际造成伤害，检查队列能恢复前进。用旋转 180° 的几何复验。 |
| `forest-heavy-leading` | 保持敌人数量和类型分布，把后续四只重型敌人提前到开场，验证开局体型与到达顺序变化。 |
| `bend-bridge` / `sealed-bridge` | 快慢和体型混合经过弯道与窄桥。另用完全封死的桥确认地图数据问题能被单独定位。 |
| `open-assault` / `blocked-flank` / `tower-flank` | 比较开阔基地、地形封住一侧，以及实际建造塔基占据一侧的情况。 |
| `two-entries` / `merge-entries` / `legacy-portal` | 两条路独立进入基地近区、两条路先合流，以及旧关卡达到传送门逃脱上限。 |
| `compound-detour` | U 形围墙三面封闭，所有敌人必须绕到开口一侧才能命中。 |
| `size-limited-flank` | 820 毫米开口只允许 600 毫米宽的敌人通过。960 毫米宽的敌人从东路进场，两路都应实际命中。 |
| `crossing-entries` | 西路和北路正交相交，各有独立的近区入口。两路单位都应经过交点后继续进攻。 |
| `sealed-approach` | 主路可走到近区入口，近区被连续墙体隔开。敌人可抵达等待区，基地始终无伤害。 |
| `wide-three-entries` / `wide-three-vacancies` | 宽基地三侧同时进场。分别验证所有敌人逐一命中、释放单个位置后的补位。 |

逻辑地图源在 `LogicalMaps.cs`，可由 Unity 验证场景复用 `LogicalMaps.Create(name)`。坐标单位为毫米。

定位时同时看实际攻击、已入场与到点但未入场的数量。基地满位仍在持续扣血，且入口被前一只敌人的体型占用时，入口等待属于队列压力。释放攻击位后整队仍无进展，或者可直达的空位迟迟无法产生一次真实命中，才有死锁证据。容量表中的多个相邻格可能只能站一只敌人。

候补报告里的 `static`、`live` 和 `attackOnly` 分别记录空场、保留当前敌人和只保留当前攻击者时，从近区入口出发能走到的格子数。`attackOnlyPack` 从这些格子中选一组彼此留有完整体型间距的位置，用于估算站位空间。实际能否填满还取决于入场顺序与行进通道。

每个模拟刻的体型重叠按零容忍检查。报告另外用相邻模拟刻的直线插值估计画面上的重叠深度。跨折线转弯时，直线插值会略切过实际分段路径。超过 16 毫米的估计重叠会使验证失败。

实际画面复核可用已解锁出征的存档，从营地选择林间哨站，确认出征并等待初始倒计时。第一局观察无塔情况下的过桥、两个弯道、前排攻击与候补位置。第二局用靠近基地的一座塔击杀前排，观察最近的可达候补何时补位。录像以窗体正常游玩为准。

五张新增地图定义在 `LogicalMaps.cs`。逻辑测试和 Unity 几何画面都读取这个定义，其中包含路点、墙体、近区、基地边界及出兵规则。逻辑测试可单独运行以下场景。

```powershell
dotnet run --project 'Verification~/NavigationScenarios/NavigationScenarios.csproj' -c Release -- 'compound-detour,size-limited-flank,crossing-entries,sealed-approach,wide-three-entries,wide-three-vacancies' 'Artifacts/NavigationLogicalMaps' 12345 --repeat
```

原道路基线的正交交叉场景曾出现两路队首互相挡住。当前用 `crossing-hold` 验证所有单位在无人为移除时能通过交叉口并完成围站。封死近区仍作为负对照，敌人能抵达近区入口且始终无法命中基地。

如本地已准备 `Artifacts/ForestPresentationFixture`，可用其中的 `Capture-Forest.ps1 -LogicalMaps` 在 Unity 中渲染同一批几何地图。Unity 先留存自然拥堵画面，再对可通行场景逐步移除已命中过基地的敌人以核对后续通行。`success` 表示采集成功，`verificationPassed` 表示所有逻辑地图均满足验证条件。
