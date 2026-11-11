# D失败条件的派生分类

共同49原始摘要和完整案例已校验；本分类不增加覆盖、不判断未证明的根因。23个失败可有多个条件标签。

Runner仅在PASS分支赋旧候场计数，失败报告默认0不是实测。本驱动对两份实际死亡用例核原压缩trace SHA，再重建死亡前32名候场者及最终状态，得到0攻击、6移动、26候场。两份完整原trace已补入D分支FinalSelectedStress49/traces，SHA与原摘要一致；其余大轨迹的部分窗口与外部库存说明保持明确。

迁机可将D分支的该证据目录作为报告根，执行：
python3 d-failure-classify.py --report /abs/FinalSelectedStress49/summary.json --output /tmp/d-classification.json

绝对源路径随迁机改变，JSON字节不承诺相同；输入、报告、trace指纹与派生计数应匹配。缺失或错误trace会标unknown，不以默认0推断。d-failure-classification-verification.json保存本次原始派生及负向检查，未执行新模型。
