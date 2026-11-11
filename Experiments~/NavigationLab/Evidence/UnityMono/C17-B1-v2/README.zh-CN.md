# Unity/Mono选择性检查：C17与B1-v2

Unity 2022.3.62f1 Editor Play/Mono，Null图形后端。实际加载Core/Oracle和候选DLL与各自模型构建清单一致，运行ID/manifest新鲜、前后源与DLL一致、shutdown与临时工程清理均通过。没有据此声称图形、IL2CPP或设备验收。

| factory / 场景 | 结果 | 累计实际出生 | 存活 | 攻击 / 候场 | 最后tick |
| --- | --- | ---: | ---: | --- | ---: |
| Factory-forest-wave-1 | PASS | 8 | 8 | 8 / 0 | 825 |
| Factory-forest-full44-large | PASS | 44 | 44 | 18 / 26 | 2704 |
| Factory-moving-noncenter-death-and-reinforcement | PASS | 25 | 24 | 16 / 8 | 360 |
| OrcaYieldFactory-forest-wave-1 | PASS | 8 | 8 | 8 / 0 | 851 |
| OrcaYieldFactory-forest-full44-large | FAIL | 44 | 44 | 13 / 3 | 12000 |
| OrcaYieldFactory-moving-noncenter-death-and-reinforcement | PASS | 25 | 24 | 16 / 8 | 347 |

C17三项均通过；它是后续死亡补队修复C23之前的阶段源，不作为新C23的验收。B1-v2两项通过、Forest44失败：44实际出生且仍存活，但到12000tick有28人Moving，因此整次进程退出1。源码与输入相同仍需分别报告.NET与Mono结果，不要求两个运行时的浮点轨迹逐字一致。

每场使用原身体、速度和目标，每5ms独立检查连续运动；通过需200个完整50ms区间驻留。移动中死亡补兵的累计25出生、1死亡、24存活严格分开；没有丢弃单位。原输入逐字SHA在native-provenance中，转换后输入同目录保留。

此JSON同时含两候选，按factory和候选SHA区分，不能把文件总数当作某一算法成绩。只代表这六个选择性检查，不代表49项全在Unity执行。完整控制器脚本见d91e168，原生来源记录绑定driverSourceSha256；日志留原执行目录。
