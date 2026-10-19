# PR47复审与最新main最终集成

原PR #47于本轮结束前被并行操作按旧head合并。后续[PR #53](https://github.com/yuan2go/importer-pc/pull/53)继承main `aaa30b57cf8f810637b6502748783cdab66bbf36`全部PR48～51发布实现/上传证据，并提交最终差额；未覆盖并行代码、原始证据或未跟踪用户文件。其他开放PR #52是独立旧发行证据/工作流任务，本轮不代为合并。

最终冻结源码为 **`55025ce9c62173fea5cdc5ceecfb29453ed5e7fa`**。继承`022c898`完整修正，追加“原账已有启动事实而DB丢失”拒绝空库保护；配置字节不变。打包脚本继承最新main的统一冻结/归档/依赖机制，所以另冻六介质，不重标旧包。业务1.4.0 / Application.Contracts1.10.0 / DB8；新Simulation/TestOnly静态扩展版本1.4.4，不安装或执行插件。旧签名包保留。

[六介质身份](media-identities.json)、[逐项结果](checks.json)与[复审裁决](../../../reviews/pr47-commercial-productization-review.md)共同定位当前交付。原`bd86fbb`、`022c898`及两批中止候选均保留各自身份。回执所在文档提交与main合并号不作为已运行介质源码号。

本轮只追加集成风险必要检查：[统一发布门禁9项](unified-integration-tests.log)、[六介质库存/ZIP核验](inventory.log)，以及同一最终Sportmaster Windows发布DLL的[核心](core-result.json)/[日志](core.log)、[原生WPF](wpf-result.json)/[日志](wpf.log)、[卸载内核](removal-result.json)/[日志](removal.log)、[静态组件](component-result.json)/[日志](component.log)。机器结果核对同一SourceCommit及同一库存散列，验证前后介质不变。原生首配截图见[截图](commercial-first-configuration.png)。

最终介质运行：仓库根`.artifacts/comm-review-47/final-releases/sportmaster-55025ce-win-x64/Setup.cmd`，安装后指定Windows账户非提权启动`desktop/Ipc.Desktop.exe`，新站先配置，失败使用`Maintenance.cmd`；具体目录/权限/原请求守卫见[操作指南](../../commercial-productization-01-operations.md)。ZIP仅本机保留，未自动上传。其他业务/目标的文件名、字节数与SHA256在上述身份表。

复现时检出精确冻结源码，使用匹配SDK10.0.402和全新输出目录：`python scripts/package-application.py --application sportmaster --target win-x64 --output <新目录> --dotnet <SDK路径>`。用同版`verify-commercial-media.ps1`、`verify-commercial-wpf.ps1`、`verify-commercial-removal.ps1`对该目录执行本轮同源夹具；组件沿`build-extension.py`生成新不可变版本后静态检查。使用继承的单一发布入口，不从历史证据目录执行旧编排。

**NOT_RUN**：Windows提权安装/UAC、第二账户与跨用户DPAPI、受保护登记切换/ARP修复卸载、真实重启删除、真实第二机器、完整目标触控/DPI/读屏及现场负载。**BLOCKED / NOT_READY**：真实SDK/协议/设备/组合Production资格、换机秘密/来源退役/外部编号交接和现场商业投产。旧库缺机器来源的自动接管仍BLOCKED。局部合成/当前Windows检查不关闭历史D/RV或覆盖原HTML验收。
