# A 与 C33 围攻站位的共用定向验证

三个无炮塔输入共用 16×13、1 米格的地图。NPC 从西侧一格宽通道进入基地周围的开阔区。每只 NPC 的目标均为同一基地，未指定目标格。序列间隔 80 逻辑刻，以便直接观察前一批已经占据的位置。单个逻辑刻 50 ms，每个物理步 5 ms。

| 输入 | 验收 |
| --- | --- |
| `siege-near-front-and-side` | 第一只实际从最近的正面格 103 攻击。第二、三只使用两侧格 87、119。前三只在首次攻击前均不绕到基地东侧。已攻击的单位维持原位置和攻击状态。终态 3 攻击。 |
| `siege-fill-first-then-second` | 该输入的攻击距离让恰好四个互不冲突的格符合冻结 Oracle 的驻留和攻击条件。第 320 刻增员前，前四只须已占满。后两只在距任意攻击中心不超过一格的合法候补格停留。终态 4 攻击、2 等待。 |
| `siege-second-ring-refills-vacancy` | 先达到 4 攻击、2 等待。第 780 刻按输入移除攻击者 ID 1，至少一只既有候补补上，其他攻击者维持原位置。终态 4 攻击、1 等待。 |

所有终态需稳定 200 逻辑刻。独立验证器使用交付版冻结 Core 和 Oracle 验证连续身体碰撞、速度、越障、位置与 ID 守恒、合法候补和攻击距离。最近格和两侧格按该图的几何定义单独检查。正面格与出生点之间有 5 米直线。两侧格都能沿通道中线先到 (6.5, 6.5) 米再斜向进入，构造路径约 5.41 米，与正面已占位单位保持约 0.71 米的中心间距。其他合法攻击格离出生点的直线距离已超过这条路径。候补一格限制也只适用于这张验证图。结果不外推到任意地图和体型。

原 JSON 使用导航实验字段。目录 `unity/` 下是以仓库转换器生成的等价 Playground 输入，可使用正常的终态人数及稳定时长验收。独立验证器另外检查实际首次攻击位置、首次接近过程中是否绕到后侧、已攻击单位是否被移位、候补到攻击层的距离。普通 Unity 终态满足人数时，仍可能未满足这些行为断言。

## 执行

```powershell
dotnet build ./SiegeChecks.csproj -c Release
dotnet ./bin/Release/net8.0/SiegeChecks.dll ./siege-near-front-and-side.json ../../GameStarter/Assets/NavigationPlayground/Editor/Plugins/NavigationLab.CSupCom2.dll NavigationLab.CSupCom2.Factory ./C33-frozen-siege-near-front-and-side.json
```

将路径及工厂替换为待测候选。每份 JSON 写出输入、候选、Core、Oracle 的哈希，出生目标、攻击和等待起点、关键帧、最终位置、失败断言及实际稳定刻数。Failed 退出码 1。Passed 和 Unsupported 退出码 0，可通过结果的 `status` 区分。

## 原冻结版本

| 候选 | 近侧攻击 | 一二层位置 | 候补补位 |
| --- | --- | --- | --- |
| A | Passed，三个攻击格为 103、87、119 | Failed，两个候补距攻击层均约 2.24 m | ID 5 确实补位，初始候补位置断言仍失败 |
| C33 | Failed，三只均经过基地后侧，攻击格为 91、123、107 | Failed，ID 1 首先使用东侧格 106，ID 5 首次等待距攻击层约 3.61 m | ID 5 确实补位，最初正面和候补位置断言仍失败 |

冻结候选这六次运行均达到普通终态人数和 200 刻稳定窗口。报告中的 Failed 来自额外的站位策略断言。此处使用 Windows .NET 独立逻辑验证，Unity 实机证据另行采集。

## A 更新

源码提交 `2df27b79f9ac8abf7c2301b09eb8141a184cb82e`，DLL SHA-256 `96fdf5a72eb8f02b2863c050db4686435aa3b3902535e52c9747569eef25f725`。把全部合法攻击格交给一次 SIPP 搜索，在第一圈满了时优先尝试靠近攻击层的合法候补格。候补优先选不会占用其他体型或射程所需攻击格的位置。已攻击单位保持原位。候补让位只在无法直接到空攻击格时参与恢复。

三项严格验证通过。Unity 验证终态及安全，独立报告另核对实际首攻位置和是否绕后。[正面与侧面](A-verified-siege-near-front-and-side.json) · [两层](A-verified-siege-fill-first-then-second.json) · [补位](A-verified-siege-second-ring-refills-vacancy.json)

定向回归中 Forest44 和 Forest45 分别达到 18、12 人攻击，原动态封路再开放用例通过。第六波的 29 人均稳定，攻击者从中间构建的 12 人增到 14 人，旧输入要求 15 人。Forest44 晚到用例在第 3999 刻有 43 人入场，其中 1 人仍在移动，未达到初始 44 人就位要求。

## C33 更新

源码提交 `3811018111547ac97d279bb10cb0809e532b60ca`，DLL SHA-256 `e6c556f0edae077273ee5fb90017086e7add19019fbf32dc9f367247d022435f`。用格子通路代价比较攻击位置，已到位攻击者不参与后续站位重分配。新增的局部恢复用实际身体检查验证可走的侧向连接。

三项严格验证和 Windows Unity Playground 的三项运行均通过。[正面与侧面](C33-final-siege-near-front-and-side.json) · [两层](C33-final-siege-fill-first-then-second.json) · [补位](C33-final-siege-second-ring-refills-vacancy.json) · [Windows Play 事件](windows-play-c33/events.jsonl)

定向回归的双桥和原封路再开放用例通过。林间哨站波次 3、5、6 分别还有 1、4、4 只未到位。Forest44、Forest45 的结果较旧版恶化，晚到 2 人、满容量死亡补员和先近战后远程用例新增失败。此构建保留在实验分支。

两个最终构建在 Windows Unity 2022.3.62f1 的三项用例均通过，共六次 Play。可以在 `D:/Game+Server/GameStarter-Nav-Compare` 的 **Tools → GameStarter → Navigation Playground** 选择带 `(Siege)` 标记的方案。真实 Play 核对了终态和连续几何，近侧先打、第二层位置和已攻击者留在原位来自独立逐步验证。该场景用几何体表示单位，正式战斗模型朝向还未实机验证。[最终 Play 事件](windows-play-final/events.jsonl) · [A 两圈站位](windows-play-final/A-siege-fill-first-then-second-siege-final.png) · [C33 两圈站位](windows-play-final/C33-siege-fill-first-then-second-siege-final.png)
