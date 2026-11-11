# Unity/Mono选择性检查：B0与D-r8

Unity 2022.3.62f1 Editor Play/Mono，Null图形后端。实际加载Core/Oracle和候选DLL与各自冻结模型构建清单一致，运行ID/manifest新鲜、前后源与DLL一致、临时工程清理均通过。没有据此声称图形、IL2CPP或设备验收。

| factory / 场景 | 结果 | 实际出生 | 攻击 / 候场 | 最后tick |
| --- | --- | ---: | --- | ---: |
| OrcaFactory-forest-single-large | PASS | 1 | 1 / 0 | 1358 |
| OrcaFactory-forest-wave-1 | PASS | 8 | 8 / 0 | 940 |
| OrcaFactory-dynamic-route-block-and-open | PASS | 24 | 16 / 8 | 800 |
| Aoe4Factory-forest-single-large | PASS | 1 | 1 / 0 | 1196 |
| Aoe4Factory-forest-wave-1 | PASS | 8 | 8 / 0 | 830 |
| Aoe4Factory-dynamic-route-block-and-open | FAIL | 24 | 14 / 8 | 12000 |

B0三项均通过。D-r8两项通过，动态堵路/开放场景到12000tick仍有2人Moving，故进程退出1。D-r8是后续R9/R10修复前的阶段版本，不拿它作为最终D源的验收；本JSON同时含两候选，不能把文件总计当作任一候选的通过数。

每场使用5ms独立连续检查；通过需200个完整50ms区间驻留，最终运动与原身体/速度/目标守恒。原输入逐字SHA在native-provenance中，转换后输入保存在同目录。这里只归档选择性结果与构建来源，不代表整个49项在Unity执行。

完整控制器脚本版本见汇总分支提交d91e168；driverSourceSha256由原生来源记录绑定。原始Unity日志留原执行目录，未收入此证据包。
