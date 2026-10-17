> DELIVERY-12待验入口：[12报告](extension-windows-delivery-12.md)。当前授权包含Windows参考验证；开工检查无本机/VM/自托管runner/交互桌面。新增远端原生WPF冒烟另列实际结果，不能替代本文件W01～06。原11/09记录保留，不改变支持矩阵；保护拒绝不算正常适配PASS。

> 当前待验版本绑定[REVIEW-FOLLOWUP-11](review-followup-11.md)的新SourceCommit/包SHA。下文上一版边界保持；没有新增Windows/真实集成执行，W01～06 NOT_RUN，B01～06 BLOCKED。

# WINDOWS-VALIDATION-06 同版待验记录

2026-10-16；授权已具备，**实际Windows环境缺失：BLOCKED**。当前执行宿主为macOS ARM64；没有可用Windows客户端、工控机、显示/DPI设备、扫码器、触控或扬声器听审环境。包身份及SHA引用[09同版检查](reference-validation-09-checks.json)。不拿HTML导出、旧包或跨平台核心结果代替WPF操作。

|项|实际结果|未执行内容/解阻条件|
|---|---|---|
|W-01|NOT_RUN|Windows客户端1920×1080与1366×768各100/125/150%、跨屏DPI、实际客户区、长字段/六业务状态独立截图、1280×680保护和空间恢复|
|W-02|NOT_RUN|同Runtime双窗、独立草稿/筛选/详情/焦点/滚动、重复命令与关闭副窗；核心单所有者验证不能替代实机|
|W-03|NOT_RUN|真实扫码Enter/Tab、初始箱码焦点、风险确认取消默认、Tab圈定/Esc和归还；静态处理不等于设备通过|
|W-04|NOT_RUN|接受/拒绝/拆包提示实际播放和相邻通道可辨、静音视觉等价、触控/持续阅读；核心提示队列验证不证明听到声音|
|W-05|NOT_RUN|同版win-x64自包含包解压启动、PowerShell、指定普通账户/ACL、跨会话租约、路径、旧库显式升级/回退隔离|
|W-06|NOT_RUN|实际WPF慢盘/故障持续绘制、过期禁用、停止失败/重启及未决恢复；来源/SDK缓冲需真实能力证据|

本轮WPF交叉构建和win-x64发布/文件完整性分别在09登记PASS或FAIL，不扩张为本表PASS。实际客户端截图数量为0，未制造空截图、拼图或将v1.1 PNG更名为WPF。全部格保持NOT_RUN；环境阻塞原因与尚未执行的动作分开登记。

后续必须使用09标明的同一SourceCommit/ZIP SHA及新的合成数据根。记录OS build/CPU/账户会话、物理分辨率/系统缩放/实际DIP、前置/动作/观察/预期、执行者/时间与独立截图/请求证据；改代码后重新形成同版身份。按[06工作包](../work-packages/WP-IPC-WINDOWS-VALIDATION-06.md)逐格执行，不复用旧截图。Windows参考通过也不能关闭[05真实能力](integration-05.md)或现场D/RV。
