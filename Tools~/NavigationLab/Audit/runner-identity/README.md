# Runner 编译身份独立复核

A、B0、B1、C、D-r9、E 六份实际冻结构建清单的 Runner 原始 DLL SHA-256 各不相同，但实际参与编译的文件中，唯一不同的是生成的 `NavigationLab.Runner.AssemblyInfo.cs` 内的 `RepositoryRoot` 属性。MSBuild 会按路径长度将字符串拆成不同行片段。其他编译源码字节、Runner csproj、清单记录的实际编译属性及 SDK 记录均一致；Core 与 Oracle 的实际 DLL 仍严格同 SHA。

共同编译输入身份为 `e17b632f65d9b62990907f10726b84af8d6fa3747c1aa6d20bc0dbd260195544`，方案名称 `NavigationLab.Runner.CompileIdentity.v1`。这称为编译输入身份，不能称为 DLL 字节一致或无条件语义等价。

规范化仅允许实际 Compile 项中指定生成文件的一条、形状准确的 `AssemblyMetadata("RepositoryRoot", ...)` 属性。原字符串必须严格等于该 Runner 项目生成的路径，且解析后指向该清单的 snapshot。替换该属性后，其余每个字节保留；每份原始源码、实际 Compile hash、csproj 与 Runner 原始 DLL 依旧分别和构建清单绑定。不会忽略其他属性、方法内容、编译属性或 SDK 记录的变化。

`RepositoryRoot` 会被 Runner 在运行时读取以构建用例和读取输入，因此例外还依赖每个 snapshot 的冻结源核验，以及跨报告逐例 `InputSha256`、协议、套件和案例集合一致。Root 的 `audit/shared-inputs-v2.json` 另行核验了六组 49 个原始导出文件字节一致；该证书只证明输入相同，不代表候选已全部运行完成。

`checks.json` 记录九项检查：真实六清单归一；仅根路径差异通过；实际编译源码、编译属性、csproj、错误根路径、重复根属性、其他 AssemblyInfo 改动均拒绝；缺失显式 `freshManifestBoundReport=true` 也拒绝。隔离临时夹具仅测试校验器，不是实际构建或算法验收，未伪造多算法运行结论。

测试仅对已完成的 A49 调用了 `load_report`；未读取其他尚未完成 49 的摘要为通过证据。已完成 A49 的实际 CLI 还在 `audit/report-normalized-runner-single` 成功生成摘要及 SVG。没有运行导航矩阵。

复现：`python3 -B checks.py tool-source.py`。原始冻结 snapshot 和完成的 A 报告仍须存在。`manifest.json` 绑定工具源码副本、测试、结果及实际六份构建清单，不改任何 harness 或候选源码。
