# 局部临停与完整事务回退

此分支是独立调度延迟实验，基于冻结 B2 Domain `a9ee55d0667eaea51207568e41c52c11aed2f78d1998af0031d78db656a24ef7`。不包含主线后续的大阵容攻击份额恢复，不作为整个导航任务完成的证明；未修改 SDK 或地图。

## 结果

真实 Forest 先让 44 个半径 250、速度 55、射程 550 的敌人到位，从构造时就注册半径 180、射程 450 的 Small profile。Small 两请求均从原 S 进入，身体、速度上限与地图保持原样。

| 输入 | B2 全部到位 | 本候选全部到位 | 本候选终态 |
| --- | --- | --- | --- |
| Small 165，tick 4000 请求 | 9880 | 5228 | 46 身体，20 攻击、26 候场 |
| Small 100，44 Big 稳定后 tick 3229 请求 | 9109 | 4664 | 46 身体，20 攻击、26 候场 |

165 速场景新增的等待从 5880 tick 降到 1228 tick。按正式业务每 tick 50ms，等于 294 秒降到 61.4 秒，减少约 79.1%；不是对整场从第 0 tick 开始的用时计算降幅。每名 Small 仍实际从原出生点行走约 103/104 个格；两次入场事务仍串行，不预先创建尚未接受的请求。

四个让路 Big 使用基地西侧相邻的临时通行格 `382–385`，完整出/返路径分别只有 `4/8/12/16` 条边。原 B2 对应 `16/24/124/130` 条边，其中两人绕到地图另一段道路。全部 Big 仍回到各自原 Goal；Small 最终精确占 `256/257`。没有通过临停在通道里、删掉 Big 或增加速度计作完成。

- 两种单 profile 的 44/45 饱和、两种 Small 速度的晚到 46、全部攻击格逐次死亡补回共 **70 轮通过**。
- 真实 Forest 多门过程中死亡的完整 **30/30 通过**：两种 Small 速度、Big 外撤/临停/返程/格心、Small 途中/到达后、同时删除两门或四门。每例最终仍为 44 Big + 2 Small、20 攻击、26 候场、0 队列，并保持 200 tick。
- 两轮正式报告均记录源码与测试源码前后不变。测试是纯 .NET 8 独立模型与连续几何检查；未执行 Unity、IL2CPP 或设备验收。

完整输入、路径、死亡与补兵命令保存在 `Evidence/Full`、`Evidence/Lifecycle`；逐案例 JSON 使用 gzip，`summary.json` 保留原始格式。生产完整 14 文件的 SHA 清单见 `source.json`。实际测试工程编译 Configuration、Navigation 及 NavigationLayout，清单中的其他 Domain 文件仅作源快照，不冒称由该工程执行。

## 为什么需要回退

原布局同时要求：门到出生点的整条静态路线不能临停、临停不得相邻、放置临停单位后所有无关通行格也必须保持全连通。它们比本次出/入/返事务实际需要的条件更强，会排除近处可用侧袋。

局部模式保留 Small 路线保护和静态净空，但不添加这三项额外筛选。随后仍使用完整 SIPP 预约实际撤出、Small 入场和每个 Big 返场，全部成功才提交；失败保留实际身体、时间、Goal 和原路径。

仅删除三项筛选的裸局部候选虽然初始 46 与静态死亡 70 轮通过，多门过程死亡矩阵却只有 **6/30**：死亡后的贪心临停选择可能无法执行后续兼容重分配。这个候选被拒绝，不交付为生产改善。

本候选将原执行段抽成 `TryExecuteAdmissionGateLayout`，先试局部布局，失败后原子回滚再试原保守布局，两轮共用原有搜索预算。其余路径预约、速度、身体与连续碰撞门槛不变。不能省略保守回退，也不能只照抄删除条件的早期补丁。

同一份输入的隔离比较见 `comparison.json`：只去门路径保护到 7120；只允许相邻到 9840；只去全连通却保留路径禁令会失败；三项同时放宽才获得近处完整可执行事务。失败日志作为比较保留，没有混入通过数量。

## 复现

在本分支根目录，使用 .NET 8：

```bash
dotnet run --project Experiment~/AllLegalSlots/AllLegalSlots.csproj -c Release -- Artifacts/LocalYield --late-small
dotnet run --project Verification~/NavigationGateLifecycle/NavigationGateLifecycle.csproj -c Release -- --stress --output Artifacts/LocalYieldLifecycle
```

前者默认运行 44/45 饱和以及双速 46 和 70 轮静态死亡。`--late-only --late-small --no-deaths --only-165` 仅用于首轮探索，不能代替完整验收。

向主线移植时只参考 `local-buffer-with-fallback.patch` 中的布局策略及执行 helper；主线 B3 的 quota 参数、永久退让、历史份额回滚等逻辑必须保留，不能直接以本分支的 B2 文件覆盖。
