# INSTALLER-FIRST-RUN-UI-02 · 本机原生证据

日期：2026-10-22。代码提交 `6220fd6a6d3e5d49f9179eb3197b6c0b34518bbc`；最新 main 基准、PR88 继承关系、[修改清单与源码映射](../../installer-first-run-ui-02.md)分别记录。仅本地提交，无推送、PR、发行或设备操作。

Windows 本机实际缩放 **125%**。17 张 PNG 来自已显示 HWND 的 WPF/Dispatcher 内容渲染（不含系统非客户区），使用合成目录、配置与安装结果；不是 HTML、生产数据或机器安装成功的截图。安装服务边界注入可控异步结果，管理授权失效和直接保存拒绝使用真实进程内 `LocalManagementMode` / `ParcelStartupMaintenance`，不宣称重验 System 服务宿主或 UAC。

| 必要检查 | 结果与原始记录 |
|---|---|
| 本包定向原生旅程 | PASS，58 项；[NATIVE.log](NATIVE.log)。往返/独立快捷方式/长路径、阶段解析/无效终态、准备取消/失败、安装中关闭守卫、失败/未知、首配入口/取消、密码错误/服务不可用、实际过期后的提交拒绝和同一输入恢复、安装后不自动启动。 |
| 既有首配风险回归 | PASS，93 项；[STARTUP-REGRESSION.log](STARTUP-REGRESSION.log)。完整候选、字段定位、未知保存、权限恢复和安装入口合同。 |
| 共用管理窗口回归 | PASS，26 项；[MANAGEMENT-NATIVE.log](MANAGEMENT-NATIVE.log)。工作窗进入、首次设密、失败、改密码撤权与操作员身份独立。 |
| 安装原请求与升级阻断 | PASS，8 项；[CHANGE-GUARDS.log](CHANGE-GUARDS.log)。Unknown 责任拒绝安装回调、不重发原请求。 |
| 管理模式服务层 | PASS，69 项；[MANAGEMENT-CORE.log](MANAGEMENT-CORE.log)。真实本地权限、失败保护和业务守卫；外部端口为合成。 |
| 构建 | Desktop、原生验证和核心验证工程均 0 警告/0 错误；见 [Desktop](DESKTOP-BUILD.log)、[Native](NATIVE-BUILD.log)、[Core](CORE-BUILD.log)。安装器构建另见同目录 `INSTALLER-BUILD.log`。 |

完整 Git 原字节清单 5954 项和本包 Markdown/HTML 本地引用核对通过。原两份未跟踪交接文件与归档副本逐字节一致。计数仅定位日志，不代表系统级、现场或旧 RV 的验收覆盖。

## 逐页截图

| 页面 | 实际原生截图 |
|---|---|
| 安装选项 / 小逻辑工作区 | [常规长路径](installer-options-1366x768-100-equivalent.png)、[较小区域](installer-options-1024x768-125-equivalent.png)、[最小区域](installer-options-1024x768-150-equivalent.png) |
| 确认安装 | [完整换行与同一选择](installer-confirm-long.png) |
| 正在安装 | [等待授权](installer-stage-WaitingForAuthorization.png)、[复制校验](installer-stage-CopyingFiles.png)、[提交登记](installer-stage-CommittingInstallation.png) |
| 安装失败 / 结果未知 | [明确失败](installer-failure.png)、[未知结果](installer-unknown.png) |
| 安装完成 | [确需首次配置](installer-complete-first.png)、[已有配置](installer-complete-existing.png) |
| 管理密码 | [首次设置](password-first-setup.png)、[验证失败](password-rejected.png)、[服务不可用](password-service-unavailable.png) |
| 首配入口 | [未设密](first-run-unconfigured.png)、[已到期](first-run-expired.png) |
| 安装后启动入口 | [等待主动启动](after-install-start-gate.png) |

截图名中的 100/125/150-equivalent 指原生窗口的等效 DIP 工作区大小；所有截图的真实系统缩放均为 125%。不是实际切换了目标显示模式或已完成三个系统 DPI 的人工验收。超长内容滚动、主操作固定；WPF 焦点遍历已检查，物理键盘/触控待验。

## 原失败与修复

`EARLIER-*.log` 原样保留三次早期原生夹具失败：反射 Render 与 WPF 基类同名方法冲突；直接读取后尚未执行布局，视觉树查询早于渲染。已分别限定 DeclaredOnly、等待 Dispatcher 布局，没有删减断言或吞异常。最终 58 项基于修正后的夹具和最终代码通过。

`INITIAL-INSTALLER-NO-ASSETS.log` 保留首次无 restore 的构建失败；`INITIAL-INSTALLER-PROPERTIES.log` 保留未全局传入 self-contained 的构建失败。按现有 `scripts/package-installer.py` 的一致 win-x64/self-contained/锁文件参数恢复并构建；不改依赖或放宽锁文件。未执行 publish 或生成安装载荷。

独立源码复核发现的“无可信终态允许重复安装”和“首配窗口未限制工作区”已修正并覆盖；第二轮只读复核未发现未解决实质问题，不代替原生/现场验证。

全库旧归档工具在 Windows 自动换行的导出树上产生哈希差异；最终直接按 Git blob 流式核对全部清单，未改写历史原件。工具还报告未更改的旧 UTF-16 JSON、字体/示例、旧 PPTX 等扫描项，完整诊断保留在本地 `.artifacts/installer-ui-02-archive-check-final.json`；本包不将旧工具的全库结果记为 PASS。

## 来源与未验范围

开发构建在本地代码提交前完成，代码文件与上述提交一致；不是按该提交打包的发行介质，不能将开发程序集版本元数据当作新发行身份。二进制/源码原字节哈希、代码 blob 及输入身份见 `RESULT.json`。合成数据与凭据目录留在 D 盘测试区，不复制进仓库；这里只保留无明文密码的日志和截图。

完整 UAC/机器新装/升级/备份/系统服务与失败恢复、独立单 EXE、重启与双用户、实际系统 100%/150% DPI 与目标显示模式、工控机触控、SDK/RCS/实体及现场均 **NOT_RUN**。未改变 D 盘存储登记、安装状态、Windows 显示设置或生产设备状态。历史 PASS 与介质保持原来源。
