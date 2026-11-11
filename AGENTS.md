# C# 代码规范

- `if`、`else`、`for`、`foreach`、`while`、`try`、`catch`、`finally` 的执行语句必须另起一行。
- `if`、`else`、`for`、`foreach`、`while`、`do` 等控制语句必须使用大括号，即使只有一条语句。
- 禁止 `if (condition) return;`、`try { Work(); }` 等压缩写法，也不要在同一行放多条语句。
- 大括号使用 Allman 风格，开始和结束大括号各自独占一行。
- 格式调整遵循根目录 `.editorconfig`，保持现有行为。
- 函数声明和调用优先保持一行。整行长度包含缩进、函数名、括号和参数，在 160 列以内时不得为突出参数、命名参数或视觉分组而提前换行。
- 超过 160 列确需换行时，每个参数独占一行，不混用一行多个参数和单参数换行；参数自身的多行 lambda 保留正常块结构。
- 这项参数规则不要求批量压缩或重排对象/集合初始化器；只调整实际需要修改的声明和调用，保持行为与初始化顺序。

# 跨对话与 SDK 协作

- UnityPackages 由 [UnityPackages 对话](codex://threads/01a1b12e-ff35-762a-b973-67981ef710ad?hostId=durable) 统一维护；本项目负责 GameStarter、业务接入和真实 Unity 验收。
- 本项目及其子代理不直接修改 UnityPackages 源码、版本、聚合包目录、发行标签，也不切换对方 checkout 的分支。需要 SDK 修复时先按 [交接约定](Documentation/SDKCoordination.zh-CN.md) 描述问题、接口语义与验收条件，交 SDK 维护方处理。
- 不假定两个对话共享文件或运行实例。若确实共用环境，每个任务使用独立 checkout/worktree，不共用一个正在导入或构建的 Unity 项目目录。
- SDK 交付 PR 和固定提交 SHA 后，再更新本项目的 manifest、可选模块目录及 lock，按下方日常验证约定检查。不得自动追踪 SDK main，也不得依赖相邻仓库的 file: 路径。
- 没有跨对话发送工具时明确说明；不得把读取对话、写本地文件或建立需求单说成已发送或已获对方确认。没有实际需求时不建立空 Issue。

# 日常验证

- 日常 Unity 代码和资源修改默认只等待 Editor 完成本次导入与脚本编译，检查项目相关的 Error。工程已打开时使用现有 Editor；否则按 `ProjectSettings/ProjectVersion.txt` 使用本机 Editor，仅传入 `-batchmode -nographics -quit -projectPath <工程路径> -logFile <日志路径>`。本机已验证的 Editor 路径为 `C:/Program Files/Unity 2022.3.62f1/Editor/Unity.exe`。
- 本机 Unity Hub 的 Professional 许可证已激活。自动化沙箱可能无法访问当前 Windows 用户的授权上下文。需要批处理时，优先使用经权限流程允许的本机启动方式，复用已登录 Windows 用户的现有授权。日志中出现许可证刷新失败时，继续看后续的 `Successfully resolved entitlement details`、项目编译结果和退出码。授权已解析且编辑器正常退出时按项目编译结果汇报。只有编辑器实际因授权失败中止时才把授权列为阻塞。
- 进入 Play Mode、冒烟、回归、完整测试、构建和设备验收仅在用户明确指定时执行，按指定范围运行。
- 仅修改文档等不影响 Unity 导入或编译的文件时，只检查对应的文本差异。

# 交付约定

- 用户已授权：本仓库的改动完成相关验证并通过 GitHub 检查后，直接将对应 PR 合并到 `main`，无需再次逐项确认。
- 检查失败或存在冲突时，先修复并重新验证，再合并；不绕过分支保护。
- 已发布的版本标签保持不变。
