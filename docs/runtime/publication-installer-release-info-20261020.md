# PR67 代码、报告与既有单 EXE 不测试发布

2026-10-20 用户要求不测试，将代码/报告/文档及刚刚任务产物提交推送、通过 PR 合并 main，并合并全部开放 PR。开工 main 为 a9b9be889ca9833ecf29e382f0fb082a028e2a0b；主工作区干净，刚刚成果已在 PR67 分支完整提交推送，无需重复创建同内容 PR。其他任务工作树不覆盖。

PR67 原 head b49be3c50e0387fcacb782df2eaf932a7b5a1279，包含5个独有提交；已通过普通 expected-head merge API 实际合并为 0fe3cbfb31ef57d674c4040d7b3bd52bf469b46a。该截止点相对开工 main 去重6个提交＝4普通＋2合并；包含原分支整合提交，不能只按本轮命令次数计数。发布回执PR及最终main SHA由API完成后另行回读，不伪造自身后继SHA。

## 原执行产物上传

[新 Release](https://github.com/yuan2go/importer-pc/releases/tag/installer-sm-v1.4.1-7c384540393b)；[单 EXE 下载](https://github.com/yuan2go/importer-pc/releases/download/installer-sm-v1.4.1-7c384540393b/CainiaoSupplyStation_SM_Setup_v1.4.1_win-x64.exe)。文件 `CainiaoSupplyStation_SM_Setup_v1.4.1_win-x64.exe`，172493384字节，SHA256 `826f7ff47fd8bcddbe785a4a9e3cdbc92ec91cc512ca108e5de8050ca9a6b5f2`。

实际运行/打包源码 `7c384540393b1e77ceeba048a87a3916dd1af63c`，原分支 `codex/installer-release-metadata`，Sportmaster / SM / 业务1.4.1，EXE文件版本1.4.1.0。文件名不加commit，实际分支和完整commit仍在安装目录BUILD-INFO中。PR67后的报告/发布回执提交不替换EXE来源。

原字节产物从原任务目录取得，上传前只比对原delivery记录中的大小与SHA256，没有重新构建、打包、运行、安装或执行测试。GitHub上传大小与digest回读一致，原DELIVERY.json作为历史证据保留；PUBLICATION和REMOTE-UPLOAD记录本次上传身份。4个新Release资产均保留原文件名；旧Release/旧EXE不覆盖、不改标。

## 本轮边界与运行方式

本轮测试、构建、重新打包、安装、运行和SDK/现场验证均 NOT_RUN_BY_USER_REQUEST。原任务41项实际升级及5/6/2/14项脚本结果只属于原7c源码与原主机，不算本轮重跑或最终main新PASS。原Windows22631主机成功不代替受支持系统/干净环境资格。

下载EXE后按安装向导人工安装或升级，已安装数据/目录/账户仍沿原保护规则。具体基础依赖、海康SDK不再分发、配置和源码记录读取方式见[安装交付指南](installer-release-info-01.md)。基础包不含未许可SDK/驱动、生产信任或客户资料。未做Authenticode签名，真实海康/WCS/RCS/现场未验，Production未批准；原D/RV、批准和历史失败保持。

[上传及原交付证据](evidence/publication-installer-release-info-20261020/REMOTE-UPLOAD.json)只证明上传身份，不授予生产或现场资格。
