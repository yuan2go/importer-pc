# 生命周期优化 · 验证与交付边界

开工 main `3a5b1d13`，规划原件提交 `4c42f567`。本批测试使用 D 盘独立合成数据，真实 Application、SQLite、Windows WPF/HWND/Dispatcher；设备、身份预检和外部服务按夹具明确模拟。源码冻结、最终命令、产物哈希和截图清单在收口时填写。

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
|当前指南、依赖库存和启动|release_documents 定向测试、同源候选及启动冒烟|源码冻结后执行，实际产物身份另列|

## 明确保留的门槛

G01：RCS 原合同没有权威结果 Query/获批准人工裁决、去重和实体责任政策。可读交接不清偿 Unknown，也不解锁重发。G02：没有获批准的管理密码恢复政策，保留决策入口；没有默认密码、删账重新首设或 Windows 权限后门。二者继续 `BLOCKED_EXTERNAL`。

本批不控制设备，不实际安装/卸载当前机器软件，不执行真实 UAC、系统登记往返、重启删除、SDK/RCS、目标工控机触屏、24h/72h或现场验收；这些为 `NOT_RUN`/外部待验。未改历史删除算法、SQL/索引或完整维护预算，不重复大库资格测试。旧 D/RV、FAIL/PASS、介质及 manifest 保持原身份。

## 本轮发现与修正

失败原件与最终PASS分别保留：首次到期维护回执目录不符合原恢复集允许清单，改沿原32位身份目录；丢终态核对持有自身SQLite会话导致WAL读取失败，改在核对原指纹前释放；G02政策长文挤出首次设密视口，改为既有密码部署的可展开政策；新增日志日期断言起初期待不存在的提示文字，改为实际“尚无有效查询快照”，双导出禁用及真实日期/时区断言完整保留。未删断言、跳过关键测试、吞异常或放宽业务守卫。

原生DPI实测1.25，标准1366×768、最小800×420客户区；实际HWND/Dispatcher、焦点、键盘及滚动与操作分别计证，不代表改变OS DPI矩阵或触屏实测。日志上下文401条原记录约388万字符，呈现65536字符并保留完整导出说明；新增原生日期/快捷范围/UTC偏移绑定与条件变化后的两类导出禁用验证通过。对应后端管理模式测试覆盖真实来源、上下文、固定选择、完整/时间范围导出、撤权、取消和原子文件行为。

本批只执行受影响定向验证及三产品装配构建、同源候选/启动冒烟；不会因旧测试列表存在而全量重跑大库和长期测试。源码冻结和候选之后无新源变化时不重复已通过的全量套件。
