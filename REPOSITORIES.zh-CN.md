# 仓库边界

- UnityPackages 只维护各独立 UPM 包和聚合安装入口，不包含 GameStarter 或其他 Unity 工程。
- GameStarter 是独立 Unity 工程，只通过 Git/UPM 使用这些包，默认引用 Framework All 并解析全部包依赖。
- 本工程的 Packages/manifest.json 和可选模块目录均使用发布标签的 Git 地址，使用者无须保留包仓库的本地目录结构。
