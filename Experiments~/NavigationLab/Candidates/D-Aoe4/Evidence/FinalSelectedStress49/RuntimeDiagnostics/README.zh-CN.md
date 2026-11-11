# D256 原始长测的最小诊断记录

原进程为 PID119238，仍使用选定的9e61候选DLL。本次只采集两次线程栈JSON，命令为现有.NET运行时的createdump --crashreportonly --name <audit输出路径> 119238。没有使用heap/core选项、崩溃信号、重启或修改候选源码、DLL、逻辑tick与时限。工具前后均记录目标为存活且处于R状态；目录没有生成heap/core转储。

第一次：2026-11-11 10:52:39.047–10:52:39.690 UTC，约0.643秒。第二次：10:55:31.467–10:55:31.854 UTC，约0.387秒。两次工具调用累计约1.030秒；附加诊断可能短暂停顿目标，这不是精确暂停时长。原wall/API计时可能包含该扰动，不能用于设备FPS或性能排名。

第一次主线程为WorldDefinition.Center → HierarchicalFlow.FindRoute → AoeNavigator.Step。现有DLL的反射IL定位确认FindRoute的0x2f6位于A*边松弛已写cost/parent后、准备open.Push/Heuristic的位置，不是父链重建。第二次为PointSegmentSquared → CanSegment → TileDistances → Graph → FindRoute → Step。两次样本没有证明死循环，也不是CPU热点分布采样。

之后可读轨迹前缀已从1432推进到2775微步，读取于2026-11-11T10:57:08.389401+00:00；该旧样本中256人全部入场，118攻击、138移动。压缩输出有缓冲，完整行仅构成可见进度下界，不代表当前精确步数、验收通过或停滞结论。正在刷新的last-case-progress.json不纳入本目录指纹。

源码审查已确认净空、门户图、分量及流场存在缓存。可能的后续原型包括几何实例内有向CanEdge精确布尔memo，以及TileDistances的有界精确缓存或tile局部表示；它们尚未被本次诊断证明有效，也没有被选入本轮9e61成绩。不能混用新原型结果填补原49。原128例记录了129次静止圆重建、130次门户图构建和约212.86GB候选累计分配，但这些事实本身不能证明D256的唯一瓶颈。

原始快照、命令和计时分别位于metadata*.json、createdump*.log、threads*.crashreport.json；findroute-il.log保留IL定位，diagnosis.json保留诊断边界与128例来源SHA。摘要只采用方法名、时间、命令及数据证据，没有复制寄存器地址、环境变量或凭据。
