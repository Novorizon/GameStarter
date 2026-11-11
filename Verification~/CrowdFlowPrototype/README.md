# 群体移动原型

从 GameStarter 项目目录运行。

```powershell
dotnet run -c Release --project 'Verification~/CrowdFlowPrototype/CrowdFlowPrototype.csproj'
```

同目标、同体型的单位共用方向场。反向搜索建立距离，运动层读方向并采样下一步。碰撞按单位半径做连续线段检查。允许让行的静止单位可以侧移。十字路口试验额外设置通行权，进入后持有到清空路口，再按等待顺序放行。

方向场和转向分层来自《帝国时代 IV》和《最高指挥官 2》的公开开发资料。原型采用八邻接 Dijkstra 和自研采样，便于在小型逻辑地图上隔离验证。狭口准入规则属于本项目的适配。

- [《帝国时代 IV》开发者讲义](https://media.gdcvault.com/GDC%2B2022/Speaker%2BSlides/Pathing%2BIn%2BAge_Cheng_Frank%2B2022-03-29%2B00.16.38.pdf)
- [《最高指挥官 2》开发者章节](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf)
- [Cooperative Pathfinding](https://ojs.aaai.org/index.php/AIIDE/article/view/18726)

测试包含绕障、体型受限的窄口、有无主动让行的对照，以及开阔和狭窄的交叉口。每次推进检查地形扫掠和终点实体重叠。当前覆盖独立逻辑地图。林间哨站有序必经口、基地围站及攻击结算、Unity 表现和性能属于后续接入验证。
