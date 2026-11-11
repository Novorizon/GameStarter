# C33同选定DLL的Unity/Mono检查

Unity 2022.3.62f1 Editor Play/Mono，实际候选DLL `1f5318fa321da4c4d6b567393b3e1ea2da0b6737250f1c3d721f3038c8c0d372` 与同包模型构建清单一致。实际加载公共Core/Oracle、来源新鲜、前后源/DLL、shutdown和临时工程清理均通过。

| 场景 | 结果 | 累计出生 | 死亡 | 存活 | 原队列 | 攻击/候场 | 最后tick |
| --- | --- | ---: | ---: | ---: | ---: | --- | ---: |
| Factory-forest-full44-large | FAIL | 44 | 0 | 44 | 0 | 17/25 | 12000 |
| Factory-forest-full44-two-late-small | PASS | 46 | 0 | 46 | 0 | 20/26 | 5486 |
| Factory-capacity-death-refills-original-queue | PASS | 52 | 13 | 39 | 148 | 12/27 | 5861 |

44人场景全出生仍有2人Moving，到12000tick失败；晚到46真实通过初始44前提和20攻击；容量补队实际删除13、补入13，39存活/148原队列。后两项各稳定200个完整50ms区间；每5ms核原身体、速度、世界与全段运动。失败未被无碰撞或提前清场掩盖。

本次2通过、1失败，进程退出1，与真实结果一致。Null图形后端只证明选择性Play/Mono，不能扩成全49/渲染/IL2CPP/设备验收；早期C17结果另存，不借用旧源。原输入SHA、转换输入、实际程序集及控制器hash均留同目录，完整Unity日志留原执行目录。
