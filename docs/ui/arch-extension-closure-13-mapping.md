# CLOSURE-13 WPF增量与后续Windows场景

原v1.1实物、原HTML/分离CSS/JS、tokens、完整交接及[12映射](extension-delivery-12-mapping.md)保持原字节；34张图仍是HTML导出。**本轮实际WPF截图0；Windows全部WINDOWS_NOT_RUN**，不是交叉构建失败或需要寻找环境的开发阻塞。未缩减06支持矩阵、未移除保护、未缩小字号。

|增量|实现/绑定|后续同版Windows必验|
|---|---|---|
|业务应用边界|MainWindow/ExtensionsView接收IStationRuntime/IStationApplication/IExtensionManagement；App仍是产品装配根|启动NotReady/未登录管理可达；多窗/切页面共用同一RuntimeId及来源/连接/发送器|
|两类实例|ExtensionsView共用模块选择、实例列表、schema分组参数、有效值/草稿；设备DeviceId/Role，服务Services/Ports/Target|两设备+两服务创建/编辑/禁用、模块切换、参数隔离、服务不要求假DeviceId、重复端口/配额/缺能力拒绝|
|服务目标与唯一部署|ServicePort/TargetId/Revision/Contract/Endpoint表单；MainWindow.BuildDraft统一requests目标与Integration|新目标ID/修订/地址一次保存；传统维护表单修改也更新同一端口；预览差异正确，CAS冲突和身份过期不覆盖旧Effective|
|管理与诊断|原PAGE08维护抽屉新增服务诊断tab；真实IntegrationDiagnostic和安装/恢复记录|实际健康/在途/停止/故障显示；加载/失败/只读/待重启/ActivationFailed；UAC取消/ACL拒绝均不得假成功|
|原请求恢复|Reconcile经精确IntegrationRuntime；缺旧包/参数指纹错误明确阻断|换新包/当前目标后查旧Unknown仍原目标/原ID；拒绝缺旧能力和坏回执；只入一次接受账|
|生命周期与卸载|返回对象先接管；未终止Stop/Dispose/查询/T3/审计保留实际资源|在途发送/查询时退出、慢Stop/Dispose、构造失败、升级失败、依赖卸载拒绝和完全停止后成功|
|原PAGE01～08|业务事实/数量/波次/双通道固定动作/拆包/关闭/新箱焦点/历史/报警继续原契约|同版完整旅程；首次Loaded不足区、多窗暂停所有权、长文本、焦点ticket、扫码Enter/Tab、触控/听审、W01～06分辨率DPI与跨屏|

Windows应从13新候选开始留OS/build、账户/交互会话、DPI/实际客户区、宿主/插件来源/组合/SHA及逐动作证据。保护拒绝不能记作正常适配PASS；键盘注入不能代表实际扫码硬件。原12 OPEN/NOT_RUN和D/RV仍由原登记负责，不以本轮代码关闭。

PR12复核增量：`ExtensionDraftEditing`由实际管理页调用；`editedInstance`保留原选中身份，服务与设备重命名均替换原项/绑定，重名或占用端口拒绝，不静默覆盖。显式“新建另一个实例”清除编辑身份并保留其他实例；参数仍为本窗草稿。Mac执行同一纯草稿策略及WPF交叉构建；实际点击/焦点/异步列表回填场景仍WINDOWS_NOT_RUN，详见补充Windows场景。
