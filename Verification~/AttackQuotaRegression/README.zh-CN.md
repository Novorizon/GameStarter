# 攻击份额与需求生命周期回归

默认运行一个入口的两种需求生命周期；`--stress` 扩为四个入口、共八例。固定 35×35 地图、1000mm 格、r250、1000mm/tick；不改变人数或攻击期望来通过测试。

```bash
dotnet run --project Verification~/AttackQuotaRegression/AttackQuotaRegression.csproj -c Release -- Artifacts/AttackQuotas
dotnet run --project Verification~/AttackQuotaRegression/AttackQuotaRegression.csproj -c Release -- Artifacts/AttackQuotasStress --stress
```

第一种先建立 Short16 / Long68 攻击，删除 15 Short 后应为 Short1 / Long83，补回 15 后必须恢复 Short16 / Long68。已有 Small/Short 临时让路可以打开被围住的最后一个位置，最终仍返回合法目标。

第二种删除全部 Short，Long84 应真正补满，不能替已不存在的旧份额空留位置。新波三个 Short 只要求首次服务一名，得到 Short1 / Long83。随后显式删除竞争的 Long 候场者及占用 Short 兼容位的 15 Long，隔离“普通空位增长”条件；再补足 Short16，必须达到 Short16 / Long68，不能把历史份额当成上限。

连续 oracle 检查身体扫掠、障碍、基地、速度、实际目标、攻击距离和射线；生成 API 不得移动旧身体或推进时钟，所有活体 ID 必须对应原请求，移除均通过公开 Remove。报告保存生产源、测试、二进制摘要与阶段状态。`Evidence/Stress` 对应测试文件的精确摘要以报告为准；Root 最终 CI 另对构造调用做了无逻辑变化的参数排版，并独立复验。
