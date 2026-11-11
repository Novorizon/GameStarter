# E最终选定F0源码的选择性Unity/Mono图形检查

本包绑定DLL `f0e3f31d8ade5fbb7eb691228b7b7fd71491f30c51ffffb4938755c78b99d29d` 与同包模型构建清单。它包含精细连接、永久Goal半径和相容性与自身旧Claim查询修正；不借用7f旧源结果。Unity 2022.3.62f1实际Play/Mono、运行ID/源及DLL前后一致、shutdown和工程清理全通过。

| 场景 | 结果 | 实际出生 / 存活 | 攻击 / 候场 | 最后tick | 图形验证 |
| --- | --- | --- | --- | ---: | --- |
| WarcraftRefinedFactory-large-body-sparse-group | PASS | 8 / 8 | 8 / 0 | 268 | True |
| WarcraftRefinedFactory-forest-full44-large | PASS | 44 / 44 | 18 / 26 | 1618 | True |
| WarcraftRefinedFactory-target-relocation-after-settlement | PASS | 24 / 24 | 16 / 8 | 1313 | True |

三项各自保持200个完整50ms区间，最大漂移均0；每5ms按原半径/速度/世界做独立连续运动检查。大体型群组实际8人，执行新半径和检查分支；Forest44为无炮塔围攻；重定位场景确实触发更新并重新就位。原3个密集Goal冲突补例及自身促升补例另在模型中验证，不把稀疏群组冒称密集补例已在Unity执行。

图形为OpenGLCore/llvmpipe软件渲染。Forest44 PNG与此前已实看的7f图片字节SHA相同，逻辑通道、红攻击者、金候场者和蓝基地可见。截图是逻辑调试，不能作为商业美术或设备帧率证据。

同DLL在完整模型中可能仍有失败，须查正式49报告；这三项不扩写为全49、IL2CPP、HybridCLR主包、Windows或手机通过。原始输入SHA/实际加载程序集/控制器脚本绑定与转换输入同目录保留，Unity日志留原执行目录。
