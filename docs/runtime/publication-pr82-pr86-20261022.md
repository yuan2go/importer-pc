# PR82～86 与 SM1.10.2 不测试发布

2026-10-22。用户要求将当前代码、报告、文档及刚刚任务执行产物提交推送，通过PR合并main，并合并全部开放PR；本轮不测试。开工main为 `3b883fe6a53b58b29eeca18c3719b5bfc0ee388e`，原工作区干净，当前分支`codex/operator-admin-logs`已推送到`3b27c9b2d37f8d6ccb8b8e145d4acbfca23b8582`，已有PR82～86。相同代码/报告不重复提交或重复建PR。

## 实际合并结果

Git祖先关系确认PR82/83/84/85的最终head均已完整包含在PR86。按用户“全部合并”授权将84/85由草稿转就绪，再合并整合后的PR86；GitHub随后逐项确认82～85也已间接合并，非仅关闭PR。

| PR | 原head | GitHub实际merged状态对应提交 |
|---|---|---|
| [82 参数精简与中文](https://github.com/yuan2go/importer-pc/pull/82) | 58abdbca13b4d9f3cd41360446920aad26427051 | 58abdbca13b4d9f3cd41360446920aad26427051 |
| [83 参数继承](https://github.com/yuan2go/importer-pc/pull/83) | 0fea1b506b84437e519c62d9aa5d740043dfbad8 | 0fea1b506b84437e519c62d9aa5d740043dfbad8 |
| [84 作业信息](https://github.com/yuan2go/importer-pc/pull/84) | 587fbe8dd0d7ca3561dbdfafd385182f6c1a7ae8 | 0df396d823e12d8684cba240e4bab119ff581e74 |
| [85 安装首配与TCP](https://github.com/yuan2go/importer-pc/pull/85) | d52ade7758d78ca7ad8b6adbd34d1a92fcdf4710 | 1923f372b1141d72d132fe0a56033cc120867d0e |
| [86 作业、管理员、日志与D盘](https://github.com/yuan2go/importer-pc/pull/86) | 3b27c9b2d37f8d6ccb8b8e145d4acbfca23b8582 | 1df42b4f8a2ef0a2a51bea29c47187a2117cb9f4 |

PR86后的main为`1df42b4f8a2ef0a2a51bea29c47187a2117cb9f4`。相对开工main去重增加40个提交：32普通、8合并。该数量不包含本发布回执提交及其后续PR合并；最终数字在回执PR合并后重新读取。按Git祖先差集计数，不累加各PR的commits或ahead数。

## 原执行产物

上传原任务最终冻结的SM1.10.2产物，SourceCommit均为`c9e482a30d2d1643a498e7e7ba7f12563f46f3f5`。064030a只修正测试异常类型，482d04c和3b27c9b补报告/证据，不改标冻结源码。源码、CI、合成原生、本机实际升级与D盘原账起停的精确来源分别见[原交付报告](validation/operator-administrator-logs-01.md)、[操作说明](operator-administrator-logs-01.md)、[D盘记录](d-drive-storage-20261021.md)及其原证据。

| 原文件 | 字节数 | SHA256 |
|---|---:|---|
| CainiaoSupplyStation_SM_Setup_v1.10.2_win-x64.exe | 178035322 | 8fd0afe6a053dfe1b0db7da63a7a6812a94070f8369442daf63fe141dcc3ab88 |
| sportmaster-win-x64-c9e482a30d2d.zip | 106636560 | 1b2f978a97d811cd30b18f3e1967a60ae94e5839331eab935fb1ef3ce2ce2064 |
| sportmaster-portable-c9e482a30d2d.zip | 18680170 | f8bd542dd01fccfc8a9d819f4bd6829677cbcd6ca0005bf6b06d8d96f3f3695f |

采用新独立预发布`installer-sm-v1.10.2-c9e482a30d2d`，不覆盖旧版、不重新构建/打包，不将介质标为后续main。原INSTALLER回执中ActualWindowsInstallation=NOT_RUN保留形成时身份，后来获授权的1.10.2实际升级另有`d-storage-installed-1102.json`，不倒写历史。安装器未签名。

## 本轮边界

本轮未运行测试、构建、打包、安装、启动、UI验证、设备/SDK调用或机器迁移。只读取Git/归档/身份及上传回执，未将原PASS扩大为最终main新增验证。原Production拒绝、298项历史归档问题、D/RV、真实SDK/相机/RCS/现场、目标IPC/触控与未执行的完整安装器交互、双账号/重启边界保持。

仅公开获授权的代码、项目报告、合成证据和通用冻结软件产物；不上传报价表、客户原稿、账号账/运行数据、现场数据、凭据、私钥、测试信任、SDK/字体或私密链接。原机D盘迁移属于已经完成的历史操作，不将真实账号及数据副本加入Git或Release。项目命令先加载project-environment.ps1，临时数据、开发缓存与产物留D盘；不更改全机环境或绕过受保护根权限。

采用独立集成工作树保存发布回执，不切换或重置原活动工作区，不写原索引，不通知其他任务。已有实现和全部原报告/文档已随PR86进入main；本回执以开发分支另建main PR提交。

## 已上传下载与回执

[SM1.10.2下载Release](https://github.com/yuan2go/importer-pc/releases/tag/installer-sm-v1.10.2-c9e482a30d2d)已完成安装EXE、win-x64 ZIP、portable ZIP及INSTALLER/RELEASE/PUBLICATION/SHA256SUMS的原字节上传，远端大小和SHA256均与原身份相符。[原上传回执](evidence/publication-pr82-pr86-20261022/artifact-upload-receipts.json)和[实际合并回执](evidence/publication-pr82-pr86-20261022/merge-receipt.json)随本PR入库。没有重新运行产物或继承原PASS为本轮结果。
