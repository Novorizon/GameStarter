# A–E 导航比较协议

本目录是独立导航实验，不会替换正式战斗入口或修改 UnityPackages。A 使用 `8c0dd50` 中冻结的15份领域源（聚合 `074502ae2c712a92094d4b851c451afba25ac300c063f78e28b605b767a72c2f`）；B–E 是独立候选插件。商业项目的公开架构复现与本项目新增调度必须在各候选 README 中分开标明。

## 构建与入口

Core、独立 Oracle 和候选均为 `netstandard2.1` / C#9，Runner 是 .NET8。正式记录通过 [冻结构建脚本](../../Tools~/NavigationLab/freeze-run.py) 先双读冻结源码、在快照中编译，再核对 MSBuild 实际 Compile 文件及候选/Core/Oracle/Runner 四个 DLL。只检查运行前后源码未变不足以证明 DLL 的来源。

开发调试入口：

```bash
dotnet build Experiments~/NavigationLab/Runner/NavigationLab.Runner.csproj -c Release -m:1 --disable-build-servers
dotnet Experiments~/NavigationLab/Runner/bin/Release/net8.0/NavigationLab.Runner.dll \
  --assembly /path/to/candidate.dll --factory Full.Namespace.Factory \
  --suite smoke --scenario forest-wave-1 --no-replay --output /tmp/nav-debug
```

`--suite full` 是共同的完整有限矩阵；`--suite stress` 增加不同几何/请求顺序和大地图样本。`--export-only` 导出所有实际输入，不运行候选。`--factory` 明确选择一个 DLL 中的具体控制器变体，实际工厂类型和 DLL 指纹会进入报告。`--no-replay` 仅用于开发诊断，不算完整重放证据。

## 统一规则

- API 的一次 `Step()` 是5ms，单位速度参数仍为每50ms允许移动的毫米数。每一步按前后快照之间的直线段解释运动；不得隐藏子步内的额外路径、传送或身体半径变更。
- 出生只在原声明入口格心发生。拒绝出生的请求保留原ID/半径/速度/射程；出生、删除和世界更新均不能瞬间移动其他活体。
- Oracle 不引用任何候选实现。它用原输入半径检查相对圆盘连续扫掠、身体到闭格矩形/基地圆的整段距离、速度、原目标、身份守恒、实际攻击距离与视线。
- `Waiting` 必须处于可驻留格和集结范围；`Attacking` 必须物理到位且满足射程/视线。长射程攻击位置可以超出候场范围。出生格和通行专用格不能作为完成位置。
- 几何容差为0.1mm，格心到位容差为5mm。完成后要连续保持200个完整50ms区间；每5ms检查状态、ID及相对于驻留起点的漂移，超过0.1mm立即重新计时。报告记录实际最大漂移。
- 固定场景已知攻击人数作为门槛：原72目标集12攻击、真实Forest波次及44/45/晚到46的原人数、大混合阵容的总攻击人数和短程份额。没有将一般静态二分匹配上界当作未知地图可达结论。
- 原72输入保留原终点集合。Forest保留原500mm地面、出生点、所有holding mask和真实Luban profiles；普通围攻不向候选传可行见证的路径、站位或执行顺序。明确的对向命令才使用 `AssignedGoalCell`。
- 超容量场景分别记录真实面积上界与离散终点规则。9×9/r490的面积上界60与终点数39不同；要求真正填满39并保留其余队列，死亡后用原队列补回。
- 反向流、大体型、快速单位、动态地形和目标位置更新逐项记录能力边界。未支持是 `Unsupported`，不会计入通过；基本静态围攻与死亡补位不能靠关闭能力标志规避。

`target-relocation-*` 只测试瞬时、离散的目标位置更新。后置更新在旧24个身体全部实际到位后把目标向左平移11m；独立证书排除新圆覆盖旧身体，并验证新16个攻击格。没有验证实体目标沿11m连续运动的扫掠，也没有把这种更新称作移动实体的完整物理模型。

## 证据与边界

每个实际输入导出为JSON，记录世界、原始请求和事件规则。输入指纹不依赖工厂；另有忽略ID的物理输入指纹，避免把相同流量换seed标签当新覆盖。每5ms完整状态进入哈希，压缩轨迹保存可重建的变化帧；完整出生成功/失败、删除和世界更新命令另有哈希及单场报告。主摘要不重复塞入全部命令。

正式模式提供 `--build-manifest` 并使用 `--require-build-manifest`，记录编译与运行来源；默认对成功场景在全新实例完整重放。交错多实例/暂停检查使用不同地形、目标与profile，并在另一实例删除、重生及按声明更新世界。此项不等于并行线程安全。

进程退出0表示所选矩阵全部支持且通过；退出1表示真实失败或来源/隔离错误；退出2表示存在未支持能力。失败和未支持均保留，不把它们重新归类成完成。动态输入先由Oracle独立检查；会瞬间覆盖活体的事件标为`InvalidInput`，不计算法失败，也不计通过。

候选的Create、TrySpawn、Step、Remove、Apply、Dispose及Snapshot耗时/分配分别记录；输入复制、Oracle和JSON不混入候选API计时。Snapshot包含验证所需的额外读取，需单列。开发环境并行运行的墙钟数据只供诊断，不能作为设备FPS或公平CPU排名。

这些是有限、可复现的地面二维圆盘场景，不是数学普适性证明；本轮没有覆盖加权地形、水陆飞行、多高度、连续运动的实体障碍、IL2CPP或移动设备性能。真实Unity/Mono加载同一候选DLL另由Native记录，仍与正式业务接入及设备验收分开。
