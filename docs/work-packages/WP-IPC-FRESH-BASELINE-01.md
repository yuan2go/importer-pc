# WP-IPC-FRESH-BASELINE-01

起点：main `6a55d9d537fb5365717b67c19d72374a4a52371b`。用户授权按B1→B2→B3清除C01～C20中的自有旧版本兼容；C21～C28保留。上一轮审计未在受跟踪文档中找到完整C编号表，按本次用户清单、先前审计和当前代码逐项复核。新worktree实施，不触碰其他worktree、未跟踪附件、本机安装或运行数据。

所有开发数据可放弃，不需要旧站接管。历史Releases、安装包、证据、回执、PASS/FAIL和合成旧库ZIP保持原字节。当前工具不重验历史介质。任意非当前格式明确拒绝，请使用全新数据目录。保留当前成功、拒绝且字节不变、折叠机制等价三类验证，不削弱Unknown、原件、WAL/FULL或资源守卫。

| 批次 | 范围 | 状态 |
|---|---|---|
| B1 | C14/C15/C18、smoke缺键和两处文案 | PR120已合并：e4dae94c9195793fa3e025c4026b786d0a454622；四项CI通过 |
| B2 | C05～C13/C17/C20 | 当前实现；CI待记录 |
| B3 | C01～C04/C16 | 基线与等价验证已实现；恢复与起停CI通过前不合并 |

## B1源码核查

verify-operation-upgrade.py仍要求已退出的ForwardMigrationCommitted；station.ps1的Upgrade/Rollback调用退休命令，备份恢复独立可保留。smoke-application.py读MaintenanceLogin但当前schema3模板不产生该键；删除它不删除Login操作员能力。StrictSingleCode为当前明确策略，改为“严格单码校验”；管理服务只提示设置当前密码。

C18原指南中classic、旧库升级、MaintenanceLogin及历史资格重验指令与当前代码冲突。原字节保存在pre-fresh-baseline-01，当前指南和随包列表统一。B1无公共签名变更；不为文案改动重发旧包或改变数据库身份。后续版本/契约只在所属批次提升一次。

## 验证、合并及边界

各批在此追加实际源、定向测试、CI和合并SHA，不把历史PASS转记。系统安装、HKLM、SCM、已安装站点及真实设备操作本机NOT_RUN。源码回滚使用反向提交并重新CI；B3数据格式不可通过回退程序降级读取，使用全新目录。

B1本机验证：桌面验证工程Release/locked构建0警告0错误；随包文档3项测试通过；station.ps1语法检查通过；直接执行smoke可选能力实际表达式，三个当前Parcel模板均通过。首次辅助检查误遍历Inspection模板得到Wcs缺键，限定本脚本支持的三个Parcel模板后通过，未修改Inspection。无产品/安装/设备运行。CI及合并待记录。

## B2当前契约收口

C05退休迁移API及孤立收据保存删除；未知CLI命令在任何运行时创建前统一拒绝。C06当前schema3路径直接执行，左右位、称重、图像原件责任不变。C07删除类型转发及旧构造桥；Platform/Supply契约与实现升2.0.0，Application契约与三个业务升3.0.0，受控驱动RCS/海康包装升2.0.0，参考包升3.0.0。外部RCS version1、SDK原生签名不变。所有项目锁文件仅更新本地Project依赖版本，外部包contentHash不变；正式构包继续按实际二进制产生新ABI和包哈希，不改冻结包。

C08观察与称重只暴露Controlled启动端口，驱动/替身/示例同步编译。C09正式发布只接受schema2且必须提供实际业务程序集；无发布登记的源码开发入口保留。C10确认账不再接受旧OriginalEnvelope字段，InitialRepair原件保留。C11当前编辑草稿live-v2完整字段必填，先验证临时模型再替换输入；CAS不变。C12只删C→D旧模拟目录接管；通用备份恢复来源证明和退役围栏是当前正确性机制，核实后保留。C13种子只接受schema3，retainingOriginal夹具改为当前未完成配置并验证旧schema拒绝不写文件。C17只验当前schema3记录/拓扑/能力组合及实际输入字节，旧验收参数和三个旧记录用例折叠为拒绝测试。C20删除Sportmaster旧Workstation及VM声明，两处测试使用共享Shell；另外两个业务页面不变。

源码与审计差异：ParcelMachineOrigin通用恢复证明仍有当前写入者，不能全部删除；InitialRepair与当前不完整种子仍可达。C16两份仅服务退休升级API的测试提前随B2删除以保持编译，其他库夹具在B3处理。发现Product CLI未知动作原来会落入snapshot，已统一未知动作拒绝。新增测试覆盖缺草稿字段、旧确认账字段拒绝且字节不变。

本机验证：业务验证工程Release/locked通过；WPF编译发现缺System.IO引用，已修正后复验。运行时、机器租约、安装/设备相关验证交隔离CI，本机不接触已安装状态。独立扩展及剩余检查结果随PR记录。未处理的相邻标识：LegacyNamedCapabilities文件名和Ipc.Application.Contracts命名空间、DeploymentExtensions旧二参便利构造、ImporterPc.Administrator服务名称；后者是当前安装归属，不是旧路径。

B2本机完成：三个Assembly、Application/Supply/WPF验证工程、三个示例扩展和海康包装Release/locked编译通过；Python发布校验31项通过。WPF测试共享Shell工厂曾返回错误基类，Supply两处调用仍用退休签名，均按当前端口修正后重新构建通过。

B2首轮CI：aqa01-boundaries与p0-stability-boundaries通过；current-contract-boundaries发现CapabilityClosure仍断言退休五参构造存在，已改为当前六参存在且旧桥不存在，不削弱运行时断言。重新CI待记录。

## B3基线与门禁

实际旧链规范化快照与当前基线对比见[等价证明](../verification/fresh-baseline-01/README.md)。Parcel10/Supply6一次建库，迁移执行器与多代解释删除。当前读者在打开写连接前拒绝非当前身份，缺回包索引明确拒绝且原文/哈希/冲突校验保留。Supply/Parcel维护结果移除退休升级字段，当前责任阻断仍保护备份恢复和包卸载。Platform.Storage/ParcelFlow升2.0.0，Supply升3.0.0，项目锁与组合同步；本批不再次改Application.Contracts版本。

C16不再重建多代完整旧schema，只构造非当前身份输入验证只读拒绝；现有当前恢复、强杀、Unknown和原件责任测试继续执行。新增结构等价、实际WAL/FULL、独立从零运行时起停验证。历史ZIP、回执、发布和冻结证据未修改。`platform_migrations`/`schema_history`名称保留以避免无关消费标记改名，但仅含一条当前基线身份，无迁移步骤。

B3本机定向验证：Application/Supply/WPF验证工程locked构建通过；纯结构及缺身份表及畸形格式登记拒绝16条与Python校验31项通过。CI增加当前基线/起停和缺索引拒绝用例；main合并后自动跑同一关键套件及完整Solution构建，避免在本机复用已安装机器状态。
B2第二轮定向CI发现CapabilityClosure合成夹具把同一相机/秤绑定左右两位，违反已保留的位置隔离守卫；已为每位构造独立合成端口，保留原测量、图像与Unknown断言。生产守卫未放宽。

CapabilityClosure同时按当前PLC合同显式查询原请求完成事实后再验证图片释放；提交接受时先断言图片仍保留，生产发送/查询逻辑不变。

B2后续CI核实：ApplicationArchitecture仍启动schema2升级夹具，改为schema1/2/4拒绝且配置字节不变、无建库；CapabilityClosure清单补齐当前角色绑定，使用实际AdmissionId关联测量，并保留未授权/过期会话拒绝业务的断言。受控源排入观察证据不等于实体准入。Windows发布遇CI临时磁盘设备不存在错误，重试验证，不降低发布检查。

B2原报文断言按当前管理授权读取完整投影；普通操作员投影继续隐藏原payload/提供者参数。业务登录及未授权管理测试保持独立，未放开生产权限。

随包文档复核另发现21份当前指南仍含旧数据库或升级指令，已按当前基线同步并保存来源提交的原字节/SHA；独立回执与历史PASS文件不改。批量锁更新误包含的3份历史probe锁文件已恢复起点原字节。
C10写入端复核：当前确认来源只有Seed，启动资源成功自动确认；删除不可达的AwaitingMaintenance、ConfirmConfiguration命令和旧账手工确认页面。所有原账读取复用严格当前确认身份校验，InitialRepair原件和启动失败/中断恢复仍保留。补测退休状态/字段拒绝且原文件不变、退休命令统一拒绝。

CapabilityClosure崩溃恢复夹具补上当前要求的中断输入责任核查后才继续原请求；不解除生产守卫。C20共享Shell测试不再检查已删除Sportmaster旧Workstation中的条码控件，保留共享导航、布局和当前SupplyStation业务界面检查。

B3 CI复核补齐：启动维护备份在创建目标前验证当前格式；旧库拒绝保留原字节。数字请求身份夹具保留原回包文件，避免用不完整副本掩盖编号归属检查；缺身份索引直接验证仓库启动门禁，不伪造机器来源。两个介质验证脚本只验证当前提交构建的介质，删除独立历史夹具参数并同步非当前格式拒绝夹具。

当前随包说明二次扫描补齐管理模式、作业日志和参数首配三份原件归档；删除旧账号转换、MaintenanceLogin与旧首配接管指引。原有冻结回执和PASS不改，当前操作规则与本次源码一致。
