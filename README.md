# GameStarter

独立 Unity 2022.3.62f1 启动工程。本分支实现塔防业务原型，依赖 UnityPackages 固定提交 `afc98c6424844d0032e3138f31b7c994e6722470`，包含 Timer 更新驱动生命周期修复（SDK PR #14）；包元数据预备版本 preview.27 尚未发布标签。工程通过固定 Git 来源管理依赖，不依赖相邻仓库或本地 file: 路径。

## 从哪里开始

- [Unity 云端编译与验收](Documentation/UnityValidation.zh-CN.md)：从 GitHub 获取、导入全部示例、Editor/Player 脚本编译、Play、IL2CPP 和补丁。
- [Demo 入口](Documentation/Demos.zh-CN.md)：示例前置条件和预期结果。
- [更新站部署](Deployment/README.md)：Nginx、目录与上传顺序。
- [验证范围](VALIDATION.md)：模型回归与原生验收分别记录。
- [塔防业务原型](Documentation/TowerDefense/README.zh-CN.md)：本分支的玩法、9 张业务表、逻辑/表现分层、账号与结算契约、UI 与地图方案。
- [塔防本轮交付记录](Documentation/TowerDefense/Delivery.zh-CN.md)：实际 Unity 画面/通关结果、已实现边界和仍待补验的项目。

## 启动与业务

打开 Assets/Scenes/Bootstrap.unity 后 Play，日志包含 LUBAN_EXAMPLE、HOT_UPDATE_READY、GAME_STARTER_READY。

```text
GameEntry
  → HotUpdateManager 选择并验证发行
  → 准备精确版本资源；内置发行读取离线资源
  → 加载 AOT 元数据与热更 DLL（Editor 使用已编译程序集）
  → HotGame.InitializeManagersAsync
  → HotGame.StartGameAsync：加载 Luban 表、可选模块、业务
  → ReportReady
```

GameEntry 是 AOT 入口，不引用生成表。HotGame 是普通 C# 游戏入口；业务写在 Assets/HotUpdate，由 StartGameAsync 组织。关闭 Initialize Hot Update 时使用编译源入口，Manager 初始化仍在 HotGame。普通编译源 Player 使用 Native Validation → Build Local Source Player 菜单。

StartupModule 是可选扩展，默认空列表；GameSession 只协调这些模块。普通 Manager 在 HotGame 直接 Initialize，退出时调用 Release，不需要在业务层登记 Manager 所有权或失败回滚。

GameStartupOptions 只传输入配置和可选模块启动回调；资源就绪后，HotGame 直接创建场景加载器和热更侧 YooAssetUIAssetLoader，不通过 object 工厂传递。正常退出由现有集中释放列表清理 Manager（包括事件与定时器），不再通过全局生命周期注册表；关闭 Domain Reload 时，基类与 Source Generator 共用已有 EditorStaticReset 恢复编辑器状态。

命令行 Player 退出探针与表结构检查分别集中在 GameEntry.NativeValidation.cs、HotGame.NativeValidation.cs，供验收脚本使用；正常业务按生成类型直接查询表。共享 Game.Startup 构造签名和 AOT 程序集引用发生变化，已有 IL2CPP 主包需要重建基线后再发布补丁。

## 本分支的塔防业务原型

塔防是这个项目在 `Assets/HotUpdate/TowerDefense` 下实现的业务示例，不是 UnityPackages 通用框架新增的固定玩法。`HotGame.StartGameAsync` 加载 Luban 后，通过[塔防业务装配](Assets/HotUpdate/TowerDefense/Presentation/HotGame.TowerDefense.cs)创建纯 C# 战斗会话和表现；换其他游戏时替换该业务入口，继续复用资源、UI、数据和网络能力。

首次打开执行 `Tools → Novorizon → Tower Defense → Prepare TMP Font`，等待 `TOWER_DEFENSE_TMP_FONT_READY`；需要的 TMP Essentials 会由该配置流程导入。随后打开 `Assets/Scenes/Bootstrap.unity` 并 Play，日志应出现 `TOWER_DEFENSE_READY prototype-1` 和原有 Ready 标记。也可用 `Create Prototype Scene` 创建独立场景，它仍通过 AOT GameEntry 进入同一业务流程。

当前真实表内容为 6 波、7 建造点、3 塔型、4 怪物型、7 张卡，使用 9 张新增业务表，加原示例共加载 10 张表。选建造点和塔型、升级、开始波次，在波间三选一，守到通关或基地归零。主流程使用真实表，5 波的 `DemoConfiguration` 只用于独立领域测试 fallback。

地图是 imagegen 生成的 2D 底图，塔、基地和 NPC 暂用 3D primitives；所有文字使用 TMP。图片与动态 SDF 字体通过 YooAsset 租约加载，按 maps/ui/fonts 分类；字体许可随包保留在 `Assets/StreamingAssets/ThirdPartyLicenses/Noto-CJK.txt`。PC 与手机横屏、触控共同入口已配置，移动真机和最终 3D 美术尚未验收。

本轮已在真实 Unity 2022.3.62f1 / Linux / OpenGLCore 的 llvmpipe 软件渲染环境跑通 6 波胜利，验证实际 Button 事件、重复操作拒绝、TMP 文字及停止 Play 清理，保留 4 张实际渲染截图；72 项完整模型、12 项领域、13 项账号协调回归通过。当前没有真实登录或发奖后端；失败/重开、手机真机、IL2CPP 和声音仍未执行原生验收。具体指标、日志与补验状态见[交付记录](Documentation/TowerDefense/Delivery.zh-CN.md)。

## Luban 与设置

| 内容 | 路径 |
| --- | --- |
| Excel 与表定义 | Config/Luban/Data、Config/Luban/Defines |
| 热更表代码 | Assets/HotUpdate/Generated/Data，Game.Data.Generated 程序集 |
| YooAsset 数据 | Assets/GameAssets/Data/Luban |
| 设置保存/恢复 | Assets/HotUpdate/GameSettings.cs |

生成表关闭 autoReferenced，AOT 不引用 cfg.Tables。表结构可在现有 AOT 能力与程序集拓扑范围内随补丁更新。表已通过 YooAsset 字节加载器接好，不需逐表注册：

```csharp
var row = DataManager.Instance.Get<cfg.Tables>().TbExample.Get(1);
GameSettings.Instance.SetMasterVolume(0.8f);
GameSettings.Instance.SetGroupVolume(AudioGroup.Music, 0.5f);
await GameSettings.Instance.RebindAsync("Move", 0);
```

修改表后执行 Tools → Novorizon → GameStarter → Generate Tables，并等 Unity 编译完成。设置在音频/输入初始化后恢复，修改、后台、失焦和退出时保存；自动保存不覆盖无效存档，明确修改设置后才替换。

## 构建与发行

Tools → Novorizon → GameStarter：

1. Generate Tables：使用 .NET 8 和官方 Luban CLI 生成表。
2. Build Resources：构建当前平台的 YooAsset 资源。
3. Build New Player：资源、公钥、HybridCLR 原生运行时、基线和 IL2CPP 主包。
4. Export Signed Update：编译兼容 DLL、构建资源、签名配套清单、整理发布目录。

首次主包构建在工程外生成 RSA-3072 私钥，公钥资产进入 Assets/Resources。已有公钥时需使用匹配私钥；可通过 NOVORIZON_SIGNING_KEY_PATH 指定工程外的私钥文件，不自动替换发行密钥。

输出在 Artifacts/Publish/{assets,manifests}，先上传包含代码与普通资源的 assets，最后发布清单；旧主包仍需对应基线和资源。旧 WR 备份流程的 patches 目录继续按原版本维护。下载根为 https://15000.top/game-updates/（443），游戏 TCP 为 15000.top:22370，统一配置在 GameReleaseConfiguration。模板不自动连接业务服务器。

签名清单绑定 DLL 哈希、基线和资源版本。资源准备失败时，在加载代码前切回配套内置发行；加载代码后不在同一进程替换发行。AOT、契约和程序集拓扑变化需重建主包。

## 更新与维护

```sh
python3 Tools~/sync-packages.py --ref afc98c6424844d0032e3138f31b7c994e6722470
python3 Tools~/verify.py --dotnet /path/to/dotnet
```

固定 Git 标签不会自动更新。模型回归不代替 Unity 导入、图形或 IL2CPP；原生结果按验收环境生成的日志和报告核对。导入包示例只用于验证副本，不提交回模板。

DLL 和 AOT 补充元数据现已纳入 YooAsset 同一资源版本，默认先统一下载全部缺失资源，再校验并加载代码。构建、职责与上传顺序见 [统一下载](Documentation/UnifiedDownloads.zh-CN.md)，原 UnityWebRequest 实现保存在 [备份分支](Documentation/UnityWebRequestBackup.zh-CN.md)。
