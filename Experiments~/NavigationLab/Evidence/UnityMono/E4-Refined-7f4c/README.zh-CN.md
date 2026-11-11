# E4精细连接的选择性Unity/Mono图形验收

本包仅绑定候选DLL `7f4cdb6b660ab3280276239034bd763d084b5a28d7411021cc30cdf4fb11869f` 与同包模型构建清单。三项均在Unity 2022.3.62f1 Editor Play/Mono实际运行并渲染；源/DLL前后一致、运行manifest新鲜、shutdown及工程清理均通过。后续若候选源码修改，不能把本包作为新DLL的验收。

| 场景 | 结果 | 存活 | 攻击 / 候场 | 最后tick | 实际渲染 |
| --- | --- | ---: | --- | ---: | --- |
| WarcraftRefinedFactory-u-obstacle | PASS | 56 | 16 / 40 | 351 | True |
| WarcraftRefinedFactory-forest-full44-large | PASS | 44 | 18 / 26 | 1618 | True |
| WarcraftRefinedFactory-target-relocation-after-settlement | PASS | 24 | 16 / 8 | 1313 | True |

每5ms使用共享独立Oracle检查原身体、速度和全段运动，最终保持200个完整50ms区间。U形场景检验精细连接修复在Mono执行；Forest44检验无炮塔围攻；到位后重定位场景确实触发目标变化并重新就位。三个模型原输入的逐字SHA在native-provenance中，转换后输入同目录保留。

图形后端OpenGLCore，设备llvmpipe软件渲染。Root实际查看了Forest44截图，通道、红攻击者、金候场者与蓝基地可见。截图为逻辑调试图，不是商业美术或设备性能验收。未将三项扩写为完整49、IL2CPP、HybridCLR主包、Windows或手机验收。

同DLL在其他场景仍可能失败，见对应模型完整报告；本包不宣称普适性。控制器driverSourceSha256由原生来源记录绑定，Unity日志留原执行目录。
