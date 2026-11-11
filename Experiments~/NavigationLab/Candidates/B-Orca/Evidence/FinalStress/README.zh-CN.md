# B当前选定版本的冻结证据

B0和B1均基于源提交8c6a4a2；实际frozen candidate DLL SHA均为a69bc87d5e10f614f463929f374d2b294ed0c0840377c91fcfc7b7f7280e2831。Factory分别为NavigationLab.Candidates.BOrca.OrcaFactory和OrcaYieldFactory，协商让路/目标交换只在B1开启。Core/Oracle保留共同v2标准，原RVO源码、体型、速度、地图、Goal集合保持。

每包含精确源码、实际Compile输入、四角色DLL及原build-manifest，verify-packages.py检查原字节而不是重跑模拟。FrozenDllRegressions在相同实际a69 DLL上通过了完整原自检（包含U清路径绕行和完整2000个5ms稳定interval）及B0/B1各3个不同体型补充例/fresh；这些补充例不计入共同49。

B1共同49+fresh已完成：45通过/4失败/0不支持，通过项45次fresh均一致，隔离/暂停/源和二进制不变检查全部通过；严格report工具已检查原件。full41只是按原InputSHA从这49派生，37通过/4失败，不是第二次执行。B0的完整49仍在云端results/b0-v2-final-stress执行，不能拿B1结论替代B0。

B1的4失败：44实际全出生但35仍Moving；late46在初始44尚未稳定时失败，后续2小体型阶段未获验收；overflow200实际39出生/161原pending但未稳；capacity-death也未先稳到39，因此死亡事件未触发，不能推断死亡回填已验。原最终态、原输入、原队列、策略计数与首末128帧/checkpoint均保留，forest44-final.svg只画实际末态。完整大trace留在trace-storage.json所记录的云端精确路径/SHA，未放Git；本地可运行相同冻结源码重新获得完整轨迹，portable archive校验不会冒称已重新验证未包含的完整轨迹。

Native-A69-Selected是同一实际DLL的原始6项Unity Play/Mono报告：B0各2通过1失败、B1各2通过1失败。平台人数独立记录（不同于.NET8），global false/exit1保持；Null图形设备不代表渲染/IL2CPP/真机。

旧源码、被替代的中止阶段、旧Native原件均在Evidence/SupersededBeforeStartJoin，不能用旧结果替代本版本。
