# 验证范围

验证器按工程清单的固定 Git 标签下载 Framework 与热更封装，或接受维护者显式传入 --framework；不会自动读取相邻源码仓库。清单同时声明聚合包和全部依赖来源，不使用 file:../../../。结构检查覆盖包闭包、Bootstrap 场景、输入配置和资源 GUID。

实际 Framework Core、MonoSingleton 与 IGameModule/IGameHost 契约分别编译。Game.Startup 保存共享启动参数；GameStarter.Runtime 不引用 Game.HotUpdate。热更编译禁用传递项目引用，并检查每个直接引用与生产 Game.HotUpdate.asmdef 一致。测试用 Manager API 模型按生产程序集名拆分，防止合并编译掩盖缺少引用。

49 组受控场景覆盖：原有可选模块的顺序初始化、Tick、取消和清理；关闭热更时的 AOT 初始化；开启热更后由真实 HotGame 初始化普通 Manager；输入和资源参数的传递；就绪前不进入 Ready；退出取消；可选模块先于 Manager 释放；重复启动、异常清理、保留场景组件重启；包安装和解析行为。

新增用例让自定义热更入口不创建普通 Manager，验证 GameEntry 也不会代为创建它们。默认热更入口由测试执行器通过工厂注入，AOT 模型和生产 AOT 代码都不引用热更具体类型。

模型中的 Unity/UPM/Manager API 不执行真实设备、资源包、渲染或 IL2CPP 行为。UnityPackages 验证包内实际服务；本工程验证启动接线。真实 Unity 2022.3 导入、Play 和目标平台构建仍需单独验收。Game.Startup/link.xml 保留热更端读取的共享 AOT 参数类型。增加 AOT 接口或程序集后需要重建 HybridCLR Player。

Data 包迁移同时更新 AOT/热更 asmdef 和模型程序集为 Game.Framework.Data，验证单一字节加载器分别读取清单与表后释放两个独立资源租约；旧 Config 依赖不再进入工程 manifest。
