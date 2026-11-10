# B7 同输入性能与语义对照

该工程复放真实 Forest 的同一压力输入：从原 S 生成 44 Big（r250/range550/speed55），两 profile 从开始注册，tick4000 后从同入口生成 2 Small（r180/range450/speed165）。不删单位，不变更地图、身体、速度或恢复频率。

工程默认编译当前 worktree 的 Domain；`-p:CandidateDomain=` 可以指向任意固定副本。基线应固定为 B7 `b7ea41a6…bb9b5db`，不能在运行期间修改候选。

```bash
dotnet run --project Verification~/NavigationPerformance/NavigationPerformance.csproj -c Release \
  -p:CandidateDomain=/absolute/frozen-b7-domain -- \
  --output Artifacts/NavPerformanceBaseline --native-csv /absolute/native/full-44-large-two-late-small.csv

dotnet run --project Verification~/NavigationPerformance/NavigationPerformance.csproj -c Release -- \
  --output Artifacts/NavPerformanceCandidate --compare Artifacts/NavPerformanceBaseline \
  --native-csv /absolute/native/full-44-large-two-late-small.csv
```

`--runs 2` 对两个全新 crowd 复放，保存到 `run-1` / `run-2`，用于观察热运行；基线和候选须使用相同 runs。`--contracts-only` 只执行独立 oracle 的切线、1mm 重叠、API 出生边界、攻击线触角和障碍扫掠小回归。

每例硬检查实际全到位为 tick5228、46 活体、20 攻击/26 候场、Small 只占 256/257、全部 Big 返回精确原目标及额外 200 tick 稳态。出生 API 不能移动旧身体或推进时钟；失败调用不能创建隐藏单位。

输出：

- `trajectory.csv`：每 tick 全部 ID、身体输入、位置、状态、目标、到达时刻、剩余/累计距离。
- `commands.csv`：每一次成功或失败的出生请求及其原参数/结果。
- `counters.csv`：逐 tick 全部已有公开数值/枚举计数。候选必须保留全部旧列且逐值相同，新诊断列单独列明。
- `measurements.csv`：出生 API+Step 与纯 Step 分别计量的当前线程分配、Stopwatch wall time、GC collections；输出/独立几何不在计时区间。
- `result.json` / `summary.json`：轨迹、输入、计数、源/测试/地图/二进制摘要，以及 1400–2099 热窗统计。

`--compare` 必须逐字节保持完整每 tick 轨迹和每次出生结果，并逐值保持所有旧公开计数；性能数字不纳入语义等同比较。`--native-csv` 逐个核对 Unity CSV 已采样的 `(tick,id,x,y,radius,state,goal)`，此前基线已匹配全部 10840 条。

独立 oracle 检查圆盘连续相对运动、障碍矩形扫掠、基地/边界、速度、最终 holding、攻击距离及射线。它不调用生产的 CanTraverse/HasAttackLine 作为几何结论。

.NET wall time 受 JIT、GC 与主机调度影响；验证和跟踪虽不计入时间/分配区间，仍会影响环境 GC 压力。因此该工程用于同输入语义与分配归因，Mono CPU/帧延迟的最终结论需由 Root 的 Unity 探针独立确认。生产不依赖诊断 instrumentation 类型；若私有候选提供 `NavAuditMetrics`，报告会可选读取其公开 long[] 指标。

## 冻结变体与结果

生产提交 `311c1fa88d5d149e847f277d4d9deeb3df4b2b67`，完整 15 源摘要为 `d155d9838c07d7de133a3e1ef4424388bd441086f99f27968c4163bc1d7f2582`，详见 `candidate-source.json`。仅 Crowd.cs 增加 16 行：在 Crowd 私有字典按身体半径保留最近一个纯几何连通分量，所有调用者只读；未加入 Profile/Occupant 行为字段。不同分量交替只影响命中率，不影响结果。

缓存沿用原 `CanTraverse`，保留偏心基地整段避让；仅用于 `includeGoals=false`、无额外阻塞格、非预算事务。预算中的试算及全部动态目标保护仍执行原 Flood，因此节点预算、回滚和恢复调用频率不变。Grid 构造时复制且无修改 API，目标/交通/入口不影响这项纯几何结果。

`Evidence/summary.json` 保存未 instrument 的 B7/变体对照：216,732 条逐 tick 实体、所有出生结果和 25 列旧公开计数完全一致，22947 节点、tick5228 全到位、额外 200 tick 稳态；Native 的 10840 条采样全部一致。两个版本各通过 6,467,381 次独立连续身体对检查及 321,842 次身体扫掠检查。

| .NET 当前线程分配（API + Step） | B7 | 几何缓存 |
| --- | ---: | ---: |
| 全程 | 515,496,488 B | 145,487,824 B |
| tick1400–2099 | 444,696,352 B | 90,622,352 B |

全程减少 71.78%，指定热窗减少 79.62%。`Evidence/Functional/comparison.json` 另记录实际 Forest 四类/70 轮死亡补位与需求生命周期八例，其完整物理和公开结果与 B7 逐字段完全相同。

该改动没有消除所有冷运行峰值。Root 的独立 Mono 复测仍记录一次零展开的冷帧高 CPU；热运行与完整 Unity 功能/资源验收由 Root 另行归档，不能用这些 .NET 分配数据替代 Mono 或设备性能结论。
