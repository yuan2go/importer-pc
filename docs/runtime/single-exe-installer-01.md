# 菜鸟供包台单 EXE 安装程序

任务范围和开工裁决见[工作包](../work-packages/WP-IPC-SINGLE-EXE-INSTALLER-01.md)。目标是现场只需接收并双击一个 `菜鸟供包台安装程序.exe`，无需手工解压、脚本、.NET 安装或联网下载。内部继续使用既有安装向导，普通人工安装即可，不新增批量部署要求。

自包含启动器负责内嵌介质的完整性、准备进度及安全展开；实际安装仍复用 `StartupSetupWindow`、`ParcelStartupServices` 和 `WindowsDeploymentSetup`。选择运行账户、程序目录和原数据目录，核对摘要后由原 helper 请求 UAC。旧配置/数据/请求、升级备份、并排目录与回滚边界保持。打开/关闭向导不等于安装成功。

技术依据：[Microsoft 单文件部署](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/)中的自包含和原生库内嵌方式；不裁剪 WPF、不新增运行时下载依赖。准备页沿现有品牌/系统字体、固定按钮和实际进度，不从参考 iFinD 复制资产或声明其许可。

实现和最终验证进行中；完成后记录构建命令、精确来源、文件散列及未验边界。此前 `cainiao-branding-03.zip` 仍仅是原冻结 ZIP 介质，不重标为本次单 EXE。
