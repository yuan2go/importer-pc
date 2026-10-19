# 修正交付候选 · 565abcc

冻结运行/媒介源码565abcc63b885dc60873aa21e25f2d4612963c0d，业务1.2.8 / Application.Contracts1.8.0 / Parcel DB7；能力2.1.6、reader1.2.6、RCS1.0.8。上一1401b7d候选因composition声明Contracts1.7.0而退出最终交付，旧发布/证据仍在父目录及原Git提交，不覆盖或重签旧身份。此次业务逻辑/SQL与1401b7d相同，修组合契约声明/打包验证、提升不可变身份及同步随包当前版本。

- `ARTIFACTS.json`与`media-verification.json`绑定7个ZIP、3个签名包及3个identity；除原hash/CRC/完整文件集合/指南同源/PE位数外，再核composition=项目Version=发布deps中的Application.Contracts1.8.0。
- `release-input.log`、`release-snapshots.log`、`release-maintenance.log`、`compatibility.log`以新发布核心DLL执行；`verification-runtime-files.json`绑定实际DLL。DirectPlc/WcsToPlc独立Flow仍为同源验证构建字节，其真实发布DLL由9个解压进程验证。
- `signed-t1-migration.log`复核真实新签名RCS加载、左右双向反序T1、各一次POST、实际原图释放/归档、重启/备份恢复旧范围退役及SQL6→7保原请求/只读拒绝歧义。`signed-original-unknown.log`复核原Unknown/重连不重发。
- `smokes/`为3个Application × default/BeforeMain/Attached，实际从/tmp起进程；`selected-journey.*`为新精确包安装/绑定/激活/失败保旧/卸载CLI；`cli/`为实际解压DirectPlc的维护入口。
- `contract-python.log`保留首轮修正仍未改到canonical组件map的失败，证明新守卫拒绝旧声明；`contract-python-fixed.log`为修正该map后19项通过。`input-setup-missing-workspace.log`为调用测试入口漏首个workspace参数的设置失败，后续传入原约定参数，不改产品或断言。
- `source-parity.json`核最终文档/证据提交未改冻结运行/配置/打包/随包指南输入；`publication.json`逐一比较远端资产状态/size/SHA256。`public-path-normalization.json`仅规范本机路径并保留原始字节/双摘要，hash字段含义同父目录。

原功能/资源/P2/历史失败继续按父目录各自SHA解释，不把旧日志改名算新运行。公开归档原267项FAIL保留，本轮增量另核；Windows原生/UI/普通账户/ACL/UAC/升级/设备和目标负载/离线仍NOT_RUN/BLOCKED，不能用于现场或商用准入。
