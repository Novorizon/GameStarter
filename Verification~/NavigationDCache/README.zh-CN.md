# D12有向格边精确缓存试验

仅缓存同一几何快照、半径及有向相邻格边的原布尔结果。World和身体在构造时复制；Apply更换World后同时清空格心与格边缓存；不同几何实例完全独立。不量化浮点，不合并反向边，不缓存任意点线段；同格、跨行伪相邻和非邻接仍执行原式。每个半径最多CellCount×8字节。

OriginalGridGeometry.cs只对原9e冻结源码更名namespace，作为独立原式基准；不编译进候选程序集。当前所有Clearance调用方只读其数组，未新增并发写线程安全保证。

Release自测共412146次原式对照、14轮冷/热/独立实例/阻塞/重新开放与目标变更，涵盖6种半径、非法索引、同格、八方向、跨行与非邻接；全部一致。实际源与测试/Core程序集hash见result.json。此处是几何memo等价检查，不是导航通用性证明。

从仓库根目录执行：

    dotnet build Experiments~/NavigationLab/Core/NavigationLab.Core.csproj -c Release
    dotnet run --project Verification~/NavigationDCache/CacheEquivalence.csproj -c Release -- /tmp/d-cache-equivalence.json

导航四例仍需使用冻结源码和真实原始输入核对轨迹、命令、最终态、状态与全新实例重放，不能因几何检查通过便替换最终D。
