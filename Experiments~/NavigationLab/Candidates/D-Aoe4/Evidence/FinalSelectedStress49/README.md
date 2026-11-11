# D12 选定版本完整证据

共同49项完整结束：26通过、23失败、0未支持、0非法输入。完成不代表全部通过；所有通过项有全新实例重放。

Factory `NavigationLab.DAoe4.Aoe4ProgressExchangeFactory`；实际候选DLL `9e6107cfe1dcd6d83ac137314109a145835c764b3a29a87171bde4412572b19c`。原summary、49份输入、完整案例报告、冻结执行和构建身份原样保留。source-and-binaries.zip是当次实际编译源与四角色DLL；Runner根路径元数据须在迁机时重建。

trace-storage.json明确列出原完整轨迹路径与SHA；版本化trace-windows只有失败案例首尾窗口及派生检查点，不是完整轨迹。归档字节校验不等于重新执行仿真；full41是同一49输入的逐字子集，不另计覆盖。

选定DLL的真实Linux Unity/Mono子集为2通过、1失败，原件见 ../FinalSelectedNative。不是49项全Unity验收，不代表图形、IL2CPP、Windows或手机验证。

本项目依据AoE4公开架构独立实现；有限测试不证明一般完备性，也不证明商业游戏内部实现，未进行性能排名。

D256的两次仅线程栈诊断合计约1.030秒，原件及计时边界见RuntimeDiagnostics；wall/API数据不作性能排名。输入、逻辑时限和实际DLL均未修改。两个精确缓存版本另在各自辅助分支，仅四例，不计入本49成绩。

另完整保留old-waiters与settled-batch两项死亡用例的原压缩trace，供独立重建死亡前候场集合与最终补位数；trace-storage与model-archive-index已列出实际存储及指纹，其他大轨迹仍明确为部分窗口或外部原件。
