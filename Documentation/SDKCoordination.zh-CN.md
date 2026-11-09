# GameStarter 与 UnityPackages 的协作

[UnityPackages 对话](codex://threads/01a1b12e-ff35-762a-b973-67981ef710ad?hostId=durable) 维护通用包、包版本、依赖闭包、聚合目录及发行；本对话维护 GameStarter、具体业务和 Unity 接入验收。GameStarter 有需求时先交接，不同时在 SDK 中实现另一份修改。

## 交接方法

有实际问题或接口需求时，以 SDK 仓库的 Issue 或 PR 作为持久需求记录。需求尚不明确时先提供下面的信息，避免仅说“改一下初始化”。双方确认接口语义后，SDK 维护方交付 PR 与固定 SHA；GameStarter 再升级、验证并反馈结果。

应用具备跨对话发送工具时，可以直接传递这些链接；目前本对话能读取指定对话，但没有向现有对话发消息的接口。读取不代表对方已收到、认可或开始处理。若采用需求队列，应先让两个对话约定在开工和收尾时读取未关闭项；仅建立 Issue 不会自动唤醒另一个对话。

```text
现象与重现：具体触发条件、输入、日志；哪些是已验证事实。
当前输入：GameStarter 提交、SDK SHA、Unity 版本、平台和资源模式。
期望行为：先后顺序、错误和取消如何处理；哪些工作不得提前执行。
所需接口：签名、参数、返回值、状态语义及资源归属。
兼容要求：旧接口如何处理，是否影响 AOT/主包基线、数据或发行格式。
验收条件：模型回归与真实 Unity 分开说明，需要哪些通过证据。
交付：SDK PR、固定 SHA、变更说明、测试结果和未验证项。
```

等待 SDK 时，本项目继续处理不依赖新接口的业务、界面、数据和接入准备。接口尚未交付的部分明确标注等待，不靠测试桩或临时分叉宣称已完成。

## 已完成的接入基线

以下能力已在 [UnityPackages PR #15](https://github.com/Novorizon/UnityPackages/pull/15) 和 [GameStarter PR #18](https://github.com/Novorizon/GameStarter/pull/18) 合并。它们是后续优化应保留的接入约定，不是待开发需求：

| 阶段 | 接口与约束 |
| --- | --- |
| 资源系统初始化 | `ResourceManager.InitializePackageAsync` 仅建立文件系统，不查询版本、读取资源清单或下载内容 |
| 热更管理器初始化 | `HotUpdateManager.Initialize` 仅保存和校验配置，不下载、不创建 HotGame |
| 更新执行 | `GameHotUpdate.RunAsync` 调用 `HotUpdateManager.StartAsync`，执行发行选择、资源准备、代码加载与业务启动 |
| 内容准备 | `ResourceManager.PrepareContentAsync` 准备精确资源版本；旧 `InitializeAsync` 保持兼容 |
| 离线选择 | 使用配套内置发行，不损坏、拒绝或删除合法联网缓存；切回联网后缓存仍可用 |
| 退出 | GameEntry 取消本次启动，并按 HotUpdateManager、ResourceManager 的顺序释放；旧回调不得影响新一轮启动 |

接入以本项目 manifest 中的固定提交为准。包方交付 PR 与源码 SHA 后再升级；预备版本名不等于已发布标签。

GameStarter 的业务 UI 使用 TMP，图片、字体和数据通过 YooAsset 管理。未来接入多语言等包时，需要核对 TMP 绑定与字体资源的适配；不能仅因为 SDK 提供 Legacy Text 示例就把业务 UI 改回 Legacy Text。

## 平台 SDK 第一阶段

Google Play 和 App Store 发行 checkout 固定 Unity IAP `5.5.0`。共用工程保留 Android API 22，并按发行目标选装官方 SDK。Payments `0.1.0-preview.3` 固定为 UnityPackages 提交 `0329061309bd2113b17aabbcf41777e5cbdf00bb`。Ads、Firebase 及 Firebase 所依赖的 Novorizon Auth、Analytics 封装固定为 UnityPackages `v0.1.0-preview.37` 的提交 `4ebef06437166d59bc8841b89f53c5ee140448e8`。现有 Framework 与聚合包继续使用原固定版本。Novorizon Auth 和 Analytics 在这里仅提供程序集依赖，厂商产品按各发行 checkout 安装。 [平台 SDK 接口与版本](https://github.com/Novorizon/UnityPackages/blob/4ebef06437166d59bc8841b89f53c5ee140448e8/Documentation/PlatformServices.zh-CN.md)

`GameStarter.PlatformServices` 提供显式装配入口，调用方持有 Manager 并在 Unity 主线程初始化。当前启动与业务程序集没有引用该入口。未选渠道、未提供配置或不支持的平台上保持服务关闭，广告尚无加载、展示或奖励调用。

| 发行目标 | 内购 | 广告 |
| --- | --- | --- |
| 国内 Android | 等待发行方确认目标商店和 `IPaymentStore` 适配。保持 `com.unity.purchasing` 未安装。 | 国内穿山甲，Android `com.pangle.cn:mediation-sdk:7.8.1.4` |
| Google Play | `NativeStore.GooglePlay` | AppLovin MAX Unity `8.6.6` |
| App Store | `NativeStore.Apple` | 根据 iOS 发行区域选择穿山甲 `Ads-CN/BUAdSDK:7.8.1.1` 或 MAX Unity `8.6.6` |

### 内购与广告配置

- 商店发行 checkout 在 Package Manager 中安装 `com.unity.purchasing@5.5.0` 并保留生成的 lock。Google Play 发行配置需将最低版本设为 API 23 或更高，因为 IAP 5.5.0 的构建处理会加入 Billing 9.0.0。
- 内购需要平台应用标识、商店商品 ID 与商品类型、逻辑商品 ID 映射，以及绑定游戏会话的可信验单与持久化幂等发货实现 `IPurchaseFulfillment`。将商品表和该实现传给 `PlatformServiceManagers.CreateIap`。调用方初始化 IAP 后才能购买和补单。验单与持久化发货成功后才确认商店订单。 [Unity IAP 安装与初始化](https://docs.unity.com/en-us/iap/set-up-in-app-purchasing)
- 每个发行 checkout 只安装一种广告原生依赖。编译符号只控制 C# 适配器，切换渠道时也要移除上一渠道的 SDK、解析产物、Gradle 依赖及 iOS 包声明，再检查导出依赖。
- 国内发行安装 EDM4U，选择 Android 或 iOS 后执行 **Novorizon > Platform SDKs > Install domestic CSJ**，再解析 Maven 或 CocoaPods 依赖。桥接和依赖声明位于 `Assets/NovorizonPlatform/Ads`。 Android `7.8.1.4` 官方 AAR 声明最低 API 24，国内发行配置需相应设置。待提供每端穿山甲 App ID、各广告场景的广告位 ID、隐私同意来源，iOS 另需发行方确认 SKAdNetwork 和隐私清单。使用直连库存。 [穿山甲 Android](https://www.csjplatform.com/supportcenter/5397) · [穿山甲 iOS](https://www.csjplatform.com/supportcenter/28721)
- 海外发行导入 MAX `8.6.6`，保留一份 EDM4U。通过 **AppLovin > Integration Manager** 写入真实 SDK Key，再执行 **Novorizon > Platform SDKs > Enable installed AppLovin MAX**。待提供对应应用的 SDK Key、广告位 ID 与隐私同意来源。仅在 Integration Manager 已配置、广告位有效且已有隐私同意时调用 `CreateMaxAds`。 [MAX 安装指南](https://support.applovin.com/en/max/unity/overview/integration) · [MAX 8.6.6](https://github.com/AppLovin/AppLovin-MAX-Unity-Plugin/releases/tag/release_8_6_6)

### Crashlytics 发行配置

- 只导入官方 `FirebaseCrashlytics.unitypackage`，或使用官方 UPM 存档安装 `com.google.firebase.app` 与 `com.google.firebase.crashlytics`（均为 `14.1.0`）以及 EDM4U `1.2.190`。两种导入方式选一种。所需版本要求 Android API 24+、iOS 15+。iOS Firebase 依赖走 Swift Package Manager，使用 Firebase Apple SDK `13.1.1`。 [Google Unity 包存档](https://developers.google.com/unity/archive) · [Firebase Unity 版本说明](https://firebase.google.com/support/release-notes/unity)
- 需要 Firebase 项目为实际 Android application ID、iOS Bundle ID 注册的官方文件。分别放置于发行工程的 `Assets/google-services.json`、`Assets/GoogleService-Info.plist`。标准 Assets 安装用 **Novorizon > Platform SDKs > Synchronize installed Firebase SDKs**。UPM 安装仅在目标组启用 `NOVORIZON_FIREBASE_APP` 和 `NOVORIZON_FIREBASE_CRASHLYTICS`。完成配置后才可显式调用 `CreateCrashReporterAsync`。 [Crashlytics 安装指南](https://firebase.google.com/docs/crashlytics/unity/get-started)
- 冷启动默认关闭上传。Android 在最终合并的 manifest 的 `<application>` 下设置 `<meta-data android:name="firebase_crashlytics_collection_enabled" android:value="false" />`。iOS 在 Xcode 导出的应用 `Info.plist` 中设置布尔值 `FirebaseCrashlyticsCollectionEnabled = false`。SDK 的运行时启用状态会跨启动保留，发布前需验证冷启动及撤销同意的行为。第一阶段没有调用 `SetCollectionEnabled(true)`，也没有写入玩家 ID、自定义日志或异常上下文。 [Android 原生开关](https://firebase.google.com/docs/crashlytics/android/customize-crash-reports) · [iOS 原生开关](https://firebase.google.com/docs/crashlytics/ios/customize-crash-reports)
- 官方 Firebase Unity `14.1.0` 的 `AppDependencies.xml` 和 `CrashlyticsDependencies.xml` 都声明了 Android `firebase-analytics:24.0.0`。Android 发行 checkout 导入后、Resolve 前运行 `python Tools~/prepare-crashlytics-android.py`。脚本支持标准 Assets 和嵌入到 `Packages/com.google.firebase.*` 的官方 UPM 包，只移除这两处声明。清理旧解析产物并重新 Resolve，在导出工程执行 `gradlew.bat :launcher:dependencies --configuration releaseRuntimeClasspath`，确认保留 Crashlytics、Crashlytics NDK 和所需的 `firebase-measurement-connector`、`firebase-config-interop`，同时排除 `firebase-analytics`、`play-services-measurement*`、Auth、Messaging 和 Remote Config 产品库。Android 真机崩溃上报通过前保持停用。

发行验收需检查 Android 合并 manifest 与 Gradle 依赖、iOS Info.plist 与链接产物，再用渠道设备核对沙盒验单、测试广告和 Crashlytics 符号及入库。国内 Crashlytics 还需验证目标网络下的实际送达。

## 启动验收和退出

命令行 Player 验收通过 `--novorizon-native-smoke` 开启：启动完成后输出 Ready/Failed 标记，并以成功或失败码退出，供脚本判定。探针读取本次启动任务和状态，不向业务暴露额外等待接口；常规启动不主动退出。Editor 验收分别检查启动、界面和退出清理，不能代替 Player/IL2CPP/设备验收。

启动 token 在 Player 和 Editor 都使用。入口退出、被销毁或主动释放时取消本次启动，异步操作恢复后不得继续初始化服务、加载代码或发布 Ready。切后台、关闭普通界面或重开战斗不自动取消整个入口；后台事件仍按业务约定保存设置。取消不会强制中断不支持取消的原生操作，其清理完成仍由资源管理器跟踪。

## 避免环境与版本冲突

同一路径名称不证明两个对话位于同一运行实例；本地笔记也不保证另一个对话可见。交付以 Git 提交、Issue 和 PR 链接为准。

同一环境中并行开发时，各自使用独立 checkout/worktree 和分支。Unity 的 Library、资源导入、构建与许可证进程按实际运行实例处理，不共用同一个正在打开的 Unity 项目目录。一个仓库的分支切换不能作为另一个对话的通知方式。

包版本、聚合目录和新发行标签由 SDK 维护方统一安排，已发布标签不移动。GameStarter 不自行发布补丁包标签；升级时记录 SDK SHA、必要的主包重建要求以及实际验收范围。
