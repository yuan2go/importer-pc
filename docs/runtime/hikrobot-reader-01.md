# 海康标准相机当前安装、参数与诊断

当前规则：[BUNDLED-MVS-CAMERA-01](../work-packages/WP-IPC-BUNDLED-MVS-CAMERA-01.md)。目标MV-PD010003-06M-12C，Windows x64，版本1.0.0。原独立安装/TCP指引和历史验收保留在[归档](../archive/pre-bundled-mvs-camera-01/README.md)。

## 安装与参数

当前SM安装包内置真实MVS采集驱动及核查的GigE原生闭包；无需另装驱动包、配置扩展信任或退出首配再切换。原生库不上传Git，离线构建输入来自获授权本机D盘。安装器按既有受保护部署完整性、所有权、指定运行账户及生命周期安装。不得依赖MVS客户端全机搜索路径，不修改PATH。

首次配置和日常管理参数共用同一完整候选。左右直接显示海康SDK、IP、预期序列号、触发要求、关联和有界采集缓冲；没有TCP端口/keepalive或TCP切换。公共模板无现场IP。每台只有一个相机编码，取格口与心跳共用；序列号是物理设备身份，不是第二个RCS编码。

相机和RCS可以明确稍后配置；相关连接不启动，业务拒绝准入。保存候选后按原Pending/Effective受控重启启用。Production启用须预期物理序列号，左右不得重复，同一源实例重连不自动学习另一台设备。切换页面不重建连接，未保存输入保持同一候选。

## 真实SDK与边界

当前ABI为MV_CC_GetSDKVersion=0x04080201，入口DLL文件版本4.8.2.1。14个已核查x64 DLL约10MiB，主入口MvCameraControl.dll、GigE预载MVGigEVisionSDK.dll，完整字节/hash、AMD64和保护目录逐项核验，整目录只允许当前清单，禁止同名异字节已加载模块。SDK模块及文件保持至进程退出，换库受控重启。SDK缓冲由实际GetImageBuffer/FreeImageBuffer成对归还；连接/停止/资源释放沿原加固实现。

MVS仅采图，缺适用正式条码识别组件。只有真实连接/实际帧才能分别报告连接/帧；不把图像解压、模拟码或连通当识码成功。当前仅观察，不创建包裹或RCS作业，健康不发布业务Ready。正式算法仍需厂家提供适用组件、x64运行库、真实接口文档/示例与必要许可，不限定组件名称。

## 构建与必要核验

开发侧正常本机交付使用package-application.py的--mvs-runtime指向已核查的14库及SDK-NOTICE.txt目录；这不是安装用户的操作。缺文件/字节变化/非AMD64/缺许可告知应拒绝正式介质。CI无厂商文件时仅可显式生成VERIFICATION_ONLY_VENDOR_ABSENT验证介质，不宣称SDK已内置或可正式交付。

安装内置字节随application-release与安装介质清单一起校验。参数可读不证明SDK已加载；连接诊断、实际帧、算法、RCS与现场分别记录。仅枚举初始化验证不打开或触发相机，不证明真实读码。
