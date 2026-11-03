# 2026-10-20 已上传产物与开放 PR 合并记录

用户明确授权本轮不测试，并将已上传代码/报告/文档提交 PR、全部开放 PR 实际合并 main。起始 main c445b0ec32a3db44f00a1968f3c501aa36eb786f。测试、构建、打包、启动和业务验证均 NOT_RUN_BY_USER_REQUEST；报价表不上传。

PR47 已合并 76fefc803660c5546c7966fc60c4e4b31b7b01fb，上传商业快照 PR50 已合并 30b8124ad178e98ae847fa9d41fe6dcdb724a78b。PR48 原 7acbb2c 与商业化当前 INDEX/打包器重叠：保留当前商业化导航与随包指南，继承 PR48 独立临时构建目录、精确库存/ZIP/源码/合同门禁；清理范围绑定该次生成工作区。PR49 后续整合统一使用一个发行入口及共享机制，原冻结与形成时证据不改写。

本记录仅说明 Git 内容与合并裁决，不构成运行通过；旧 bd86fbb ZIP 与 07e0f7a 中断产物均不改标为最终 main 介质。原 D/RV、Windows NOT_RUN 和真实 SDK/协议/设备/现场 BLOCKED 保持。

## PR49 发行重叠裁决

PR48 实际合并 4f21b2d9cf6742a6775e47766c9f8d96f2804bca。PR49 继承其 main 祖先，统一采用 PR49 的单一 release-windows.py、release_build.py、八目标矩阵、依赖/编译版本检查与最终回执机制；保留 PR48 的输出外临时构建目录，清理仍限定两项生成子目录，app/desktop 的当前商业操作指南保持。旧 --source 参数保留但要求与干净检出 HEAD 一致，避免指定冻结身份与实际打包源码不同。

两个负向夹具的所有场景保留于同一测试文件；PR48 三项夹具只按统一 API/所需依赖库存和错误文案迁移，断言仍验证篡改、额外文件、ZIP 成员及来源身份，未运行、未跳过或吞异常。PR47 关闭扩展临时文件句柄的修正保持；依赖材料同时覆盖 desktop/app。两种证据目录各自 -text 规则保留。INDEX 同时保留商业当前入口与发行形成时记录，manifest 逐路径合并并保留历史对象，不用旧设计状态覆盖商业实现。

统一核验器同时保留 PR48 对宿主 BusinessVersion/BusinessAssembly/CompatibilitySha256、原生脚本实际派发参数和非空扩展身份的门禁。夹具错误文案按统一核验器精确迁移；仍未运行测试。

## 上传快照 PR51 与统计截止

PR49 实际合并 cfe6c0e0d0570986ea8c63f619e328d3891b82ef。PR51 将原上传快照 cfeb784 的两个独有提交及上传说明纳入同一 main 祖先；INDEX、manifest 和发行报告重叠时保留最新已整合版本，恢复阶段说明不被旧快照删除。原上传字节与中断证据保留在提交祖先和证据目录中。

最后本地整合前，去重输入为 35 个提交；加入本地双父整合后为 36，再以 GitHub merge 方法合并 PR51 后应为 37。计数基线为开工 main c445b0ec，使用 Git 图集合而非各分支 ahead 相加。PR51 的实际 merged 状态、最终 merge SHA 和最终 main 计数在 API 完成后回读；本文件不伪造自身后继合并 SHA。前四个已确认 merge 及统计方法见[机器回执](evidence/publication-merge-20261020/receipt-before-final-merge.json)。

本轮全部代码/冲突整合未运行测试、构建、重新出包或启动。运行方式继续使用产品操作指南；新发行工具统一为 `python scripts/release-windows.py --output <全新目录> --dotnet <固定SDK>`，可选完整 `--source` 须等于干净检出的 HEAD。本轮未执行该命令。六个已上传 ZIP 仍属于 bd86fbb，统一发行的 .zip.partial 仍为不可安装中断产物，均不代表最终 main 组合验收。报价表未上传，D/RV 和真实 SDK/现场边界不改写。
