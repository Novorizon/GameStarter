# C 分支证据索引

最终选择是 C33，源提交 `69d778f`。共同 v2 的49项完整执行为 **46通过、3失败、0不支持**，不是全绿实现。实际候选 DLL SHA256 为 `1f5318fa321da4c4d6b567393b3e1ea2da0b6737250f1c3d721f3038c8c0d372`。

| 证据 | 范围与结论 |
| --- | --- |
| [Final-C33-v2-stress49](Final-C33-v2-stress49/README.md) | 最终49项、全部输入、完整逐例报告、实际源/DLL/构建与执行身份、关键原始轨迹及严格核验。Full41是同次执行按输入 SHA 派生的38通过/3失败子集。 |
| [Supplement-goal-clearance/final-C33-frozen](Supplement-goal-clearance/final-C33-frozen/README.md) | 同最终 DLL 的3个端点净空补充全部通过；另1个自目标移动例通过。分母3与1分别列出，不加入49。 |
| [Stage-C17-v2-stress49](Stage-C17-v2-stress49/README.md) | 旧阶段47通过/2失败；包含最终版本发生回退的反向对照，不代表最终版本。 |
| [Stage-v9-formal-37](Stage-v9-formal-37/README.md) | 更早独立37项阶段，32通过/5失败；不可与 v2 的49项混计。 |
| [Audit-C17-actual-loop](Audit-C17-actual-loop/README.zh-CN.md) | 死亡补队旧反例的精确实际重放与局部协同见证；局部35人见证本身不是原200请求验收。 |
| [Audit-C17-wave4](Audit-C17-wave4/README.md) | 旧门户终端错误的精确重放、原格图可行见证与真实连续执行。 |
| [Audit-C27-late](Audit-C27-late/README.md) | 晚到场景旧初始44前提失败的精确重放、代价缺口与只改目标循环的反事实诊断；不将该诊断算原46项通过。 |

`Exploration` 保留未采用和失败的迭代，不使用最高分拼接不同源码的结果。最终三项失败是 `forest-full44-large`、`forest-full45-medium`、`ranged84-before-short1-fairness`；其当前项目协调逻辑仍可在拥堵状态下停滞。不能因此宣称原地图无解，也不能将本项目补充协议的限制归因于商业游戏的闭源实现。

最终 Unity/Mono 同 DLL 复验由主任务单独执行。旧 C17 的三项通过不作为 C33 的 Unity 证据；未绑定上述最终 DLL 的报告不计入最终结果。
