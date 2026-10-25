# 生命周期优化 · 验证与交付边界

开工 main `3a5b1d13`，规划原件提交 `4c42f567`。本批测试使用 D 盘独立合成数据，真实 Application、SQLite、Windows WPF/HWND/Dispatcher；设备、身份预检和外部服务按夹具明确模拟。工程提交`8488b1ce`；启动说明同步提交/冻结源`36fb420e8ca9b0f85bb6020418298ceb22328f39`。最终证据见下表及[evidence清单](../evidence/lifecycle-ux-optimization-01/FILES.json)。最终报告提交与冻结产物分别计证。

## 必要验证

|范围|入口|结果|
|---|---|---|
|原操作、完整历史、日志资源、备份和到期维护|`dotnet run --project tests/Ipc.Application.Verification -- . --lifecycle-ux`|PASS；153原实体/四页、实际CSV/取消、日志句柄、备份、维护成功/中断/丢终态|
|原生历史、权限到期/返回、停止失败和生效读回|`dotnet run --project tests/Ipc.Desktop.UiVerification -- . --lifecycle-ux`|PASS；实际权限到期解释/原页返回、失败停止、真实B读回暂停、原请求A；卸载三状态原生呈现|
|安装原状态/未知回执、首配键盘和撤权|`--installer-first-run-ui`|PASS（具体日志见evidence清单）|
|日志分页/上下文/取消/两种导出及到期|Application `--management-mode`、UI `--reliability-logs`|PASS（具体日志见evidence清单）|
|PR107/首配共模|UI `--parameter-guidance-startup`|PASS（具体日志见evidence清单）|
|固定清单/占用/保留额外文件|Verification `--commercial-removal`；Application `--installer-change-guards`|PASS（具体日志见evidence清单）|
|共享契约|Sportmaster、DirectPlc、WcsToPlc 装配构建|PASS（具体日志见evidence清单）|
|当前指南、依赖库存和启动|release_documents 定向测试、同源候选及启动冒烟|PASS：当前指南/库存及冻结发布DLL原生；普通CLI成功启动BLOCKED_ENVIRONMENT，见下文|

## 明确保留的门槛

G01：RCS 原合同没有权威结果 Query/获批准人工裁决、去重和实体责任政策。可读交接不清偿 Unknown，也不解锁重发。G02：没有获批准的管理密码恢复政策，保留决策入口；没有默认密码、删账重新首设或 Windows 权限后门。二者继续 `BLOCKED_EXTERNAL`。

本批不控制设备，不实际安装/卸载当前机器软件，不执行真实 UAC、系统登记往返、重启删除、SDK/RCS、目标工控机触屏、24h/72h或现场验收；这些为 `NOT_RUN`/外部待验。未改历史删除算法、SQL/索引或完整维护预算，不重复大库资格测试。旧 D/RV、FAIL/PASS、介质及 manifest 保持原身份。

## 本轮发现与修正

失败原件与最终PASS分别保留：首次到期维护回执目录不符合原恢复集允许清单，改沿原32位身份目录；丢终态核对持有自身SQLite会话导致WAL读取失败，改在核对原指纹前释放；G02政策长文挤出首次设密视口，改为既有密码部署的可展开政策；新增日志日期断言起初期待不存在的提示文字，改为实际“尚无有效查询快照”，双导出禁用及真实日期/时区断言完整保留。未删断言、跳过关键测试、吞异常或放宽业务守卫。

原生DPI实测1.25，标准1366×768、最小800×420客户区；实际HWND/Dispatcher、焦点、键盘及滚动与操作分别计证，不代表改变OS DPI矩阵或触屏实测。日志上下文401条原记录约388万字符，呈现65536字符并保留完整导出说明；新增原生日期/快捷范围/UTC偏移绑定与条件变化后的两类导出禁用验证通过。对应后端管理模式测试覆盖真实来源、上下文、固定选择、完整/时间范围导出、撤权、取消和原子文件行为。

本批只执行受影响定向验证及三产品装配构建、同源候选/启动冒烟；不会因旧测试列表存在而全量重跑大库和长期测试。源码冻结和候选之后无新源变化时不重复已通过的全量套件。

## 冻结、实际候选与启动

|对象|身份/结果|
|---|---|
|前置清理/原规划提交|`4c42f567`，业务编码前保存原件/过期指引处置|
|独立工程提交|`8488b1ce005439ba5473685ec09125b6b86779e9`|
|最终冻结源|`36fb420e8ca9b0f85bb6020418298ceb22328f39`，仅同步生成启动说明/增加随包引用断言；未改前述已验证业务代码|
|portable ZIP|18756068 bytes；SHA256 `e987560c5aa8958c1f000dbdfae4e8210cfffa31c0e203fe8a19675287ff93e0`；110库存文件|
|Windows x64 ZIP|134213218 bytes；SHA256 `fbf1a6b965f1e49931cc702bed15cb6811adf8d6c84fd94150c6809abc54f127`；741库存文件|
|最终版本|SM1.13.0 / Contracts1.19.0 / DB9；DirectPlc及WcsToPlc1.7.0仅受影响装配构建，不声称制作了其新介质|
|库存/当前指南|两个目标所有SHA256逐一通过；当前入口存在；生成启动说明不再引用已移出的旧UI手册；编译合同/组合/依赖一致|
|实际Windows EXE入口|`Ipc.Desktop.exe --environment-report` exit0，.NET10.0.13、Win11 x64；只读环境入口，不创建业务实例|
|冻结发布DLL原生日志|`verify-commercial-wpf.ps1 -ReliabilityLogs` PASS；SourceCommit=FixtureSourceCommit，二进制散列在RESULT；真实HWND/Dispatcher/SQLite，明确合成身份/端口|
|冻结发布DLL生命周期|复用上述同源NativeMedia工程，`--lifecycle-ux` PASS；原生配置B读回/暂停/原请求A、停止失败/重开、到期同页恢复及卸载三状态；最终截图另存，未覆盖前一源图|
|普通CLI业务启动|**未通过/环境受阻**。portable collector 20秒未取得Started、exit1、空stderr；原FAIL和事件保留。随后只读RequireRuntimeIdentity返回缺维护安装的machine runtime policy及指定账户。没有为测试继续安装/改策略；不把夹具启动当作正常机器资格通过|

产物仅在本机`D:/deliveries/lifecycle-ux-20261025-36fb420e/`，未上传ZIP/安装器，不是公开发行或现场交付。`8488b1ce`先前portable仅保留其原源身份，不混入最终候选。最终文档与PR头可后续提交，不能倒写两个候选的SourceCommit/manifest。

实际命令先加载`. ./scripts/project-environment.ps1`：

```powershell
python scripts/package-application.py --application sportmaster --target portable --output D:/deliveries/lifecycle-ux-20261025-36fb420e/sportmaster-portable
python scripts/package-application.py --application sportmaster --target win-x64 --output D:/deliveries/lifecycle-ux-20261025-36fb420e/sportmaster-win-x64
./scripts/verify-commercial-wpf.ps1 -Media D:/deliveries/lifecycle-ux-20261025-36fb420e/sportmaster-win-x64 -Output D:/DevCache/importer-pc/lifecycle-frozen-native-36fb420e -ReliabilityLogs
$env:IPC_FROZEN_DESKTOP='D:/deliveries/lifecycle-ux-20261025-36fb420e/sportmaster-win-x64/desktop/Ipc.Desktop.dll'
dotnet D:/DevCache/importer-pc/lifecycle-frozen-native-36fb420e/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/DevCache/importer-pc/lifecycle-frozen-native-36fb420e/workspace --lifecycle-ux
```

普通CLI collector失败命令及配置原件记录在失败JSON/事件中；本机策略不足时不再次调用可能进入首次运行维护的普通入口。只读策略诊断是观察结果，不证明collector失败的更细分进程原因。正常非提权业务启动、系统往返和目标资格须具备获授权安装环境后单独完成。其他独立工程已完成，G01/G02继续BLOCKED_EXTERNAL。

应用层定向/管理/安装守卫、原生首配/参数/日志、三产品构建、三个指南测试、17兼容测试、5商业交付测试与1发布构建测试均通过；全部保留原命令/日志。没有全量、大库或长期重跑。原始证据及失败均有单独哈希，不改写旧介质和历史D/RV。
