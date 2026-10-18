# 采集生命周期P2证据

运行源码`ca35b16a0ef883126b39640c19ea6f840e5a9716`；修复前main `2126ac2c06a0c352d96afbbf13ec1a719c352738`。完整结论和命令在[结果报告](../../acquisition-lifecycle-p2.md)。[checks](checks.json)保存各日志状态/计数/hash及修改源码hash；日志不作为客户或现场验收。

`logs/red-before-fix.log`是期望复现的真实失败。`initial-*`保留已修正的测试枚举、缺Fixture和冻结合同不一致失败；`baseline-build`/legacy相关为冻结提交前的源码树检查，其他运行/包使用冻结提交。`candidate-readback.json`为四个本地候选的ZIP和解压hash，三个`smoke-*.json`及可选能力smoke是实际独立进程结果。

本目录不包含运行ZIP、签名私钥、SDK、客户原图或生产数据。签名包identity只证明绑定冻结Debug宿主及TestOnly政策，不能外推为Release或Production兼容。
