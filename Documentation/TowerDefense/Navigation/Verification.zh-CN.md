# 塔防导航验收

实现与方案来源见[设计说明](../Navigation.zh-CN.md)，关卡几何见[地图说明](ForestMap.zh-CN.md)，不同算法的实际对照见[实验分支](Experiments.zh-CN.md)。本任务保持在 `feat/navigation-cooperative-recovery` 分支，不合并 `main`。

本轮生产提交为 `9686ac9`；15 份完整领域 C# 源的聚合 SHA-256 为 `d155d9838c07d7de133a3e1ef4424388bd441086f99f27968c4163bc1d7f2582`。纯导航工程实际编译其中 11 份配置/导航源，Unity 另编译完整热更程序集。[完整来源指纹](source-sha256.json)记录领域、测试、资源、原生日志与交付报告。地图 SHA-256 为 `ad5dd54b722c97779ad6b0f6e22548d3288ed380c8d758b7615d1e6894165256`，Luban 配置版本为 `forest-navigation-2`。

## 领域与接入结果

| 验证 | 最终结果与解释 |
| --- | --- |
| [完整压力矩阵](navigation-stress.json) | 76/76。68 项全部请求实际生成、抵达或按显式死亡恢复；4 项静态不可达；4 项验证明确终点规则下的容量队列。不能将后两类说成全员抵达 |
| [严格完整性挑战](strict-navigation.json) | 56/56。48 项要求全部活体到位与补员完成；8 项具有独立物理上界及可行下界的超容量队列检查 |
| [真实林地饱和](forest-saturation.json) | 单类型44/45、双速晚到46全部到位；全部攻击位置逐一死亡补回，共70轮 |
| [森林过程死亡](navigation-lifecycle.json) | 30/30。外撤、临停、返程、小体型通行/已攻击、同时删除两门/全部四门；每例恢复44大体型+2小体型、20攻击/26候场、无队列，稳定200 tick |
| [攻击份额交换途中死亡](quota-exchange-lifecycle.json) | 3/3。实际移动中的请求者、永久退到候场区者、临时让路者分别死亡并补员，最终100活体、16短程+68长程攻击、无未生成请求 |
| [需求缩减与重建](attack-quota-lifecycle.json) | 四入口×两模式，共8例；16→1→16恢复，活体归零不空留位置，新波从最少服务重新开始，真实空攻击位可以继续增长 |
| [事务审计](admission-transactions.json) | 52条检查；预算分类、63/64 tick退避、双速度缓存、真实移除立即唤醒、精确回滚、无残留预约和同tick事务隔离。唯一holding饱和证书与预算耗尽分开 |
| [业务接入](model-checks.json) | 97项API模型/数据回归；含真实Luban与导航资源、8个种子实际策略通关。另12项旧折线参考与13项账号测试后端检查，不作为真实服务器证明 |

上述报告均记录运行前后源码/测试指纹，不能拿旧报告代替后续修改。独立编译、原始失败与完整压缩命令记录保留在 `Verification~/NavigationGateLifecycle/Evidence/`、`Verification~/AdmissionTransactionAudit/` 和各实验分支。

### 原72人反例的完整修复

原4m十字场景的52/72不是物理容量。现在使用相同地面、半径、速度、入口分配和**原72个终点**，全部72人到位；12攻击、60候场。测试显式保持四个旧排除内格只作通行，不能借新增4个合法攻击格冒充原场景通过。

[逐ID与终点审计](endpoint-and-lifecycle-audit.json)核对24个原72子场景的最终格集合、出生/死亡身份和命令：所有原终点精确覆盖；途中死亡确实发生于非格心且首次到位之前。56份报告合计3280个最终活体都有实际到位记录，528次显式死亡和1236个未出生队列逐ID守恒，结束时无未完成恢复事务。

### 更大的混合阵容

长程84人先占满攻击区，再来短程1人时，最大同时攻击人数是84；可行分配为83长程攻击、1短程攻击、1长程候场。反向顺序为16短程+84长程，保持16短程和68长程攻击，其余长程候场。不能只看总攻击84而漏掉短程长期没有服务。

四个入口、两种顺序均通过，包含短程死亡补回、长程候场补位，以及所有失败出生调用的完整重放。测试按原请求核对半径、速度、射程，并分别锁定各profile实际攻击人数。[独立物理见证](../../../Verification~/MixedProfileWitness/README.md)从旧失败阵型出发构造了8条合法交换序列；它证明可行，生产76项另验证实际实现。

### 真正超容量与稳定等待

9×9小图的200个请求使用半径490。独立面积上界为60，独立连续行走见证能填满全部39个合法离散终点。生产实际到位39、保留161队列；先后显式删除13人后，累计准入52、存活39、死亡13、队列148，没有吞掉旧请求或创建替身。

真实林地合并126请求的接入检查实际准入46，80个保留在外部队列；稳定200 tick新增搜索展开为0。测试塔释放空间后126个请求最终全部生成、击杀并清空预约。该测试塔只证明队列与释放恢复，正常塔数值通过独立策略通关和Unity完整业务验收。

## Unity最终验收

[真实Unity无塔](unity-navigation.json)在Unity2022.3.62f1、Linux llvmpipe OpenGLCore、URP14下10/10通过：291人次分别在各组内全部存活到位，保持200tick，检查8,971,029对连续身体扫掠与64,351次相邻静态障碍段距离。大体型满员时最小身体间隙0mm是合法相切，半径与速度上限保持表内数值。

| 场景 | 实际到位 | 到位tick | 攻击 | 候场 |
| --- | --- | --- | --- | --- |
| wave-1 | 8 | 640 | 8 | 0 |
| wave-2 | 16 | 693 | 13 | 3 |
| wave-3 | 18 | 999 | 14 | 4 |
| wave-4 | 25 | 1048 | 15 | 10 |
| wave-5 | 30 | 1074 | 15 | 15 |
| wave-6 | 29 | 1298 | 15 | 14 |
| burst-30 | 30 | 1080 | 15 | 15 |
| full-44-large | 44 | 2020 | 18 | 26 |
| full-45-medium | 45 | 1020 | 12 | 33 |
| full-44-large-two-late-small | 46 | 5228 | 20 | 26 |

晚到场景从tick0预注册两种profile，要求tick3999时44大体型已经真实稳定；tick4000才释放两名小体型请求，最终20攻击/26候场。新增等待为1228tick，即61.4秒模拟时间；场景速度与地面未变，临停在基地近处通道，原大体型全部回原目标。

[完整业务](unity-gameplay.json)通过两场真实六波胜利（每场126击杀、0漏怪），另有失败/胜利重开、选卡、暂停叠加、射程、序章/建设/任务、返营地与退出清理。[Player脚本](player-compile.json)及[9个示例的13 Editor/9 Player程序集](sample-assemblies.json)通过；[YooAsset构建](resource-build.json)包含27主资源、11 Bundle，导航TextAsset以navigation分类发布。

性能单独核对：未缓存版本曾出现约1777ms墙钟峰值；[诊断基线](step-profile-b7.json)确认零展开也在反复构建静态连通区。最终缓存仅复用同半径的静态地形结果，动态目标、额外堵格及预算内试探仍执行原计算。相同真实输入的[独立分配对照](allocation-comparison.json)中，1487个拥堵热tick平均分配313318→66334字节（减少78.8%），逐tick物理轨迹、节点数和最终状态完全一致；[56项逐字段对照](static-cache-equivalence.json)及52条事务结果也保持一致。

[Unity两次集中复测](step-profile-d155.json)均在tick5228到位，Step合计约1761/1558ms，基线为2913/3442ms；热重放峰值117→15.94ms。首次运行仍出现一次121.67ms CPU峰值，不能将分配下降说成所有卡顿已消失，也不据这台软件渲染云机承诺手机帧率。完整10组最终复验的Step峰值为6.08–60.56ms，其中44/45满员为6.08/8.53ms、晚到46为60.56ms；所有逐tick耗时与慢步事件保留在报告中。

[正常围攻](base-surround.png) · [F3覆盖](base-surround-debug.png) · [46人满员](full-46-surround.png) · [46人逻辑覆盖](full-46-surround-debug.png)。原始逐组CSV与所有正常/F3帧在Artifacts中。

## 失败记录与方案边界

[旧f67版本](Baseline-f67e8e6/README.zh-CN.md)保留原始报告，明确52/72的旧“策略拒绝通过”不再作为完成条件。后续发现的晚到异质补兵45/46、慢堵口24/48、大混合阵容短程无攻击、需求恢复15/16都成为回归门槛，而非通过调整人数或超时掩盖。

当前选择是四邻格、固定身体与速度上限、固定基地和静态地面的协同预约。已到位单位可以真实让路、返场或合法改驻留目标。局部布局失败后原子恢复，再在同一预算内尝试保守布局。未来目标只影响安全时间区间，不能提前作为当前静态墙。

它仍是有限预算的优先规划与局部协调，不是任意多智能体问题的完备算法或最短时间算法。动态堵路、击退、途中变速、移动目标、IL2CPP/HybridCLR原生主包与移动设备尚未按这些新规则验收。这里不据云端模型或软件渲染耗时保证手机帧率。

## 复现

使用固定版本Unity打开 `Assets/Scenes/Bootstrap.unity`，从营地进入出征。正式业务通过YooAsset地址 `TowerDefense/Data/ForestNavigation` 加载几何，`FightSession`创建导航实例。战斗按F3查看身体圆、真实位置、目标格、前进/候场/攻击人数与待生成数量。

```bash
python3 Tools~/audit-forest-navigation.py
python3 Tools~/verify.py --dotnet /path/to/dotnet --framework /path/to/pinned/com.novorizon.framework

dotnet run --project Verification~/TowerDefenseNavigation/TowerDefenseNavigation.csproj -c Release -- --stress --output Artifacts/NavigationStress
dotnet run --project Verification~/TowerDefenseNavigationChallenges/TowerDefenseNavigationChallenges.csproj -c Release -- --stress --allow-compatible-goal-reassignment --output Artifacts/NavigationChallenges
dotnet run --project Verification~/NavigationGateLifecycle/NavigationGateLifecycle.csproj -c Release -- --stress --output Artifacts/ForestLifecycle
dotnet run --project Verification~/NavigationGateLifecycle/NavigationGateLifecycle.csproj -c Release -- --quota --output Artifacts/QuotaLifecycle
dotnet run --project Verification~/AttackQuotaRegression/AttackQuotaRegression.csproj -c Release -- Artifacts/QuotaDemand --stress
python3 Verification~/AdmissionTransactionAudit/run.py --dotnet /path/to/dotnet
```

严格挑战的 `--allow-compatible-goal-reassignment` 明确允许B方案在其他真实合法格重新分配原单位；原72终点集合、Big12/Small4攻击配额、队列和连续碰撞门槛保持不变。旧A协议的逐ID回原门要求不能否决B合法永久改目标。

`verify.py`执行97项接入、12/13项参考与账号模型、默认导航19、严格14、森林过程死亡6、需求变化2、份额交换3及事务审计。完整四种顺序/四入口矩阵另按上方命令运行。相同seed标签不一定产生不同物理输入，报告同时记录ID敏感与去ID输入指纹，不将重复流量冒称新覆盖。

真实Unity无塔入口：

```bash
Unity -batchmode -force-glcore -projectPath /path/to/GameStarter \
  -executeMethod Game.Editor.TowerDefenseNavigationValidation.Run \
  -logFile /path/to/navigation.log
```

需要图形设备，不加 `-quit` 或 `-nographics`，由探针完成Play、截图及退出。原六波分别隔离，另测最大波快速生成、44/45饱和、44大体型先稳后2小体型晚到。每组高生命基地、禁止建塔、无击杀/漏怪，全部实际到位后持续200个无移动tick；结果输出 `Artifacts/TowerDefense/Navigation/`，逐组CSV和正常/F3画面可检查。

完整业务用 `Game.Editor.TowerDefenseValidation.Run`；Player脚本用 `Tools~/unity-validate.sh compile`；资源用 `Game.Editor.GameDataSetup.BuildResources`。无塔验收不能代替正常胜利、失败、选卡、暂停、重开与返营地检查。
