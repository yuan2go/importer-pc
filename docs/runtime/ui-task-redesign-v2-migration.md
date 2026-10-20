# V2 实际迁移去向

保留原 S/D/C/U/B、P、A 编号；共54项V2字段、58项原字段/状态、39项操作。表中是实际 WPF 入口，菜单内的维护操作仍走原受控生命周期。可用搜索按字段名称或英文键直接定位。真实合同、SDK和现场资格不由本表授予。

| 编号 | 字段或操作 | 实际入口 | 实现/复用 |
|---|---|---|---|
| S01 | 本地工作站ID | 系统 / 站点标识 / 本地工作站标识（只读） | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S02 | 业务模块 / 运行环境 | 全局页脚 / 运行环境；业务模块 / 当前业务（固定）；独立维护 / 部署身份 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S03 | 区域ID | 系统 / 站点标识 / 区域ID | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S04 | 项目ID | 系统 / 站点标识 / 项目ID | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S05 | 供包台编码（取格口） | 系统 / 站点标识 / 共同取格口站码（旧左右冲突显式迁移） | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S06 | 输入队列容量 | 业务模块 / 处理预算 / 输入队列容量 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S07 | 调用等待上限 | 业务模块 / 处理预算 / 调用预算 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S08 | 未清偿作业容量 | 业务模块 / 处理预算 / 未清偿作业容量 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S09 | 常驻历史条数 | 系统 / 数据与记录 / 常驻历史条数 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S10 | 磁盘保留空间 | 系统 / 数据与记录 / 关键写入保留空间 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S11 | 计划备份文件夹 | 系统 / 计划备份 / 备份绝对目录 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S12 | 备份间隔 | 系统 / 计划备份 / 周期 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S13 | 最多保留备份份数 | 系统 / 计划备份 / 容量上限 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S14 | 会话有效时长 | 系统 / 数据与记录 / 操作员会话有效时长（已绑定时） | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S15 | 配置文件夹 | 系统 / 数据与记录（只读）；独立启动维护 / 本机目录与配置身份（受控修改） | ConfigurationLocations.cs / StartupWorkflows.cs |
| S16 | 作业数据文件夹 | 系统 / 数据与记录（只读）；独立启动维护 / 本机目录与配置身份（受控修改） | ConfigurationLocations.cs / StartupWorkflows.cs |
| S17 | 窗口布局 / 显示偏好 | 更多 / 标准与紧凑布局；维护 / 模板与显示 | SupplyStationWindow.xaml.cs |
| S18 | 每次请求数量 | 业务模块 / 请求规则 / 请求数量 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S19 | 指定目标格口 | 业务模块 / 请求规则 / 指定格口开关 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S20 | 指定格口 | 业务模块 / 请求规则 / 指定格口编码 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| S21 | 启用计划备份 | 系统 / 计划备份 / 启用受控计划备份 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| D01 | Hikrobot ID-MVS | 驱动 / 左相机或右相机 / 安装版本与设备身份 / 已安装类型与精确版本 | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D02 | 配置实例ID | 驱动 / 本位 / 安装版本与设备身份（稳定实例只读） | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D03 | 稳定设备ID | 驱动 / 本位 / 安装版本与设备身份（DeviceId只读） | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D04 | 相机编码（取格口） | 驱动 / 本位 / 取格口业务映射 | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D05 | 相机IPv4地址 | 驱动 / 本位 / 相机IPv4地址 | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D06 | 预期序列号 | 驱动 / 本位 / 预期序列号 | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D07 | 实体关联策略 | 驱动 / 本位 / 实体关联策略 | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D08 | 条码编码 | 驱动 / 本位 / 条码编码 | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D09 | 回调队列容量 | 驱动 / 本位 / 回调队列容量 | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| D10 | 绑定投递位 | 驱动 / 左右对象名称；维护 / 高级绑定与兼容策略 / 原角色（固定） | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| C01 | RCS服务地址 | 组件 / RCS 连接 / 组件连接目标 | ParameterSettingsControl.Capabilities.cs / ParameterEditorModel.cs |
| C02 | 协议来源 | 组件 / RCS 连接 / 协议来源 | ParameterSettingsControl.Capabilities.cs / ParameterEditorModel.cs |
| C03 | 协议语言 | 组件 / RCS 连接 / 协议语言 | ParameterSettingsControl.Capabilities.cs / ParameterEditorModel.cs |
| C04 | 独占编号区间起点（不含） | 组件 / RCS 连接 / 请求编号区间起点 | ParameterSettingsControl.Capabilities.cs / ParameterEditorModel.cs |
| C05 | 独占编号区间容量 | 组件 / RCS 连接 / 请求编号区间容量 | ParameterSettingsControl.Capabilities.cs / ParameterEditorModel.cs |
| C06 | 组件参与当前业务 | 维护与迁移 / 高级绑定与兼容策略 / 库存启用与角色绑定；必需能力由 Application 守卫 | ParameterSettingsControl.Capabilities.cs / ParameterEditorModel.cs |
| C07 | 组件来源与实例身份 | 组件 / RCS 连接 / 安装版本与设备身份 | ParameterSettingsControl.Capabilities.cs / ParameterEditorModel.cs |
| U01 | 当前包裹号 | 作业 / 本位扫描信息 / 当前包裹号 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U02 | 读码来源 | 作业 / 本位扫描信息 / 读码来源 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U03 | 扫描时间 | 作业 / 本位扫描信息 / 扫描时间 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U04 | 本次观察序号 | 作业 / 本位扫描信息 / 本次观察序号 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U05 | 任务号 | 作业 / 本位任务信息 / 任务号 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U06 | SKU | 作业 / 本位任务信息 / SKU | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U07 | 波次号 | 作业 / 本位任务信息 / 波次号 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U08 | 任务数量 | 作业 / 本位任务信息 / 任务数量 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U09 | 目标格口 | 作业 / 本位任务信息 / 目标格口 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| U10 | RCS附加测量信息 | 原作业详情 / 回执语义（值与null原样，不发明单位） | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| S22 | 工位编码（心跳） | 系统 / 站点标识 / 心跳工位编码 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| B01 | 条码筛选规则 | 业务模块 / 条码筛选 / 左右共用条码筛选正则 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| B02 | 本地候选样例 | 业务模块 / 条码筛选 / 本地候选样例 | ParameterSettingsControl.Policy.cs / ParameterOwnership.cs |
| D11 | 相机编码（心跳） | 驱动 / 本位 / 心跳 bcr-code | ParameterSettingsControl.Capabilities.cs / CapabilityEditing.cs |
| C08 | 心跳间隔 | 组件 / RCS 连接 / 心跳请求间隔 | ParameterSettingsControl.Capabilities.cs / ParameterEditorModel.cs |
| U11 | 候选与筛选详情 | 作业与记录 / 本次相机输入 / 候选与筛选详情 | SupplyStationPosition.xaml / SupplyStationViewModel.cs / ParcelViewModel.cs |
| P01 | 维护账号 | 参数配置 / 保存准备 / 维护账号 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P02 | 维护口令 | 参数配置 / 保存准备 / 维护口令 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P03 | 维护变更原因 | 参数配置 / 保存准备 / 变更原因 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P04 | 编辑来源 | 参数配置 / 维护与迁移 / 完整候选配置（高级）；单一编辑来源 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P05 | 配置修订 / 草稿CAS | 查看跨对象修改 / 草稿修订；维护 / 完整候选配置（高级） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P06 | Effective / Pending | 参数顶部草稿状态；维护 / 运行输入与待重启（完整状态） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P07 | 稳定设备ID | 驱动配置 → 本位实例 → 稳定设备标识 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P08 | 原角色 | 驱动配置 → 本位实例 → 业务用途 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P09 | 已安装提供者 / 精确组件来源 | 驱动配置 → 已安装类型 → 实例 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P10 | 实例身份 | 驱动配置 → 本位实例 → 实例身份 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P11 | 完整候选配置（高级）JSON | 参数配置 / 维护与迁移 / 完整候选配置（高级）；单一编辑来源 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P12 | SchemaVersion | 参数配置 / 维护与迁移 / 完整候选配置（高级）；单一编辑来源 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P13 | 维护核对说明 | 参数配置 / 维护与迁移 / 运行输入与待重启 / 旧配置账核对 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P14 | 原字节SHA256 / 原账状态 | 参数配置 / 维护与迁移 / 运行输入与待重启 / 旧配置账核对 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P15 | Providers.Kind | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P16 | Providers.Implementation | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P17 | Providers.Version | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P18 | Providers.Contract | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P19 | Providers.Parameters | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P20 | Requirements.MeasuredWeight | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P21 | Requirements.ImageAddress | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P22 | Requirements.Query | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P23 | Requirements.PhysicalConfirmation | 驱动或组件 / 安装版本与设备身份；维护 / 已安装模块参数（完整描述） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P24 | 能力（新增类型下拉） | 维护 / 能力库存与参数；必需能力、角色与数量仍由Application校验 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P25 | 启用 | 维护 / 能力库存与参数；必需能力、角色与数量仍由Application校验 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P26 | 旧版绑定 | 维护 / 能力库存与参数；必需能力、角色与数量仍由Application校验 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P27 | 已安装提供者 | 驱动配置/组件配置 → 已安装类型选择 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P28 | 实例身份 | 驱动配置/组件配置 → 实例技术详情 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P29 | 协议目标 / 读码身份 | 驱动配置 → 设备身份；组件配置 → 连接目标 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P30 | 合同版本 | 驱动配置/组件配置 → 接口技术详情 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P31 | 称重来源 | 业务模块配置 → 适用业务策略/旧配置迁移 → 重量来源 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P32 | 称重单位 | 业务模块配置 → 适用业务策略/旧配置迁移 → 重量单位 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P33 | 精度 | 业务模块配置 → 适用业务策略/旧配置迁移 → 重量小数位 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P34 | 配置值 | 业务模块配置 → 适用业务策略/旧配置迁移 → 配置重量 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P35 | 本件等待期限(ms) | 业务模块配置 → 适用业务策略/旧配置迁移 → 等待称重期限 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P36 | 测量新鲜度(ms) | 业务模块配置 → 适用业务策略/旧配置迁移 → 测量新鲜度 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P37 | 重量有效期 | 业务模块配置 → 适用业务策略/旧配置迁移 → 重量有效期 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P38 | 图片上传策略 | 业务模块配置 → 适用业务策略/旧配置迁移 → 上传策略 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P39 | 离线签名包路径 | 维护 / 安装管理；真实签名及独立安装帮助程序 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P40 | 原包 SHA256 | 维护 / 安装管理；真实签名及独立安装帮助程序 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P41 | 扩展库存状态 | 维护 / 安装管理；真实签名及独立安装帮助程序 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P42 | 原配置目录（包含business.json） | 系统 / 数据与记录（只读）；独立启动维护 / 本机目录与配置身份（受控修改） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P43 | 原数据根（保留Simulation/Production、站和Application层级） | 系统 / 数据与记录（只读）；独立启动维护 / 本机目录与配置身份（受控修改） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P44 | 工位身份摘要 | 系统 / 站点标识；独立维护 / 本机目录与配置身份 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P45 | 读码来源 · sportmaster.camera.left / right | 驱动配置 → 固定两个实例与投递位对应；首次启动 → 驱动实例 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P46 | 稳定设备身份（离线录入，待真实SDK核验） | 首次启动 → 驱动实例；后续驱动配置 → 标识/受控迁移 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P47 | 输入队列容量 | 业务模块 / 处理预算 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P48 | 调用超时（ms） | 业务模块 / 处理预算 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P49 | 作业容量 | 业务模块 / 处理预算 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P50 | 常驻历史条数（同库归档不释放磁盘） | 系统 / 数据与记录 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P51 | 计划备份维护限制（文案） | 系统 / 计划备份 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P52 | 维护原因（必填；保留审计与原配置字节） | 首次启动配置 → 检查并保存 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P53 | 浏览完整备份目录（最多256份；校验整个恢复集，保留无效项） | 独立维护 / 完整备份与隔离恢复（列表、完整性与目标预检） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P54 | 备份列表：时间 / 完整性 / 字节 / 备份 | 独立维护 / 完整备份与隔离恢复（列表、完整性与目标预检） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P55 | 恢复交接证据 | 维护 / 恢复交接、异常与恢复（原确切ID） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P56 | 记录命令目标 | 作业记录 → 固定原作业 → 处置详情 | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P57 | 确认已检查身份冲突旁输入框 | 维护 / 恢复交接、异常与恢复（原确切ID） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| P58 | 原接收发生 / 确切ID / RawDurable | 维护 / 恢复交接、异常与恢复（原确切ID） | MaintenancePage.xaml / StartupWorkflows.cs / 共享参数及历史模型 |
| A01 | 验证维护身份 | 参数配置 / 保存准备 / 认证维护权限 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A02 | 保存修改 | 参数配置 / 保存修改（待重启） | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A03 | 草稿与读取 / 检查连接 | 参数配置 / 保存准备 / 草稿与重新读取 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A04 | 检查修改 | 参数配置 / 检查完整候选 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A05 | 查看配置文本 | 维护 / 完整候选配置（高级） | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A06 | 编辑配置文本 | 维护 / 显式切换高级JSON编辑 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A07 | 转为表单编辑 | 维护 / 将当前高级JSON加载到表单 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A08 | 检查配置文本 | 维护 / 校验完整候选 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A09 | 保存关联修改 | 查看跨对象修改 → 保存修改（待重启）；高级编辑沿完整修订保存 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A10 | 备份与恢复 | 维护 / 完整备份；独立维护 / 隔离恢复 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A11 | 添加组件实例 | 维护 / 能力库存；Sportmaster无第二RCS/第三Reader入口 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A12 | 移除此实例 | 维护 / 移除所选未绑定实例 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A13 | 读取恢复证据 | 维护 / 恢复交接 / 读取原机交接证据 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A14 | 确认恢复核对 | 维护 / 确认已核对恢复，保持暂停 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A15 | 检查并安装 | 维护 / 安装管理 / 验证签名并安装 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A16 | 继续本次未尝试的图片上传 | 维护 / 异常与恢复（仅既有可选上传组合，Sportmaster不新增图片） | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A17 | 取消尚未发送的请求 | 记录固定原件 → 维护 / 检查后取消未发送意图 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A18 | 记录现场过载检查 | 维护 / 确认已检查过载现场 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A19 | 归档已完成的原始记录 | 维护 / 受控归档已清偿原件 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A20 | 记录身份冲突核对 | 维护 / 确认已检查身份冲突 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A21 | 选择文件夹 | 首次系统 / 本机目录与配置身份 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A22 | 读取配置 | 首次检查与完成 / 维护操作 / 读取所选配置 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A23 | 暂存设置 | 首配固定操作 / 保存未完成草稿 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A24 | 继续上次设置 | 首配业务模块 / 继续已保存草稿 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A25 | 从模板配置 | 首配业务模块或日常维护 / 导入模板与差异预览 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A26 | 上一步 / 下一步 | 首配固定底栏 / 上一步、下一步（共用模型） | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A27 | 查看本步帮助 | 帮助与支持 / 离线帮助；首配 / 离线帮助 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A28 | 检查配置 | 首配检查与完成 / 分层检查与预览 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A29 | 保存设置 | 首配检查与完成 / 保存首配待生效 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A30 | 应用并检查运行 | 首配检查与完成 / 按当前首配启动 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A31 | 选择备份文件夹 | 独立维护 / 完整备份目录 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A32 | 扫描备份 | 独立维护 / 读取并校验备份列表 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A33 | 导出工作站模板 | 维护 / 模板与显示；独立维护 / 导出工作站模板 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A34 | 修复未确认配置 | 独立维护 / 受控修复未确认配置 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A35 | 导出诊断摘要 | 帮助与支持 / 支持预览与关于；独立维护 / 导出支持摘要 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A36 | 创建完整备份 | 维护 / 完整备份；独立维护 / 创建完整备份 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A37 | 核对并接管恢复目录 | 独立维护 / 接管已恢复的原机目录 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A38 | 打开安装维护 | 独立维护 / 安装修复；沿单窗口安装流程 | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
| A39 | 暂停接收新包裹 / 恢复接收 | 全局顶部 / 暂停新入场、恢复新入场（软件准入） | MaintenancePage / StartupWorkflows / ProductSupportWindow / Application原命令 |
