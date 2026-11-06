# 本轮交付与验证记录

本文件区分源码交付、独立模型回归和真实 Unity/设备验收。当前记录对应 `feature/tower-defense-prototype` 的开发内容，SDK 固定为 `afc98c6424844d0032e3138f31b7c994e6722470`（preview.27 预备元数据，未发布标签）；没有执行的项目明确标记为补验中，不能用界面设计稿或模型回归替代原生结果。

## 交付范围

| 项目 | 本轮范围 | 边界 |
| --- | --- | --- |
| 当前内容备份 | 两仓库均已创建并推送 `backup/pre-tower-defense-20261106` | 精确提交记录在下表，既有发布标签保持不变 |
| 纯 C# 领域 | 固定 50 ms tick、编辑路线移动、固定点造塔/升级、单体即时命中、金币、基地伤害、波次、三选一、胜败 | 无 Unity/Manager/网络依赖；3 类塔尚无范围伤害、缓速或真实弹道 |
| 数据 | 9 张业务表及生成物；主关卡 6 波、7 建造点、3 塔型、4 怪物型、7 卡 | `DemoConfiguration` 的 5 波静态数据仅作独立测试 fallback；正式启动必须读真实表 |
| 表现 | 2D 地图、3D 塔/基地/NPC 占位、固定相机、HUD、建造/升级、三选一、暂停、结果/重开 | primitives 不是最终 3D 美术；原型 Canvas 不等于全部正式 Page/Popup 已制作 |
| 输入 | PC 鼠标与手机触控的共同交互入口、safeArea、CanvasScaler | 编译或模拟触控不代表真实手机手感和性能已通过 |
| 文字 | 所有显示文字使用 TextMeshProUGUI；本次运行统计 19 个 TMP 组件、0 个 Legacy Text | 实际画面包含中文；更广泛字形覆盖和目标手机内存仍需验收 |
| 字体资源 | 合法 Noto OTF 子集、OFL 来源、动态 SDF、1024 图集、多图集按需补字 | 源 OTF 和字体材质/图集依赖随 YooAsset；不承诺覆盖源字体子集以外的任意汉字 |
| 图像资源 | imagegen 生成地图、卡牌图标与透明卡框；按 maps/ui/fonts 组织 | 所有运行时图像和字体均通过 YooAsset 租约；没有资产时不配置空的 models/vfx/audio 采集目录 |
| 账号与结算接口 | IGameBackend、AccountSession、只读 DTO、内存令牌、世代隔离、过期刷新、当前进程内幂等请求 | 没有运行时假后端，没有真实登录/发奖服务，没有 Protobuf 服务器适配 |
| 设计文档 | 表结构、逻辑/表现分层、网络权威、UI、地图路线、后续分阶段工作 | 稀有度分层抽取、多效果系统、局外成长等是后续建议，不是已实现玩法 |

## 未实现或未作通过声明

- 正式账号渠道、登录协议、真实 TCP/Protobuf 业务服务与部署；`NetworkManager.Initialize()` 不等于登录。
- 服务端战斗权威、防作弊重放、正式经济奖励、排行榜、付费商城和运营后台。
- 跨进程结算恢复、查询未完成局、完整命令日志上传和服务器复演；当前报告只有摘要，hash 不能证明战斗合法。
- 多人合作、PvP、动态堵路寻路、塔合成、元素状态、AOE、物理弹道、召唤与主动技能。
- 正式 3D 模型/动画、音效、粒子、关卡编辑工具、目标手机性能与真机触控验收。
- PureMVC 和额外的通用封装层；本轮继续使用明确 owner、领域、Presenter 和现有资源/UI/网络接口。

## 数据与资源验收约定

当前物理 schema 见 [towerdefense.xml](../../Config/Luban/Defines/towerdefense.xml)。配置适配必须检查主键、关卡归属、路线与波次排序、刷怪外键、候选池和资源地址；不要在正式主入口静默改用静态 Demo 来掩盖数据加载失败。

稳定地址包括 `TowerDefense/Art/Map`、`TowerDefense/Art/CardIcons`、`TowerDefense/Art/CardFrame` 和 `TowerDefense/Fonts/TowerDefenseUI`。磁盘可以合理分类，但改目录不能悄悄改变已写进 `TdViewAsset` 的地址。主关卡视图的动态 SDF 字体、源字体依赖、图像及材质需要进入真实 YooAsset 构建，而非只在 Editor Inspector 引用下能显示。

打开/重开/退出时先销毁使用中的文字、Image、SpriteRenderer 和 3D 视图，再按 owner 归还资源租约。检查反复进入战斗后没有多份相机、EventSystem、会话驱动、字体资产或重复网络/事件订阅。TMP 动态图集页数、字体源文件和原生对象回收需要真实 Unity 内存/画面检查，不能靠纯逻辑模型断言。

## 开发运行方法

1. 用声明版本的 Unity 2022.3 打开工程，等待固定包依赖解析、脚本编译和数据导入完成。
2. 执行 `Tools/Novorizon/Tower Defense/Prepare TMP Font`，等待 `TOWER_DEFENSE_TMP_FONT_READY`。需要时该流程会导入 TMP Essentials；单独导入的菜单为 `Tools/Novorizon/Tower Defense/Import TMP Essentials`。素材导入配置入口为 `Configure Prototype Art`。
3. 打开 `Assets/Scenes/Bootstrap.unity`。可选执行 `Tools/Novorizon/Tower Defense/Create Prototype Scene` 生成 `Assets/Scenes/TowerDefensePrototype.unity`；独立场景也通过 AOT GameEntry 进入同一热更业务，使用真实 Luban/YooAsset 链路，不再维护 Inspector 素材直连和静态 Demo 的第二入口。
4. Play 后确认显示“离线原型”或等价明确状态，观察 6 波、7 建造点的真实表内容。开始前造塔，升级后守波，在波间选一张卡，直到胜利或基地归零，再重开。
5. 修改配置表需执行现有 Luban 生成流程；发布使用现有 YooAsset 统一资源/DLL 构建和签名发行流程，不能把未经构建的源图片/OTF 直接上传当资源包。

独立回归可从工程根目录执行：

```sh
dotnet run --project Verification~/TowerDefenseDomain/TowerDefenseDomain.csproj -c Release
dotnet run --project Verification~/TowerDefenseOnline/TowerDefenseOnline.csproj -c Release
```

上述用例使用纯 C# 或测试后端，不会连接真实服务器。完整工程验证、Unity 编译和原生运行命令沿用 [Unity 验收说明](../UnityValidation.zh-CN.md)。图像生成不能替代运行画面，Editor 编译不能替代 IL2CPP/真机运行。

## 本轮真实记录

| 项目 | 记录 |
| --- | --- |
| GameStarter 备份分支 / 提交 / 推送 | `backup/pre-tower-defense-20261106`，`02a96cbfbd475c62b5df1a03043eee4f4e0445ae`，已推送 |
| UnityPackages 备份分支 / 提交 / 推送 | 同名分支，`a7c18218d3b3cf3374ff68146ac156f1c39c8f7c`，已推送 |
| 代码输入与固定包提交 | 业务开发分支 `feature/tower-defense-prototype`；SDK `afc98c6424844d0032e3138f31b7c994e6722470`，已同步 manifest 与可选模块目录；Timer 生命周期修复对应 UnityPackages PR #14 |
| Unity 版本、图形与操作系统 | Unity 2022.3.62f1，Linux，OpenGLCore，llvmpipe 软件渲染；实际渲染已启用，不是 `-nographics` 截图 |
| 字体资产配置菜单 / 来源与许可证 | `Prepare TMP Font`；合法 Noto OTF 子集，源许可 `Assets/GameAssets/TowerDefense/Fonts/LICENSE-Noto.txt`，发行许可 `Assets/StreamingAssets/ThirdPartyLicenses/Noto-CJK.txt` |
| 完整工程模型回归 | 72 项通过，其中包含 5 项实际 Luban 集成场景；不能与领域/账号项相加后声称是全部原生用例 |
| 领域回归 | 12 项纯 C# 回归通过 |
| 账号会话回归 | 13 项通过；FakeBackend 只在验证中，未证明真实服务器登录或结算 |
| 9 张业务表与真实表适配 | 9 张新表加原 Example 共 10 张表由实际 Bootstrap 链路加载，关卡配置 `prototype-1`；6 波真实表内容已跑通 |
| Editor 脚本编译与运行 | 已在上述真实 Unity 环境编译并进入 Play，运行主入口、Luban、YooAsset 图像和字体 |
| Player 脚本编译 | 实际 Unity Player 脚本编译通过；8 个 Demo、11 个 Editor 程序集、8 个 Player 程序集逐项核对，包含 `Game.Framework.Demo.dll`；报告 `Artifacts/NativeValidation/compile.json` 与 `sample-assemblies.json` |
| 主入口与独立场景启动 | `Bootstrap.unity → GameEntry → HotGame → Luban/YooAsset → 战斗表现` 已验证；独立场景菜单已接同一入口，尚未单独原生验收 |
| TMP 中文、字体地址与依赖 | 实际统计 TMP 22、Legacy Text 0；地图/图像和 `TowerDefense/Fonts/TowerDefenseUI` 经 YooAsset 加载，4 张截图保留中文表现；动态图集峰值内存、更多字形和目标手机仍未验收 |
| YooAsset 正式资源包构建 | 已完成 StandaloneLinux64 实际构建：15 个地址、7 个 bundle；maps/ui/fonts/data 标签已核对，字体的 OTF 与 TMP shader 依赖均在构建报告中。版本 `20261106184748902`；本轮未重新验证 Player HTTP 下载 |
| 停止 Play 清理 / 资源重开 | 停止 Play 后视图销毁，Timer/Event 无实例；胜利/失败后通过按钮事件重开，验证新会话归零及旧塔怪原生对象销毁；整包资源引用计数和长期内存峰值未验收 |
| 实际胜利 / Button 事件 / 连点 | seed `12345`，6 波、126 击杀、0 漏怪、1540 tick、13 次升级、5 次选卡，最终 `Victory`；实际 Unity Button `onClick.Invoke` 事件与重复操作拒绝已验证；没有将其表述为设备指针点击测试 |
| 实际失败 / 重开 / 暂停按钮 | 胜利后重开 → 无塔防守第 2 波漏怪 20 个真实失败 → 失败后重开，均通过实际 Button 事件验证；暂停由验收驱动调用领域接口验证，未将此表述为鼠标/触摸点击通过 |
| 界面创建失败清理 | 空塔配置在创建前拒绝；无效配置抛出后，租约随 owner 延迟到原生对象销毁再释放，测试计数恰为 1；`failedCreationCleanupVerified=true` |
| 真实运行截图与报告 | `Artifacts/TowerDefense/validation.json`；`planning.png`、`battle.png`、`three-cards.png`、`victory.png`，均为 1440×789 实际渲染帧 |
| 外部 Unity 日志 | `/workspace/tower-defense-work/unity-visual-final.log` |
| 目标手机 / IL2CPP / 声音 | 当前尚未执行，补验中；移动横屏和触控共同入口已配置，不声明真机手感/性能、硬件 GPU 或声音通过 |
| 后端登录和正式结算 | 尚无实际后端接入，不作通过声明 |

最终交付应把实际运行日志、截图、输入版本和未完成项一起保留。若某一项因环境、素材、协议或工具限制未执行，应记录具体限制，不能把“计划中”“有接口”或“模型通过”改写成完整功能验收通过。

本轮实际报告的仓库快照见 [Validation](Validation/)，运行画面见[战斗](battle.png)和[三选一](three-cards.png)。界面资源和新依赖需要进入配套主包/HybridCLR 基线后再发行；未将本轮结果作为旧主包的热更兼容声明。
