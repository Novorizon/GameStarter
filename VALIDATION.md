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
