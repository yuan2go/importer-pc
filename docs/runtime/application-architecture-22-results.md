# WP-IPC-APPLICATION-ARCHITECTURE-22 · 交付结果

基线main **ea881293c888a824f26c9f9fa07352a22786206b**，执行和交付前均核对无开放PR、main未移动。独立工作树application-architecture-22，开发分支codex/application-architecture-22；主目录未跟踪客户资料与其他工作树未修改。本包不合并main。

业务编码前的文档收敛提交 **f670df7**；最终运行/配置/打包源码 **8981d54a3dcb45aa08ea8953f1dd9d4cd2f079f7**。后续只补本报告、清单、证据和PR回执，不改运行闭包。[现行设计](../design/application-architecture-22.md)、[实际运行/配置/升级](application-architecture-22.md)、[冻结输入及日志](evidence/application-architecture-22/README.md)。

## 实现及变更理由

|范围|交付与原因|
|---|---|
|职责与装配|继承三个独立Flow/Application及构建选装根；Sportmaster声明left/right两位、两reader，DirectPlc/WcsToPlc各main单顺序位。平台新增通用ObservationSession/秤注册协议，Application拥有拓扑准入和完成规则；驱动/组件拥有设备及外部协议。没有复制左右Application或引入流程引擎。|
|独立运行|每位有限队列/消费者，共享短事务锁，外呼释放锁。设备独立epoch/sequence/健康；各能力独立有预算的健康任务，慢位/慢健康不占另一位输入。位级Unknown、重量、断连及持久过载隔离；共享WCS/存储/暂停/总容量仍阻相关共享路径。|
|冻结与恢复|T1保存PositionId、实际ReaderBinding、Acquisition、操作员、完整有效配置及实现hash。T2/T3及原接收发生保留，WCS接受/格口与原PLC意图仍同事务；Unknown只查原请求，不换ID或重发。PLC接受不是物理完成，顺序位保持屏障。|
|可选能力|称重关闭/启用/失败分开，schema3秤注册epoch及sequence另有围栏，测量必须是原件。取图可不绑定Uploader；BeforeMain/Attached原策略继承。reader暴露受控原采集能力时工厂复用同一物理会话；无能力拒绝另建连接。仅取图终态/安全取消先持久释放再删原件，未知采集/上传保留。|
|资源|多设备工厂在身份/hash检查前接管资源；部分构造清理逐件尝试，异常保留包/设备租约。部分启动失败、Stop超时和在途重连保持责任；所有源/位任务/附加调用排空后才释放共享能力、包、库和租约。|
|配置与UI兼容|默认/可选配置完整schema3；设备、位和服务绑定同一CAS/Pending/Effective。API禁止降为单Reader；旧UI遇新结构明确只读并在保存方法拒绝，未新增页面/图片区域/预览/DirectPlc UI。三个原有WPF组合必要兼容构建通过。|
|升级与扩展|保留SQL1/2源字节和checksum，追加数据库3；数据库1/2有任何未结件/原图/上传/回执时迁移前拒绝，匹配旧发布清偿后升级。schema2不猜左右，完整schema3候选显式CAS和重启激活。Application.Contracts/能力示例1.4.0，观察/旧集成示例新1.0.9，精确契约重新构建；原包/历史身份不改。|

Sportmaster两个独立投递位已确认；旧Supply共享箱、数量与AGV规则只保留于原业务范围。本包没有把新模拟通过倒写为旧HTML修复或D/RV关闭。

## 实际验证

固定SDK10.0.402，核心构建0警告/0错误。完整命令/范围/状态见[checks](evidence/application-architecture-22/checks.json)，下列为定向参考验证，非现场资格：

- AA22：60条断言及完成标记，走真实Application/ParcelCapabilityFactory入口；包含双位实际交错、同码不同实体、错绑/非法共享、单侧重连/旧事件、局部/共享故障、顺序PLC屏障、完整默认/可选配置、登录/重量围栏、仅取图/两上传策略、原图清理、安全取消、部分构造/启动、Stop排空、CAS/拒绝降级、schema2→3显式配置激活、SQL2→3未结拒绝及真实备份恢复。
- AC19能力、AC20可靠性和BHR备份/健康：分别49/50/57条PASS日志行，原件/独立回执/健康取消/Production拒绝/实际ViewModel兼容守卫保持。原17资源组也完成定向复验；具体源码阶段以日志及checks区分。
- 新不可变能力包1.4.0：独立TestOnly签名，静态检查、实际安装、发现、绑定和真实Application的认证/称重/采图/上传/WCS/PLC及原查询激活通过。冻结Debug检查宿主公共DLL与实际验证宿主逐字节一致，Release候选未修改；不冒称该Debug包与Release候选通用，也不代表生产批准。
- 扩展构建工具14项、随包指南2项、PR20/21签名/证据格式回归通过。只运行受影响组，没有重复18全24组合或所有历史候选。
- 三个portable ZIP及一个win-x64 Sportmaster ZIP实际生成、独立解压并逐文件核SHA。五条portable进程旅程通过：三默认主链（Sportmaster实际两reader），以及WcsToPlc前置/Attached两条独立HTTP接收进程，保留原操作人、Unknown上传查询、单次执行和各自完成语义。
- Sportmaster安装包内原WPF构建及DirectPlc/WcsToPlc原页面兼容构建通过。Windows实际启动、截图、交互、PowerShell/UAC/ACL均NOT_RUN。

初始编译的可空检查失败、首版新测试错误期待“未结时可切换账号”、旧迁移夹具遗漏新版本3的真实FAIL日志均保留。账号保护原实现正确，测试改为验证拒绝及原身份保持；迁移夹具恢复真实schema1结构，未删原checksum/拒绝断言。未用删断言、跳过关键回归或吞异常换PASS。

## 候选与运行

候选均本地保留，未发布为已验收Release。完整身份见[candidates](evidence/application-architecture-22/candidates.json)：

|候选|ZIP SHA-256|
|---|---|
|sportmaster-portable|504609fae1daa723db05f5fd1fdd5fb9a52b45bdbae98a9eff6d42637842ad40|
|direct-plc-portable|4ad8dc55e04cfe58b67d5a73588b8c8cd67175f684b75963c7d97a4987432556|
|wcs-to-plc-portable|4d537bdf4a386781d754259dd48b827a011f9b3a9c5895ef233546d974f8275a|
|sportmaster-win-x64|998a837a41d6df26ed140179caf17c8a57df5a5433dceffdb90e54bfb99d0b00|

本地根：`$CODEX_HOME/artifacts/ipc-application-architecture-22-8981d54/`。每包含实际源码SHA、配置、SHA256SUMS、APPLICATIONS.md/ARCHITECTURE-22.md及其依赖指南；win-x64含实际部署保护脚本。原包/旧报告/SQL不覆盖。

```sh
dotnet /new/unpacked/app/Ipc.Assembly.dll --config /new/unpacked/app/config --data-root /new/isolated/runtime
# 每行snapshot或应用JSON命令；位输入使用EmitReferenceAt，见APPLICATIONS.md
```

## 剩余门禁和公开范围

**NOT_RUN**：Windows实际UI、DPI/触控/焦点/双窗、声音、PowerShell/UAC/ACL、安装实机启动。**BLOCKED**：真实SDK、现场认证/WCS字段和合同、PLC地址/编码/握手、秤/图像真实实体关联、离场/复位/共享联锁和现场验收。Positions.Admit只表示软件准入，不伪造安全许可或离场信号；Production缺必需真实能力/独立资格仍拒绝。

只交获授权通用源码、合成配置/证据及候选身份；不提交二进制、私钥、客户原稿、生产数据、SDK/字体或私密链接。资料校验保持267项历史FAIL（265项原PPTX媒体类型、2项原UI元数据），与PR20/21原错误集合完全一致，新增文件/hash/链接错误0。首次系统Python缺Pillow以及清单/本报告本地路径检查失败另存原输出，改用已有依赖运行时并修正新文档/清单后复验；不修改原资产或豁免换PASS。原REQ/BR/CAPI/D/RV及历史失败保持。

交付[PR #22](https://github.com/yuan2go/importer-pc/pull/22)已创建并读回OPEN，创建时HEAD16591f7；未合并main。随后仅补本回执和manifest，运行/打包闭包及候选仍固定8981d54。
