# B 冻结证据

| 候选 | Factory | 冻结业务源提交 | 实际候选 DLL SHA-256 |
| --- | --- | --- | --- |
| B0 | `NavigationLab.Candidates.BOrca.OrcaFactory` | `ae53f6b` | `8175992a5d009fe91ee8e6707feabc1e3f7a69c04ad3242315f06e54d5da36b7` |
| B1 | `NavigationLab.Candidates.BOrca.OrcaYieldFactory` | `08dc670` | `e75fd5c5a1603f92c89028d66fc1f9a177b55b9ae55ba83567b5d7725ff81b1b` |

每个包分别保存原始 `build-manifest.json`、精确 source/实际 Compile 输入/四角色 DLL 的 zip，以及逐文件 SHA。两个版本独立绑定；当前分支源码不能冒充旧 B0 DLL 的编译输入。

`verify-packages.py` 从包内原字节检查源、编译清单和 DLL，不改写原始清单的绝对路径。该检查不是重新执行模拟或验证完整轨迹。

`Native-B0-Mixed` 是原始混合6项 Unity Play/Mono 报告；B0 的3项均通过，D-r8另外3项包含1项失败，因此保留原全局失败状态及完整 JSON。这里只归属实际 `OrcaFactory` 和对应 DLL SHA 的3项。使用 Null 图形设备，不代表渲染、IL2CPP或设备验收。

本次归档提交保存冻结身份和已完成的 Native 原件；完整49项模型矩阵尚在独立冻结副本执行，本页不据此声明其已完成。
