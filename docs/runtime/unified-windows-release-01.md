# 统一 Windows 发行 01

本次以 main c445b0ec32a3db44f00a1968f3c501aa36eb786f 为输入，任务是统一冻结、构建与校验，不扩展商业产品功能。旧发布记录是历史事实，保持原字节；新的组合媒介与历史包使用不同输出路径和身份。开放 PR #47 含产品/DB/ABI增量，本任务不合并、不借用其业务或验收结果。其已公开的冻结 SourceLink、双宿主 DLL 比对及 Windows tar 管道修复可复用，避免重复实现。

当前发布入口 package-application.py 已有锁定依赖、冻结快照、业务/合同校验、签名扩展静态接纳和逐文件库存；缺少整批事务、独立出包复核、环境/命令/日志/失败状态及统一身份清单。本任务在此机制上补齐。当前指导增加本入口，原 publication-deployment-20261019.md 继续描述原任务未打包的事实。

执行：`python3 scripts/release-windows.py --source <完整冻结SHA> --output <新目录> --dotnet <SDK路径>`。默认三业务各 portable/win-x64，再构建 Supply/Inspection portable；失败保留目录、日志与 FAILED 回执，必须另选目录。成功发布 RELEASE.json、SHA256SUMS；每包完整复核 ZIP/库存、同源、合同与业务版本、公共 DLL、入口与扩展装配。此流程要求全工作树干净、SDK与global.json精确匹配；源码由git archive冻结，不跟随构建中的分支移动。

默认组合为参考模拟，不附带厂商SDK、签名私钥或生产信任。需要扩展的产品仍通过既有精确包/独立信任/依赖机制另行选装和验收，不把无扩展组合标为真实能力已就绪。Supply/Inspection portable 是兼容入口验证交付，不是独立 Windows 安装器。

跨平台构建与静态验证不能证明 Windows UAC/ACL/账户切换/中断/升级恢复/交互。全部媒介 WindowsAcceptance=NOT_RUN、PhysicalIntegration=BLOCKED、CommercialAcceptance=NOT_APPROVED，即使在Windows构建也不自动升级结论。checksum不代表签名或发行者信任。字节可重复构建尚须独立重建比较，本流程保证固定输入及可重复执行，不声称ZIP字节复现。
