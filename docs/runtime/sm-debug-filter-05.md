# 当前筛选规则预览修复 · SM-DEBUG-FILTER-05

2026-10-30；[PR155审查](https://github.com/yuan2go/importer-pc/pull/155#discussion_r4413911365)。main为`9df3901e`，本次源码父提交为`6850b23b`；PR157已进入该父提交。原01/02历史Windows/MVS证据和04未验证边界不倒写。

## 问题与修复

当前本地入口为`StationDebugEvidenceControl`。它把Effective.BarcodeFilter的null换成new("")，导致测试不同策略。实测旧按钮输入`ABC`加尾部LF时显示选中ABC，生产选择器对同样`["ABC", ""]`拒绝MultipleCodes。单候选的选择结果虽相同，规则语义/结果详情仍不同。

修复让运行期和首配预览契约及其转发实现接受`BarcodeFilterPolicy?`，UI原样传递Effective值；二进制参数类型不变，不改变生产选择器。输入改按CRLF/LF/CR完整分隔并保留空候选、重复和顺序，不删掉全部CR、不Trim。空文本代表一个空候选；零候选另由契约验证。

无规则时，一个合法原始候选才可选；ABC加尾部换行必须拒绝。显式空规则沿生产既有逻辑，可排除空候选后选中ABC，但两个有效码/重复码仍拒绝。PR157仅显式关闭多码拒绝才按原序拼接，B/A/A→B|A|A；只保存待用分隔符仍执行严格原始单码。没有通过放宽生产判定消除差异。

首次定向构建暴露本分支尚未构建代码的三个编译点，一并修正：图片窗口补System.IO；隔离入口明确要求仓库和配置已建立；清场按钮订阅移至setup初始化之后。没有删除断言、屏蔽可空警告或改用空实现。

## 实际验证

新增`--camera-filter-preview`在Windows Dispatcher真实创建现有内部WPF控件，以反射定位内部类型，不扩大生产可见性；使用独立D盘合成Simulation身份/手动端口，不连接SDK或客户RCS。不是HTML或模拟渲染。

| 检查 | 本次结果 |
|---|---|
| 修改前原生按钮回归 | RED：确实出现ABC+尾部LF假通过；保留原日志 |
| 修复后原生WPF | 22场景、113条PASS断言；退出码0 |
| 对照 | 每例先核对固定生产预期，再逐字节核对运行期/首配结果（含RuleHash及逐候选决定），再核对按钮显示 |
| 权限和副作用 | 五组规则各验证管理外/撤权拒绝、撤权清空、发送数0、作业/输入数0及配置不变 |
| WPF验证、Application验证、Sportmaster Desktop构建 | 均0警告/0错误 |
| MVS Probe | 单节点构建0警告/0错误；合成原生端口自测73条PASS，退出码0 |

早期构建失败、测试夹具直接访问内部类型的编译失败均保留。Probe首次默认并行构建发生MSBuild OutOfMemoryException；相同源码改为`-m:1 -nr:false`后构建/执行通过，不隐藏失败或修改测试。上述数字是断言数，不是现场验收数。

源码及日志在[evidence/sm-debug-filter-05](evidence/sm-debug-filter-05)归档；以源码清单中的规范化Git字节和后续冻结提交标识对应。测试后没有修改生产源码。

## PR157和责任边界

已核对祖先：`9df3901e`为`6850b23b`祖先；整合提交双亲包含`a8ee4f3c`和`9df3901e`。本次相对父提交未改MVS目录、SIMD、时间戳、预览/隔离资源所有者、原帧池/PNG/备份清理、生产SportmasterFlow/BarcodeFiltering、阶段发送和Unknown责任。原图池、PNG写入及MvsTimings核心也与PR157主线字节一致；记录于保全清单。

本地筛选仍只调用管理守卫后的有界离线选择器，不注入观察、建立任务、发送RCS或创建相机连接。此前已授权的整轮“发送请求”独立功能未调整；不能把本次本地筛选验证扩张为整轮发送/Unknown恢复通过。真实SDK、双相机、客户RCS、重连/拔插、新安装、长稳和现场验收仍NOT_RUN；旧备份三项基线失败保留原来源，本次未重跑或关闭。

## 运行

在仓库根PowerShell先执行`. ./scripts/project-environment.ps1`；命令与原始输出一并归档。

```powershell
dotnet build tests/Ipc.Desktop.UiVerification -c Release --nologo --verbosity quiet
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/projects/importer-pc-ui-debug --camera-filter-preview
dotnet build tests/Ipc.Application.Verification -c Release --nologo --verbosity quiet
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true --nologo --verbosity quiet
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:RestoreLockedMode=true -m:1 -nr:false --nologo --verbosity quiet
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-self-test
```
