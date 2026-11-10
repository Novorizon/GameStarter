# 7ee317 历史比较证据

本目录只保存旧审计快照的历史结果。对应 `NavigationCrowd.AdmissionGates.cs` 的 SHA-256 为：

`7ee317ba7628d80ae920db7d1c9624c3016aced02937113a718b435fa27a9ca3`

这些文件不是 B2 aggregate `a9ee55d0667eaea51207568e41c52c11aed2f78d1998af0031d78db656a24ef7` 的新跑结果，也不得纳入 B2 当前版本的通过数量或替代正式回归。本次归档没有执行测试。

- `OriginalAudit.md`：原 `/tmp/gate-transaction-audit/final/README.md` 的逐字副本，未改写。其旧路径及复现说明属于原临时审计环境。
- `cache-results.json`：旧预算分类、失败缓存、64 tick 退避、Remove 唤醒及同 tick 请求记录。
- `alternating-results.json`：旧双 speed 请求比较记录。原 `AlternatingProgram.cs` 主要记录结果，缺少正式通过门槛所需的硬断言；不能仅凭这些 JSON 行声称完整自动化回归通过。正式工程应补强零展开、缓存命中、分类和状态/版本不变等断言。
- `metadata-results.json`：旧回滚元数据及逐个 Remove 后无残留目标权/预约的结果。
- `death-results.json`：旧 44 Big + 一个晚到 Small 的 12 个死亡相位结果，不替代 44 Big + 两个晚到 Small 的满员验收。
- `source-hashes.json`、`input-hashes.json`：原审计使用的 Domain 和输入摘要，逐字保留。文件名中的 `source` 不表示当前 B2 源码。

本目录不复制旧 Domain、地图、项目或测试程序源码。当前 Domain 的正式测试由上层 `AdmissionTransactionAudit` 工程维护，并应独立记录当前源码摘要、硬断言及运行结果。
