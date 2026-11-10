# 准入事务独立审计

这个 .NET 8 工程直接编译选中的当前业务 Domain，不复制生产实现，不依赖 UnityPackages、Unity 或测试框架包。默认源是同一仓库的 `Assets/HotUpdate/TowerDefense/Domain`。它用于验证准入事务、失败缓存与回滚；真实移动碰撞、死亡恢复和 Unity 运行仍由各自的导航验收负责。

## 运行

在已经包含多层准入门控的仓库中：

```bash
python Verification~/AdmissionTransactionAudit/run.py
```

脚本执行全新构建，运行有限案例，将报告写到 `Artifacts/AdmissionTransactionAudit/`，并在构建前和运行后核对全部 Domain 源码、地图、解析器及测试源码的 SHA-256。测试失败、没有报告或任何输入发生变化都会返回非零状态。`manifest.json` 的 `sourceAndInputsUnchanged` 必须为 `true`。

对独立候选 checkout 使用：

```bash
python Verification~/AdmissionTransactionAudit/run.py \
  --domain /path/to/candidate/Assets/HotUpdate/TowerDefense/Domain \
  --expect-domain-sha256 EXPECTED_AGGREGATE_SHA256
```

`--candidate-domain` 是 `--domain` 的别名。默认解析器取 Domain 同级 `Data/NavigationLayout.cs`；可用 `--layout` 覆盖。地图默认取本仓库的 `Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes`，可用 `--map` 覆盖。`--output` 可指定报告目录。运行器不修改候选源码。

也可直接运行最小 C# 工程：

```bash
dotnet run --project Verification~/AdmissionTransactionAudit/AdmissionTransactionAudit.csproj -c Release \
  -p:NavigationDomainRoot=/path/to/candidate/Assets/HotUpdate/TowerDefense/Domain \
  -- --output Artifacts/AdmissionTransactionAudit/results.json
```

MSBuild 的 `CandidateDomain` 可代替 `NavigationDomainRoot`；另有 `NavigationLayoutFile`、`NavigationMapFile`。直接运行工程不会生成构建前后输入核验清单，正式验收建议使用 Python 运行器。运行器仅使用 Python 标准库及本机 `dotnet`，适合加入默认导航 CI；无需修改生产项目或 SDK。

## 硬断言

- 9×9 固定地图填满 17 个 Big 后，预算 100/3200 的失败必须报告 `SearchLimitExceeded`，预算 6400 的稳定几何失败报告 `CapacityExceeded`。
- 同 tick、63 tick 时重试必须命中缓存且展开数为零。预算失败在第 64 tick 必须重新搜索；稳定几何失败在同一时刻仍使用缓存。
- 真实公开 `Remove` 释放一个单位后，同 tick 的待入场 Small 必须立即获准，不能继续命中旧失败。另有一个明确标注的白盒案例仅改变 traffic revision，检查缓存失效。
- 实际森林中先让 44 个 Big 完全停稳，再以 500/250 两种 Small 速度交替请求。每种速度第一次耗尽预算，后续六次必须全部零展开、命中各自缓存，不能因失败回滚的伪版本变化互相失效。
- 每次失败均逐项比较实际单位、原 Occupant/Agent/Profile/Path 引用、每个 TimedCell 的原引用与字段、公开 Agent 属性、完整目标权、未来预约内容、时钟和语义版本；不是只比较单位数量或字符串散列。
- 回滚后逐一公开移除 44 个真实单位，每一步检查所有目标权与预约都属于仍存在的单位；最终单位、目标权与预约均为空。
- 成功事务不得推进模拟时钟或提前改变实际位置。紧接着的同 tick 第二次准入必须零展开报告 `ReservationBlocked`，并完整保留第一笔已提交事务。

森林回滚预算先由同一候选的两次成功准入测定：分别取两种速度完成事务所需展开数减 1、减 128。不同预算重建的初态必须与成功校准的确定性摘要完全一致，门控布局展开数也必须相同，并确实进入带时间的路径规划。这样搜索优化后，不会把已经成功的请求错误地当成“回滚已验证”；校准和全部实际阈值保存在报告中。所有候选仍须满足相同的失败分类、零展开、严格状态恢复和无残留要求。

这些案例使用的 Big 为 r250/range550/speed500，Small 为 r180/range450/speed500 或 250；生产低速移动、两个晚到 Small 的最终占位及六波攻击人数另行验收。有限事务检查不声称能证明任意多单位重排问题总可解。

## 证据版本

`EvidenceB2/` 是本工程对冻结 B2 Domain aggregate `a9ee55d0667eaea51207568e41c52c11aed2f78d1998af0031d78db656a24ef7` 的运行报告。

`Comparison7ee317/` 是更早的独立审计报告，准入源码 SHA 为 `7ee317ba7628d80ae920db7d1c9624c3016aced02937113a718b435fa27a9ca3`。它保留原始 12 个死亡时序及预算检查记录，不能作为 B2 的执行证据，也不作为本工程默认编译源。该历史报告的交替请求脚本以记录为主，本工程增加了明确的零展开与缓存命中断言。
