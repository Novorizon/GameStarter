# 验证范围

结构检查要求默认工程清单和所有可选模块来源都是发布标签的 Git URL，不依赖 file:../../../ 或父目录中的 UnityPackages。Bootstrap 场景、程序集引用和资源 GUID 也会核对。

22 组原有生命周期/安装场景使用实际 Framework Core 和 MonoSingleton 源码。未显式提供 --framework 时，验证器按工程清单从 Git 下载 Framework 至临时目录，没有相邻包仓库回退。

发行验收从只含 GameStarter 的干净解压目录执行上述检查与编译。模型中的 UnityEngine/UnityEditor/UPM 不执行原生导入、domain reload、渲染、IL2CPP 或设备行为；这些仍需要 Unity 2022.3.62f1 及目标平台模块验收。
