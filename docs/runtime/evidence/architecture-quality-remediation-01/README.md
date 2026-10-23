# AQA01 本轮可复核证据

[结果/命令矩阵](RESULTS.json)、[冻结产物清单](package-inventory.json)、[7份独立接收端执行账索引](peer-executions.json)。实现SourceCommit=7024f1e0128a4d8cf6af5ec6b51d6100a22c5d62；验证驱动fe8d9dd5a11c40f15777e4b658a624512f8a7af5。最终文档/证据提交不改产物来源。

* commands：实际命令、cwd、UTC时刻、退出码；logs：完整逐次输出，保留失败。
* combination-*：最终schema3组合原索引及事件原件，三项均独立重验。
* peer-ledgers：7个真实参考端原JSON，每个Executions=1、冻结Request完全匹配；上传原PNG在D盘，以hash核对，不复制口令账/运行数据库/二进制/图像。
* prior-events：早期事件保持原字节；早期结果索引与命令回执曾同名覆盖，最终新记录由真实重跑产生，没有重建或改标旧索引。
* native-entries/RESULT：实际自包含EXE与桌面环境报告入口；完整native stdout仅在D盘原目录保留。未创建原生业务窗口。
* run.ps1、verify-media.py、verify-native.py是本机复现命令脚本，使用记录中的D盘隔离路径；当前仓库测试/打包入口见验证记录。

SHA256SUMS覆盖本目录所有提交材料（不含自身）。SourceCommit及哈希用于可核验身份，不证明真实设备或Production资格。SQLite、loopback、CLI、UI投影、Windows目标构建及native启动分别计证；安装/UAC/保护Store修改、实际窗口和现场未做。原只读审查完整产物仍在D盘独立目录，未修改。
