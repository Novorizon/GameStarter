# UnityWebRequest DLL 流程备份

统一 YooAsset 下载之前的完整流程保存在两个仓库的 `backup/unitywebrequest-updates-20261106` 分支：

- [GameStarter](https://github.com/Novorizon/GameStarter/tree/backup/unitywebrequest-updates-20261106)，提交 `670b06282767c7b99adc04a3b50050806c65f296`。
- [UnityPackages](https://github.com/Novorizon/UnityPackages/tree/backup/unitywebrequest-updates-20261106)，提交 `9f4aa11a3478d6845c9db74aa66aba8f0e37ef9b`。

该版本 DLL 由 UpdateTransport/UnityWebRequest 下载和校验，独立缓存；YooAsset 准备匹配的资源版本。发布产物是 `patches/`、`assets/`、`manifests/`，先上传两类内容，最后发布签名清单。

需要恢复时使用这两份配套源码及备份文档。修改工程 manifest 的包提交后，必须重新构建主包；不能把新的 YooAsset 代码资源清单交给旧主包，或只切换一个下载开关来混用两套构建产物。

云端另外保存源码 tar.gz 和 Git bundle；它们不包含 Unity 许可证、签名私钥或包缓存。已发布标签保持不变。
