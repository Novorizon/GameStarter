# 验证范围

## 源码与模型回归

`python3 Tools~/verify.py --dotnet /path/to/dotnet` 按工程 manifest 固定标签获取包，依赖仅缓存在临时目录；显式 --framework 可供包维护者验证未发布源码。

编译实际 Framework Core/MonoSingleton、DataManager、Luban 导出器、官方 Luban.Runtime、生成表、Game.Startup、GameStarter.Runtime 和 Game.HotUpdate。asmdef 直接引用与模型项目对齐，AOT 入口不引用热更表、Luban 或 HotGame 程序集。

当前 64 项场景涵盖启动、可选模块、取消、退出、单例重置、安装器、真实表解码、资源租约、采集规则、构建前置、端点分离、设置恢复/坏存档保护和签名资源配对。YooAsset 边界含两个同名 BuildReport，防止 Unity 构建接口的类型歧义回归。

Unity/UPM、音频、Input System、资源构建和 Player 构建使用边界模型；不能据此宣称真实 Unity 或 IL2CPP 通过。

## Unity 原生入口

- SampleValidation.ImportSamples：把已解析自有包声明的示例导入 Assets/NovorizonSamples。
- NativeValidation.CompileScripts：真实 Unity Editor 导入后的 Player 脚本编译。
- NativeValidation.EditorSmoke：Bootstrap 的 Play 启动确认。
- NativeValidation.BuildHybridPlayer：资源与 IL2CPP 主包。
- GenerateTables/ExportPatch 与原主包 PatchPlayer：新增表类型的补丁验证。
- GpuAnimator.Editor.Tests：真实烘焙、资源保存与恢复测试。

上述入口单独由 Unity 编译，不包含在 .NET 模型结果里。命令、前置工具链和通过条件统一在 [Unity 验收](Documentation/UnityValidation.zh-CN.md)，结果保存在 Artifacts/NativeValidation。保留实际日志、JSON/XML、版本/提交以及未执行项。

## 2026-11-06 原生验证

Unity 2022.3.62f1、Debian 13 x86_64，目标 StandaloneLinux64；以工程 `032a3e1`（preview.26）加修复提交 `b31c769` 和 manifest 固定的 UnityPackages `v0.1.0-preview.25` 为输入，在独立 checkout 中验证。该修复将 YooAsset 的 FindAssetType 改为不含 `t:` 的类型名，并让 EditorSmoke 在 Play 域重载后恢复检查、写出报告再退出。

- 原记录确认复制了 8 个自有示例、当前发现的 Editor/Player 脚本编译通过，但不能认定全部示例编译通过：交叉检查发现 Game.Framework.Demo 和 Game.Framework.Demo.Editor 未参与编译，旧导入目录的 .framework 后缀被 Unity 特殊处理。
- 修复后 Bootstrap Play 成功，包含 LUBAN_EXAMPLE、HOT_UPDATE_READY、GAME_STARTER_READY，editor.json 为 success。
- GpuAnimator EditMode 共 51 项：46 通过、5 失败，0 跳过。3 项资源写入/恢复用例未完成；另 2 项分别在 Bake 前的曲线绑定 Single() 和测试网格 SetBoneWeights 阶段失败。它们是测试失败，不等于 5 个生产代码缺陷，也不能统归为 Linux 环境问题。包源码未在本次修改。
- Windows/Android/iOS IL2CPP、原主包结构补丁加载、实际界面/输入/音频及 Shader 渲染尚未验收。

完整日志、JSON 和 gpu.xml 位于验证副本的 Artifacts/NativeValidation，验证副本和导入示例不回写模板。

## 示例编译补验

基于 `6d5af4e` 加本次导入目录和程序集核对修复，仍使用 Unity 2022.3.62f1、StandaloneLinux64 和固定包标签 preview.25。在旧导入目录上运行新的核对，因缺少 Game.Framework.Demo.Editor 明确失败；随后在全新验证副本中导入和编译。

- 8 个示例全部导入；sample-assemblies.json 确认 11 个 Editor 编译程序集有实际 DLL 和导入源文件，其中 8 个 Runtime 程序集进入 Player 编译列表。
- 编译日志包含 Game.Framework.Demo.dll 的 Editor/Player Csc 和复制步骤，以及 Game.Framework.Demo.Editor.dll 的 Csc；命令退出码为 0，compile.json 为 success，包含 PACKAGE_SAMPLE_ASSEMBLIES_VERIFIED 和 NATIVE_COMPILE_READY。
- 新程序集集合下再次执行 Bootstrap Play，命令退出码为 0，editor.json 为 success，三个就绪标记齐全。
- 上述补验不扩大为 Demo 交互功能或 GPU 测试通过，也不替代 IL2CPP 和设备验收；前述 51 项 GPU 测试保留为原环境下的历史结果。

## 启动接线简化复验

移除 GameStartupOptions 中的两个 object 工厂，场景与 UI 适配器改在 HotGame 中直接装配；UI 租约适配器移入热更程序集。命令行探针移到单独的 NativeValidation partial 文件，保留现有验收脚本行为。SingletonLifecycle 的资源清理与 Source Generator 的静态字段恢复保持原有职责。

64 项模型回归通过（本地 net8.0 目标使用 .NET 9.0.22 roll-forward 执行）；真实 Unity 2022.3.62f1 的 Editor/Player 编译、8 个示例程序集覆盖检查和 Bootstrap Play 均通过。Game.Startup 及 AOT 引用有变化，已有 IL2CPP 主包需重建；本次未追加 IL2CPP 或 GPU 通过结论。
