# 当前验证范围

验收输入以项目 manifest 中的固定 SDK 提交为准；当前报告见 [EntryStartup](Documentation/Validation/EntryStartup/)。报告记录实际运行范围，不代替未执行的验收。

## 已通过

- 92 项工程模型回归：初始化顺序、取消、失败、退出清理、包安装和配置、14 张真实 Luban 表解码、旧档案迁移、四级营地建设、任务领取防重复、保存失败重试、出征奖励等级锁定、真实波次情报、叠加暂停和战绩/材料共同提交；其中 Unity/UPM 为测试边界模型。
- 12 项纯 C# 战斗领域和 13 项账号会话回归；账号测试使用测试后端，不代表正式服务登录/结算。
- SDK 固定到 `v0.1.0-preview.35` / `5bf37118c412ce6f188340fe75661e54c0d91515`；本项目完成聚合包安装与配置，报告见 [packages.json](Documentation/TowerDefense/Validation/packages.json)。SDK 自身的全仓验证由 [SDK PR #24](https://github.com/Novorizon/UnityPackages/pull/24) 交付记录负责，不冒充本轮重跑。
- Unity 2022.3.62f1 Player 脚本编译：9 Demo 的 13 个 Editor 和 9 个 Player 程序集逐项核对，包含 Story 示例。
- Linux / OpenGLCore 软件渲染下，Online 与 Offline 两轮入口配置均用 Editor 资源模拟完成启动、TMP 界面创建和退出清理。Unity 停止时销毁 DontDestroyOnLoad 入口，轮间重新打开 Bootstrap，不作为同一 Entry 跨停止保留的证明。
- 独立 AOT 启动展示、无下载时的步骤进度、下载时的字节进度和下一次启动读取缓存布局。下载控件使用 25/100 MiB 的验证输入，不作为实际 HTTP 下载证据。
- 首次 SDK 剧情 → 营地手记/建设与玩家档案 → 出征准备/确认 → 塔防 → 返回营地；两场 6 波胜利、四级建设、真实 JsonUtility 旧档案迁移、实际输入点选建造、领域射程圈、胜败重开与退出清理。分别在 1440×789 与 1440×665 的 Editor GameView、URP 14 下验收；测试后恢复原档案及备份，不是真机测试。另用 Unity 回调验证焦点/后台/手动/返回确认的叠加暂停，末波列表可以滚动到可见范围。
- 当前 Linux YooAsset 资源构建包含 14 张表和清单、公共 UI/字体/启动主题、营地与战场地图、抵达剧情和运行时材质依赖；地址与 bundle 数以[资源报告](Documentation/TowerDefense/Validation/resource-build.json)为准。没有将资源构建当作 IL2CPP 主包通过。
- 带 `--novorizon-native-smoke` 的 Editor 运行观察到成功标记；实际 Player 的自动退出码仍需在打包验收时检查。

## 尚未通过本轮验证的范围

- IL2CPP 主包、真实 Player DLL 加载与平台设备运行。
- 本轮未重跑 HTTP 更新站下载或真实离线 bundle；资源模拟不能替代这些验证。
- 硬件 GPU、手机触控、音频输出、完整渲染效果和性能预算。
- 正式账号服务、服务器复演、正式结算与发奖。
- 动画烘焙与资源持久化的完整原生测试集；不能用单例或启动回归替代其结论。

当前启动共享契约和 AOT 代码必须随匹配的主包基线构建；脚本编译通过不构成旧主包热更兼容声明。

当前客户端画面与业务报告见[交付记录](Documentation/TowerDefense/Delivery.zh-CN.md)。

## 运行

```sh
python3 Tools~/verify.py --dotnet /path/to/dotnet
UNITY_EDITOR=/path/to/Unity bash Tools~/unity-validate.sh compile
```

实际连续启动验收使用 `Game.Editor.GameEntryValidation.Run`，不传 `-quit`，由验收入口完成后退出。原始 JSON 在 `Artifacts/NativeValidation`；仓库中的对应报告用于审阅。
