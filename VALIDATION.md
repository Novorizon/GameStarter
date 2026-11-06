# 验证范围

验证器按 manifest 的固定 Git 标签获取 Framework、Data、YooAsset 适配和 HybridCLR 封装；依赖源码只缓存到系统临时目录，不复制到工程或 package。不会自动使用相邻仓库。

分别编译真实 Framework Core、MonoSingleton、DataManager、Luban 导出器、官方 Luban.Runtime、工程提交的生成表、Game.Startup、GameStarter.Runtime 和 Game.HotUpdate。热更编译关闭传递引用，并与生产 asmdef 的直接引用比较；生成表和 Luban.Runtime 已显式引用。AOT 入口不再引用生成表、Luban 或热更游戏程序集。

共 64 项验证。保留启动、模块、安装器和真实 cfg.Tables 解码、加载失败/取消、独立资源租约、采集寻址、重复配置和构建前置检查等 57 项场景；新增 7 项覆盖内置发行关闭 Host 包并使用离线资源、AOT 表引用边界、HTTPS/TCP 端点分离、音量及重绑定跨启动恢复、失焦/后台保存、坏存档保护和签名发行资源配对及 DLL 篡改拒绝。编辑器边界模型同时声明两个 BuildReport，用于防止本次类型歧义回归。

配置生成规则的 IAddressRule、IFilterRule、ECollectorType 和构建枚举直接编译固定 YooAsset 2.3.18 的源码，验证接口及枚举值。Unity/资源句柄/UPM、原生资源构建与 Player 构建仍使用边界模型，不等于实际 Unity 导入、Editor 模拟模式、Bundle 烘焙或 IL2CPP 设备验收。

模板包含官方 Luban CLI 4.12.0 从 Excel 生成的源文件和二进制；新增表结构验收夹具也已通过官方 CLI 生成。包依赖为 UnityPackages v0.1.0-preview.23；工程版本为 v0.1.0-preview.25。共享 AOT 参数和生成程序集位置发生变化，现有 HybridCLR 主包需要重建一次。

真实 Unity 验收入口为 Assets/Game/Editor/NativeValidation，执行脚本为 Tools~/native-validate.ps1。Editor、IL2CPP 主包、原主包加载新表结构补丁分阶段执行，结果写入 Artifacts/NativeValidation。当前环境没有 Unity，尚未执行这些阶段；模型验证不代表原生资源下载或 IL2CPP 加载成功，也不代表服务器已经部署更新文件。
