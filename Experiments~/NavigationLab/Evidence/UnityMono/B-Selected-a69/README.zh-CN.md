# B最终选定a69源码的选择性Unity/Mono检查

两种factory使用同一候选DLL `a69bc87d5e10f614f463929f374d2b294ed0c0840377c91fcfc7b7f7280e2831`。Unity 2022.3.62f1 Editor Play/Mono、Null图形后端；实际加载Core/Oracle/候选DLL、前后源一致、manifest新鲜、shutdown与工程清理全部验证通过。

| factory / 场景 | 结果 | 实际出生 / 存活 | 攻击 / 候场 / 移动 | 最后tick |
| --- | --- | --- | --- | ---: |
| OrcaFactory-forest-single-large | PASS | 1 / 1 | 1 / 0 / 0 | 1408 |
| OrcaFactory-forest-full44-large | FAIL | 44 / 44 | 9 / 8 / 27 | 12000 |
| OrcaFactory-dynamic-route-block-and-open | PASS | 24 / 24 | 16 / 8 / 0 | 800 |
| OrcaYieldFactory-forest-single-large | PASS | 1 / 1 | 1 / 0 / 0 | 1408 |
| OrcaYieldFactory-forest-full44-large | FAIL | 44 / 44 | 6 / 5 / 33 | 12000 |
| OrcaYieldFactory-dynamic-route-block-and-open | PASS | 24 / 24 | 16 / 8 / 0 | 800 |

B0与B1各2通过、1失败，整次退出1。两者Forest44都确实生出并保留44人，仍有移动中单位，到12000tick未完成，因此没有稳定保持；不能用无碰撞替代全员到位。单体和动态堵路场景通过，并保持完整200个50ms区间。每5ms按原半径、速度和世界做独立连续运动检查。

这是6项选择性Mono检查，不代表49项全在Unity执行；没有图形、IL2CPP或手机性能结论。.NET与Mono的浮点轨迹及拥堵人数可以不同，各自按实际报告记录，不能用一方人数覆盖另一方。

原输入逐字SHA、实际加载DLL与控制器脚本hash在native-provenance和validation内，转换后的输入与两个模型构建清单同目录保留。原始Unity日志留执行目录；本包不把B0与B1的总数混作某一个factory成绩。
