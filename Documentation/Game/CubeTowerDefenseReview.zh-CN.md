# Cube 塔防参考审查

审查输入为 [Cube 25836cb](https://github.com/Novorizon/Cube/tree/25836cb62a462891add136a686ceebbb21621958)。本次阅读塔防、地图和寻路源码，没有下载其大型美术目录，没有运行 Cube，也未将其实现或资源复制到 GameStarter。

## 值得借鉴的内容

| 源码 | 实际行为 | 在本项目中的使用方式 |
| --- | --- | --- |
| [WaveManager](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Wave/WaveManager.cs) | 区分“全部生成”和“场上清空”；可选择立即接下一波或等清空；最后一波仍等存活敌人清空才胜利 | 保留生成游标和存活计数的区别。当前肉鸽在清波后选卡，因此继续等清空，不直接启用连波 |
| [TowerBuildManager](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Tower/TowerBuildManager.cs) | 建造预览/合法性、建造与升级费用、失败返还、逐等级外观、出售返还 | 借鉴建造失败无损、升级预览、累计投入计算出售返还的产品规则；数值事务放在领域，模型加载失败由表现补偿/重建，避免用 Prefab 是否成功决定已发生的伤害 |
| [BattleTargetActionResolver](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Battle/BattleTargetActionResolver.cs) | 按目标类型和当前可用性筛选升级/出售动作，配置决定显示顺序 | 后续塔详情使用明确的可用动作及不可用原因；不把业务校验全部写在按钮回调 |
| [NpcManager](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Enemy/NpcManager.cs) | 按路径移动，到基地攻击距离后停步、转向、按攻击间隔攻击；包含眩晕、定身和动作表现 | 借鉴移动/停止/攻击的状态划分。当前仍是到达终点扣基地生命并移除，攻击基地状态机属于后续玩法变更，不声称已移植 |
| [TdWorld](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/AbilityAdapters/TdWorld.cs) | 用 IWorld/IUnit 适配 NPC、塔和基地，技能引擎再做筛选 | 当引入技能、控制与多种目标时评估现有 Ability 包的适配。当前简单单体攻击继续保留纯 C# 实现 |
| [BattleFlowManager](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Battle/BattleFlowManager.cs) | 结束先停止波次与选择，再发布结果，重复结束被挡住 | 保留“先冻结领域终态、后通知表现/结算”的顺序；正式发奖交服务器，不照搬本地奖励入账 |
| [MapDataAStarPathFinder](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Map/Pathfinding/MapDataAStarPathFinder.cs) | 在可走格之间做 A*，校验相邻地形连接 | 未来自由堵路或地形高度影响通行时参考；当前已确定编辑路线和固定点，不引入地图寻路依赖 |

## 当前不适合直接搬入的部分

Cube 的多个 Manager 直接访问 Unity Transform、全局资源、地图和物品服务；技能查询读取 Unity 时间和物体位置。直接移植会破坏当前固定 tick、逻辑/表现分离和 YooAsset 租约边界。其 NPC 移动源码没有体现 RVO 或单位间局部避让，A* 也不等于拥挤避让，不能把它当作已解决本项目 NPC 避障的证据。

后续实现顺序建议为：塔详情/出售规则 → 更丰富的塔升级与攻击类型 → NPC 攻击基地状态 → 按同屏单位量验证查询与避让。每步先明确领域规则、表数据、表现事件和验收条件；不为借鉴旧项目再建立一套全局 Manager。

Cube 根许可证为 GPL-3.0，第三方资产另有来源。本次仅参考职责、规则和交互；如将来要复制具体文件，需先明确对应文件的发布条件。
