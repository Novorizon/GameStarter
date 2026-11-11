# 大体型终点相容性的补充契约

该驱动使用同一冻结Core/Oracle，包含2个r650、3个r650、650/650/180混合三例。它们是共同49之外的补充，不合并计分。原地图、身体、速度、Holding集合和原输入SHA在运行前核验；独立可行见证只供检查器使用，不传给候选。

从本目录运行已钉住的驱动：

```bash
dotnet bin/Release/net8.0/GoalClearance.dll LABEL /abs/output /abs/Candidate.dll Full.Factory.Type
```

需要.NET8运行时。若需重新构建，`dotnet build GoalClearance.csproj -c Release`使用同目录FrozenDlls；重建后的驱动SHA必须另记，不能冒称原始hand-off二进制。`handoff-manifest.json`绑定原始源码、二进制与输入。

正向要求实际全部入场、无待生成、全体攻击到位、完整2000微步稳定、目标净距不小于原检查容差、全新实例重放一致，独立连续运动见证也须成功。负的Goal净距说明未来站位冲突，不能因物理停止器阻止穿透就当通过。原E修复前失败留在original-e-before/e-before；后续各候选结果必须标自己的源与实际DLL。

A预先声明不支持大于半格身体，应单列能力不支持，不以本驱动的未入场计作其已支持范围失败。这个小契约不证明任意密集目标分配完备，也不替代完整49或Unity验证。
