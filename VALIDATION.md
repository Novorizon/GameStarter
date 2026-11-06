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

Unity 2022.3.62f1、Debian 13 x86_64，目标 StandaloneLinux64；以工程 `032a3e1` 和 manifest 固定的 UnityPackages `v0.1.0-preview.25` 为输入，在独立 checkout 中验证。本次修复将 YooAsset 的 FindAssetType 改为不含 `t:` 的类型名，并让 EditorSmoke 在 Play 域重载后恢复检查、写出报告再退出。

- 已导入全部 8 个自有示例；另启 Unity 完成 Editor/Player 脚本编译，compile.json 为 success，包含 NATIVE_COMPILE_READY。
- 修复后 Bootstrap Play 成功，包含 LUBAN_EXAMPLE、HOT_UPDATE_READY、GAME_STARTER_READY，editor.json 为 success。
- GpuAnimator EditMode 共 51 项：46 通过、5 失败，0 跳过。失败涉及 3 项资源写入/恢复、源动画重复匹配和五骨骼权重转换；该阶段未通过，不能计为 GPU 原生验收成功。包源码未在本次修改。
- Windows/Android/iOS IL2CPP、原主包结构补丁加载、实际界面/输入/音频及 Shader 渲染尚未验收。

完整日志、JSON 和 gpu.xml 位于验证副本的 Artifacts/NativeValidation，验证副本和导入示例不回写模板。
