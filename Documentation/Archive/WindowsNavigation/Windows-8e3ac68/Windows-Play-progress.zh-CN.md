# Windows Unity 导航测试阶段记录

北京时间 2026-11-12 03:17:55 · Unity 2022.3.62f1 · 交付提交 `8e3ac68c`

已记录 109/110 项计划结果。通过 79，失败 29，不支持 1，停止 0。表格只计入已终结的测试；排队和运行中的项均未判定。

六方案的单只林间测试及生命周期检查另外通过。第三波有独立轨迹和双视角截图。 [Smoke 报告](smoke.json) · [第三波轨迹](wave3-trace.zh-CN.md)

| 输入 | A | B0 | B1 | C33 | D12 | E |
| --- | --- | --- | --- | --- | --- | --- |
| `forest-wave-1` | Passed 8/8/0 | Passed 8/8/0 | Passed 8/8/0 | Passed 8/8/0 | Passed 8/8/0 | Passed 8/8/0 |
| `forest-wave-2` | Passed 16/13/0 | Passed 16/13/0 | Passed 16/13/0 | Passed 16/13/0 | Passed 16/13/0 | Passed 16/13/0 |
| `forest-wave-3` | Passed 18/14/0 | Passed 18/14/0 | Passed 18/14/0 | Passed 18/14/0 | Failed 18/13/1 | Failed 18/14/2 |
| `forest-wave-4` | Passed 25/15/0 | Passed 25/15/0 | Passed 25/15/0 | Passed 25/15/0 | Failed 25/13/5 | Failed 25/15/5 |
| `forest-wave-5` | Passed 30/15/0 | Passed 30/15/0 | Passed 30/15/0 | Passed 30/15/0 | Failed 30/9/15 | Failed 30/11/10 |
| `forest-wave-6` | Passed 29/15/0 | Passed 29/15/0 | Passed 29/15/0 | Passed 29/15/0 | Failed 29/14/1 | Failed 29/14/7 |
| `forest-full44-large` | Passed 44/18/0 | Failed 44/9/27 | Failed 44/6/33 | Failed 44/17/2 | Failed 44/17/27 | Passed 44/18/0 |
| `forest-full45-medium` | Passed 45/12/0 | Failed 45/10/6 | Passed 45/12/0 | Failed 45/12/2 | Passed 45/12/0 | Failed 45/12/3 |
| `forest-full44-two-late-small` | Passed 46/20/0 | Failed 44/9/29 | Failed 44/6/34 | Passed 46/20/0 | Failed 44/17/27 | Failed 46/18/2 |
| `single-cell-throat` | Passed 48/16/0 | Passed 48/16/0 | Passed 48/16/0 | Passed 48/16/0 | Failed 48/3/45 | 运行至 10750 刻 |
| `cross-junction-original-4m` | Passed 72/12/0 | Failed 72/11/3 | Passed 72/12/0 | Passed 72/12/0 | Failed 72/12/2 | Passed 72/12/0 |
| `mixed-speed-approach` | Passed 64/16/0 | Passed 64/16/0 | Passed 64/16/0 | Passed 64/16/0 | Passed 64/16/0 | Passed 64/16/0 |
| `large-small-sparse-group` | Unsupported 0/0/0 | Passed 11/11/0 | Passed 11/11/0 | Passed 11/11/0 | Passed 11/11/0 | Passed 11/11/0 |
| `moving-noncenter-death-and-reinforcement` | Passed 24/16/0 | Failed 24/15/1 | Passed 24/16/0 | Passed 24/16/0 | Passed 24/16/0 | Passed 24/16/0 |
| `capacity-death-refills-original-queue` | Passed 39/12/0 | Failed 33/4/21 | Failed 39/7/21 | Passed 39/12/0 | Failed 31/12/2 | Failed 18/11/2 |
| `old-waiters-refill-without-newcomers` | Passed 42/16/0 | Passed 42/16/0 | Passed 42/16/0 | Passed 42/16/0 | Failed 42/10/6 | Passed 42/16/0 |
| `static-unreachable-retains-pending` | Passed 0/0/0 | Passed 0/0/0 | Passed 0/0/0 | Passed 0/0/0 | Passed 0/0/0 | Passed 0/0/0 |

单格数字为存活 / 攻击 / 移动。每个已完成结果的复现步骤是打开 Navigation Playground，选择对应方案和输入后开始，等待终态。

[已完成逐案数据和截图文件名](Windows-Play-progress.csv)
