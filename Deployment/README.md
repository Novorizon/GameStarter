# 更新站点部署

既有 Nginx/HTTPS：15000.top:443。游戏 TCP 15000.top:22370 不经过本下载配置。

在现有 HTTPS server 中包含 nginx-game-updates.conf，文件映射为：

- /game-updates/manifests/ → /srv/gameserver/updates/manifests/
- /game-updates/assets/ → /srv/gameserver/updates/assets/

由服务器管理员创建目录、赋予发布账户写入权限和 Nginx 读取权限，nginx -t 通过后 reload。不要用新 server 块覆盖已有网站或证书配置。

将客户端项目 Artifacts/Publish/ 下的 assets/ 和 manifests/ 两个目录按原结构上传。先完整上传 assets/（包括 DLL 和 AOT 元数据资源），最后通过临时文件重命名发布 manifests/ 内的 release.json。清单不可先于内容上线。模板仅提供静态文件，不需要 TCP 游戏进程读取这些目录。

私钥不属于发布文件。公钥已在主包中；服务器只保存签名后的版本清单、DLL 和资源。

验证时用相同 baselineId 的原始主包连接新清单。新的主包基线对应独立清单路径，避免把只适用于新主包的补丁交给旧主包。

旧 WR 流程保留的 patches/ 可继续服务旧主包，不要删除既有发布文件。新 YooAsset 流程不再上传独立 DLL；详见 [统一下载](../Documentation/UnifiedDownloads.zh-CN.md) 与 [WR 备份](../Documentation/UnityWebRequestBackup.zh-CN.md)。
