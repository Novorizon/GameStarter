# NavigationLab 工具

工具用于独立候选实验，不会将业务 `Bootstrap` 或 `FightSession` 切换到新算法。默认游戏仍使用冻结A的业务实现。各候选分支包含公共框架与自己的候选；汇总分支包含A适配器和跨分支工具。

## 冻结构建与正式运行

需要Python3和.NET8 SDK；Core、Oracle和候选编译为netstandard2.1/C#9，Runner使用.NET8。下例在B分支根目录运行，用新的输出目录，正式结果默认包含全新实例重放：

```bash
python3 Tools~/NavigationLab/freeze-run.py \
  --harness-root . \
  --candidate-root . \
  --candidate-project Experiments~/NavigationLab/Candidates/B-Orca/NavigationLab.BOrca.csproj \
  --factory NavigationLab.Candidates.BOrca.OrcaFactory \
  --snapshots /tmp/navlab-snapshots \
  --output /tmp/navlab-b0-stress \
  --suite stress
```

`--harness-root`和`--candidate-root`也可分别指向相同公共框架的汇总工作树与候选工作树。程序先保存内容寻址快照，再从该快照构建，不直接运行仍在编辑的工作树二进制。源码、实际MSBuild编译项、输入、Core/Oracle/Runner/候选DLL及执行后的指纹写入 `build-manifest.json` 与 `frozen-execution.json`。

| 候选 | project | factory |
| --- | --- | --- |
| A | `Experiments~/NavigationLab/Candidates/A-Sipp/NavigationLab.ASipp.csproj` | `NavigationLab.Candidates.ASipp.SippFactory` |
| B0 | `Experiments~/NavigationLab/Candidates/B-Orca/NavigationLab.BOrca.csproj` | `NavigationLab.Candidates.BOrca.OrcaFactory` |
| B1 | 同B0 | `NavigationLab.Candidates.BOrca.OrcaYieldFactory` |
| C | `Experiments~/NavigationLab/Candidates/C-SupCom2/NavigationLab.CSupCom2.csproj` | `NavigationLab.CSupCom2.Factory` |
| D0 基础/历史 | `Experiments~/NavigationLab/Candidates/D-Aoe4/NavigationLab.DAoe4.csproj` | `NavigationLab.DAoe4.Aoe4Factory` |
| D12 本轮选定 | 同D0 | `NavigationLab.DAoe4.Aoe4ProgressExchangeFactory` |
| E 基础/历史 | `Experiments~/NavigationLab/Candidates/E-Warcraft3/NavigationLab.EWarcraft3.csproj` | `NavigationLab.Candidates.EWarcraft3.WarcraftFactory` |
| E 精细连接 | 同E | `NavigationLab.Candidates.EWarcraft3.WarcraftRefinedFactory` |

退出码0表示该次所选场景全部通过；1表示真实失败；2表示包含事先声明的不支持能力；3表示构建或工具错误。应继续核对summary的分类和来源标志，不能只按非零退出码称为算法失败。`--no-replay`仅供开发诊断，不是正式验收；`--build-only`只构建，不产生通过结论。

工厂名与源码指纹一起确定实际版本。同一工厂在后续修复中会生成新DLL，必须独立冻结与验证；不能用旧源码的完整报告拼成新源码成绩。D12已集成到D实验分支，增加项目终点交换与路线进展判定；D11/D13辅助分支保留历史对照。选定D12实际DLL为`9e6107cfe1dcd6d83ac137314109a145835c764b3a29a87171bde4412572b19c`，不能用默认D0工厂复现D12成绩。

`full`的41项逐字包含于`stress`的49项。正式stress完成后可按InputHash提取full子集；不要把重复运行算成新地图覆盖。长测会包含每5ms全身体的连续几何检查和压缩轨迹，完整wall time不等于候选Step时间或游戏帧率。

## 同源比较

在包含实际冻结快照与正式输出的机器上执行：

```bash
python3 Tools~/NavigationLab/report.py \
  --report A=/tmp/navlab-a-stress/summary.json \
  --report B0=/tmp/navlab-b0-stress/summary.json \
  --output /tmp/navlab-comparison \
  --diagram forest-full44-large
```

工具核对相同场景集合、逐字输入、公共框架、检查协议、源码/实际DLL绑定、构建与重放标志。Core/Oracle要求DLL字节一致。Runner的生成AssemblyInfo包含各自快照的绝对根路径，该属性会被运行时用于读取输入，因此其原始DLL的SHA可不同；比较时只规范这一条已经验证精确路径的属性，同时核对其余实际Compile源、csproj、编译属性、SDK记录及逐例输入。每份原始Runner DLL仍独立与自己的构建清单绑定，报告保留所有原始SHA，不宣称它们字节相同。不同协议、不同编译身份或未完成报告会被拒绝，不能用临时结果拼出最终比较。

图示只应用实际已执行的地图事件；失败前尚未触发的基地变更不会画成已发生。

## 真实Unity/Mono子集

`native-run.py`当前适配本轮Linux Unity编辑器环境。它把正式冻结的同一Core、Oracle及候选DLL临时导入目标工程，执行Play/Mono和独立检查，记录实际加载DLL指纹、运行ID与前后源一致性。它不导入A适配器DLL，因为原项目已包含同命名空间Domain类型。

```bash
python3 Tools~/NavigationLab/native-run.py \
  --project /path/to/GameStarter \
  --build-manifest /tmp/navlab-b0-stress/build-manifest.json \
  --inputs /tmp/navlab-b0-stress/inputs \
  --scenario forest-wave-1 \
  --output /tmp/navlab-b0-native \
  --wall-seconds 1800
```

可多次指定 `--build-manifest` 和 `--scenario`。`--graphics`另要求真实渲染截图通过颜色检查；不带该选项的运行只证明Play/Mono与几何行为。验证结束清理自己的临时文件，非预期工程改动会报告工具错误并保留证据。不要让多个Unity进程同时打开同一工程。

Unity子集通过不表示完整49项已在Unity执行，更不表示IL2CPP、HybridCLR主包、Windows或手机已验证。模型与Unity各自保留真实的通过、失败和未执行范围。

## 大体型终点相容性补充

[独立补充驱动](Supplementary/GoalClearance/REPRODUCE.zh-CN.md)固定了2个r650、3个r650及650/650/180混合三例。Holding包含相邻冲突点与足够合法替代点；原输入逐字SHA、独立连续可行见证、实际Core/Oracle和驱动二进制均保留。候选只能看到原世界和请求，不获取见证的目标分配。

三例检查同时持有的未来Goal几何间距以及实际到位、队列、运动与重放，结果单列，不加进49项得分。A已声明大于半格身体不支持，单列该边界。补充驱动本身也不证明任意多体型终点分配完备。
