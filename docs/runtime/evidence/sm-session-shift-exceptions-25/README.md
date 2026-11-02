# SS25 冻结源码验证

SourceCommit: `8ed282ff739fb2632ea1619020c101451948e607`。基线main: `0fc6af419d402568f12c9ee5fcae28fd434f6d76`。

- 3项Release构建PASS，0 warning/0 error：应用验证、实际WPF验证、Sportmaster桌面。
- Core 13组、557项断言PASS；Native 14组、565项断言PASS，共1122项。
- Release发布使用no-build/no-restore，同源EXE `--environment-report` exit=0，InformationalVersion `1.0.0+8ed282ff739fb2632ea1619020c101451948e607`。
- 实际Windows 11 x64 23H2（22631）、.NET 10.0.13；原生截图客户区1366.4×768、1092×613.6、800×420 DIP，实测DPI125%。截图为实际WPF与合成数据，不是原稿截图或现场验收。

复现：干净提交树运行 `./scripts/verify-sm-session-shift-25.ps1 -Section Core`、`-Section Native`、`-Section Publish`。脚本绑定精确提交，Native/Publish要求同源Core通过；任何源变化或失败均停止。

`core-summary.json`、`native-summary.json`、`publish-summary.json`记录准确命令、退出码、断言数、原生证据目录和源码身份。日志及九张截图的原字节见 `SHA256.json`；Git按二进制字节保留收据，不转换终端换行。

SS25专项包含：真实软件正常开关/实际子进程终止、管理/多窗口不切班、同分钟与回拨时钟、原T1跨班归属、101条未知分页、输入拒绝与RCS原因分开、原件定位/无重发、汇总不一致拒绝启动、固定快照防调用方篡改/过期/不覆盖/根目录保护、25小时夏令时自然日。受影响回归包含隔离与生效、统计清理、MVS软件链的合成接口、管理权限、超时、读码反馈、10/11兼容升级和关闭资源等。实际范围以每组日志为准。

物理MVS/客户RCS/受保护安装/现场持续运行NOT_RUN；安装入口使用真实WPF与合成部署回调。CI保持停用。未公开SDK/字体/客户原稿/真实生产记录。后续证据/说明提交只增加文档，合并前核对src/tests/scripts与上述测试提交没有差异；不把文档提交伪称重新编译的来源。
