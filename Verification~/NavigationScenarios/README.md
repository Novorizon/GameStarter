# NPC 进攻导航验证

从 GameStarter 根目录运行，直接编译生产领域代码，读取正式 Luban 字节表及地图解析器。

```powershell
dotnet run --project 'Verification~/NavigationScenarios/NavigationScenarios.csproj' -c Release
```

默认对每个场景运行两遍，比较逐刻位置、状态、伤害及结束条件的哈希。任一断言失败时返回非零退出码。可以在 `--` 后指定场景并输出步进耗时。

```powershell
dotnet run --project 'Verification~/NavigationScenarios/NavigationScenarios.csproj' -c Release -- --timing forest-saturation forest-dense-spawns forest-heavy-leading
```

`--details` 输出最后一刻的单位位置。`--timing` 统计 `FightSession.Step` 的 p95、p99 和最长耗时，诊断计算与打印位于计时范围外。首次运行包含 JIT，比较性能时同时保留第二次运行的结果。

场景覆盖真实地图无塔攻击、32 只及 126 只围站、加密和重型先到、300 秒稳定期、实际击杀补位、多入口与晚到入口、地形绕行和封路、低耐久基地败北、传送门完整路线、相邻路口的循环持锁、建塔中断预约和暂停恢复。每一刻检查单位身体间距、线性渲染插值、地形扫掠、攻击位置和持续转向反复。

六张守基地地图的正式数据均以 100000 耐久运行 3600 刻，检查首次受伤、跨攻击间隔持续扣血以及结束前仍受攻击。林间哨站低耐久用例单独验证败北，两个传送门关卡继续验证原有的逃脱败北阈值。

容量探针使用冻结当前单位后的独立可达性检查。`freeStagingCells` 中相邻格子属于可能重叠的备选位置。`reachableStagingCells` 和 `reachableAttackCells` 表示队首在当前占位下可走到的候选位置。正式地图、实现策略与结果见 [导航研究记录](../../Documentation/TowerDefense/NpcNavigation.zh-CN.md)。

`admitted` 是已进入近区导航的人数，`settled` 是已到攻击位或停在候补位的人数，`localManeuvers` 是近区仍有预约路径的人数。`queueReserved` 表示已预约轨迹、在避让时间窗内等待出发的单位，`queueStopped` 表示无此类预约且至少两秒没有移动的未安置单位。126 只的 180 秒场景允许近区继续收拢。300 秒的常规、加密出兵、重型先到三个场景要求全部停稳，至少 76 只进入基地中心 4.5 米内，并检查外围候补已用完可直接到达且足够靠近的空位。

`ADJACENT` 检查停在基地轮廓外 0.30–0.85 米的候补，距离从单位身体边缘算起。探针依据地形、入口通道和第一圈攻击者判断八个扇区中哪里有可站位置。三个 126 只的长时间场景均要求该带至少有 12 只候补，分布在至少六个扇区和三个方位，连续空着的可站扇区不得超过两个。

补位场景在第 6100 刻使用真实炮塔造成一次击杀，也测试移除南侧、西南侧的单个攻击者、同时移除东侧两个攻击者，以及相隔 10 刻移除南侧的两名攻击者。指定位置的测试要求每个空缺重新有人站住并打中基地，候补实际参战，攻击阵线恢复至减员前的人数。独立的狭道场景要求前方候补先退入侧面空间，后方较小的候补才能穿过狭道并打中基地。可单独逐一复测正式森林 126 只场景中的所有已占用攻击位置。

```powershell
dotnet run --project 'Verification~/NavigationScenarios/NavigationScenarios.csproj' -c Release -- --forest-vacancy-audit
```

`--near-space` 检查停在基地中心 4.5 米外的候补能否进入附近空地。它按这些候补的体型计算地形和占位允许的格点、符合收拢站位规则的格点，以及保持其他单位原地时可走到的格点。`nearMonotonicStagingCells` 还要求候补至少靠近基地边缘一个最小收拢步长。加 `--details` 可查看实际起点与可达落点。多个格点或多个候补可能争用同一片空间，结果表示独立可行性。

```powershell
dotnet run --project 'Verification~/NavigationScenarios/NavigationScenarios.csproj' -c Release -- --near-space --details forest-long
```
