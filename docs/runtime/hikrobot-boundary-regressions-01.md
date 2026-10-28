# 海康诊断与边界回归

工作包：[HIKROBOT-BOUNDARY-REGRESSIONS-01](../work-packages/WP-IPC-HIKROBOT-BOUNDARY-REGRESSIONS-01.md)。本轮继承main `d506f77`的内置MVS与单相机规则，只修ABI拒绝诊断、当前指引及测试；自有版本保持1.0.0。MVS仍仅采图，缺正式算法不发布业务Ready或条码作业。

实现及被测源码：`2f668db0a1bf6650e5088ac63dcf6158e52b81ac`。测试在该提交形成前的同字节工作树执行，原日志、逐项命令、源Git blob及SHA256见[验证清单](evidence/hikrobot-boundary-regressions-01/verification.json)；随后的验证发布只更新文档。起点`d506f77`的3项打包基线单独登记，不转记为本次新增测试结果。

| 本机Windows验证 | 结果与边界 |
|---|---|
| Application、SDK Probe、独立审查、生命周期模拟四个工程locked构建 | 全部0警告、0错误 |
| 打包输入校验 | 10项通过；含原3项模式规则、合成对照及DLL缺失/同大小改字节/错误长度/x86/缺少或空告知文件；失败均在产生输出前拒绝，无厂商库加载 |
| SDK Probe自检 | 读码器51、MVS 26、硬化16项检查及原生资格策略通过；包含新增UTF-8字节长度、ASCII高位字节拒绝、前导零、内嵌NUL保留、残缺UTF-8拒绝；仅合成原生端口 |
| 独立审查／生命周期模拟 | 20／7个场景通过，failed=0、skipped=0；包括并发释放、失败保有、100代启停和双路30秒合成输入；realPixels=0，不是解码吞吐或现场长稳 |
| Application六组 | `--parameter-refactor`（包含字段继承）、`--hikrobot-hardening`、`--hikrobot-sdk-editing`、`--station-single-camera`、`--deferred-connections`、`--rcs-timeout-errors`全部通过；控制字符仍拒绝，单相机按位、候选完整性与Unknown原请求责任保持，RCS为环回夹具 |

每次Windows命令先执行`. ./scripts/project-environment.ps1`。Probe构建复用本工作树同源`IpcContractPath`，不改锁文件或用旧宿主DLL替代。新增Python测试接入原`hikrobot-sdk.yml`，沿原失败即退出流程。当前文档归档按Git原始LF字节核验，未用Windows CRLF检出副本冒充原件。远端CI以本PR实际运行结果为准，本表只记录本机执行。

厂商待提供适配目标设备及实际固件的正式解码组件、Windows x64完整运行依赖、结果及编码、缓冲所有权、线程/退出合同和部署许可。现场另验真实图像与条码、可信包裹关联、双机资源/长稳及RCS物理格口闭环。本轮不操作设备、不形成新安装介质、不修改生产准入或Unknown原请求责任。
