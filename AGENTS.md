> 2026-10-28 最新执行口径以 [MVS-SOFTWARE-READER-01](docs/work-packages/WP-IPC-MVS-SOFTWARE-READER-01.md) 为准：MVS采图＋同一ZXing-C++软件识别引擎进程内接入，逐次识别结果交RCS去重；本轮RCS按用户要求由本机Python模拟。此前MvCodeReader默认路径及通过记录保留原验证范围。

> 2026-10-28 用户确认同机旧客户端可读码，当前复用入口改为 [MVCODEREADER-RESTORE-01](docs/work-packages/WP-IPC-MVCODEREADER-RESTORE-01.md)：沿现有 MvCodeReader SDK 条码回调接入新版 Application。用户后续纠正现场没有 PLC，电脑直接连接相机；默认连续采集，每个独立识别结果均上报 RCS，由 RCS 去重；不是 PCS。先前外部触发默认仅保留形成时范围。旧仅 MVS 采图约束保留历史范围；实际识码与现场验收分别记录。

> 2026-10-27 单相机作业、显示整理及固定根密码恢复以[STATION-SINGLE-CAMERA-RECOVERY-01](docs/work-packages/WP-IPC-STATION-SINGLE-CAMERA-RECOVERY-01.md)为准：一台就绪相机可服务对应位置；恢复密码只用于重置，原管理授权随重置撤销。旧全连接阻断/G02未定政策保留历史范围；MVS与现场边界继承。

> 2026-10-27 用户再次纠偏以[内置MVS安装与参数](docs/work-packages/WP-IPC-BUNDLED-MVS-CAMERA-01.md)为准：SM相机只使用内置SDK，首配与日常均显示SDK参数，安装软件即带运行依赖；下文独立驱动安装/TCP切换要求保留形成时范围，不再作为当前用户流程。真实识码和现场资格边界不变。

> 2026-10-27 用户最新纠偏以[SDK入口与单一相机编码](docs/work-packages/WP-IPC-CAMERA-SDK-SINGLE-CODE-01.md)为准：每台相机只有一个编码，取格口与心跳共用；SDK通过IP连接、不使用TCP端口；正式启用需预期物理序列号，模拟/独立诊断可省略。下文旧分开编码说明保留形成时范围，原请求与历史证据不改写。

> 开发阶段版本规则（2026-10-27）：基座、SM业务模块、驱动和组件的自有产品版本统一固定为 `1.0.0`。开发期间的功能修改、问题修复、提交、构建和打包均不自动递增版本；只有用户明确要求调整版本时才变更。程序集、装配/提供者声明和安装包继续保持版本一致，不同开发构建使用 `SourceCommit` 和文件 `SHA256` 区分。第三方依赖/厂商SDK版本、外部协议及数据格式编号按实际合同保留；历史介质与证据不改标。

> 2026-10-25 用户纠偏以 [PARAMETER-GUIDANCE-STARTUP-01](docs/work-packages/WP-IPC-PARAMETER-GUIDANCE-STARTUP-01.md) 为准：仓ID、生产线ID；供包台编号由同一候选明确统一到取格口与心跳；新站日志/已结历史默认7天；增加本机窗口启动模式；相机/RCS可明确延后配置，待配置能力拒绝业务。下文旧分开编号与启动必填条款保留历史适用范围。

> TYPOGRAPHY-01：现行字体与字号以 [字体规范](docs/design/typography-01.md) 为准。用户授权离线交付官方 OFL Noto Sans CJK SC Regular/Medium；只替代此前系统字体/不交付字体限制，其他受限资产不变。

> 2026-10-22用户纠偏优先：管理能力改为[管理模式](docs/runtime/management-mode-01.md)，不是管理员账号；本轮见[MANAGEMENT-MODE-01](docs/work-packages/WP-IPC-MANAGEMENT-MODE-01.md)。旧账号说明及SM1.10.2证据保留历史适用范围。

> SM1.10.2历史软件管理员、导航和日志实现见 [OPS-ADMIN-LOGS-01](docs/runtime/operator-administrator-logs-01.md)。本轮已删除帮助与支持，替代旧维护认证/导航描述；四类参数和原请求责任不变。历史介质与验收保持原身份。

本机用户要求全部项目文件、安装与运行数据使用D盘。Windows开发命令先执行 `. ./scripts/project-environment.ps1`，将临时文件、dotnet用户目录及NuGet缓存保持在D盘；不改全机环境。受保护部署使用管理员配置的HKLM存储根，不以链接绕过权限检查。仓库外原稿与账号/现场数据只迁移，不加入Git。

# Codex / Agent 工作约束

1. 从最新main读取 `docs/INDEX.md`、本任务工作包、`docs/design/framework.md` 与 `framework-decisions.md`，再读相关代码、D/RV及公开范围。先核验工作区和并行PR，不覆盖他人修改。
2. 按任务范围执行：BASELINE-01仅归档、FRAMEWORK-01仅设计文档；收到RUNTIME-03实施任务后可以开发，不把旧“仅归档”限制扩大为永久项目禁令。编码前清理本包相关过期当前指引，已正确内容不重复重写；原始要求及历史证据保留。
3. RUNTIME-03已新增F-02独立参考工程，当前实现与验证见docs/runtime/。优先核验并继承新取得的获授权既有C#实现，不并行重建第二套框架；不伪称旧现场迁移完成。目标设计、当前实现、模拟验证、真实接口和现场验收分开。
4. 基座提供机制，业务拥有状态和命令守卫，UI只调用应用契约，驱动/外部适配器实现端口。一个业务实例可服务多个页面/布局；不因页面切换重建连接、作业或发送器。不引入无依据微服务、万能事件总线、PLC直控或传图平台。
5. 分清观察、实体准入、作业、业务请求、发送尝试与接受事实。同码不等于同实体；识别、保存、发送、接受、物理完成不能互相代替。恢复只解除允许解除的阻断，不盲重发未知请求；旧请求固定原报文、合同及目标版本。软件暂停不是安全急停。
6. 真实SDK/合同/实体/数量/权限不足时，完成不受影响代码及显式模拟验证；Production缺必需能力拒绝业务，不静默回退成功。未经授权不控制设备；不编造SDK方法、客户规则、批准人或已支持接口。
7. 保留REQ/BR/CAPI/D/RV编号。D批准与RV状态按证据更新，历史issues和原验收记录不倒写。新实现的局部验证不能直接宣布旧HTML已修复或所有历史RV已关闭。
8. 必要测试以本包风险为准：适当基线、受影响模块、关键恢复/防重/资源验证、必要构建与发布启动冒烟。实际修复相关失败，区分过期测试和环境限制；禁止删断言、跳过关键测试、吞异常换PASS。未做Windows/SDK/现场验证不得声称通过。
9. 公开范围限制持续有效。不得上传未获许可客户原稿及衍生资产、私密访问链接、凭据、生产数据、SDK/字体文件；脱敏不自动等于许可。不自行改变仓库可见性。使用获授权新通用规格与合成数据，不从问题表补造完整客户要求。
10. 同步真实实现路径、配置/依赖/部署说明、manifest及验证边界。业务修改在开发分支提交并创建main PR，遵守保护规则，不强推；文档任务按当次授权提交。报告明确已验证、待联调、受阻与未完成，附实际commit/PR及运行方式。

## UI实现必须读取的实物基线

参数页从2026-10-20本轮用户纠偏起采用 [PARAM-UX-02](docs/design/parameter-settings-experience-02.md) 和 [参数设置V3](docs/ui/parameter-settings-v3-20261020/README.md)。它们替代下述V2的参数布局、保存按钮组织及“维护与迁移”入口；四类归属、原业务合同、原型/原生/现场证据边界继续有效。先读本轮决定再改参数代码，不重复实现PR74已完成的响应字段或重写历史证据。

Sportmaster 作业、首次启动和既有任务合同的实物基线为 `docs/ui/sportmaster-task-redesign-v2-20261020/README.md`。修改页面前实际查看 V2 交互原型、全部界面、HANDOFF、当前字段映射及原 58 项字段/状态和 39 项操作的迁移去向，再读 `docs/design/sportmaster-task-redesign-01.md` 与 `docs/work-packages/WP-IPC-UI-TASK-REDESIGN-01.md`。V2 参数分为系统、业务模块、驱动、组件四类；首次配置是五步独立启动流程，不在日常导航中；左右相机框固定相机、扫描、任务三组信息。用户已确认的字段归属和新增 RCS 响应字段通过必要契约补齐；原型示例值、版本号和模拟成功不能成为生产默认或验收证据。

旧 `docs/ui/sportmaster-task-redesign-20261020/`（V1）、`docs/ui/ss-ui-vis-001-v1.1/` 及历次视觉方案保留为历史参考，不再是本包并列页面主定义。编码前先清理相关过期当前指引及冲突字段归属，修正索引和替代关系；原件、编号、原哈希与历史证据保留，已正确内容不重复改写。四类页面仍编辑同一完整候选，复用唯一配置账、Application 守卫及原请求责任，采用工业级完整任务闭环，不选择最小演示实现。必要测试覆盖实际改动风险；Windows 实机、原型模拟与真实设备验收分别记录。

2026-10-21核对main `42a047c3`：PR78参数V3及PR79启动回退已合并，当前续做以 [现有工作包的最新核查和Codex提示](docs/work-packages/WP-IPC-PARAMETER-SETTINGS-02.md) 为准，不再将开发分支待合并当作现状。`bf61337`保留为V3形成时整合基线；PR69～77及历史发布各守原身份。本包收口PARAM-UX-02，不重复实现V2/V3主体，也不将历史验证扩张为V3验收。修改参数前读 `docs/design/parameter-refactor-01.md`、`docs/runtime/parameter-refactor-01.md` 及上述增量记录，复用参数/首配共模、条码筛选、保存核对、会话修复、启动回退、心跳和安装。用户本轮四类字段归属优先于快照临时摆放；取格口与心跳编码按真实用途保留，不静默互换。历史PASS和介质保留原SourceCommit，不扩张到最新合并树或V2。

## 项目级 UI/UX skill

UI/UX设计、实现与审查可使用 [ui-ux-pro-max](.agents/skills/ui-ux-pro-max/SKILL.md)，安装来源、版本与命令见 [skill说明](.agents/skills/README.md)。当前桌面实现查询使用 `--stack wpf`；通用建议须服从上述实物基线、业务边界和公开范围，不能替代Windows/真实接口/现场验收。

本轮视觉优化同时使用项目级 [Impeccable](.agents/skills/impeccable/SKILL.md) 的 Operate 模式，先读 [WPF适配](.agents/skills/impeccable/WPF.md)；来源/固定提交见 skill说明。网页检测/CDP不用于原生WPF验收，独立 `--stack wpf` 查询不与 `--design-system`混同。
