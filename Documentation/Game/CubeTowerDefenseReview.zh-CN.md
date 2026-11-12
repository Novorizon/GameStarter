# Cube 塔防参考审查

本次审查参考 Cube 的塔防业务源码。 [Cube 25836cb](https://github.com/Novorizon/Cube/tree/25836cb62a462891add136a686ceebbb21621958)

## 值得借鉴的内容

| 源码 | 实际行为 | 在本项目中的使用方式 |
| --- | --- | --- |
| [WaveManager](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Wave/WaveManager.cs) | 区分“全部生成”和“场上清空”，可配置下一批出场条件 | GameStarter 分别记录出兵进度和存活敌人数。增援按时间出场，选卡按击杀积分触发。 |
| [TowerBuildManager](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Tower/TowerBuildManager.cs) | 建造预览/合法性、建造与升级费用、失败返还、逐等级外观、出售返还 | 借鉴建造失败无损、升级预览、累计投入计算出售返还的产品规则；数值事务放在领域，模型加载失败由表现补偿/重建，避免用 Prefab 是否成功决定已发生的伤害 |
| [BattleTargetActionResolver](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Battle/BattleTargetActionResolver.cs) | 按目标类型和当前可用性筛选升级/出售动作，配置决定显示顺序 | 后续塔详情使用明确的可用动作及不可用原因；不把业务校验全部写在按钮回调 |
| [TdWorld](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/AbilityAdapters/TdWorld.cs) | 用 IWorld/IUnit 适配 NPC、塔和基地，技能引擎再做筛选 | 当引入技能、控制与多种目标时评估现有 Ability 包的适配。当前简单单体攻击继续保留纯 C# 实现 |
| [BattleFlowManager](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/TowerDefense/Battle/BattleFlowManager.cs) | 结束先停止波次与选择，再发布结果，重复结束被挡住 | 保留“先冻结领域终态、后通知表现/结算”的顺序；正式发奖交服务器，不照搬本地奖励入账 |

## 当前不适合直接搬入的部分

Cube 的多个 Manager 直接访问 Unity Transform、全局资源、地图和物品服务。技能查询读取 Unity 时间和物体位置。直接移植会破坏当前固定 tick、逻辑与表现分离以及 YooAsset 租约边界。

塔详情、出售、升级与攻击类型可以分别定义领域规则、表数据、表现事件和验收条件。

Cube 根许可证为 GPL-3.0，第三方资产另有来源。本次仅参考职责、规则和交互；如将来要复制具体文件，需先明确对应文件的发布条件。
