# PARAMETER-UI-REFINE-01 验收记录

2026-10-23，源码实现提交 `a73b53990a564aeb4a253dc94a44b3e3fce66a89`（后续清单提交只更改记录），Windows 本机；源码、设计原件、原生合成与设备验收分别记录。执行基线与字段/源码映射见[实施说明](../parameter-ui-refine-01.md)。本轮使用项目 Impeccable Operate/WPF 与 UI UX Pro Max WPF 适配，保留系统字体及原生技术。

## 已验证

| 风险 | 执行与结果 |
| --- | --- |
| 构建 | Sportmaster WPF 及 UiVerification 构建通过，无警告/错误；冻结打包另在本地交付记录中核对 |
| 单一导航及状态 | `--startup-experience`：相同编辑器往返、单目录/正文滚动、固定页脚、当前错误/跨页定位、真实保存回读、已提交丢回执、未提交、未知阻止重复、完成不启动，通过 |
| 已填仍 required | `ParameterUiRefinement.cs`：通过实际绑定输入修正后只清除对应实例错误；左右同文错误不合并，同实例/字段/规则去重，通过 |
| IP、条件及语义 | IPv4 格式与 Any/Broadcast 地址规则分开；指定格口未知/false/true 与内存值保留、MiB、原字段共同站码更新、RCS 编号字段及未截图字段保留，通过；没有连接相机或 RCS |
| 日常参数 | `--parameter-experience`、`--parameter-v3-only`：共享完整候选、对象切换、快照中焦点/选择/输入、搜索定位、连接证据边界、敏感引用遮蔽、真实保存审阅及既有防护，通过 |
| 首配与菜单权限 | `--management-mode`、`--installer-first-run-ui`：实际原生管理菜单、密码确认失败/取消/错误密码、五位重复数字、修改密码撤权、到期返回入口、同一未保存候选重新验证、失权直接调用保存/草稿/修复被服务拒绝，通过 |
| 服务与配置 | Application `--management-mode`、`--ux-commercial-configuration`：到期令牌撤销、直接写拦截、身份独立、实际完整候选校验/CAS/Pending、受控重启及并发草稿保护，通过；端口和配置均为合成 |
| 尺寸及操作 | 本机实际 DPI 125%。首配 960×656、800×600 DIP；日常客户区 1366×768、1092×614、800×420 DIP。原生表单/滚动/页脚/菜单已查看；精确字段焦点、WPF Tab 遍历、原生 UIAutomation 菜单及长路径完整选择验证通过。DIP 矩阵不等于 OS 分辨率/DPI 切换 |

本轮发现并处理：标签受全局垂直居中影响而不对齐、会话时长只隐藏输入而残留标签、共同站码的底层值改变后显示不更新、旧检查失败状态不随成功继续清除，以及已知保存失败缺少单独短状态。相关失败记录保留，随后通过对应风险断言；没有删除业务/授权断言换取 PASS。旧日常测试中的页面标题、菜单位置和身份单行高度按新呈现合同更新。

配置 Application 回归第一次未取得 Pending 后触发旧测试的空引用；在保留原断言并增加实际返回状态诊断后，同一业务代码重跑通过，实际输出为 `PendingRestart`。原失败保留，未将该次失败改写成 PASS，首次瞬时失败原因尚未确认；不能由重跑推断现场配置可靠性已验收。

## 实际截图及证据

下列图片是实际 WPF HWND/Dispatcher 渲染的合成配置截图，非本包 PNG/HTML。无 SDK/生产设备，截图中 `SYNTHETIC`、文档保留地址和示例编号只用于验证。

- [条码筛选](../evidence/parameter-ui-refine-01/startup/refine-barcode.png)
- [处理参数](../evidence/parameter-ui-refine-01/startup/refine-processing.png)
- [供包台标识](../evidence/parameter-ui-refine-01/startup/refine-station.png)
- [相机错误](../evidence/parameter-ui-refine-01/startup/refine-right-camera-error.png)
- [格口条件错误](../evidence/parameter-ui-refine-01/startup/refine-chute-errors.png)
- [跨页检查](../evidence/parameter-ui-refine-01/startup/refine-check-errors.png)
- [RCS 服务](../evidence/parameter-ui-refine-01/startup/refine-rcs.png)
- [管理菜单原生 Popup](../evidence/parameter-ui-refine-01/startup/refine-management-menu.png)
- [权限到期](../evidence/parameter-ui-refine-01/authorization/first-run-expired.png)
- [保存后尚未启动](../evidence/parameter-ui-refine-01/authorization/after-configuration-start-gate.png)
- [日常小窗口](../evidence/parameter-ui-refine-01/daily/rcs-800-focused.png)

完整结果、来源目录、文件哈希及修改清单见[证据目录](../evidence/parameter-ui-refine-01/README.md)。公开证据只包含合成运行资料，用户原图、ZIP 与衍生设计资产仍在本机私有区。

## 未执行及剩余事项

- 真实 Windows 100%/150%/200% DPI、跨显示器 DPI 切换及目标 IPC 分辨率未验证；没有把逻辑缩放矩阵或浏览器预览作为替代。
- 物理键盘完整人工流程、触摸及屏幕阅读器未验证；已执行的是 WPF 焦点/选择/遍历与 UIAutomation 原生菜单操作。
- 新包真实 UAC 批准/取消、受保护鉴权服务安装、正式安装/升级/卸载、后台事务未知关闭及新包全流程重测未执行；遵守用户不自动安装的约束。
- 海康 SDK、生产 RCS、现场地址/编号分配及实体作业未联调；保留原接口、存储与服务层权限检查，不能宣布现场验收或历史 RV 全部关闭。
- 上述首次配置回归的瞬时 Pending 缺失未复现、原因未确认，原始失败作为后续排查证据保留。

本地交付 EXE 的路径、SHA256、冻结 SourceCommit 与包/共享 DLL 一致性记录在交付目录，版本仍为 SM1.11.1。没有推送、发布或自动安装。
