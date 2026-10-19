# 最新 main 运行成果的统一发行 · 55025ce

准备状态：本任务实际 cfe6c0e 发行完成后，main 合并了 PR53 的缺失原数据库拒绝新建空库等修正，形成 fe9e5bb。cfe6c0e 发布及其通过记录保持原身份，但不包含该修正，不重标成最新组合。

优先继承 PR53 已经实际构建和验证的六套介质，冻结源码 `55025ce9c62173fea5cdc5ceecfb29453ed5e7fa`。它是最新 main 的祖先；src/config/scripts、固定 SDK 和随包指南输入经 Git 比对与 fe9e5bb 相同，后续 a729125/合并提交仅留证。原六包文件名、ZIP 字节、SourceCommit、SHA、原生局部验证与未验边界不改写。原清单见 [PR53 六包身份](evidence/pr47-main-integration/media-identities.json)，来源与核心/原生/恢复结果见[该包证据](evidence/pr47-main-integration/README.md)。

单一 scripts/release-windows.py 增加显式 --source、--reuse-directory、--reuse-manifest：只接受最新 main 祖先且发布输入没有差异的冻结来源；复用必须有独立原始 ZIP 身份清单，再实际核对 SHA/全部成员/CRC/展开文件/源码/业务及合同/入口；原字节复制到新输出并再次复核，保留原文件名及 Original 字段。不从新版本号猜测旧包可用，不更改原件或历史 PASS。缺少目标在隔离的精确 Git checkout 中调用既有打包器构建。构建控制器提交与运行源码提交分别记录，全部八包通过才生成统一成功回执。

本次补齐 Source55025ce 的 Supply / Inspection portable，与六套 Parcel 媒体组成同源八包；不是再建框架或重建已有六套。新受控来源与复用入口的错误来源、错误 hash、额外展开文件、重复项和路径逃逸均须拒绝。实际执行及下载结果将在完成后另记；Windows 完整 UAC/ACL/另一账户/受保护安装/中断升级/真实现场仍按各原证据未验/受阻，不转换成商用通过。
