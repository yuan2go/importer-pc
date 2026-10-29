# SM同帧原图留档与多码配置

2026-10-30，工作包 [SM-ORIGINAL-FRAME-01](../work-packages/WP-IPC-SM-ORIGINAL-FRAME-01.md)。本次实现不沿用旧MVID供包台的同步UI/HTTP/Bitmap共享缓冲。复用当前MVS独占采集、Application输入/作业账与RCS原请求链；当前SM仍为MVS采图＋进程内ZXing、连续识别、独立请求交RCS去重，无PLC。

## 同帧与原始像素

MVS完整Mono8帧→复制托管工作缓冲→归还SDK借帧→保留原始像素副本→对比度/ZXing→Observation→Application准入→后台PNG。

原图在对比度原地改写前复制，保存完整灰度8位像素，不缩放、不加框/水印、不用JPEG。帧身份为设备ID、epoch、SourceEventId、AdmissionId和ObservedAt，作业保留同一AcquisitionIdentity；不重新拍照、不按条码找图、不用后来一帧替代。多码拼接仍是一份作业、一张原帧图。

空帧直接释放；多码/规则/时效/健康/容量等拒绝输入不建立任务图片。每台相机原始缓冲最多32帧且池数组总量最多256MiB，超限在解码前明确故障，不丢已识别输入来腾空间。归还入口和保存线程分别持有引用；后台真正退出后才清零归还，重复释放不释放另一所有者。重连新epoch不改变已保留旧帧。

## 后台保存与原责任

Application在建任务同一持久提交中登记OriginalFrame.Pending；随后由最多2个后台保存线程编码并落盘。采集线程不编码、不执行文件写入或等待图片保存。内存复制和原有SQLite入队仍在原路径，磁盘/CPU争用仍须实测，不能宣称零影响或最坏时延达标。时延诊断新增image.original-copy及逐帧OriginalCopyMs，不挪动ObservedAt。

文件：`<数据根>/<运行模式>/<站点ID>/sportmaster/images/original-frames/<作业ID>.png`。文件名使用作业ID，不使用条码；详情显示原帧号、尺寸、留档状态和相对路径。写入唯一.png.pending、Flush(true)，再原子改名；不覆盖已有文件。记录原始像素SHA256、PNG文件SHA256/长度、Acquisition和完成时间。

Saved只表示原图保存完成，不代表RCS接受或物理完成。Failed记录错误并显示诊断，业务接受/Unknown事实保持；不因图片失败重发请求。进程中断后Pending变为Interrupted，不假称恢复了丢失内存，不重新拍照；保留可能存在的原文件。启动核对驻留Saved文件的身份、长度和哈希，异常改记留档失败，保留原证据哈希及业务事实。

写盘后重新读取最新作业，只更新OriginalFrame；该字段不进入ParcelPayload或冻结的HTTP报文。旧请求、合同、目标、尝试和Unknown守卫不变。图像后台任务不能继承输入的activeIngress提交身份。停止等待真实写入，取消调用者等待不释放后台所有权；有写入中的原帧时不做一致性备份或归档该作业。

## 备份、容量与保留

原图位于既有images恢复范围，完整备份按真实文件哈希包含PNG及未决残件；恢复、D盘根、权限及防链接检查沿原机制。保存前检查磁盘保留空间，不绕过reserve。现有恢复集文件数/manifest/磁盘容量配额仍有效，原图数量须计入容量规划；大量原图的长期吞吐与整套备份时长未验收。

不在线按时间盲删图片。沿既有已结历史策略，由停机授权维护先生成完整备份，再提交retired tombstone，最后只删除该已退役作业ID的.png/.png.pending。删除失败下次维护可重试；活动、Unknown或仍Pending的作业不会因此被清除。图片落盘失败/中断不是业务Unknown，也不为原请求提供清责依据。

## 多码参数

业务模块→条码筛选新增“拒绝多个有效条码”和“多条码分隔符”。首配、日常、测试、保存核对、导入导出共用同一个候选。默认拒绝，默认分隔符`;`；关闭后先按既有允许/排除规则逐项筛选，再按原候选顺序拼接，保留重复。例如`B,A,A`和分隔符`|`得到`B|A|A`，作为一份上游loadCode，quantity仍为1。

空白、非法、筛选超时、完整性不足等原守卫仍有效；不丢弃有效候选凑唯一，不截断。拼接结果最长512字符，沿当前RCS上限；分隔符无控制字符，关闭拒绝时必须含可见字符且不超过16字符。测试只使用Application选择器，不发送请求。编辑开关/分隔符会使旧测试结果过期；空分隔符错误定位到该字段。

两个新增字段为可选配置字段；缺省沿原严格策略和分号，默认序列化保持既有规则原字节/哈希。只编辑待用分隔符也不将旧null严格策略隐式变成筛选。新设置仅用于新作业，旧配置、候选证据、原请求及未知责任不重新解释。

## 验证

本机候选验证：有界原帧引用/双重释放/取消与停止；故意堵住写盘仍完成3帧采集和3份真实HTTP模拟请求；逐像素核对对比度处理前PNG；默认拒绝、显式拼接、重复保留、筛选、分隔符及512上限；失败/中断/文件变化不覆盖原请求或Unknown；完整备份包含原图，退役清理不删除活动文件。实际ZXing＋6MP合成SDK端口另贯穿SQLite/HTTP/PNG，排空后原始像素哈希匹配。

Windows原生WPF覆盖首配/日常共模、默认开关、编辑/预览/校验/刷新保留、紧凑窗口滚动；Windows PNG解码器独立核对原始像素。构建、断言数、精确提交和边界见[摘要](evidence/sm-original-frame-01/summary.json)。计数是断言行，不是现场用例数。SDK/真实相机、现场RCS、新包安装/拔插、目标机长稳及新增留档后的性能验收均NOT_RUN。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --sm-original-frame
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --mvs-software-flow --six-megapixel
```

[前一阶段时延调查](mvs-latency-01.md)的代码/数据保持原来源。该阶段“不放宽多码”的结论属于形成时范围；本次由用户明确授权改为参数，不倒写旧拒绝记录，也不以关闭拒绝制造旧性能PASS。旧当前指南原字节见[归档清单](../archive/pre-sm-original-frame-01/manifest.json)。
