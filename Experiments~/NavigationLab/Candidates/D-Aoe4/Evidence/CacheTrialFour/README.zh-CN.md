# D12 有向格边缓存：四例试验

本分支仅缓存同一几何快照、半径及有向相邻格边的原判断，未更改寻路、站位、物理阈值或输入。源码提交02b340935bd832edb519c366ceefb034c560373a，实际候选DLL为16030a4aff5df5773bd5431870f1d51e3e599774ca001aa8c0ef51bf617ed05e，factory仍为NavigationLab.DAoe4.Aoe4ProgressExchangeFactory。

这只是四个原用例的等价与开销试验，**没有运行新完整49，不选作最终D**。几何原式对照自测另有412146次比较及14轮冷/热缓存、不同半径、变更失效和多实例检查，见仓库Verification~/NavigationDCache。

| 原用例 | 结果 | 原9e API秒 | 缓存版 API秒 | 原／新累计分配GB |
| --- | --- | ---: | ---: | ---: |
| open-arena | 通过 | 4.635 | 2.988 | 1.895／0.844 |
| dynamic-route-block-and-open | 通过 | 1.311 | 2.756 | 1.198／0.656 |
| forest-full44-large | 失败，27移动 | 207.115 | 261.476 | 138.463／135.167 |
| large-portal-grid-128 | 失败，1移动 | 418.611 | 205.359 | 212.860／158.182 |

四例的Input、PhysicalInput、Geometry、Status、Trace、Commands、FinalState均与原9e完整个例逐字相同；两个通过项均全新实例重放一致。Core/Oracle DLL字节相同，Runner实际编译输入身份仍为e17b632f65d9b62990907f10726b84af8d6fa3747c1aa6d20bc0dbd260195544。四份新原始报告分别通过严格单报告校验；原9e在本次归档时整轮仍在运行，未以四份原个例冒称原49已结束。

128人大图API耗时下降约2.038倍，但完整wall time仅540.659→330.038秒，其他场景存在回退，不能宣传统一提速。累计分配是整个调用过程总量，非同时驻留内存。测量受并发云端负载影响，仅作实现诊断，不是游戏原版、设备FPS或公平性能排名。

cases/保留四份完整原摘要、完整案例报告、输入、**完整压缩轨迹**、构建/执行指纹、实际Compile清单和日志；本目录没有用窗口代替完整轨迹。source-and-four-dlls.zip按冻结manifest逐条包含实际源码/编译项和四角色DLL，source-package-index.json绑定每个成员。original-9e-reference仅保存对照个例及原构建身份。comparison.json包含反向变化、Root未选结论及编排进程清理记录。

执行下面命令可验证归档字节、源包成员、DLL身份、输入及四项物理对照：

    python3 verify-archive.py

该命令不重新仿真，也不声称在任意机器上从绝对路径构建清单直接重验运行来源。重新仿真可从此分支根目录按Tools~/NavigationLab/freeze-run.py的相同factory、stress套件及四个--scenario名分别冻结运行，使用新输出目录；迁机重建Runner会改变根路径元数据，需另行记录。

可选整轮编排已在四场景子进程完整结束后终止，未启动d-cache-v2-final-stress；未暂停或终止原9e测试，未修改原D、其他候选、main或UnityPackages。
