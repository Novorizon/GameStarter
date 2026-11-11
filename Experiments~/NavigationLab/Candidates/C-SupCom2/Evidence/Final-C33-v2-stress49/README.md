C33最终同源正式阶段：共同49项46通过、3失败、0不支持、0非法输入。全部通过项fresh replay，实际编译/DLL绑定、源不变与多世界隔离检查通过；严格report.py以完整外部原始目录独立核验成功。Full41是同一执行逐项InputSHA核对的38通过/3失败子集，未重复执行。

三失败均按原截止保留：large44全入但2移动/17攻；medium45全入但2移动/12攻；ranged84→short1全85活但2移动/82攻。完整late46（含初始44前提及20攻）、死亡后原200队列52出生−13死=39活/148原队列、Wave4、无新人补位、两300、128/256大图、动态/随机图均通过。C17旧阶段同44/45/该公平性曾通过，作为反向对照保留；C33修复确定的terminal portal与端点半径错误后，原本有利的进场顺序改变，当前项目补充的局部转向/让路/目标代价仍依赖拥堵状态，不具有联合规划完备性。不能归因地图几何无解或商业原版算法缺陷。

完整49压缩报告、原输入、实际编译源（含生成项）、4实际DLL、编译项与执行指纹齐全；两失败44/45、失败公平性、关键late46与死亡补队的原始trace共约26.6MB入Git，其他原件完整保留外部目录，大小/hash/绝对路径见trace-inventory.json。此为明确选择性trace归档，不可伪装成全部原始trace目录。Root严格汇总实际读取 /workspace/navigation-comparison/results/c-final2-v2-stress49。StrictSelfCheck另含逐例证据核验和按真实半径绘制的最终态SVG。

补充Goal净空三例与额外自目标移动例均使用同冻结候选SHA1f5318fa321da4c4d6b567393b3e1ea2da0b6737250f1c3d721f3038c8c0d372通过，分母3与1另列，不加入49。同选定DLL的Unity/Mono复验已完成，2通过、1失败：晚到46与容量死亡补队通过，44人围攻失败。原件及来源位于Native-Selected，早期C17三P不能代替本次。

复现：在安装.NET8 SDK的本分支运行 Tools~/NavigationLab/freeze-run.py，以本仓库同时作为harness-root与candidate-root、该候选csproj/Factory、suite stress、新output与snapshot目录进行冻结；不加--no-replay。失败退出1为真实结果，不是改成通过的脚本异常。旧实际Runner内含当时绝对RepositoryRoot，迁机应重建并重新绑定manifest，不假定旧Runner可在任意路径直接执行。
