# SM 页面规格24验证证据

最终代码 SourceCommit：`c1a50a3976b5dbb4089306b74f4effb78722d46a`；main 基线 `9284a32e5633df8c1ff0208c7a94e253328fe7a8`。本目录全部最终构建/回归/发布汇总均明确指向此源。之后的提交只补文档、证据与清单，不转记历史资格。

|项目|结果|
|---|---|
|Release构建|3项，零警告、零错误|
|Application回归|12套，521 PASS|
|原生WPF回归|13套，551 PASS|
|合计|25套，1072 PASS|
|发布|framework-dependent、no-build/no-restore；实际EXE `--environment-report` 退出0，InformationalVersion匹配SourceCommit|
|Windows|本机原生HWND/Dispatcher、125% DPI；1366×768、1092×614、800×420 DIP|

复现：在干净的上述源提交执行 `scripts/verify-sm-ui-spec-24.ps1 -Section Core`、`-Section Native`、`-Section Publish`。每条命令、退出码、断言数与原始日志见三个 summary JSON；程序集及发布DLL SHA256见对应哈希/发布汇总。

最终覆盖隔离无业务副作用、完整候选热生效/待重启、续期/原票据期限、结构化日志查询、同短事务列表/汇总、20帧真实ZXing软件识别与SQLite、同帧原图/原请求、DB11升级/WAL、取消/权限/资源排空、相机预览生命周期、配置编辑/保存、三个尺寸与格口实际44DIP/主按钮64DIP/紧凑历史三行、安装阶段/卸载未知结果和离线场景底栏。

中间失败独立保留，不计入最终PASS：`intermediate-70488950-chute-failure.log` 是源70488950的实际格口缩放退化；`intermediate-uncommitted-history-reserve-failure.log` 是基于70488950的未提交开发树，字体已恢复但记录空间不足。最终相同原断言均通过，未放宽字号/按钮/行数。`historical-camera-debug-interface-mismatch.log` 来自未提交开发树对已淘汰单Image/ListBox界面的旧测试，不作为现行契约；旧脚本保留，当前按轮及预览生命周期套件单独通过。中间Source变更导致脚本停止的部分运行只在本机保留，不合并统计。

所有数据与部署回调均为合成；没有修改现场配置或部署。未执行真实海康物理连接/客户RCS、受保护安装/UAC/重启卸载、目标IPC、物理触摸/扬声器/PDA、现场长稳，均为NOT_RUN。P07/P08页面改造延期。CI保持停用。不得将这些软件证据扩张为历史D批准/RV关闭或现场验收。
