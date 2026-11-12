# 双桥封路逻辑验证

这些输入由 `generate-fixtures.py` 生成，使用导航实验原始 JSON 字段。它们是新增的独立输入，未改动 Playground 的 49 个冻结输入和哈希绑定。三个输入使用同一个目标、四只 NPC、相同速度和同一时间表。NPC 1–3 在西岸出发，NPC 4 从东岸出发。

- `bridge-open-control.json` 保持桥梁开放，三只西岸 NPC 都应通过较短的直桥。
- `bridge-short-cut-detour.json` 在第 26 个逻辑刻（1.30 秒）封闭直桥中段的三格，此后不开通。西岸三只需从北桥绕行，四只均进入攻击并保持 200 个逻辑刻。
- `bridge-short-cut-no-alternate.json` 移除北桥，按相同时间封闭直桥。此输入使用独立验证器的负对照判定，西岸三只已出生并应安全保留在西岸，东岸 NPC 应进入攻击，最终位置保持 200 刻。原 schema 的 `FullArrival` 无法表达已出生后被永久隔开的终态，普通 Playground 的终态判定会按到位不足处理。协议也没有专门的不可达状态，西岸单位可保留 `Moving` 状态但实际位置稳定，`Waiting` 仍须满足原 Oracle 的集合区规则。

封路前须已生成全部四只，均处于移动状态，西岸队首已进入直桥，东岸对照已在桥的另一侧。封路合法性、ID 守恒、速度、障碍扫掠、身体连续碰撞、攻击射程和驻留均由交付的 `NavigationLab.Oracle.dll` 检查。附加验证器核对西岸三个 ID 实际跨越地图 x=14 m 的桥段，东岸对照封路后 500 ms 内朝目标至少前进 500 mm。此阈值核查单位是否持续移动，单位配置的 500 ms 最大位移为 1800 mm。短桥开放时四邻域中心线最短距离为 19 格边，封路后为 30 格边，无北桥时不可达。合法性仍按实际连续圆盘检查。

## 使用

从本目录执行。参数 DLL 路径可以指向新候选，但测试引用从交付版本的 Core/Oracle DLL 取得。

```powershell
dotnet build ./BridgeChecks.csproj -c Release
dotnet ./bin/Release/net8.0/BridgeChecks.dll ./bridge-short-cut-detour.json ../../GameStarter/Assets/NavigationPlayground/Editor/Plugins/NavigationLab.CSupCom2.dll NavigationLab.CSupCom2.Factory ./C33-frozen-bridge-short-cut-detour.json
```

每次运行写出输入、DLL 和 Oracle 哈希、封路前后位置、跨桥时刻和坐标、对照位移、稳定刻数、守恒数据和候选指标。退出码 1 表示 Failed；Passed 和 Unsupported 都以 0 结束，须读取 JSON 的 `status` 区分。此处使用 Windows .NET 独立逻辑验证，Unity 实机证据由其他测试记录。

## 冻结候选对照

| 候选 | 开放短桥 | 断桥绕行 | 断桥无北桥 |
| --- | --- | --- | --- |
| A | Passed | Unsupported | Unsupported |
| B1 | Passed | Passed | Passed |
| C33 | Passed | Passed | Passed |

C33 在断桥后 500 ms 东岸对照朝目标前进 1673.87 mm，与该候选开放短桥的对照相同。三个西岸 NPC 的实际跨越位置均为北桥 y≈18.49 m。没有北桥时，西岸三只的位置保持在断桥前的位置，东岸正常攻击。

## 新候选结果

| 候选 | 源码提交 | 开放短桥 | 断桥绕行 | 断桥无北桥 |
| --- | --- | --- | --- | --- |
| A | `67c21760320440e6bf8b77f31f939b81316adb79` | Passed | Passed | Passed |
| C33 | `66f3782ee17d73cbd189cc170a4cfb4c13268629` | Passed | Passed | Passed |

- A 在 50 ms 导航步边界应用断桥变更，重算受影响的时空预约。三只西岸 NPC 经北桥到达，东岸对照在变更后 500 ms 前进 1610.66 mm，和各自的开桥对照一致。独立探针还检查了半途撤回和关闭格覆盖单位时的原子拒绝。[绕行报告](A-final-bridge-short-cut-detour.json)
- C33 在两个断桥输入中各保留 1 条未受影响的活动路线，失效 3 条受影响路线。新旧 C33 在这三个输入中的稳定时间相同。Forest44/45 仍各有两只未到位，现有证据显示单位持续交换或轮换目标格。
- 两套导航器都会拒绝把单位身体所在位置直接改为障碍。正式炸桥时桥上单位的玩法处理尚未接入。无路可走的单位仍保留，当前实验的状态枚举用 `Moving` 表示未到达，不能用这个状态名推断单位仍在移动。

两个开发工作树分别为 `D:/Game+Server/GameStarter-Nav-A` 和 `D:/Game+Server/GameStarter-Nav-C33`。共用 Unity 对照工程位于 `D:/Game+Server/GameStarter-Nav-Compare`，保留对磁盘与加载 DLL 的 SHA-256 校验。两套实验实现的 SHA-256 分别为 `d57b1e9b48c6bc1346d5c3397fc06e1a398539e8b75116c96e8314b01c302e16` 和 `c3d767647a84b1bbbcff1353a901a2a73aea921d984bee236d9590e5753565e3`。

## Windows Unity Play

Unity 2022.3.62f1，Direct3D 11。由真实菜单进入 `Assets/NavigationPlayground/Scenes/NavigationPlayground.unity`。首轮发现 `JsonUtility` 将空的 `replaceTarget` 读为默认目标，导致两套新候选拒绝封桥。修正专用场景的事件读取后复跑，也复测了真实目标移动。候选 DLL 在前后两轮的哈希一致。[首轮事件](windows-play/events.jsonl) · [修正后事件](windows-play-fixed/events.jsonl)

| 输入 | A | C33 |
| --- | --- | --- |
| 短桥开放对照 | Passed，4 攻击 | Passed，4 攻击 |
| 短桥永久关闭，改走北桥 | Passed，4 攻击 | Passed，4 攻击 |
| 原封路再开放用例 | Passed，24 存活，16 攻击 | Passed，24 存活，16 攻击 |
| 目标移动回归 | — | Passed，24 存活，16 攻击 |

共 7 项实机运行通过，全部达到各输入规定的稳定窗口，退出后回到编辑模式。没有北桥的场景使用独立验证器的不可达终态规则。[A 绕行画面](windows-play-fixed/A-bridge-short-cut-detour-bridge-final.png) · [C33 绕行画面](windows-play-fixed/C33-bridge-short-cut-detour-bridge-final.png)
