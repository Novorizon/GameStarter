# 完整聚集与死亡恢复挑战

本项目直接编译指定候选的 `Configuration.cs` 与 `Navigation/*.cs`，复用独立连续圆盘/障碍/基地圆检查器。测试没有塔，不会靠到达删除 NPC、基地死亡或场景清空完成。

**可以容纳的请求必须全部生成并实际到位。** `CapacityExceeded`、`GoalAccessBlocked`、暂时预约冲突和预算失败都不能消费出生队列。每 tick 必须满足：

```text
累计请求 = 当前活体 + 尚未生成的请求 + 明确死亡
累计准入 = 当前活体 + 明确死亡
```

原 4m、72 人场景已有同一地形与目标的序贯进场 witness。52 到位、20 仍排队属于失败。验收还要求实际攻击位有攻击者，不能把近战停在远处候场、内圈空着当作恢复成功。

## 编译并指定候选源码

```sh
dotnet build Verification~/TowerDefenseNavigationChallenges \
  -p:NavigationDomainRoot=/absolute/path/to/Assets/HotUpdate/TowerDefense/Domain \
  --output /tmp/navigation-challenge-build
dotnet /tmp/navigation-challenge-build/TowerDefenseNavigationChallenges.dll \
  --output Artifacts/NavigationChallenges
```

自定义输出目录后明确执行该目录下的 DLL。汇总中的 `domain` 和逐文件 SHA256 必须对应实际候选，不用输出目录的名字推断版本。可以先复制只读源码快照再编译；运行中候选源码改变会使该轮失败。

`--scenario <片段>` 过滤场景，`--seed <整数>` 指定种子；`--stress` 执行四个固定种子。`--max-ticks` 用于调查，正式运行使用各场景的默认时限。

## 12 类挑战

- 原 4m 场景 72 人全部到位并持续保留 2000 tick。
- 满员后逐一杀死 12 个攻击格的占用者，每次补入新请求，恢复到 72 活体再继续。
- 同时杀死全部前排，补入同数请求并恢复满员。
- 后排、侧面和混合批次死亡后恢复满员。
- 其他 NPC 仍在途中时发生死亡，验证预约、碰撞与补位。
- 四个出生口、不同速度的 72 人完整聚集。
- 远程外环建立后进入近战，随后两次杀死全部近战并重新补入。
- 大体型远程围住小体型近战空槽，检查搬移链体型兼容、基地圆净空和补位。
- 多出生口的混合射程内圈恢复。
- 慢 NPC 堵住单格口，200 tick 后移除：首个可动跟随者须在 20 tick 内实际前进，全部 48 人须在额外 1000 tick 内恢复到位，不能保留已失效的数千 tick 等待安排。
- 无塔连续生成 8/16/18/25/30/29，共 126 人，全部活着聚集并继续保留 2000 tick。
- 有物理容量证据的小场地 200 请求：必须先让已构造 witness 的 39 人全部到位，其余 161 个请求仍在队列中。

出生和死亡 API 不能暗中推进 tick 或移动已有身体；只有 `Step` 可以产生位移。每一步检查真实速度、连续身体相撞、扫过障碍/基地、攻击范围与视线。轨迹每 25 tick 采样，断言每个 tick 都执行。

## 真容量证据

小场地为 9×9、1m 格，外围一圈是障碍，可走外接方形为 7m×7m；基地半径 1m，NPC 半径 490mm。身体全部位于方形内、彼此不重叠且不穿基地。利用 `π > 3.14`，面积可证明同时存在的物理上界为 60，故 200 人不可能同时容纳；这不表示 60 人可达。

另一个独立算法构造并执行了固定入口的 **39 人序贯进场**，共 218 格移动，最小身体净隙 20mm，全部通过连续几何检查。上界与可达下界分别保存在 `capacityEvidence`，不能因为 200 人不可能就只接纳少量 NPC 并通过。

通过这项证明用例的结果标记为 `physical-overflow-proved-and-queue-preserved`，不会称为“200 人全部到达”；其余用例只能以 `all-requested-arrived-after-every-recovery` 通过。
