# 菜鸟供包台单 EXE 安装程序

任务范围和开工裁决见[工作包](../work-packages/WP-IPC-SINGLE-EXE-INSTALLER-01.md)。目标是现场只需接收并双击一个 `菜鸟供包台安装程序.exe`，无需手工解压、脚本、.NET 安装或联网下载。内部继续使用既有安装向导，普通人工安装即可，不新增批量部署要求。

自包含启动器负责内嵌介质的完整性、准备进度及安全展开；实际安装仍复用 `StartupSetupWindow`、`ParcelStartupServices` 和 `WindowsDeploymentSetup`。选择运行账户、程序目录和原数据目录，核对摘要后由原 helper 请求 UAC。旧配置/数据/请求、升级备份、并排目录与回滚边界保持。打开/关闭向导不等于安装成功。

技术依据：[Microsoft 单文件部署](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/)中的自包含和原生库内嵌方式；不裁剪 WPF、不新增运行时下载依赖。准备页沿现有品牌/系统字体、固定按钮和实际进度，不从参考 iFinD 复制资产或声明其许可。

构建顺序：提交精确运行源码及随包指南，执行 `python scripts/package-application.py --application sportmaster --target win-x64 --output <新介质目录> --dotnet D:/tools/dotnet/dotnet.exe`；随后用 `python scripts/package-installer.py --media <同版介质ZIP> --output <新EXE路径> --dotnet D:/tools/dotnet/dotnet.exe`。封装器拒绝混用其他源码的 ZIP，不替换旧输出，现场只需交付生成的 EXE。构建工作目录保留供追踪，不是现场输入。

安装文件会自动展开到当前 Windows 用户的随机私有临时目录。校验完成后仅调用内嵌的 `desktop/Ipc.Desktop.exe --setup`；安装政策和 Production 守卫不变。关闭向导后清理本次介质目录，取消准备不启动安装。失败显示可复制诊断；若文件占用阻止清理，保留具体错误与目录，不误称安装完成。Windows/.NET 自身单文件运行缓存不属于业务数据。

没有提供 Authenticode 签名证书，因此生成 EXE 未签名；独立 SHA-256 可用于核对字节，不能替代分发来源可信。此处不生成临时“可信发布者”证书或虚构许可协议。

最终交付身份和实际验证在完成后补记。此前 `cainiao-branding-03.zip` 仍仅是原冻结 ZIP 介质，不重标为本次单 EXE。安装准备和打开向导通过也不代表实际 UAC 安装、升级/卸载、SDK/设备或现场验收已通过。
