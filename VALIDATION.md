# 验证范围

结构检查要求默认工程清单和所有可选模块来源都是发布标签的 Git URL，不依赖 file:../../../ 或父目录中的 UnityPackages。Bootstrap 场景、程序集引用和资源 GUID 也会核对。工程清单必须包含 all 和聚合目录中的全部依赖来源，确保初次解析时即可满足运行时程序集引用。

26 组生命周期、Manager 初始化和安装场景使用实际 Framework Core 和 MonoSingleton 源码。未显式提供 --framework 时，验证器按工程清单从 Git 下载 Framework 至临时目录，没有相邻包仓库回退。UI、ECS、资源和网络 Manager 使用明确隔离的测试 API 模型，覆盖 Manager 先于启动模块、重复访问不重复初始化、配置驱动的网络准备、资源失败和退出期间的异步完成；这些模型不执行对应库的真实资源或原生逻辑。

发行验收从只含 GameStarter 的干净解压目录执行上述检查与编译。模型中的 UnityEngine/UnityEditor/UPM 不执行原生导入、domain reload、渲染、IL2CPP 或设备行为；这些仍需要 Unity 2022.3.62f1 及目标平台模块验收。
