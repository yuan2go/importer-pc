# 海康采集诊断与模块范围

MV-PD010003-06M-12C 的生产输入为 PC 条码生产者的 TCP 文本行，范围见 [BARCODE-PRODUCER-01](../work-packages/WP-IPC-BARCODE-PRODUCER-01.md)。本页只说明保留的 SDK 诊断能力，不定义生产接入协议。

## MVS 诊断

`src/Ipc.Drivers.Hikrobot.Mvs` 使用 MVS 采图。ABI 为 `0x04080201`，入口 DLL 文件版本 4.8.2.1。14 个 x64 DLL 及 SDK-NOTICE.txt 的精确闭包按原生清单核查，禁止依赖全机 PATH、替换同名异字节库或绕过保护目录。SDK 字节只保留在获授权的 D 盘离线输入，不上传 Git。

独立诊断先停生产者并确认相机释放。MVS 按规范 IP/预期物理序列号绑定，用 `device:hikrobot-gige:<serial>` 机级租约及 SDK 独占打开；不修改 IP/曝光/触发，不发软件触发。采集检查原像素/负载/触发条件，缓冲有界且逐帧归还。停止、归还或销毁失败保有原 owner 与租约；取消等待不代表相机已释放。

MVS 只报告连接和帧诊断，`entity.mode=observe-only`，始终不提供业务 Ready、不产生条码、包裹或 RCS 请求。诊断和生产者不得同时配置同一物理相机。外部读码平台不执行供包台机级租约；现场停止及释放确认必不可少。

## 智能读码器模块

`src/Ipc.Drivers.Hikrobot`、MvCodeReader/IDMVS 资格材料和 Probe 仍有构建、工具及历史证据引用，因此保留但仅适用于其真实智能读码器型号。不得用于本标准工业相机，不得将既有枚举/0码记录改标为本型号读码验收。

## 验证边界

工程使用 `tests/Ipc.Drivers.Hikrobot.Probe` 的合成原生端口验证资源生命周期和拒绝条件。真实 SDK 加载/枚举、真机帧、生产者解码、实体关联、RCS 与现场分别计证。历史结果见各原工作包和回执，不继承到新源码。
