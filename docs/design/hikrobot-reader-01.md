# 海康模块适用范围

MV-PD010003-06M-12C 的条码接入由 [BARCODE-PRODUCER-01](../work-packages/WP-IPC-BARCODE-PRODUCER-01.md)统一定义。供包台通过 TCP 接收 PC 生产者输出，本页不另定义接入设计。

`Ipc.Drivers.Hikrobot.Mvs` 为采集诊断模块，操作及依赖见[诊断说明](../runtime/hikrobot-reader-01.md)。`Ipc.Drivers.Hikrobot` 的 MvCodeReader 实现适用于有自身解码能力的智能读码器，不适用于本型号，不是其备用生产路线。

这两个模块仍被工程、Probe、打包工具及原验收证据引用，保留代码、资格材料和原始证据。通用原生缓冲、实际停止后释放及物理独占守卫继续适用；已有帧/0码记录不能证明本型号条码链路通过。
