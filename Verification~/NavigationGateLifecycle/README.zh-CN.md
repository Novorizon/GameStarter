# 让路期间的死亡与补兵审计

默认验收真实森林地图的多门协调事务：先让 44 个 Big 全部实际就位并保持 200 tick，再从原 S 入口请求 2 个 Small；在让路过程中删除参与者，并从原 S 补回相同身体、射程和速度的单位。最终必须恢复 **46 = 44 Big + 2 Small、20 个攻击者、26 个候场者、0 队列**，并保持 200 tick。没有发生指定死亡注入不能算通过。

B2 正式候选 `445a7db` 的完整矩阵 **30 / 30 通过**，见 [Evidence/ForestStress/summary.json](Evidence/ForestStress/summary.json)。默认 6 类是其强注入子集；默认入口仍由仓库总验收与 CI 单独执行。本次纯模型测试累计耗时 88.009 秒，各例最终 tick 为 9408–12430（包括初始填满、晚到、死亡与补兵和保持期）。所有源及测试文件在运行前后相同，完整命令序列和哈希在同目录。

生产 Domain 的完整候选聚合 SHA-256 为 `a9ee55d0667eaea51207568e41c52c11aed2f78d1998af0031d78db656a24ef7`。本工程实际编译 `Configuration.cs` 与 9 个 Navigation 文件共 10 个，逐项匹配候选清单；生产清单中另有 4 个未由本工程编译的业务文件，不混称本测试覆盖了整个 Domain。测试 DLL SHA-256 为 `4e67196a16ff6bcb388dc9d94114f742782d428898cd8ec9e6d71f32b936a5c6`。

Big 使用 250 mm 半径、550 mm 射程、55 mm/tick；Small 使用 180 mm 半径、450 mm 射程。地图、500 mm 逻辑格、原点和偏心基地均使用项目真实数据，不扩图、不缩身体、不提速、不瞬移。Small 最终必须占据仅适合小体型的 256 / 257 两格。

默认有 6 类强注入，Small 速度为 100 mm/tick：Big 外撤途中、非 holding 格静止临停、Small 已到达后 Big 向原目标移动时、Small 通行途中、Small 已到达攻击位置、同时删除全部让路 Big。选中者必须是实际 `GateActors` 事务中的活体；途中要求坐标不在格心，临停要求连续两个 tick 坐标相同且位于非 holding 格，到达要求实际攻击状态。

`--stress` 在 Small 速度 100 / 165 下各跑 15 种时序，共 30 例：

| 每个速度的覆盖 | 数量 |
| --- | ---: |
| 4 个让路 Big 逐个在外撤途中删除 | 4 |
| 3 个确实停留的 Big 逐个在临停时删除 | 3 |
| 同 3 个 Big 在 Small 已到达后向原目标移动时删除 | 3 |
| 另一个没有静止等待的 Big，在实际通行格心删除 | 1 |
| Small 通行途中、到达后分别删除 | 2 |
| 同时删除两门、全部四门 | 2 |

第四个 Big 的轨迹没有静止等待，因此不制造一个不存在的“静止临停”条件。它仍接受独立的途中和通行格心死亡测试。所有时序均显式执行实际死亡和同 profile 补兵。

## 严格检查

每次出生（含拒绝）和死亡均检查调用前后时钟、其他单位坐标完全相同；出生成功者必须仍在原入口。每个 tick 检查请求守恒和精确 ID 集合，身体、速度与射程不得改变，未就位者不能伪装为攻击或候场。

身体和静态障碍使用已有独立连续 oracle。真实 500 mm 地图先平移原点并将坐标及身体半径乘 2，等价变换到 oracle 的 1000 mm 单位；算法仍在原地图运行。偏心基地使用真实圆心、半径的独立点到线段距离检查，攻击距离也按真实世界坐标检查。它们不替代 Unity 渲染、IL2CPP、设备或性能验收。

反射只读取实际 `vacancyJob` 对象和 `GateActors`，用于证明注入发生于协调任务内；没有反射改状态或替算法执行计划。每一轮 job 按对象引用区分，在任务首次可观察时捕获已经提交的 Goal，只在同一事务活动期间检查稳定。后续 vacancy 或兼容重分配可以合法地分配新 Goal，不能把正常的“候场格 → 攻击格”晋升误判为丢失旧 claim。

源文件、测试文件、地图、编译后的 DLL 及实际命令序列均记录 SHA-256；测试期间源文件必须不变。完整命令包括被拒绝的出生请求、实际死亡坐标和原始 profile。通过结论仅属于报告所记录的候选源码，接入新版本必须重跑。

## 已保留的反例与测试准备说明

全合法初稿中，Big 2 外撤途中被删除后，Small 46 占据 Big 兼容的 297 候场格，而 Small-only 256 仍空；Big 47 补兵被拒绝。到 tick 20001 仍为 45 活体、19 攻击者、1 排队。该例是算法未完成可行请求，不能记作容量通过，见 `Evidence/ForestBeforeCompatibilityFix/`。

探索时，要求所有 Big 都曾静止等待的旧分类在一个不停车的 Big 上没有触发；此时虽然 46 人已全部到达，也没有算通过。测试改为覆盖实际发生的通行格心条件，静止检查保留给真实停车的三者。另一个草稿将后续新 job 的正常晋升请求者也锁在前一轮 Goal，属于测试假设错误，现已按真实 job 对象区分事务。

## 原方案的 8 例冻结审计

[Evidence/summary.json](Evidence/summary.json) 与同目录 8 个 `.json.gz` 保留 `750f091` 原方案的 **8 / 8** 冻结证据，测试源码来自提交 `929fa9b`。每例累计 65 次实际准入、40 个实际攻击者、25 次显式死亡、0 排队，4 个不同请求顺序的去 ID 物理命令 hash 也不同。

原方案先放置 28 个射程 4 m 的单位，再加入 12 个射程 1 m 的单位；全内侧死亡后，在旧单门事务中删除外侧或内侧移动者并补兵，再做一轮全内侧死亡。它通过 `--legacy` 显式运行。全合法方案改变分配策略后，这个几何可能不再触发旧单门任务；这种“目标注入未发生”不能当作 8 例通过，也不等同于导航卡住。因此默认改为确实制造多门任务的真实森林验收。

原私有准备稿曾使用 1000 mm/tick，恰好每 tick 走整格，无法触发非格心死亡；没有当作算法失败或通过。原方案正式审计使用 250 / 500 mm/tick，未修改生产配置。

## 复现

在仓库根目录使用 .NET 8，默认跑真实森林 6 例：

```bash
dotnet build 'Verification~/NavigationGateLifecycle/NavigationGateLifecycle.csproj' -c Release --nologo
dotnet 'Verification~/NavigationGateLifecycle/bin/Release/net8.0/NavigationGateLifecycle.dll' --output Artifacts/NavigationForestGateLifecycle
```

完整矩阵及单例：

```bash
dotnet 'Verification~/NavigationGateLifecycle/bin/Release/net8.0/NavigationGateLifecycle.dll' --stress --output Artifacts/NavigationForestGateLifecycleStress
dotnet 'Verification~/NavigationGateLifecycle/bin/Release/net8.0/NavigationGateLifecycle.dll' --phase outbound-big --ordinal 0 --small-speed 100 --output Artifacts/GateOne
```

原方案及其他冻结 Domain：

```bash
dotnet build 'Verification~/NavigationGateLifecycle/NavigationGateLifecycle.csproj' -c Release -p:NavigationDomainRoot=/absolute/frozen/Domain --nologo
dotnet 'Verification~/NavigationGateLifecycle/bin/Release/net8.0/NavigationGateLifecycle.dll' --output Artifacts/GateCandidate
dotnet 'Verification~/NavigationGateLifecycle/bin/Release/net8.0/NavigationGateLifecycle.dll' --legacy --output Artifacts/LegacyGateCandidate
```

Domain 路径需包含 `Configuration.cs` 与 `Navigation/*.cs`。实际地图可用 `--map /absolute/ForestNavigation.bytes` 指定。运行明确构建出的 DLL，避免自定义输出路径后误用旧二进制；不要并发构建或运行同一个工程的输出目录。
