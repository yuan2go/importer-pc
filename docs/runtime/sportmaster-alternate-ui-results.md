# Sportmaster 第二套 UI · 实施与验证结果

2026-10-19；开工main `b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d`。用户确认本次衍生WPF代码可提交公开仓库，原HTML不上传。[运行指南](sportmaster-alternate-ui.md)与[Windows后验](validation/sportmaster-alternate-ui-windows.md)。

## 已实施

`--ui classic|supply-station` 在业务/设备创建前严格校验；默认classic。新Sportmaster原生窗口复用同一 `IParcelApplication`，左右稳定投影/草稿、原WCS格口/请求/原因、每位历史、历史/维护导航、辅助窗口、显式Simulation按位输入和主窗有界排空。业务Flow、ParcelRuntime、SQL、驱动和协议源码未修改；公共应用契约与Views/Layouts不变。

资源字典由独立位控件自己声明，避免构造时依赖未挂接父树；刷新失败及3秒到期标旧/禁用，成功命令刷新与轮询都有防旧失败覆盖；关闭快照排空5秒超时后恢复窗口并保留责任。静态审查发现的这些问题均已处理；Windows实际资源加载仍需后验。

## 实际验证

- UI定向：52项断言通过，使用真实SportmasterApplication/ParcelCapabilityFactory与独立参考WCS接收账，覆盖两套VM共享实例、双位同码不同实体、原请求一次执行、拒绝、提交后丢回复/按原件核对、schema2兼容、稳定草稿、快照失败/到期/乱序及命令刷新竞态。
- 既有架构22：60项断言通过，包括独立队列、恢复、部分构造失败、Stop排空/重试、原图/重量/上传及迁移屏障。
- Sportmaster、DirectPlc、WcsToPlc WPF编译及Windows专用烟测工程交叉编译：均0警告0错误。
- 随包文档渲染测试2项通过，发布脚本语法检查与diff空白检查通过。
- 初轮曾发现桌面项目缺少System.IO导入、Windows烟测装配选择及源链接导入问题，均已修复并重新编译；未删除断言或用跳过换PASS。

[checks](evidence/sportmaster-alternate-ui/checks.json)与同目录定向/回归/构建日志只包含合成参考信息，环境路径已规范化。日志里的临时数据不进入发布或仓库。

## 候选与交付身份

实际候选在冻结本报告及运行源码后生成，候选生成完成后追加确切SourceCommit、ZIP SHA-256和解包/启动结果。此形成时状态不声称候选已经通过。

## 未执行与受阻

Windows原生窗口/启动/焦点/DPI/触控/截图实际 NOT_RUN；已提供可执行烟测工程，不将交叉编译、BAML或HTML截图当作真机PASS。真实SDK、WCS/PLC、设备/实体及现场验收 BLOCKED。Production继续按原守卫NotReady，不改变D/RV、旧HTML或历史验收结论。PR按用户授权创建main目标，不合并。
