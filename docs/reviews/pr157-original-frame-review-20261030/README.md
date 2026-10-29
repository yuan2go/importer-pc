# PR157同帧原图与责任守卫复核

2026-10-30。用户要求复核PR #157的同帧图像关联、后台保存资源释放和原请求/Unknown守卫，确认后合入main。审查基线为main `f870ffc00abcba78497b3d01d83251d786052ae3`、原PR头`736e5b6c79fc5a8b3558b7bb9a1b3979b62b7b05`。

结论：未发现阻止合并的生产缺陷。本轮只补强测试与审查证据，生产源码与原已验证实现`7afd1e11`一致；不修改输入时效、设备身份、原请求或Unknown规则。不是新增真机、安装包或现场验收。

| 检查点 | 源码与执行证据 |
|---|---|
| 同一识码帧 | SDK借帧复制并归还后，在对比度改写前保存独立原像素。以设备、epoch、SourceEventId、AdmissionId、ObservedAt完整键查找；任务保存同一AcquisitionIdentity。拒绝按条码、仅帧号或最新帧替代。原有三个不同像素、相同条码任务逐图解码/哈希核对通过。 |
| 重连与帧号重用 | 新增真实MvsAcquisition类配合合成SDK端口的两epoch测试：两次均帧号1和相同条码，旧writer跨Stop与新Start存活，分别读到各自不同像素。错误epoch取图拒绝，不损伤两个writer。 |
| 原帧所有权 | 输入临时引用与后台writer分别计数；拒绝、空帧、消费结束释放对应所有者。内存有帧数和实际池数组字节上限。借帧在解码前归还；待交付结果仍保留并阻止盲重连。 |
| 保存、停止和释放 | 最多两个编码/写盘任务；采集线程不做PNG或文件IO。停止先排空生产者和消费者，再等待实际辅助任务，最后Dispose源。取消Stop等待不提前归还内存。新增两个epoch writer同时存在时Dispose拒绝、释放后重试及重复Dispose测试，确认池引用和native teardown不重复释放。 |
| 异步交错 | 保存线程清除继承的activeIngress，IO后持业务gate重新读取当前作业，只更新OriginalFrame。外部调用返回也读取当前作业。新增原图先Saved、真实本机HTTP请求仍Dispatching、随后超时Unknown场景，确认Saved及完整FrozenRequest均未被旧快照覆盖。 |
| 原请求与Unknown | OriginalFrame不进入ParcelPayload或冻结的HTTP报文。成功、失败、进程中断、文件损坏都不创造RCS接受事实或新增发送尝试。Saved/Failed与Unknown组合均重启验证，原请求、回包、一次尝试及Unknown独立保留，无自动重发。 |
| 历史与备份 | Pending阻止作业归档和一致性备份；文件与hash纳入现有恢复集。停止维护完成完整备份、提交retired身份后才清理对应图片；活动和Unknown不作为可清历史。原有专项及保留维护回归通过。 |

## 本轮验证身份

先在干净原头`736e5b6c`重跑：同帧留档22、Unknown重启3、RCS超时57、保留维护61、完整Probe141条PASS断言。Application和按冻结合同路径构建的Probe均0警告、0错误。

补充测试最终提交`865dda64`在干净工作区构建：同帧留档26、6MP实际ZXing→合成SDK端口→Application/SQLite/HTTP/PNG全链12、完整Probe146条PASS断言，退出均0。两项构建0警告、0错误。补测只修改两个验证文件；生产代码差异为空，没有以删除断言或修改守卫通过。

所有运行日志、二进制及本机数据留在D盘私有验证根；精确提交、日志SHA256、构建与运行结果见[摘要](summary.json)。计数是断言行，不是现场用例数。原`7afd1e11`的WPF及其他回归保留其原身份，本轮没有把它们转记为新源验证。

验证过程保留两次未通过记录：首次Probe调用漏传已文档化的IpcContractPath，锁定还原报NU1004；改用文档中的冻结合同命令后通过。补测初稿`4d54e2a6`的Probe用例局部变量重名报CS0136，`865dda64`仅修正用例变量后重新构建和执行通过。没有生产修复或被忽略的最终失败。

旧backup-health-recovery三项失败仍按[原说明](../../runtime/sm-original-frame-01.md)保留：两个用例缺Windows符号链接权限，一个用例采用当前SM已拒绝的旧手动读码配置；已在相同main基线复现。本轮不把该整套标记为通过。

## 未验证边界

真实海康SDK/设备、实际拔插、客户RCS、新安装包、目标机长稳及开启留档后的真机端到端时延均NOT_RUN。上述源码审查及合成资源/HTTP交错测试支持代码合并，不解决已有ZXing整体长尾，也不代替现场资格。CI保持停用，未操作并行PR #155/#156。

## 复现

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
$env:IPC_VERIFICATION_WITHOUT_VENDOR_RUNTIME='1'
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --sm-original-frame
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --mvs-software-flow --six-megapixel
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:IpcContractPath=<绝对本轮Ipc.Platform.dll路径> -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --self-test
```
