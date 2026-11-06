# YooAsset 统一下载

DLL、AOT 补充元数据、Luban 数据及其他游戏资源使用同一个 DefaultPackage 的精确资源版本。默认通过 YooAsset 下载这个版本全部缺失的文件，完成后读取代码字节，按签名发行清单校验，再交给 HybridCLR/Assembly.Load。缓存中已有且有效的文件不重复下载；当前不按 code 标签单独安排第一批下载。

## 管理器职责

ResourceManager 负责 YooAsset 初始化、下载、资源租约和释放。HotUpdateManager 负责代码发行选择、基线兼容、签名与字节校验、AOT 元数据及热更程序集加载、HotGame 启停；它通过项目回调等待完整资源准备，不接管场景、音频或 UI 等资源的日常管理。

GameEntry 提供 prepareContentAsync 和 loadCodeAsync：前者初始化并下载所选资源版本，后者通过 ResourceManager 的 TextAsset 租约读取 `HotUpdate/<baselineId>/<assembly>.dll.bytes`。必须先准备资源，再读取代码。读取结束立即归还资源租约；字节校验和程序集加载不受句柄释放影响。

## 构建及发布

1. 新主包：先准备供 HybridCLR 生成阶段使用的资源，再生成 DLL/AOT 基线。GameBuild.BuildContent 回调把这些代码文件暂存为 `.dll.bytes`，重新构建最终 YooAsset 内置资源，绑定精确版本，最后构建 Player。
2. 补丁：先编译热更程序集、验证 AOT/桥接兼容，再暂存 DLL 和该主包归档中的 AOT 元数据，构建新的 YooAsset 资源版本，最后导出签名发行清单。
3. 暂存目录 `Assets/GameAssets/HotUpdate` 由工具生成，源码仓库仅保留目录和采集规则。每个代码文件单独打包，保留同名文件 GUID，支持增量下载。AOT 元数据的内容固定在主包基线中，更新不得替换。
4. `Artifacts/Publish/assets/` 包含资源包及版本清单；`manifests/` 包含签名发行入口。先完整上传 assets，再以临时文件重命名发布 manifests 中的 release.json。新流程不发布独立 patches DLL。

发行入口 release.json 仍是通过 HTTP 读取的小型控制清单；DLL、AOT 元数据和普通资源的下载及缓存均由 YooAsset 负责。代码缓存只保存发行清单和启动健康记录，不再额外缓存一份下载的 DLL。

## 失败与兼容

选中远程发行后，资源准备或任何代码哈希校验失败，会在加载程序集之前退回匹配的内置资源及代码。取消不会改成回退；加载过元数据或程序集后不会尝试在同一进程替换程序集。

新基线声明 codeInResources，缺少资源代码提供方时直接失败，不静默改用独立 DLL 下载。本次 AOT 接口、构建顺序和发行格式有变，已有游戏需要重建主包。旧流程见 [WR 备份](UnityWebRequestBackup.zh-CN.md)。

当前工程提供启动状态与下载进度，尚无实际启动 UI；统一下载不会自动创建更新界面。
