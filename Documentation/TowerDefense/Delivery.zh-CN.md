# 当前塔防表现与验收

启动场景为 `Assets/Scenes/Bootstrap.unity`，SDK 固定提交以 manifest 为准。当前地图配置 `forest-diorama-1`，由真实 Luban 表加载 6 波、7 建造点、18 个路线点、3 塔型、4 怪物型和 7 张卡。

## 表现与资源

- 地图为 imagegen 生成的低多边形台地式 2D 预渲染图；道路、桥梁、池塘和台地厚度已绘入图中。固定 55° 正交相机，逻辑坐标映射到 Unity XZ 平面；台阶不是运行时 3D 导航地形。
- 塔、蓝顶基地和怪物使用程序化共享低模网格；当前没有正式骨骼动画、LOD 或外部模型资产。
- 松绿/铜金九宫格面板、3×3 塔和强化图标、独立 TMP 文本。没有烘焙价格、血量或效果文案。
- 地图、图标、面板和字体经 YooAsset 租约加载，继续按 maps/ui/fonts 分组。素材来源与校准方式见 [ArtSources.json](ArtSources.json) 和 [UIAndArt](UIAndArt.zh-CN.md)。

## 已通过的范围

| 项目 | 当前证据 |
| --- | --- |
| 项目回归 | 69 项；包括真实 Luban 解码、编辑路线、配置关卡可通关与固定 tick 重放 |
| 战斗 / 账号 | 12 项纯领域、13 项账号协调回归；账号使用测试后端 |
| Unity 脚本编译 | Unity 2022.3.62f1；8 Demo、11 Editor / 8 Player 程序集 |
| 桌面图形 | Linux / llvmpipe OpenGLCore，1440×789 实际渲染帧 |
| 宽屏布局 | 同一 Editor 在 1440×665（约 19.5:9）渲染与验收，不是真机测试 |
| 输入 | 七个建造点的中心均可被 Physics 射线命中且没有 HUD 遮挡；Input System 注入鼠标按下/抬起经过实际 Update 点选，再调用真实建塔 Button 回调，验证扣费与落点 |
| 按钮与战斗 | 暂停/继续、三选一卡、拒绝重复选卡、胜利与失败重开、停止 Play 清理 |
| 通关 | seed 12345，6 波、126 击杀、0 漏怪、2178 tick、13 次升级、5 次选卡 |
| 字体 | 两种布局均 TMP 40 / Legacy Text 0，字体依赖完整 |
| 资源包 | StandaloneLinux64，15 地址、7 bundle，资源版本 20261107141655999；包括新版地图、图标、面板、字体/OTF/TMP Shader 依赖 |

## 画面与报告

[部署](planning.png) · [战斗](battle.png) · [三选一卡](three-cards.png) · [胜利](victory.png) · [宽屏战斗](phone-battle.png) · [宽屏选卡](phone-three-cards.png)。全部为实际 Unity 渲染帧。

[桌面报告](Validation/prototype.json)、[宽屏报告](Validation/phone-layout.json)、[编译](Validation/player-compile.json)、[程序集](Validation/sample-assemblies.json)、[资源包](Validation/resource-build.json)、[输入范围](Validation/inputs.json)。

原生运行入口为 `Game.Editor.TowerDefenseValidation.Run`；不传 `-quit`，由验收完成后退出。加 `--novorizon-phone-layout` 检查宽屏布局。完整构建和发布沿用项目的统一资源/DLL 发行流程，图片、表和代码需要配套发布。

## 尚未完成

正式账号服务、服务器结算与发奖、IL2CPP、硬件 GPU、手机触控/性能和声音未作通过声明。当前战斗采用固定路线、固定建造位置与单体即时命中；范围伤害、动态堵路、召唤、正式角色动画与长期性能优化属于后续工作。
