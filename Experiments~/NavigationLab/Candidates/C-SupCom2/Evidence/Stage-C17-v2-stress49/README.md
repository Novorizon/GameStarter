C17共同v2正式阶段：49项47通过、2失败、0不支持；所有已通过项fresh replay，Source/DLL未变与隔离检查通过。两失败为Wave4单人和死亡后原队列补充，不被后续修复覆盖。Full41是同一执行按原始InputSHA逐项核对的39通过/2失败子集，不另跑。

本归档保留49完整压缩案例、49原始输入、实际编译源（含生成项）、4个实际DLL、完整编译/执行指纹。trace优先保存两失败及关键44/46，其余原件留在外部完整目录，大小/hash/原件绝对路径见trace-inventory.json。这里是明确的选择性归档，不能伪装成含全部trace的严格原始目录；Root严格汇总使用原完整目录 /workspace/navigation-comparison/results/c-final-v2-stress49。

复现旧阶段应使用actual-compiled-source.zip内旧候选及共同源重新冻结/构建；实际旧DLL也原样保留。Runner内记录旧绝对RepositoryRoot，迁机需重新构建Runner并重新绑定manifest，不能假定旧Runner直接在任意路径运行。
