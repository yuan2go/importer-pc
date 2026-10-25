# 生命周期优化 · 本机定向证据

开工main 3a5b1d13，规划原件4c42f567；工程冻结/候选身份见[验证报告](../../validation/lifecycle-ux-optimization-01.md)。本目录仅公开独立合成数据的日志和原生PNG；不含数据库、DPAPI凭据、生产材料、完整配置、二进制或四份受限附件。

Windows 11 Home 10.0.22631，.NET10，Windows DPI1.25。HWND/Dispatcher/SQLite/文件是真实实现，设备/运行身份/外部端口在夹具中明确模拟。测试通过范围以每个日志断言为准；UAC、系统安装卸载往返、真实重启、SDK/RCS、目标IPC触控、长期和现场NOT_RUN。G01/G02继续BLOCKED_EXTERNAL。

`directed-before-recovery-fix`、`directed-wal-read-failure`、`first-password-layout-failure`、`log-range-test-expectation-failure`是修正前原件；最终PASS分别在directed-final、installer-first-run-ui和reliability-logs。最后一项为新增测试对提示文字的错误预期，双导出禁用和真实日期/时区断言保留。

源码文件SHA清单记录本机验证对应的实际工程输入；同源候选从后续冻结commit构建，SourceCommit不与规划commit或最终报告commit混用。FILES仅校验本目录原字节，不重写历史验收。

## 冻结候选

`frozen-36fb420e/`为同源portable及Windows候选、实际WinExe只读环境入口、冻结发布DLL原生两套夹具及最终截图。NativeMedia工程由同commit源码和候选DLL生成；复用该工程运行--lifecycle-ux，没有改业务/SDK守卫。

普通portable CLI冒烟没有取得Started，20秒后collector记录FAIL/exit1/空stderr；随后只读RequireRuntimeIdentity确认本机缺维护安装的运行策略。原FAIL/事件保留，不改成PASS；未为测试安装/改策略或继续正常业务启动。冻结DLL夹具的明确合成身份资格不替代该门槛。失败JSON的Checks是计划检查，不表示各项通过。
