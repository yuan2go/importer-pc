# PR20/21 整合证据

最终运行/打包源码 **d5c8b7981ba8e4c8b9daf22df1cb686243815f3b**；风险/资源组源码5af8d2ccfd6e2433657690ea89f034696c922ce9。固定SDK10.0.402，macOS。[逐文件最终源码](frozen-source.json)、[检查与命令关联](checks.json)、[最终ZIP身份](final-candidates.json)、[签名参考包身份](final-signed-reference-identity.json)。5af8d2c到最终仅增加Create获取证据的大小写一致比较及该实际路径测试；相关签名、获取、能力、发布/解包进程均在最终源码复验。

## 原始失败与修复

- PR20：[链接负例FAIL](logs/pr20-red.log)、[测试清理FAIL](logs/pr20-fixture-cleanup-failure.log)、[修复PASS](logs/pr20-green.log)、[构建](logs/pr20-build.log)。修复源码1b6962e。
- PR21：[Python格式FAIL](logs/protocol-builder-red.log)、[初始夹具缺公共合同](logs/review-fixture-contract-failure.log)、[实际签名ZIP/小写资格FAIL](logs/signed-review-red.log)、[14项Python工具PASS](logs/protocol-builder-green.log)、[最终签名格式/大小写/篡改PASS](logs/final-review-regressions.log)。
- 独立审查发现：[实际Create获取FAIL](logs/acquisition-red.log)、[修复前冻结PASS](logs/acquisition-green-before-freeze.log)、[最终实际获取/激活PASS](logs/final-qualified-activation.log)。完整路径为安装、独立签名资格、StartAsync、Create、认证/采图/实测/WCS/PLC原查询。
- 原件资源验证：[Mac系统别名触发FAIL](logs/mac-temp-alias-failure.log)，随后测试根使用实际路径（不弱化业务守卫），[资源/重启PASS](logs/integration-resources.log)。

## 定向集成

[备份/健康/取消/恢复](logs/integration-bhr.log)、[能力可靠性/实际ViewModel/迁移](logs/integration-reliability.log)、[最终19能力](logs/final-capability-closure.log)。原18的24组合及无关业务全矩阵未重复。最终核心/选装编译日志均在logs目录，只有受影响路径构建，没有新Windows交叉构建。

[Sportmaster解包进程](final-smoke-sportmaster.json)、[DirectPlc](final-smoke-direct-plc.json)、[WcsToPlc](final-smoke-wcs-to-plc.json)、[前置上传独立HTTP](final-smoke-before-main.json)、[附加上传独立HTTP](final-smoke-attached.json)，均为d5c8b79源码候选。执行scripts/package-application.py，再真正解压ZIP后运行scripts/smoke-application.py；附加两条带--optional-features及--peer-fixture，附加旅程再加--attached-upload。[被替代5af8d2c候选](superseded-5af8d2c-candidates.json)保留原hash/本地字节，不宣称为最终产物。

资格签名只用本地临时合成信任，私钥/包二进制未入库，测试包和资格不构成生产批准。公开日志只有合成输入及本地测试路径；文件SHA由docs/manifest.json记录。完整操作命令/设计见[整合报告](../../pr20-21-integration.md)。

Windows/Junction/UI/UAC/ACL实际NOT_RUN；真实SDK/设备/WCS/PLC/现场BLOCKED。Production仍需真实提供者及独立资格。D/RV和原失败不改。
