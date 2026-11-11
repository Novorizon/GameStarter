# 本地 Unity 可视化对照

## 直接测试

```bash
git fetch origin
git switch --track origin/experiment/navigation-unity-playground
```

已有同名本地分支时使用 `git switch experiment/navigation-unity-playground`。请先提交或妥善保存本地改动，再切分支。

1. 用 Unity **2022.3.62f1** 打开仓库根目录，等待导入和脚本编译。
2. 打开菜单 **Tools → GameStarter → Navigation Playground**。
3. 选择方案、测试地图，首次建议 `forest-single-medium`，再试 `forest-wave-1`。
4. 点击“开始 / 重新开始”。工具进入专用 Play 场景；Game 视图显示全景，Scene 视图可以自由观察。
5. 用窗口按钮暂停、单步 50ms、继续、停止；停止完成后可切换方案并重新开始。退出 Unity Play 会恢复原编辑场景。

无需本机 Python、.NET SDK、重新编译实验 DLL，也不用切换六次分支。不要通过 Bootstrap 判断选中了哪套实验算法：**Bootstrap 仍是原来的 A 方案业务入口**，实验选择只影响本工具。

## 方案与独立分支

| 本工具选项 | 独立实验分支 | 说明 |
| --- | --- | --- |
| A / SIPP | `experiment/navigation-a-sipp` | 冻结的现有实现 |
| B0 / ORCA | `experiment/navigation-b-orca` | ORCA 基线 |
| B1 / ORCA + Yield | 同 B 分支 | 同一 DLL 的另一工厂，增加让路/恢复；不是额外分支 |
| C33 / Flow field | `experiment/navigation-c-supcom2` | 参考最高指挥官 2 公开架构的流场方案 |
| D12 / Hierarchical + exchange | `experiment/navigation-d-aoe4` | 参考帝国时代 4 公开架构的分层寻路及交换恢复方案 |
| E / A* + refinement | `experiment/navigation-e-warcraft3` | 参考魔兽争霸 3 可观察行为的独立实现 |

C、D、E 是公开资料/行为基础上的独立实现，不代表拿到了商业游戏源码或完全复现其内部算法。原独立分支用于源码、版本和证据审查；本工具在一个新分支中集中加载最终选定的六个冻结实现。

## 看什么

无炮塔，地图使用逻辑格子，NPC 使用简单几何体。青色移动、红色攻击、金色等待；蓝柱为基地/目标。石色道路，绿色允许等待，赭色出生口，深灰阻挡。多个目标、动态障碍和补员事件遵循所选原输入。

在 Scene 视图点击单位或在 Hierarchy 选择 Unit，可查看身体占用圆、攻击范围和目标格。**Goal 连线仅表示目标方向，不是导航器实际路线。** 球体的 XZ 直径等于两倍逻辑半径；Unity 物理不参与导航，也不会推开单位掩盖问题。

开始时同一输入重新创建独立导航器。出生失败的单位仍在外部待出生队列中，可能包含尚未到达计划时间的请求；不会在堵住时悄悄删除或移动 NPC。死亡只按输入事件发生。

`Passed` 是本次观察达到输入规定的终态与稳定窗口，`Failed` 会给出连续碰撞/终态/超时等原因，`Unsupported` 是工厂预先声明不支持该输入所需能力。最后画面保留供检查，之后可停止或重新开始。

## 时间与隔离

每物理步 5ms，每逻辑 tick 50ms；单步按钮推进一个逻辑 tick，即 10 个物理步。调速改变目标墙钟节奏，不省略物理检查。最终稳定窗口按原输入执行；初始批次检查也保持原有协议。复杂场景（尤其 D 的大规模输入）可能明显慢于所选倍速；界面显示最近一步计算耗时，不代表商业游戏帧率。

算法和连续几何校验在同一个后台线程串行执行，Unity 主线程只读取已发布快照。停止、退出 Play、代码重载会等待当前算法调用返回再清理，不能强行中断一次正在计算的候选调用。请先从小场景开始；49 个输入中包含原长时限压力场景。

七个 DLL、49 个转换后的输入和对应原始哈希保存在 `Assets/NavigationPlayground/Editor/playground-bindings.json`。启动时核对文件及实际加载程序集的 SHA-256。A 使用 ASipp.dll 内隔离的原 Domain，不引用当前业务 HotUpdate 类型。插件只启用 Editor，且必须显式引用；独立 asmdef 不依赖业务程序集，不进入 Player、YooAsset 或热更发布。

转换只为适配 Unity JsonUtility 的字段格式，原始语义和原输入哈希保留。当前窗口属于交互观察工具，**不会把本地运行自动记为发布过的 .NET 全 49 测试或独立轨迹回放证据**。完整对照证据仍见 [Findings.zh-CN.md](Findings.zh-CN.md)。

## 验证范围

本分支交付时执行 Linux Unity 2022.3.62f1 的真实导入、Play 和生命周期检查；Windows 编辑器仍需你在本机实测。它不代表 Windows Player、手机、IL2CPP 或正式战斗场景已经验收。
