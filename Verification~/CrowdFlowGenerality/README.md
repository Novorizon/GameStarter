# 群体移动通用性验证

在 GameStarter 根目录运行。默认链接 `CrowdFlowPrototype` 的 `DirectionField`、`CrowdSteering` 和 `Junction`，可在 `--` 后列出需要重跑的场景。

```powershell
dotnet run -c Release --project 'Verification~/CrowdFlowGenerality/CrowdFlowGenerality.csproj'
```

对比候选方向场时，保留原转向和路口模块。此命令把窄道拐角的几何连接点传给候选方向场。

```powershell
dotnet run -c Release --project 'Verification~/CrowdFlowGenerality/CrowdFlowGenerality.csproj' `
  '-p:CrowdDirectionFieldSource=../CrowdFlowCandidate/DirectionField.cs' '-p:CrowdPortalField=true'
```

验证候选方向场和候补重排时，额外指定候选转向文件。

```powershell
dotnet run -c Release --project 'Verification~/CrowdFlowGenerality/CrowdFlowGenerality.csproj' `
  '-p:CrowdDirectionFieldSource=../CrowdFlowCandidate/DirectionField.cs' `
  '-p:CrowdSteeringSource=../CrowdFlowCandidate/CrowdSteering.cs' '-p:CrowdPortalField=true'
```

| 场景 | 判定 | 原型 | 方向场与转向候选 |
| --- | --- | --- | --- |
| `oblique-crossing`、`oblique-crossing-swapped` | 57° 斜交窄路，双向混合体型，互换出兵先后。各 16 只必须过交点并抵达。 | 两局均 16/16 | 两局均 16/16 |
| `mixed-lane-aligned`、`mixed-lane-shifted` | 同一条 0.98 米直窄道。网格原点偏移 73 毫米前后，直径 0.96 米的重型单位和小单位均应通过。 | 5/5、3/5 | 5/5、5/5 |
| `bent-lane-aligned`、`bent-lane-shifted` | 两段 0.98 米窄道在拐角相连。独立扫掠确认两段可通行，再核对偏移前后的小、重单位。 | 2/2、1/2 | 2/2、2/2 |
| `two-blocker-restaging` 及 `-mirrored`、`-reordered` | 两名候补沿通道前移后转进侧袋。镜像和调换输入顺序后，领头单位也要抵达。 | 三局均 0/1 | 三局均 1/1 |
| `three-blocker-restaging` | 三名候补依次清出通道，每名候补沿通道前移至少 0.20 米。 | 0/1 | 1/1 |
| `fixed-pocket-control` | 同一地图的两名阻挡者设为不可移动。领头单位应停下。 | 0/0 | 0/0 |

每个场景重复两次，按位置轨迹比对结果。检查每一步的地形扫掠、移动距离、步末体型重叠和不可移动单位的位置。每对相邻单位还检查两种先后移动顺序是否至少有一种能保持扫掠无碰撞。路口记录任一路线在入队后被对方连续越过的次数。

候选方向场允许从当前位置向目标或几何连接点执行精确半径扫掠，粗网格继续处理普通区域。直窄道可直接扫到目标，弯窄道依赖几何提供连接点。任意亚网格弯道的自动发现仍需几何层提供信息。

候补重排一次协调一名允许让路的单位。请求者等待，前方候补按体型寻找局部侧袋，移动路线在到位前保留。当前实现沿短直廊判断阻挡，适用于测试里的单入口侧袋。多段弯路连锁重排、与十字路口通行权联动，以及实际关卡的密集围站仍需独立验证。
