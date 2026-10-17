> 当前执行：WP-IPC-ARCH-EXTENSION-CLOSURE-13，基线 main `feed6e9c`。职责、装配、两项P2及类型化设备/集成接入正在实施；本段是执行范围，不代表目标已经完成。Mac核心验证与WPF交叉构建在本轮，Windows/W01～06、实际UI与现场统一延期，状态保持 `WINDOWS_NOT_RUN`；不寻找VM/runner，不扩OAuth。原当前指引见 [归档](archive/pre-arch-extension-closure-13/README.md)。

> 上一版11执行：[REVIEW-FOLLOWUP-11](work-packages/WP-IPC-REVIEW-FOLLOWUP-11.md)，起点main `4ae5319`。修复PR9独立复核的两个P2并补Product组合所有权动态证据；结果、同版包及新SHA见[11报告](runtime/review-followup-11.md)与[11 checks](runtime/review-followup-11-checks.json)；[前版本](archive/pre-review-followup-11/README.md)保留。下文09/a080825为上一版证据，不自动代表本次输出；Windows/真实集成仍按06/05分项阻塞。

## DELIVERY-12 当前入口

[原工作包](work-packages/WP-IPC-EXTENSION-WINDOWS-DELIVERY-12.md) · [受控扩展设计](design/controlled-extensions.md) · [扩展开发与安装/恢复](extensions/developer-guide.md) · [12实施/AC/候选交付](runtime/extension-windows-delivery-12.md)。从main 9f320459起开发；原11证据、SQL/请求/设计原件和D/RV保留。核心验证与Windows参考验收、远端候选和真实现场资格分别判断，未验不关闭整包。

# 统一资料与实施入口

2026-10-16形成；上一版执行 **[WP-IPC-AUDIT-RECONCILIATION-01](work-packages/WP-IPC-AUDIT-RECONCILIATION-01.md)**，基准main `856e4de73f09a1c4f12af798c4d21e8631a6a86f`。用户本次授权08/10及09合成/参考环境必要测试、构建、启动和发布验证；生产WCS写入与真实设备动作未授权。[纠偏前原字节快照](archive/pre-reconciliation-01/README.md)保留原报告、工作包、入口、manifest及blob/SHA，历史不测试/NOT_RUN按形成时含义保留。

## 上一版实现与证据（a080825）

- [09实际验证与同版交付](runtime/reference-validation-09.md)：SourceCommit、运行日志、持续双箱旅程、Release/Storage/Contract、模块/配置指纹与ZIP SHA；[检查JSON](runtime/reference-validation-09-checks.json)。源码路径、合成运行、Windows操作和现场准入分别记录。
- [10逐页UI映射](runtime/ui-baseline-alignment-10.md)：v1.1实物到PAGE01～08、双通道、数量/拆包、确认及低频抽屉；实际Windows截图与行为另按[06待验](runtime/windows-06.md)。不能以HTML图或交叉编译替代WPF验收。
- [运行/发布/维护](runtime/operations.md)：固定SDK、实际选择器、控制台与桌面、同版打包、旧格式只读检查/备份/显式升级/回退隔离、固定运行账户与机级租约。
- [05真实能力边界](runtime/integration-05.md)：B-01～06分别记录SDK/旧工程、WCS、实体、数量、身份和现场缺项。Production继续NotReady，不用模拟成功回退。

08统一旧新历史/详情读取的应用层有效身份检查；当前故障快照保持可发现，退出/过期后桌面旧阅读结果明确失效。启动在取得数据根租约后选定最终Effective/Pending配置、核验站点/模式，再锁定实际站点/设备资源，最后创建应用/来源/发送器；失败激活保留旧Effective，不并行重建第二套框架。

09保留schema1/id2/id3 SQL/checksum、原请求字节/目标/合同、旧读者及未来格式拒绝和失败保旧断言。双箱旅程使用真实ProductRuntime来源队列、合成心跳、后台发送器、SQLite与独立loopback接收账；接受事实和模拟实体离场/复位分开。实际分项结果以检查JSON为准，不以本段描述代替证据。

## 页面开发实物基线

[SS-UI-VIS-001 v1.1入口](ui/ss-ui-vis-001-v1.1/README.md) · [34张独立状态图](ui/ss-ui-vis-001-v1.1/GALLERY.md) · [原交互HTML](ui/ss-ui-vis-001-v1.1/供包工作站_高保真交互原型.html) · [完整UI交接](ui/ss-ui-vis-001-v1.1/docs/供包工作站_UIUX设计与开发交接说明.md)。PPTX、画板JSON、tokens和分离HTML/CSS/JS均实际入库。11份源文件保留原字节，PNG是同源重新导出，均不是Windows客户端截图。本次WPF增量继承专业信息层级，不复制旧HTML已知业务缺陷；原设计不因代码取舍被重写。

该组生成资产已有用户限定公开授权，不扩张为其他客户资料许可。完整客户材料归档仍为 **PUBLIC_SUBSET_ONLY**。

## 必读设计与原问题

1. [框架架构](design/framework.md)与[决策依据](design/framework-decisions.md)：目标模块化单体、基座/业务/UI/适配器边界、单业务所有者和多视图；F-02是独立参考技术路线，不是旧现场迁移完成。
2. [复核02](reviews/main-static-review-02.md)、[复核03](reviews/main-static-review-03.md)及[03 checks](reviews/main-static-review-03-checks.json)：保留形成时问题证据。03当时E-06/E-10残留由08补齐、UI差异由10实施、运行债务由09承接；历史报告不倒写为新验收。
3. 实际工作包：[08](work-packages/WP-IPC-ENGINEERING-FOLLOWUP-08.md)、[10](work-packages/WP-IPC-UI-BASELINE-ALIGNMENT-10.md)、[09](work-packages/WP-IPC-REFERENCE-VALIDATION-09.md)、[06](work-packages/WP-IPC-WINDOWS-VALIDATION-06.md)、[05](work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)。本次结果由纠偏包统一绑定，原不测试要求只作为历史记录保留。
4. [D决策确认包](readiness/decision-pack.md)、[技术基线](readiness/technical-baseline.json)、[RV状态](reviews/STATUS.md)与[完整历史issues](reviews/issues.json)：D批准未变，RV-01～17仍OPEN；参考运行不能关闭客户/现场验收。
5. [当前manifest](manifest.json)、[公开范围](baseline/PUBLICATION.md)、[缺失资料](baseline/MISSING.md)与[冲突](baseline/CONFLICTS.md)：不上传未许可客户原稿、私密链接、凭据、生产数据、SDK或字体。

## 历史实施与资料归档

[RUNTIME-03实施](runtime/implementation.md)、[历史交付](runtime/delivery.md)、[HARDENING-04](runtime/hardening-04.md)、[ALIGNMENT-01](runtime/alignment-01.md)和[07实施](runtime/engineering-closure-07.md)保留形成时范围与检查，不能用旧包或旧PASS替代当前源提交。PR #2～#8合并历史与各次OPEN_AT_CREATION分别理解，实时PR状态以GitHub为准。

[BASELINE-01](work-packages/WP-IPC-DESIGN-BASELINE-01.md)全量归档尚未关闭；九张PRD图/七个HTML依赖已取得但未公开，新的Canva完整导出及部分历史截图仍缺。不能以README、目录或摘要冒充原字节交付。[历史入口](archive/pre-framework-20261016/README.md)、[READINESS准备](superpowers/plans/2026-10-16-ipc-readiness.md)和旧检查保留，不重新施加“永久只能归档”。

资料校验见[工具说明](../tools/README.md)；其PASS只证明所覆盖资料，不代表业务、Windows或现场通过。
