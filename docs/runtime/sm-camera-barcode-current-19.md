# SM主条码随最新观察更新19

2026-11-02，基线main `039ccdee`；范围、守卫及现场现象见[工作包](../archive/work-packages/WP-IPC-SM-CAMERA-BARCODE-CURRENT-19.md)。最终验证产品源为 `1740ab47eb36ebefbac35db4a488ddf89f66d60b`，三个Release构建0警告0错误，16套件519条PASS断言。历史16/17/18及PR170原证据、原NOT_RUN不改写；本页不把本机合成验证称为现场修复验收。

## 原因与修复

MVS每帧独立生成候选，Application按位替换最新已处理输入；前轮正常12帧未复现跨帧字符串追加。已确认的代码问题是主框取已处理输入/作业，而读到快反馈只更新码尾。持久交付未完成或本位消费者还在等待上一笔RCS时，新码已读到，主框仍显示旧码。旧输入若是多码拒绝，`InputCode`把该帧候选逐行放入可编辑主框，就出现多条可复制旧码滞留。

本轮从Application的既有只读快反馈补充候选数量与源序号，不加候选数组、不执行消费者、不等待业务锁或改变持久责任。WPF主框立即显示最新观察的完整单码；多个或非法候选只显示明确占位提示，原始数组、顺序、重复、筛选和审计仍保留。处理完成后显示本次业务选中码，包括用户显式开启的多码拼接结果，不替用户挑首码。

最新观察水位与1秒提示寿命分开。同一epoch按源序号判断新旧；没有该元数据的兼容反馈沿原观察时间比较。旧通知、迟到完整快照和排队中的旧作业完成均不能覆盖较新观察；提示过期及新开窗口不能恢复旧主码。水位只用于显示，不创造实体、准入或RCS接受事实。

新观察同时撤下旧格口、四个任务值和图片资格。图片命令再次验证当前观察/作业/采集身份；未持久、未准入或正在编辑人工草稿时不打开上一件图片。匹配的新结果确认后，PR170常驻任务字段正常恢复。已有Unknown仍保留原请求责任，提示明确“上一件”的待核对结果。

自动赋值沿原更新标志，不生成编辑草稿或输入命令。真实人工草稿、光标、焦点、提交中/Pending/丢失回执及原CAS核对继续保留；不因相机变化重新设定人工输入基准。连续识别每次独立持久/发送、FULL/WAL、原冻结报文、容量/健康/权限、左右独立连接及资源所有者不变。没有放松相机/RCS门槛，也没有通过丢弃已识别输入或重发Unknown来清队列。

## 原生负向复现与回归

新增`--camera-barcode-current`在原main产品上首次执行即失败：首个观察已解码、码尾已推送，但主框仍为空。基线构建通过，产品源哈希和真实失败日志保存在[证据目录](evidence/sm-camera-barcode-current-19/README.md)。仅首个断言作本轮负向复现，不声称基线执行了后续全部检查。

最终专项21条断言经过真实ZXing、Application、SQLite、内置RCS适配器/本地HTTP和原生WPF。IMvsApi帧与对端为明确合成，不连接现场设备。覆盖首次读到、预持久挂起、较旧快照、提示过期、新窗、本位慢RCS/多个排队观察/迟到结果、同码独立请求、多码原证据、控制字符、完整长码、人工草稿与原基准Conflict、左右位独立、原请求字节和图片/任务对应。没有把原始输入丢弃来让显示通过。

回归另发现PR170恢复四项任务值后，未修改main的800×420格口实绘只有39 DIP，低于现行44 DIP断言；独立main checkout实际复现。修复紧凑输入区间距及44 DIP输入框下限，保留四字段、64 DIP按钮、32 DIP主动作间隔及原断言。最终800×420实绘51.4 DIP，1366×768仍112 DIP；完整一行异常原因也通过。没有将旧失败改记PASS，原main和中间源的失败日志均保留。

最终源16套件：Application六套299条、原生WPF十套220条，共519条PASS；包括RCS超时/原请求、同帧原图/显式多码拼接、MVS软件链路、停止资源、呈现乱序/慢盘、参数就绪输入、人工供包、PDA逐键、操作体验及预览生命周期。数量是断言数，不是现场案例数；没有修改、删除或跳过失败断言。精确命令、每套退出码、程序集与原始文件指纹见[summary.json](evidence/sm-camera-barcode-current-19/summary.json)。

## 发布、运行与边界

同一产品源完成framework-dependent发布，实际`Ipc.Desktop.exe --environment-report`启动退出0，报告X64/.NET10.0.13，程序集版本`1.0.0+1740ab47...`；[回执](evidence/sm-camera-barcode-current-19/publish-smoke.json)。这只证明发布输出和EXE环境报告入口可启动，不代替安装/UAC、SDK加载、完整日常启动或现场连机。

在D盘独立checkout执行：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll $PWD.Path --camera-barcode-current
```

完整回归用[verify.ps1](evidence/sm-camera-barcode-current-19/verify.ps1)的`-Workspace <绝对工作树>`；要求干净源，顺序运行原生套件，保留PDA前台HWND/焦点/修饰键守卫，不并行控制同一桌面。

本轮已完成代码、构建、本机Windows合成、原生几何/输入/资源回归和上述发布启动冒烟；真实相机/服务器、客户RCS、物理PDA/声卡、新安装升级及现场长稳仍NOT_RUN。未修改现场程序或配置，未发布安装包，未改变D/RV批准。现场若仍有不同帧条码被真正拼接，需要用现场SourceCommit、原始候选和帧号进一步核对，不能把本轮未复现部分写成已验收关闭。
