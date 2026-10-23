# DUAL-CAMERA-COMMUNICATION-FIX-01 · 必要验证

基线 main `165c2ffe353c59e69729ede4c92fcfd827344c9f`；独立分支 `codex/dual-camera-communication-fix-01`。Windows本机，固定SDK10.0.402，Release/RestoreLockedMode；每次先执行环境脚本，全部临时数据位于D盘。实际日志、哈希、早期失败与外部边界见[RESULTS.json](../evidence/dual-camera-communication-fix-01/RESULTS.json)。以下断言数不含COMPLETE标记。

|必要检查|结果|边界|
|---|---|---|
|Verification、Sportmaster Desktop、UiVerification构建|PASS，0警告/0错误|真实Windows目标构建；不等于安装/设备验收|
|--dual-camera-communication|77项PASS|即时首帧与启动屏障、失败/取消排空、分帧/粘包/UTF-8/CRLF/4096边界、非法/半帧/EOF、回调异常/拒收、停止取消保有/再排空、两位故障隔离、旧Epoch拒收、同码实体、Pending/Effective重连/受控激活、原请求不变、过载持久围栏、日志不逐次续期、草稿不能变当前连接|
|--application-architecture|63项PASS|通用三业务架构、位间调度、部分启动失败、停止资源所有权及历史迁移|
|--architecture-quality-remediation|100项PASS|当前输入/过期、管理投影、真实进程故障恢复和原请求/资料/命令责任|
|--configuration-confirmation|40项PASS|原账、CAS/内容/runtime、启动回退、双窗口确认和保存保持暂停|
|--sportmaster-rcs / --rcs-heartbeat|138 / 26项PASS|隔离HTTP、双位编码与原请求、Unknown不重发、数字ID、原响应资料；心跳独立于业务|
|--version-compatibility及Python兼容|56 / 17项PASS|新业务版本、精确提供者与冻结声明，旧包不改身份|
|参数核心ParameterRefactor|41项PASS|保存/筛选/首配共模及原策略；原--parameter-refactor随后进入继承套件，整条命令并非PASS|
|ParameterInheritance / --parameter-inheritance|ENVIRONMENT_BLOCKED，退出-532462766|已定位为既有HKLM登记D盘UserData下presentation目录AccessDenied；未改ACL、注册表或绕过权限；完整含真实SID日志保留Git外。失败断言及后续未运行部分不计PASS。相机本轮配置/激活已由专项独立覆盖|
|PS5.1脚本夹具|13项PASS|默认无主动探测、白名单摘要、源/接口/下一跳、IPv4掩码、TCP所属进程、缺项明确、显式loopback ICMP/TCP零应用字节|
|本机只读采集|PASS，主动探测0、缺项0|PS5.1.22621.6133，19条适配器；无相机目标，未发设备连接，真实网络输出留Git外|
|--dual-camera-diagnostics|4项PASS|实际WPF单一视图、管理证据、长内容滚动和下一操作可到达；125%DPI合成截图[见图](../evidence/dual-camera-communication-fix-01/native-diagnostics.png)|
|随包文档检查|2项PASS|不可变来源及越界/缺链接拒绝|

源码命令：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --dual-camera-communication
powershell.exe -NoProfile -File scripts/test-diagnose-camera-network.ps1
```

修复前：本轮确定性夹具在原main运行时稳定失败于首帧入队。最初过宽IO异常隔离使既有“第二资源启动失败”断言失败，现只接受明确已清理连接的ObservationConnectionException；不删断言。配置回归的一次确认恰遇已有监测资源预留，夹具原有限重试增加同类明确调用前拒绝，不重试Pending/未知执行。RCS旧夹具文字仍为SKU而现行源码为商品编码，且普通模式原报文已遮蔽，现按实际管理流程和现行文案核原事实，未改业务返回值。

原生首图发现展开连接证据挤占后续动作，已改为原详情内有界滚动，再核对本轮单一视图；未做无关页面/全量视觉/长压测。按项目Impeccable Operate/WPF及ui-ux-pro-max使用既有布局；网页检测不充当WPF验收。

清单按Git原始blob核验：基线37项既有内容身份差异，本候选25项，无新增。先前Windows archive导出的CRLF转换不作为原字节身份依据；历史未触及资产不改写。

冻结本地候选的打包/解包启动冒烟在提交后补充独立回执；不创建Release、不安装生产介质、不发布网络或设备配置。真实左右相机、SDK/RCS、实体语义、目标工控机、拔网/静默断网识别时限、真实安装/UAC及现场均NOT_RUN/待确认；原D/RV不变。
