# 选定D12的Unity/Mono子集

Factory为NavigationLab.DAoe4.Aoe4ProgressExchangeFactory，实际DLL `9e6107cfe1dcd6d83ac137314109a145835c764b3a29a87171bde4412572b19c` 与同包正式模型构建清单一致。实际Unity 2022.3.62f1 Editor Play/Mono运行，公共Core/Oracle、fresh manifest、前后源与DLL、shutdown和工程清理均验证通过。

| 场景 | 结果 | 实际出生/存活 | 攻击/候场 | 最后tick |
| --- | --- | --- | --- | ---: |
| Aoe4ProgressExchangeFactory-open-arena | PASS | 48/48 | 16/32 | 252 |
| Aoe4ProgressExchangeFactory-forest-full44-large | FAIL | 44/44 | 17/0 | 12000 |
| Aoe4ProgressExchangeFactory-dynamic-route-block-and-open | PASS | 24/24 | 16/8 | 800 |

2通过、1失败；Forest44全员仍在，但到12000tick尚有移动体，故保留进程退出1。通过场景各稳定200个完整50ms区间，每5ms独立核验原身体、速度和连续运动。

Null图形后端只验证选择性Mono，不代表渲染、完整49、IL2CPP、商业原版或设备FPS。正式49模型仍须完整跑完后另行记录；本包不能使未结束模型计成完成。原始输入SHA/转换输入/运行ID/实际加载程序集和控制器hash同目录保留。
