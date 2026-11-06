# 账号与单人战斗结算接入

此目录是纯 C# 客户端边界，不含运行时假服务器，也没有真实登录协议。`IGameBackend` 需要业务服务器适配后才能联网；测试中的 FakeBackend 只验证会话协调，不代表已连通服务器。

```csharp
using var account = new AccountSession(backend);
await account.LoginAsync(new LoginRequest(provider, loginProof), token);
var profile = await account.GetProfileAsync(token);
var ticket = await account.BeginRunAsync(stageId, configVersion, token);
// 用 ticket.Seed 和完全相同的配置版本启动单人战斗。
// 战斗结束：把真实命令日志按约定格式哈希，生成不可变 RunReport。
var settlement = await account.SubmitRunAsync(report, token);
// 展示服务器返回的奖励；不要从客户端模拟结果自行增加账号余额。
```

- `SessionTokens` 只保留在内存中。登出/Dispose 清引用并取消会话请求，不写入存档、PlayerPrefs 或日志；不承诺托管字符串已从内存物理擦除。
- 网络操作在当前账号范围内串行。临近过期的 access token 会先刷新；刷新、档案和票据不能更换账号/关卡/配置版本。迟到的上个会话响应不能恢复已退出账号，即使底层忽略取消，也不阻塞新登录。
- 同一局第一次提交会冻结摘要、生成 idempotencyKey。同摘要重复点击共享进行中的请求；成功后复用结果；失败后复用原请求及 key 重试。摘要不同会拒绝，避免响应丢失后以另一份内容重复结算。
- 取消 `SubmitRunAsync` 的 token 只取消当前调用者等待。后台请求可能已经落库，继续由会话拥有；Logout/Dispose 才取消共同请求，且不能撤销服务器已处理的结算。
- `RunReport` 只携带胜败、波次、模拟 tick、剩余基地生命、击杀数、命令数和命令日志 SHA256。它没有客户端奖励字段。摘要或 hash 本身不能证明战斗合法；正式发奖还需要服务端保存/验证 ticket、规则版本、命令日志本体或服务器复算，并在数据库事务中按玩家+runId 幂等结算。
- 当前进程重试保留请求，不包含跨进程恢复。若结算响应丢失后程序退出，正式后端需提供“按 runId 查询结算状态/未完成战斗”接口；不能简单开始新局掩盖未确认结算。token 续期/重登、账号封禁和服务器错误码也需与后端协议一起定义。

现有框架 `NetworkManager.Initialize()` 只准备传输会话，不会登录；认证回调必须在首次初始化时传入。框架 `RequestAsync<T>` 使用 Protobuf 消息，本目录未虚构消息号、JSON-over-TCP 协议或服务端地址。真实适配应在确定 `.proto`、认证流程与服务端接口后实现 `IGameBackend`。

离线原型可以直接使用战斗领域层，但 UI 应明确显示“离线原型”，不能调用本会话伪造登录和在线奖励。

独立验证：`dotnet run --project Verification~/TowerDefenseOnline/TowerDefenseOnline.csproj -c Release`。
