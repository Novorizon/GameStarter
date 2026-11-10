# 塔防导航独立验收

本项目直接编译实际 `Assets/HotUpdate/TowerDefense/Domain` 与 `Domain/Navigation` 源码，不复制导航实现，也不引用 Unity、SDK 或测试替身。

```sh
dotnet run --project Verification~/TowerDefenseNavigation -- --output Artifacts/Navigation
dotnet run --project Verification~/TowerDefenseNavigation -- --stress --output Artifacts/NavigationStress
dotnet run --project Verification~/TowerDefenseNavigation -- --scenario three-hundred --output Artifacts/Navigation300
```

默认执行 19 类场景；`--stress` 使用 4 个固定种子，共 76 个样本。种子改变出生入口、速度与单位编号的组合，判定门槛相同。每个成功样本按相同输入时间戳重新执行，比较逐 tick 与每批 7 tick 的最终状态。该重放验证固定 tick 接入方式和确定性，不代表可直接跳过导航 tick。

## 场景与通过条件

- 空旷场地、不同速度、大半径转角、单格窄道、U 形障碍、直角走廊和十字路口：所有请求单位实际进入场景，到达目标周围的攻击位或集结位。攻击位必须填满；外圈等待必须真的到达自己的目标格，不能把远处暂停移动算作成功。
- 单出生口连续拥堵：外部出生队列保留被占用请求，入口腾空后继续；不得重叠出生或静默丢单位。
- 原始 5 m 集结范围的十字路口与单出生口各 72 单位、原始 12 m 集结范围的 300 单位：必须全部实际到达；这些场景保留了早期逐层贪心占位发生保守拒绝的地图和请求规模。
- 永久通行巷道与停车位：另外提供十字路口 72、单出生口 72、300 单位三种明确布局。独立证书删除全部停车格后验证通行骨架连通、所有入口接入，以及每个目标停车格有合法边连接骨架。证书容量只统计射程外停车位，不借攻击位凑人数。这是固定尺度、单一体型/射程场景的结构证明；不向混合体型/射程推广。
- 不可达目标：所有请求必须返回明确的 `NoStaticPath`，不能假装到达。
- 全满攻击位与死亡补位：先全部聚集，再移除半数攻击者；原来的等待者必须进入攻击位，直到攻击位再次填满。
- 保守容量：原始 4 m 集结范围的十字路口保留 72 个请求，允许全部到达或明确拒绝；200 单位的小场地必须明确拒绝超过候选上界的请求。已经接纳的单位仍必须正常到达。分别记录 `CapacityExceeded`、`GoalAccessBlocked` 和实际接纳/拒绝数；这表示当前优先规划策略与已有站位承诺的限制，不代表地图物理绝对无解。
- 混合射程：先建立远程外环后进入近战，及先建立近战内环后生成整圈远程；随后删除近战并验证新的近战进入、删除远程并验证原候场者补位。为未来进入留出的外环缺口不能算作算法失败，也不能简单要求各类独立候选数同时全部站满。
- 多入口尾支：先从主入口聚集，再从没有候场位的支路入口生成单位；后来的单位必须真正离开支路并进入目标周围合法站位。

每个 tick 独立检查圆盘之间的连续相对运动、圆盘与障碍矩形、圆盘与基地圆的最小距离；同时检查占格、交换、速度上限、攻击距离和攻击视线。检查器先使用已知的转角相撞、穿障碍等反例自检。格子契约额外检查偏心基地圆造成的“端点合法、中途穿越”问题、对角线禁行、地图边界和非法 tick。

前置规划契约还验证：插入未知体型/射程的候选数查询不会改变完整 800 tick 轨迹，也不等于注册该类型；稳定容量限制下连续 200 tick 请求不再展开搜索；删除攻击者后缓存失效，新的单位可以准入并补回攻击位。

`summary.json` 给出每个样本的结果、实际人数、容量、节点扩展数、重规划次数与本机耗时。各场景 JSON 保存地图遮罩、目标、出生/移除命令及每 10 tick 的真实毫米坐标轨迹，失败时另存失败瞬间。完整每 tick 都执行几何断言，轨迹采样不降低断言频率。JSON 可用于回放和可视化。

汇总记录生产与验收 C# 的逐文件 SHA256，并在运行结束复核源码未变。`elapsedMilliseconds` 是主执行耗时，`replayElapsedMilliseconds` 是重放耗时；汇总自己的总耗时另含契约测试与 JSON 序列化。均为当前机器的测量，不是移动设备实时性能保证。

`outcome=all-admitted-and-arrived` 表示全部请求真实到达；`conservative-admission-limit` 表示按场景约定正确报告保守拒绝；`unreachable-rejected` 表示正确报告不可达。不能把这三种通过结果都称为“所有单位到达”。

```sh
python Verification~/TowerDefenseNavigation/visualize.py Artifacts/Navigation/three-hundred-units-parking-lanes-1.json Artifacts/Navigation/replay.html
```

生成的 HTML 自带地图和轨迹，无需网络，可以拖动时间轴并点击单位查看目标与轨迹。

本项目验证纯逻辑；实际 Luban 地图与每波高血量无塔战斗由主回归和 Unity 原生验收覆盖。纯逻辑成功不等于画面、贴图、交互、IL2CPP 或移动设备验收通过。
