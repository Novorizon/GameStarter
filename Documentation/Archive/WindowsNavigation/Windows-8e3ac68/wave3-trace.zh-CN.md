# 第三波未到位单位轨迹

Unity 2022.3.62f1 · Windows Direct3D 11 · 交付版冻结 DLL · 目标倍速 20×

轨迹来自 Navigation Playground 发布给主线程的真实位置快照，导航器仍执行每个 5ms 物理步。计算取截止前 50 秒逻辑时间的采样位置，因此累计距离为样本之间的下界。

| 方案 | 结果 | NPC | 晚期采样数 | 位移累计 | 起末净位移 | X / Y 跨度 | 目标格切换 | 最终目标格 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| D12 | Failed | 3 | 260 | 0.0 mm | 0.0 mm | 0.0 / 0.0 mm | 0 | 259 |
| E | Failed | 15 | 118 | 37074.6 mm | 500.4 mm | 500.0 / 59.9 mm | 0 | 215 |
| E | Failed | 16 | 118 | 36751.6 mm | 500.4 mm | 500.0 / 60.0 mm | 0 | 304 |

[D12 原始位置快照](trace/D12-forest-wave-3-positions.jsonl) · [D12 终态](trace/D12-forest-wave-3-trace-final.png)
[E 原始位置快照](trace/E-forest-wave-3-positions.jsonl) · [E 终态](trace/E-forest-wave-3-trace-final.png)

- D12 在移动阶段的实际相机已验证角度变化。[俯视](trace/D12-forest-wave-3-trace-moving-overhead.png) · [斜视](trace/D12-forest-wave-3-trace-moving.png)
- E 在移动阶段的实际相机已验证角度变化。[俯视](trace/E-forest-wave-3-trace-moving-overhead.png) · [斜视](trace/E-forest-wave-3-trace-moving.png)

[补采原始事件](trace/events.jsonl) · [补采编辑器日志](trace-editor.log) · [补采驱动源码](WindowsPlayTrace.cs)
