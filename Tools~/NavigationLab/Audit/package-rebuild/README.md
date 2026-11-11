# 交付源码包独立重建

从B1、C33、D12、E最终分支的版本化源码ZIP解到独立目录，清空bin/obj后使用本轮同.NET8 SDK、Release、PathMap到/navigation-lab及ContinuousIntegrationBuild重建Runner/候选。没有访问原冻结snapshot的编译缓存，也没有运行新模拟。

四者候选、Core、Oracle实际DLL均与对应原build-manifest逐字SHA一致；各自重建Runner仅执行export-only，49份输入逐字等于原正式报告输入。Runner生成RepositoryRoot随路径变化，原始DLL不宣称字节相同；比较仍使用严格编译输入身份及实际输入核对。

verification.json记录实际源运行、重建路径与hash；编译/导出日志原样保留。源码ZIP位于B Evidence/FinalStress/B1/source-and-binaries.zip、C Evidence/Final-C33-v2-stress49/actual-compiled-source.zip、E Evidence/FinalGoalSafeStress49/frozen-source.zip。这项验证证明交付包可在独立目录重建相同候选及相同测试输入，不把它算作新49或算法通过。

D12另存package-rebuild-d-verification.json；源包在D的Evidence/FinalSelectedStress49/source-and-binaries.zip，实际选定factory为Aoe4ProgressExchangeFactory。独立重建与导出也未执行导航模拟。
