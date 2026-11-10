# 让路期间的死亡与补兵审计

本工程补验已经提交的协同导航恢复逻辑，不修改生产 Domain 或 UnityPackages。采用 `TowerDefenseNavigationChallenges` 的原始混合射程几何与请求，复用独立的连续身体、静态障碍、基地和视线 oracle。

被审计代码来自 `750f091`，本次结果为 **8 / 8 通过**。每例结束时累计 65 次实际准入、40 个存活且已经到达真实攻击格心的单位、25 次显式死亡、0 个排队请求。原始 .NET 模型结果在 [Evidence/summary.json](Evidence/summary.json)，完整命令序列在同目录的 8 个 `.json.gz` 中；解压即可读取。它们不替代真实 Unity、IL2CPP、设备或画面验收。

## 实际执行的过程

1. 外侧射程 4 m 的 28 个单位先就位，再加入内侧射程 1 m 的 12 个单位。身体半径均为 250 mm。
2. 第一轮将 12 个内侧单位全部显式移除，从原入口排队补回相同身体、射程和速度的单位，触发内侧空位恢复。
3. 在真正的 `VacancyJob.GateActors` 让路任务进行时，删除一个正在两格之间移动、实际坐标不在格心的参与者。分别覆盖外侧让路者 `gate` 和内侧通行者 `requester`，再从原入口补回相同 profile。
4. 所有人重新到达后，再将 12 个内侧单位全部移除并补回。最终 40 个单位全部实际到达攻击位置，并保持 200 tick。

`GateActors` 只通过反射只读检查，用于证明注入发生在目标协调事务内。没有反射修改状态、瞬移、临时删除碰撞体或替算法执行计划。任务没有触发、选不到实际非格心参与者、未完成补兵或者只完成部分请求，都会失败。

每次出生（含拒绝）和死亡均检查调用前后时钟及其他单位坐标完全相同；出生成功者必须仍在原入口。每个 tick 检查连续运动的身体、障碍和基地间隙、真实速度、身体/射程/速度不变、攻击距离与视线，并核对 `总请求 = 存活 + 队列 + 显式死亡` 及精确 ID 集合。

## 输入与证据范围

4 个 seed 为 `1 / 17 / 733 / 20261110`，各自实际打乱请求顺序；单位按 ID 使用 250 或 500 mm/tick 的速度，因此不同请求顺序会改变物理交通，不只是给同一批运动重新编号。两种死亡角色分别有 4 个不同的请求计划 hash 和 4 个不同的去 ID 物理命令 hash，详见汇总。

原先私有草稿沿用 1000 mm/tick，恰好每 tick 移动一整格，无法触发“非格心死亡”，准备条件不成立。草稿没有作为算法失败或通过证据。当前明确使用 250 / 500 mm/tick 来触发半格或四分之一格运动；没有改动生产速度、地图或目标。

源文件 hash、测试源文件 hash、编译后的测试 DLL hash 和输入 hash 都写入汇总，并要求测试期间源文件不变。证据对应这次冻结的源内容；将本测试合入其他生产版本后必须对新内容重跑，不能复用本次通过结论。

## 复现

在仓库根目录使用 .NET 8：

```bash
dotnet build 'Verification~/NavigationGateLifecycle/NavigationGateLifecycle.csproj' -c Release --nologo
dotnet 'Verification~/NavigationGateLifecycle/bin/Release/net8.0/NavigationGateLifecycle.dll' --output Artifacts/NavigationGateLifecycle
```

单独检查某个输入：

```bash
dotnet 'Verification~/NavigationGateLifecycle/bin/Release/net8.0/NavigationGateLifecycle.dll' --role gate --seed 17 --output Artifacts/Gate17
```

可通过 MSBuild 参数指定另一个冻结的 Domain，例如：

```bash
dotnet build 'Verification~/NavigationGateLifecycle/NavigationGateLifecycle.csproj' -c Release -p:NavigationDomainRoot=/absolute/frozen/Domain --nologo
dotnet 'Verification~/NavigationGateLifecycle/bin/Release/net8.0/NavigationGateLifecycle.dll' --output Artifacts/GateCandidate
```

Domain 路径需包含 `Configuration.cs` 与 `Navigation/*.cs`。采用这个独立构建的 DLL 运行，避免自定义输出路径时误运行旧二进制。不要并发构建或运行同一个工程的输出目录。
