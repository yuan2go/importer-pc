# 原生部署与全部入口门禁不测试发布

用户本轮要求提交推送代码、报告、文档及刚刚任务执行产物，并实际合并全部开放PR。测试、构建、重新打包、签名、启动及仓库验证器均为 **NOT_RUN_BY_USER_REQUEST**，Git hooks禁用。受限客户原稿、私密配置、运行lease和本地重复文档不上传，无关工作树保持。

## 输入、权限策略和合并裁决

开工远端main `524916b2088f267175bec46daf01e6df020932b7`；PR44原head `310b9ea5501ca1f2a6f897924e3734113ecfbac5`，PR45原head `506bf7a09a2a1ff028728346b7b23d05925bacb5`。PR44已按精确head实际合并为`fea535c94c12a50c9559928a2de0bf680e09dba7`。PR45后继整合提交保留双方祖先及原PR，不以关闭/替代冒充合并。

保留PR44的部署/修复向导、离线维护、可选扩展目录隔离及最终统一账户策略，保留PR45在Supply ProductRuntime与Inspection初始化配置/持久资源之前调用RequireRuntimeIdentity。全部入口复用同一最终MachineRuntimePolicy；没有第二套策略或绕过。用户在PR44工作包明确允许指定未提权账户的UAC deny-only组身份，此要求更新较早SID任务的组限制；启用管理员权限、服务账户、错误SID仍拒绝。原SID报告/旧测试保持形成时策略，不倒写旧结论。当前Supply/Inspection指南按最终政策表述。

冲突为manifest、摘要及test_runtime_identity.py一处参数构造：保留PR44默认DataRoot选择与runtime-prepare/安装断言，同时保留PR45 File/Destination参数、5类维护派发和Supply/Inspection源码顺序断言。断言未删、未执行。其他功能文件分别保持原PR44/45的Git blob；INDEX/框架/首次运行说明的双方内容自动合并。逐文件身份、原PR45说明和裁决见[机器记录](evidence/deployment-publication-20261019/merge-resolution.json)。这只是静态Git内容与来源比对，不授予运行PASS。

## 既有执行产物原字节发布

- [原生部署参考e79a8bf](https://github.com/yuan2go/importer-pc/releases/tag/startup-deployment-reference-e79a8bf)：固定源码`e79a8bfdc65328619c77013fb314eaaef4a35a2f`，原Sportmaster portable及三个win-x64 ZIP，共4个归档，另有ARTIFACTS/SHA256SUMS。包含PR44向导/修复，不包含PR45新增Supply/Inspection入口守卫。
- [Supply/Inspection入口参考16ecb57](https://github.com/yuan2go/importer-pc/releases/tag/runtime-all-entrypoints-reference-16ecb57)：固定源码`16ecb571cba32188f62a6e6e677030cd589dbf48`，原两个portable ZIP，另有ARTIFACTS/SHA256SUMS。采用该源码形成时的旧共享账户政策，不含PR44安装向导/UAC更新；非Windows运行不证明Windows身份/ACL。

6个ZIP均从原任务工作树上传，不重建、重签或覆盖。原报告“仅本地”保留形成时间，新公开上传由本次用户明确授权；原形成时检查、日志和媒体SHA保持。两组共10项附件。**当前main是两项源码的组合，本轮没有生成包含二者的新可信媒介。** 不混用旧宿主/精确扩展或对旧ZIP改标；下一次发行须从新固定组合源码构建并验证精确合同/包/指南。

[PR44固定交付报告](startup-deployment-02-results.md)、[PR45原报告](runtime-identity-preflight-02.md)、旧首次运行01候选、原失败/PASS、267项历史资产问题、REQ/BR/CAPI/D/RV均保留形成时身份。Windows原生SID/UAC/ACL、不同账户授权、安装/修复/中断/快捷方式及窗口交互未验 **NOT_RUN**；真实SDK/协议/设备/现场 **BLOCKED**，Production **NotReady**。不宣称MSI事务、工业标准认证或商业验收。
