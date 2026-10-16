# DELIVERY-12 受控扩展与Windows候选交付

本报告是当前实现与证据入口。**源码已实现、核心参考验证PASS；Windows参考验收OPEN / NOT_RUN；真实现场资格BLOCKED。整包未验收完成。** 不以PR、交叉编译、HTML图或保护拒绝替代Windows验收。

## 身份与范围

开工及冻结时main均为 `9f320459969ee4398dc602ae037ecdcb3685996c`，与分析基准相同；开工无并行PR。隔离工作树、`codex/wp-ipc-extension-windows-delivery-12`分支实施，原工作区未跟踪的客户材料及用户输入留在原处，未覆盖、未公开。工作包从用户放入docs的原文件逐字节复制到 [正式工作包](../work-packages/WP-IPC-EXTENSION-WINDOWS-DELIVERY-12.md)。

前置文档提交 `7e61323` 在业务改动前保存原始证据到 [pre-extension-delivery-12](../archive/pre-extension-delivery-12/README.md)，更新当前入口与F-04/F-06/F-10，允许受控后装与配置扩展。历史PASS/NOT_RUN、D/RV批准与实际现场缺口不倒写；目标未改为现有代码即正确。

候选宿主源码冻结 `11f0728e506753dafcdc22fb190c5b268118423d`；独立插件源码 `6507edb4c10699df3bdeb8f91da3bf31f81b94f9`（示例不在宿主solution或project引用图）。实现主提交 `b4c88bd`，确认窗身份/版本冻结修复 `ae57856`，独立源码编译目录隔离 `11f0728`。后续报告/manifest提交不改变运行、配置、依赖、打包脚本及包内操作/扩展指南输入。

Release `EXTENSION-WINDOWS-DELIVERY-12`；Storage/MinimumReader=3；业务合同 `SIMULATION-v1`；ExtensionContract=1。三包SourceCodeSha256均为 `ad9a5c5b9c3cab04858b31002c9897887aa1c2bedc9620f7683041c46282ba93`。插件按各宿主公共契约SHA分别编译，不能跨RID混包。所有测试身份/实体/条码均为授权合成参考数据，不调用生产WCS或设备。

## AC逐项判断

命令C1～C8及逐份日志身份见 [checks](extension-windows-delivery-12-checks.json)。下表的PASS只覆盖明确写出的参考范围；OPEN保留实际UI/Windows证据要求。

|AC|状态与实现路径|提交、验证和证据|剩余边界|
|---|---|---|---|
|AC12-01|PASS；INDEX、framework/decisions、归档、完整工作包、controlled-extensions设计增量|7e61323；开工与冻结main/PR核验；归档manifest和 [环境记录](evidence/extension-windows-delivery-12/environment-and-windows-blocker.json)|历史D/RV及未授权原稿范围不变|
|AC12-02|PASS参考组合；`config/compositions.json`、`scripts/composition.py`、`package-reference.py`锁定核心及barcode闭包|b4c88bd/11f0728；C5/C7；[三个候选包精确库存](evidence/extension-windows-delivery-12/candidate-inventory.json)，每包composition.lock与SHA256SUMS回读|Windows产物仅构建及静态回读，尚未运行|
|AC12-03|PASS核心实际链路；独立BarcodeExtension仅引用冻结Ipc.Platform；签名后装到同一Product来源→Supply→SQLite/Outbox/loopback|6507edb/11f0728；C2/C6；[发布宿主实测](evidence/extension-windows-delivery-12/final-extension-smoke.json)，宿主前后完整哈希不变，接受数量2；[独立源码与原请求旅程](evidence/extension-windows-delivery-12/extension-evidence.json)|出现参数的实际WPF视觉/输入仍NOT_RUN；核心schema及实例调用已运行|
|AC12-04|PASS核心；Platform ExtensionPackages/Store + Product ExtensionManagement，先静态验证后执行；单独离线测试信任|b4c88bd/11f0728；C2；[E12日志](evidence/extension-windows-delivery-12/ipc12-11f0728-extensions.log)，10份持久操作、4安装阶段中断、真实子进程Exit73后同ID恢复、坏签名/路径/链接/RID/契约/依赖/配额等负例|Windows ACL/UAC普通账户实测NOT_RUN；无生产根/吊销/厂商SDK资格|
|AC12-05|OPEN（核心PASS、WPF已实现未验）；ExtensionsView标准控件生成分组/嵌套路径/集合/条件/只读/默认/凭据引用，实例设备角色区分、草稿/有效/差异与回执|b4c88bd/ae57856；C2/C3/C1；两实例不同参数、CAS竞争、身份到期与换身份拒绝、确认窗冻结原Revision；[源码映射](../ui/extension-delivery-12-mapping.md)|完整实际表单、键盘/触控/屏幕阅读与敏感参数操作证据NOT_RUN|
|AC12-06|PASS核心；原StationConfig/ConfigRevision统一包/实例/参数/绑定；Product最终选择后拿设备/包租约；Save/Activate到事务提交持锁，卸载独占后重读引用|b4c88bd/11f0728；C2/C3/C4；extension-evidence保留原Unknown请求ID/报文/合同/目标/Attempt；配置竞争、激活卸载围栏、真实迟停与构造/Dispose故障均验证|未把包锁当设备独占；Windows跨会话资源/ACL仍NOT_RUN|
|AC12-07|OPEN（核心PASS、安全UI待Windows）；安装/恢复原操作ID，升级待重启、失败保旧、停用/依赖卸载、核心管理入口；Stop未终止不释放|b4c88bd/11f0728；C2/C3；成功升级/失败回退/解除绑定后卸载/反向依赖/原未决引用与中断退休证据；Windows专属1.1.0升级包另附SHA|同版Windows UI从安装到升级/退回/停用/卸载未运行；不允许回滚业务事实|
|AC12-08|OPEN；PAGE01～08继承实际应用契约；扩展页在同一维护抽屉，关键事实可见；首次测量/Loaded前每窗不足区条件由应用持有，多窗不能互相绕过|b4c88bd/ae57856；C1/C2/C3/C4；[页面映射及待验项](../ui/extension-delivery-12-mapping.md)|实际WPF截图0；分辨率/DPI/客户区及焦点、固定动作、长波次、拆包关闭全部须按06复核；保护拒绝不是正常适配PASS|
|AC12-09|NOT_RUN；提供包内 `windows-reference-validation.ps1`、场景09、Windows06与完整操作/恢复指南|11f0728候选；[环境及阻塞](evidence/extension-windows-delivery-12/environment-and-windows-blocker.json)；核心持续旅程C4仅作参考|W01～06、Windows11普通账户、实际双窗/多轮连续旅程、扫码器/触控/听审/恢复/性能均未取得；OAuth无workflow scope，未执行远端workflow|
|AC12-10|候选交付PASS / Windows验收版OPEN；宿主、离线1.0.0、Windows升级1.1.0、独立插件源码、精确依赖、外层及内层SHA、部署审计和指南齐备|11f0728/6507edb；C5～C8；[预发布](https://github.com/yuan2go/importer-pc/releases/tag/delivery-12-reference-11f0728)、[远端回读](evidence/extension-windows-delivery-12/remote-readback.json)|明确Windows未验候选，非现场放行；7个资产约186MB均已下载，SHA/ZIP精确文件集/内层哈希匹配；无生产签名与现场资格|

## 实现与验证的界线

只扩展现有机制：受信托管观察来源、有限schema、原事务部署修订与维护契约。SDK/系统驱动是不同前置类别；当前不支持原生DLL/PInvoke/任意模块kind或任意schema执行语义时拒绝，未造假SDK接口。ALC只隔离依赖，不是安全沙箱；包信任与管理员维护是代码准入边界。普通运行账户不写包库，受限安装助手从Program Files可信目录执行，Simulation测试根与Production完全分开。

旧请求固定原始报文、合同、目标与配置身份。改端点或升级插件不重投Unknown；需原请求核对，不回滚业务数据库掩盖外部事实。二进制改变默认受控重启。安装只是Installed，只有部署修订验证、终止原所有者、资源取得、来源初始化完成才提交Effective；失败保旧记录ActivationFailed。Stop/构造/Dispose未终止时保留实际数据/设备/包租约，不能抢启第二所有者。来源观察代次/不可沿用旧码与Outbox事务继续由原业务拥有。

按范围执行了构建、扩展全风险、原防重/恢复、E07/E08/E11和发布冒烟，未反复运行无关全量。C4在b4c88bd执行，此后只变更WPF确认令牌、文档workflow模板及独立编译目录；核心Platform/Supply/Product/Adapters/config自b4c88bd至11f0728相同，另在最终11f执行E12和发布冒烟，ae57856执行E11。证据逐份保留真实提交，不把早期运行记到最新提交。

保留失败：首次编译CS9124、新测试CS8602、空安装库枚举、来源Enter异步队列顺序、WCS健康投影CAS竞争；修复后保留原断言并等待真实已持久状态/健康。系统Python缺Pillow导致归档工具测试失败，改用现有捆绑Python/Pillow执行9项PASS，无删除/跳过测试。最终构建0警告0错误。所有失败原始字节SHA及脱敏公开SHA见 [证据身份清单](evidence/extension-windows-delivery-12/evidence-identities.json)。只替换本机账户/临时路径，原文件仍在本机保留。

归档工具本轮按原策略执行，不新增豁免、不降低断言。原main归档检查已有267项失败（PPTX媒体类型及私密元数据标记），须保留负面证据；本轮未新增错误（同一267项集合），文件身份与链接差异见 [归档差异](evidence/extension-windows-delivery-12/archive-comparison.json)。本包不将历史资产问题改写PASS。

## 交付与接手

下载预发布资产，先核对外层SHA，再解压内层SHA256SUMS和composition.lock；远端下载的每个资产均与本地SHA和内容库存对照。候选不是MSI/生产代码签名安装器；使用现有Windows部署脚本完成受保护目录/普通账户设置，实际安装边界必须在Windows验收。禁止直接从可写Downloads目录提权助手。

按包内EXTENSIONS.md及 [扩展开发/恢复指南](../extensions/developer-guide.md) 在隔离合成Windows参考机执行：安装扩展 → 参数/实例/角色绑定 → 保存差异/审核/激活 → 受控重启 → 验箱/双通道/拆包或供包 → Unknown原请求核对 → 关闭/新箱历史 → 重启恢复 → 升级1.1.0或失败安全退回 → 停用/卸载。不要使用本文核心证据勾选Windows动作。W01～06的环境、分辨率、缩放、实际客户区、动作/对象/部署/请求身份、截图、行为结果均需独立记录。验证入口不会把键盘注入写成扫码器、触控或听审。

当前支持矩阵未缩减，关键字号未缩小换PASS。区域不足仅安全阻断并保留核心维护；不能宣称1366分辨率各缩放已正常适配。生产SDK/WCS/权限/实体/数量/声音及现场B01～06依原05/06审批，全部保持BLOCKED/NOT_RUN。无新现场批准、D批准或RV关闭。
