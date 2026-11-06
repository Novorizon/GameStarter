# Unity 云端编译与验收

本说明用于直接编译 GitHub 中的工程及包。使用真实 Unity；.NET 模型回归与原生结果分别记录。

## 仓库与输入

| 内容 | 来源 |
| --- | --- |
| Unity 工程 | https://github.com/Novorizon/GameStarter，main；本次发行 v0.1.0-preview.26 |
| 包仓库 | https://github.com/Novorizon/UnityPackages，manifest 固定提交 9502631577cc0ae31d335332d09bb91cebc83a48（待发行 preview.26） |
| Editor | Unity 2022.3.62f1，或先明确记录使用的兼容版本 |
| 场景 | Assets/Scenes/Bootstrap.unity |
| 运行入口 | GameEntry 准备发行/资源；HotGame 初始化 Manager、设置与 Luban 表 |
| 验证输出 | Artifacts/NativeValidation，完整 Unity 日志和 JSON；测试另保留 XML |

```sh
git clone --branch main https://github.com/Novorizon/GameStarter.git
cd GameStarter
git rev-parse HEAD
```

工程 manifest 已列出 framework.all 与全部包来源。不需要把 UnityPackages 拖入 Assets，不修改为本地 file: 依赖。测试未发布包改动时，先发布固定提交/标签，再更新工程来源；编译日志中记录工程提交与包引用。

环境需要已授权 Unity、Git、Python 3。重新生成 Luban 表还需要 .NET 8 SDK。Editor 必须能访问 GitHub 与 UPM 服务；不把许可证、签名私钥或访问令牌提交到仓库。

## 1. 导入并编译全部示例

普通 UPM 安装不编译 Samples~。先在独立验证副本中导入声明的自有包示例，再用另一个 Unity 进程编译，确保新导入的程序集进入检查。

Linux/macOS shell：

```sh
export UNITY_EDITOR=/absolute/path/to/Unity
bash Tools~/unity-validate.sh import-samples
bash Tools~/unity-validate.sh compile
```

Windows PowerShell：

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe"
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase ImportSamples
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase Compile
```

ImportSamples 从已解析包的 package.json 读取 samples，复制到 Assets/NovorizonSamples，并写 imported-samples.txt。包目录中的点替换为下划线，例如 com_novorizon_framework，避免 .framework 后缀被 Unity 识别为原生插件目录而跳过 C#。目录已存在时拒绝覆盖；重新验证使用新的 checkout，或先自行移走该验证目录。它只导入自有包公开示例，不复制第三方包源码。

CompileScripts 在 Unity 完成 Editor 导入后，调用 HybridCLR 的脚本编译器检查 Player 程序集，核对热更 DLL 输出并写 compile.json。另按包中声明的全部示例 asmdef，逐一核对 Unity 实际发现的 Editor 程序集、DLL 文件、导入目录下的源文件以及非 Editor 示例是否进入当前 Player 编译列表，写 sample-assemblies.json；缺少任一项即失败。当前 8 个示例应有 11 个 Editor 编译程序集，其中 8 个 Runtime 程序集参与 Player 编译。此阶段不安装或替换原生 il2cpp，不构建 Player。宿主必须安装所选 Build Target 的支持模块；shell 保留工程当前目标，Windows 脚本使用 Win64。

也可直接调用入口：

```sh
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" -executeMethod Game.Editor.SampleValidation.ImportSamples -quit -logFile import-samples.log
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" -executeMethod Game.Editor.NativeValidation.CompileScripts -quit -logFile compile.log
```

通过条件：Unity 进程成功退出、日志无编译错误、compile.json 和 sample-assemblies.json 的 success 为 true，日志包含 NATIVE_COMPILE_READY 与 PACKAGE_SAMPLE_ASSEMBLIES_VERIFIED。导入清单的条目数本身不能证明示例参与编译。

## 2. Editor 启动与 Demo

```sh
bash Tools~/unity-validate.sh editor
```

Windows 使用 native-validate.ps1 的 Editor 阶段。该阶段不加 -quit，由启动探针判定 Ready/失败后退出。检查 editor.json 和日志中的 LUBAN_EXAMPLE、HOT_UPDATE_READY、GAME_STARTER_READY。

Bootstrap 使用已提交的 Luban 生成物与 YooAsset 模拟模式，不需要连接游戏服务器或下载站。默认没有公钥时不启用远程 DLL 下载。重新生成所有表可另调用 Game.Editor.NativeValidation.GenerateTables；它是异步入口，不带 -quit，生成完成后自己退出。

可交互 Demo 的入口和前置条件见 [Demo 索引](Demos.zh-CN.md)。Framework/UI 可以创建独立演示场景；资源和 GPU 示例需要匹配的采集/烘焙资源。批处理 C# 编译不作为界面、输入、音频和渲染通过的证明。

## 3. GpuAnimator EditMode 测试

在验证副本中配置测试依赖，保留现有清单字段：

```sh
python3 - <<'PYTHON'
import json
from pathlib import Path
path = Path("Packages/manifest.json")
manifest = json.loads(path.read_text())
manifest["dependencies"]["com.unity.test-framework"] = "1.1.33"
manifest["testables"] = list(dict.fromkeys(manifest.get("testables", []) + ["com.novorizon.gpuanimator"]))
path.write_text(json.dumps(manifest, indent=2) + "\n")
PYTHON
mkdir -p Artifacts/NativeValidation
rm -f Artifacts/NativeValidation/gpu.xml
```

```sh
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -assemblyNames GpuAnimator.Editor.Tests -testResults "$PWD/Artifacts/NativeValidation/gpu.xml" -logFile "$PWD/Artifacts/NativeValidation/gpu.log"
```

不带 -quit，由 Test Runner 管理退出。XML 必须包含非零测试且全部通过。图形环境、平台模块或授权不足时记录具体失败条件，不把跳过写成通过；实际 Shader 渲染仍需图形环境验收。

## 4. IL2CPP 主包与表结构补丁

该流程当前覆盖 Windows x64、Android ARM64、iOS ARM64。Linux Editor 可以先执行导入/脚本编译；它不等于已通过 Windows IL2CPP 构建。使用支持目标平台原生工具链的主机；Windows 验收脚本还会运行生成的 exe。

```powershell
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase Player
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase SchemaPatch
# 按发布步骤上传 assets、patches，最后发布 manifests 后：
.\Tools~\native-validate.ps1 -UnityEditor $unity -Phase PatchPlayer
```

BuildHybridPlayer 自动构建资源、准备公钥和基线、安装匹配的 HybridCLR 原生运行时，再构建 Player。SchemaPatch 会在验证副本中增加 TbNativePatchProbe，重新生成表和导出补丁；必须保留并运行原主包，不能用重建的新主包冒充表结构热更验证。

更新站为 https://15000.top/game-updates/，HTTPS 443。游戏 TCP 为 15000.top:22370，与下载独立。上传规则和 Nginx 配置见 [部署说明](../Deployment/README.md)。没有部署权限时保留 Artifacts/Publish 和验收状态，补丁下载阶段由实际部署后执行。

## 结果记录

记录工程提交、依赖标签、Unity/宿主版本、Build Target、执行入口、退出码、日志、JSON/XML 和未执行项。最终区分：Editor/Player 脚本编译、Editor Play、GPU 原生测试、IL2CPP 构建、原主包补丁加载、图形与设备验收。

编译失败后从第一条真实错误修复并重跑，不用旧 DLL、已有 Library 或空 XML 充当本次成功结果。验证过程中新增的 Library、Builds、Artifacts、导入 Samples 和生成补丁夹具不回写模板分支。
