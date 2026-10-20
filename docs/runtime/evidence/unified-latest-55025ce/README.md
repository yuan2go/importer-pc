# 55025ce 完整八包发行证据

[交付说明](../../unified-release-latest-55025ce.md)与 [RELEASE.json](RELEASE.json)是完整集合入口。ZIP 实际发布到独立 GitHub prerelease；本目录不重复提交二进制。

形成顺序：PR53 六个原包按 [原清单](../pr47-main-integration/media-identities.json)逐字节继承；第一次组合在隔离 Git 55025ce 中实际补构建 Supply/Inspection，八个包全部核验后，临时 Git 只读 pack/长路径清理失败，原 FAIL 回执与日志保留为 complement-build-failed-cleanup.*。其中的成功单包不是成功整套回执。修复没有放宽门禁、删除断言或把失败改成 PASS。

新控制器以 original-eight-inventory.json 及其绑定的原构建回执 SHA 接收八个候选，逐一复核内容/CRC/源码/依赖/版本/入口，复制到新目录并复核后生成当前成功 RELEASE；没有重新编译或覆盖原目录。原六包名字和 ZIP SHA 不变，两新包保留同源构建身份。首次 CLI 参数冲突、首次证据聚合器把 desktop DLL 路径误放 app 的失败日志也保留，均经修正后另记通过，未改变产品断言。

release-tests14.log 为当前来源/复用/版本/完整性/只读及长路径清理门禁；cleanup-fix.json 还绑定实际失败现场清理。entry-results.json 与 entry.log 是八个当前 Windows 实际发布入口的未配置机器策略拒绝检查：Parcel 仅显式只读 runtime-check；Supply/Inspection 是缺配置/data 路径的真实入口。未进行完整普通首启 UAC 或业务启动。

targeted-validation.json 将同一原库存与 DLL 绑定 PR53 的 core/wpf/removal/component 四组原始证据，明确是继承而非本次重跑；PE x64 六入口另核验。其业务修复/配置账恢复/Unknown 守卫和 TestOnly 扩展静态装配均按原证据范围，未安装或执行扩展。原断言、截图、历史 D/RV 不改写。

verify-final.log 为新集合在入口验证后的全部八包复核；remote-publication.json / public-readback.json 是实际上传及公开 API 的11资产大小和 digest 比对。SHA256SUMS 对应发布根的八 ZIP 与两 JSON；本目录的其他原始日志由仓库 manifest 单独校验。build-record Inputs 是控制器工作树锁文件字节；每包 DEPENDENCIES.json/源快照绑定实际冻结输入。original-eight-inventory.json 通过 SHA 继续指向原 FAIL 构建记录，避免丢失补构建命令及真实来源。

Windows 全流程提权安装/UAC/ACL、另一账户、受保护修复卸载、安装/升级中断及恢复、重启和目标桌面/触控矩阵仍 NOT_RUN；真实 SDK/设备/现场 BLOCKED，Production NOT_READY，无商用合格声明。全套八 ZIP 再构建逐字节可重现本次 NOT_RUN；复制同一 ZIP 字节不能替代重建复现。
