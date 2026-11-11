# B：ORCA、动态重寻路与路线简化

B 分支从冻结 A `8c0dd50` 分离。这里只增加独立候选和实验，不替换正式战斗或 SDK。

选定源码 `8c6a4a2` 的共同49项正式测试已完成：B0为28通过、21失败；B1为45通过、4失败。通过项均完成全新实例重放，实际源码/DLL、隔离与暂停检查通过。同一选定DLL的Unity/Mono子集两工厂各2通过、1失败，Forest44未全员到位。[完整冻结证据](Evidence/FinalStress/README.zh-CN.md)分开记录模型与Mono人数，不宣称普适或设备验收。

## 实际实现

- **B0 / `NavigationLab.Candidates.BOrca.OrcaFactory`**：官方 RVO2-CS ORCA 速度求解器，八邻 A* 全局路线，实际停止身体参与动态重寻路，精确静态扫掠和停止身体约束下的可见路点简化，死亡或目标移动后的空攻击位刷新。
- **B1 / `NavigationLab.Candidates.BOrca.OrcaYieldFactory`**：B0 加显式临时让位、到位者返场，以及合法目标交换。相同profile/target且没有显式assigned goal的移动单位可以交换交叉目标，要求总欧氏距离至少下降四分之一格且两条新路线静态可达。所有永久终点集合、实际体型、速度和目标身份保留。B1 的结果单独报告，不覆盖 B0 的失败。

路线节点为原格边长的一半，支持身体通过偶数宽走廊的中线。**最终站位仍只允许原世界的 holding cell**；路线节点不会增加最终目标数，也不修改地图、单位身体或速度。

目标由当前世界、实际请求和可用目标动态计算。没有读取旧72人的见证路线、预分配目标或成功次序，也没有调用 A 的 SIPP 代码。统一 Runner 可以提供显式目标测试，此时保留请求的目标身份。

每次 Step 是5ms；配置速度仍使用mm/50ms。ORCA 时域2秒、障碍时域0.5秒、邻居上限256，邻域至少5米或12倍本体半径。实际 ORCA 半径等于请求身体半径，没有沿用旧1m实验的20mm永久膨胀，避免把Forest的500mm相切终点人为改成无解。

## 实际物理执行

ORCA 给出下一微步提议后，候选仅在原始float位置未推进时，对原有路点0.25mm邻域作速度与0.025mm步长双重约束的epsilon点提案，再由连续非穿透执行器检查障碍、基地、速度和双方相对运动。发现不安全提议时，本微步保持相关身体的原位置并将速度置零；双方停止采用闭包迭代，避免前车停止后后车继续穿入。

`UnsafeStaticOrSpeedProposalStops` 和 `UnsafePairProposalStops` 记录这些提议。最终运动仍由**不同实现的共享 Oracle**逐5ms验证，不能把提议过滤说成原ORCA求解器从未产生碰撞风险。所有提议保留原始身体和速度上限，并检查完整运动线段；执行器拒绝不安全提议，无法继续前进仍会作为到达超时失败保留。

状态除目标距离外还检查实际射程/视线或实际候场半径；持有 Goal 不等于身体已经到位。基础实现首轮在候场外边界提前约1mm标记Waiting的问题已保留为开发失败并修正，不计为“ORCA不适用”的证明。

基地的ORCA外包多边形按最小射程选择精度，常规场景通常128条边，短/零射程提高精度。对靠近圆边界的合法Goal加入径向切平面，再逐注册半径核对全部原holding位置，不能因外包近似删掉终点。相关指标为 `BasePolygonGoalCompatibilityChecks/Failures`、`CriticalGoalTangentPlanes`、`MaximumPolygonExtraClearanceMicrometres` 和顶点数。原精确圆的物理扫掠始终保持。

自检覆盖r250/490/700与range0/10/450的非轴向圆接触终点；另实际执行零射程、基地与上边界同时相切的到位/攻击/200tick稳定。目标距离容差不替代实际攻击条件：攻击Goal需真实满足攻击距离/视线才会停止。

## 官方代码与标准库适配

上游为 [官方 RVO2-CS](https://github.com/snape/RVO2-CS/tree/de124a275d51d421bb9e2a77952210527ab60b04)，固定提交 `de124a275d51d421bb9e2a77952210527ab60b04`，Apache-2.0。`ThirdParty/upstream.json` 记录所有原字节 SHA，许可证及版权头保留。上游源码未修改。

上游的近期BCL参数校验API不属于netstandard2.1。新增的 `ArgumentGuardCompatibility.cs` 在RVO命名空间提供等价int/float校验，抛出实际 `System.ArgumentOutOfRangeException`；这不改变有效参数下的速度求解器。候选目标为netstandard2.1/C#9，Runner为.NET8；Unity/IL2CPP结论分别以对应实际运行报告为准。

上游 Agent/KdTree 硬编码 `Simulator.Instance`，且 `_instance` 是静态只读字段。`SimulatorContext` 在共享锁内切换完整实例字段，调用结束后保留本battle状态并恢复host字段；第三方文件仍保持原字节。NumWorkers=1、DoStep同步结束后才恢复。它提供**串行交错隔离**，不宣称每个battle有可并行独立的原生模拟器或同一个navigator可以并发调用。

死亡和动态地形变化会重建本battle的RVO索引，保留其余身体的真实位置、速度和请求ID。自检比较单独执行与插入另一battle Step/出生/死亡时的全轨迹，并验证回调实际变更后抛异常仍恢复host、局部battle实际变更不丢失。

## 构建与执行

共享 Core/Oracle/Runner 同步后，在本分支根目录：

```bash
dotnet build Experiments~/NavigationLab/Candidates/B-Orca/NavigationLab.BOrca.csproj -c Release -m:1

dotnet run --project Experiments~/NavigationLab/Runner/NavigationLab.Runner.csproj -c Release -- \
  --assembly Experiments~/NavigationLab/Candidates/B-Orca/bin/Release/netstandard2.1/NavigationLab.BOrca.dll \
  --factory NavigationLab.Candidates.BOrca.OrcaFactory --suite full --output Artifacts/NavigationLab/B0

dotnet run --project Experiments~/NavigationLab/Runner/NavigationLab.Runner.csproj -c Release -- \
  --assembly Experiments~/NavigationLab/Candidates/B-Orca/bin/Release/netstandard2.1/NavigationLab.BOrca.dll \
  --factory NavigationLab.Candidates.BOrca.OrcaYieldFactory --suite full --output Artifacts/NavigationLab/B1

dotnet run --project Experiments~/NavigationLab/Candidates/B-Orca/SelfTest/NavigationLab.BOrca.SelfTest.csproj -c Release
```

开发时 Core/Oracle 可以用 `NavigationLabCoreRoot`、`NavigationLabOracleRoot` MSBuild属性指向协调工作树；发布分支使用仓库内部相对路径。通过、失败、不支持、原始队列和身份守恒由共享 Runner 报告。有限矩阵只说明已执行输入上的表现，不构成对任意多单位场景的完备性证明。

## 开发记录

`Evidence/Development` 保留初始实现的范围状态失败、修复后的smoke和原72对照，以及当时完整候选源和实际DLL。b-000两工厂均5/7，open-arena和原72因候场半径边界提前标Waiting而失败；b-001两工厂smoke7/7，但运行时共享harness有修改，不能作为最终冻结同源证据。b-002单独原72两工厂通过。这些历史结果不能替代选定版本的完整49项报告。

动态路线对停止身体同时检查节点与连接边，不能让擦过圆盘的边反复被物理执行器拒绝。临时让路使用实际路线阻挡关系、附近物理空位置与动态身体约束的完整A*路线，并保留原永久Goal；终态仍要求返回或合法重新取得永久目标。

## 浮点执行容差回归

独立Oracle的数值容差固定0.1mm。B的执行器最初几乎零容差：Y原点-60mm、500mm恰宽走廊、r250身体时，RVO将2190mm表示成2190.000057mm，导致7000微步全被静态门截回，身体始终停在出生点。这是适配层bug，不能计作ORCA的寻路能力边界。

执行层现使用0.025mm数值容差，仍由原0.1mm独立Oracle核对原始半径/速度。静态Goal资格和世界输入不变。相同走廊已真实到位并稳定200tick，零射程接触、r250/490/700外包Goal、移动target、交错实例与异常恢复也重新通过；原源码/DLL/失败及修复后报告保存在 `Evidence/OffsetCorridorRegression`。指标 `ExecutionNumericalToleranceMicrometres=25` 公开该数值规则。

## 策略对照边界

`Evidence/StrategyDevelopment` 保存003至008轮已完成的开发结果摘要、冻结构建清单与SHA；这些使用`--no-replay`的结果不作为最终正式矩阵。007增加停滞移动同伴让位，Forest45从006通过退为1个Moving超时，因此已撤回。008同时加入交叉移动目标交换，Forest45通过但44仍未完成；009隔离目标交换本身，不借用逐场景最佳策略。007/008的`MovingPeerYieldRequests`是在进入Yielding后取样，混入原已到位者，不能据此推断真实移动同伴请求数。

ORCA的[官方说明](https://gamma-web.iacs.umd.edu/ORCA/)和本项目所用[RVO2-CS](https://github.com/snape/RVO2-CS)提供局部速度选择；这里的全局A*、终点分配、让位与目标交换属于本实验适配。到达死锁不能仅凭一个失败样本归结为官方ORCA缺陷，模型通过也不证明任意地图和单位数上的普适收敛。

## 实际起点和拐角精度回归

执行器允许0.025mm数值带，但旧全局图以零数值带拒绝真实浮点起点：相同点共享Oracle可通行，ClosestJoin却返回-1。当前只给“实际起点→已有严格合格节点”的连接同一0.025mm数值带；普通图节点、普通边和Goal资格仍保持原规则。`PlanningStartJoinToleranceMicrometres`公开该值。可见性简化仍可保守拒绝快捷线，完整严格节点路线保留。

恰宽折返走廊的完整回归还发现旧0.25mm提前跳路点会配合ORCA浮点渐近积分，在拐角留下无法完成的微小分量。当前仅当原始ORCA输出位置等于本步起点的精确float roundtrip（位置未推进）且roundtrip误差不超原0.025mm数值带时，在该原邻域内提出沿既有路点的epsilon运动；单次步长同时限于原speed/10及0.025mm，经过同一static/pair/speed闭包检查，实际到点后再推进路点。原ORCA已整合出的任何真实位移均保留，包含到位者的真实让路；此条件并不证明solver零步必由量化造成，因此属于本项目有界epsilon控制提案，不是官方ORCA输出。`NearWaypointServoProposals`总计提议，`NearWaypointServoMotionProposals`计非零提议，`StationaryRoundTripCorrectionProposals`计无推进表示纠正，`NearWaypointRawMotionPreserved`计保留分支；提议数不等于已执行位移数。

空world-change清掉路线后，新的B0/B1均真实走完被直线障碍阻隔的U路线并连续稳定200tick，逐5ms由原Oracle检查。join-only首拐角失败及源/DLL另存`Evidence/StartJoinRegression`；旧两轮未完成49项矩阵和旧Unity原件移到`Evidence/SupersededBeforeStartJoin`，不能混作新源结果。这些修复属于适配和执行精度，不据此声称ORCA理论改善。
