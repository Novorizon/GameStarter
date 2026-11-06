# 验证范围

验证器按 manifest 的固定 Git 标签获取 Framework、Data 和 HybridCLR 封装；依赖源码只缓存到系统临时目录，不复制到工程或 package。不会自动使用相邻仓库。

分别编译真实 Framework Core、MonoSingleton、DataManager、Luban 导出器、官方 Luban.Runtime、工程提交的生成表、Game.Startup、GameStarter.Runtime 和 Game.HotUpdate。热更编译关闭传递引用，并与生产 asmdef 的直接引用比较；生成表和 Luban.Runtime 已显式引用。

共 57 项验证：原有 49 项启动/模块/安装器场景保留并改为固定初始化 YooAsset；新增 8 项场景覆盖：AOT 和热更均加载真实 cfg.Tables；表成功解码前不运行模块或报告 Ready；读取失败和取消阻止业务启动；每次资源读取释放独立租约；寻址规则与 manifest 一致且排除 marker/meta；自动配置重复执行不新增采集器且保留用户分组；源数据变化阻止误用旧生成物；资源构建先于主包且失败/错误平台资源不能继续构建。

配置生成规则的 IAddressRule、IFilterRule、ECollectorType 和构建枚举直接编译固定 YooAsset 2.3.18 的源码，验证接口及枚举值。Unity/资源句柄/UPM、原生资源构建与 Player 构建仍使用边界模型，不等于实际 Unity 导入、Editor 模拟模式、Bundle 烘焙或 IL2CPP 设备验收。

模板包含官方 Luban CLI 4.12.0 从 Excel 生成的源文件和二进制。数据包仍是 UnityPackages v0.1.0-preview.22；工程本次版本为 v0.1.0-preview.23。共享 AOT 参数和生成程序集发生变化，使用现有 HybridCLR 主包的项目需要重建 Player。
