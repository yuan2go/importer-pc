# SM-OPERATOR-UX-18 · Windows 本机定向证据

SourceCommit `44b41c9359e746d319ae09fc06079257ad4e278e`，包含 main PR168 `3b7779d9`。三项Release构建0警告0错误；21套件574条PASS（Application242、原生WPF332）。[summary.json](summary.json)保留每套件退出码、源文件和本机自有构建程序集指纹。音效验证为触发与身份限次，未声称扬声器可辨度已通过。

复验：在独立D盘工作树检出上述SourceCommit，先运行项目环境脚本与锁定依赖还原（UI/App两个验证工程，以及`src/Ipc.Desktop/Ipc.Desktop.csproj -p:BusinessModule=Sportmaster`），再使用本目录`verify.ps1 -Workspace <该工作树绝对路径>`。脚本要求干净源，所有构建/断言必过；PDA套件保留前台HWND、焦点和修饰键守卫，失败不得向其他窗口发键。不要并行运行界面套件或覆盖正在运行的程序集。

旧e9bfd4e1的21套件573PASS仅对应整合前源；中间截图发现原因半行截断后新增一项真实绘制检查。最终44b41c93完整重跑，未将旧PASS转记。之后仅补证据与文档，原始日志字节按gitattributes保留。

截图均为合成条码/本机回环RCS，1000×525位图对应800×420DIP和当前125%DPI；不等于物理工控屏、现场手套或服务器验收。原生PDA用Windows SendInput逐键验证，不等于真实PDA设备绑定。真实MVS、客户RCS往返、声卡设置和现场验收NOT_RUN。用户明确暂不新增统计。

- [800成功](operator18-success-800.png)
- [1366成功](operator18-success-1366.png)
- [800暂停](operator18-paused-800.png)
- [800拒绝及完整原因](operator18-rejected-800.png)
- [800记录](operator18-history-800.png)
