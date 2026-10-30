# 10验证证据

2026-10-30，Windows x64原生WPF，显示器DPI 125%。来源基准及变更代码字节见[SOURCE](SOURCE.json)，详细边界见[实施记录](../../sm-manual-debug-flow-10.md)。开发分支交付，不把局部PASS当作main/安装/设备或现场验收。

| 检查 | 结果 | 证据 |
|---|---|---|
| 人工输入到真实内置RCS适配器/SQLite/本地合成HTTP | 24 PASS | [日志](manual-application.log) |
| 既有单轮扫码Application | 36 PASS | [日志](camera-rounds.log) |
| 原生调试单码/原帧/绿框/冻结JSON/暂停锁定/再次扫码/明确发送 | 24 PASS | [日志](debug-wpf.log)、[锁图](round-locked-native.png)、[1024](round-1024-native.png)、[800](round-800-native.png) |
| 无相机时普通模式人工/PDA供包 | 8 PASS | [日志](manual-wpf.log)、[待配置](manual-deferred-native.png)、[断线](manual-offline-native.png) |
| 既有MVS软件识别链路 | 15 PASS | [日志](mvs-software-flow.log) |
| 既有作业UI | 30 PASS | [日志](operator-wpf.log) |
| Application/WPF构建 | 0错误、0警告 | [Application](application-build.log)、[WPF](wpf-build.log) |
| 历史TCP deferred夹具 | 两树同处失败，非PASS | [当前分支](historical-tcp-current-fail.log)、[未修改fa39f6bc基准](historical-tcp-baseline-fail.log) |

Production配置人工验证使用明确的合成未配置/断线相机端口，离线相机不提供原图；帧识别验证使用合成MVS API像素及实际ZXingCpp解码器。照片截图是测试产生的条码像素，没有使用用户附件参考照片。HTTP对端、编码、格口、任务均为合成值；外呼受明确发送动作控制。

未运行实际SDK/相机、物理PDA、客户RCS和现场验收。没有备份、生产数据或SDK二进制进入此证据目录。最初无效随机AdmissionId被原守卫拒绝的测试夹具已改为合法固定前缀；保留健康守卫断言后通过，未修改业务身份规则。
