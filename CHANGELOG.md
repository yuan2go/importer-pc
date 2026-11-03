# 变更记录

## 2026-11-03 · IE01实施中

- 固定恢复密码验证器已从运行源码移除，改为Windows管理员批准的本站重置；旧会话/委托撤销、原操作核对及恢复安全代次已实现核心逻辑。
- 独立维护缓存、安装库存、静默参数和语义化版本保护正在实施；PR/main自动CI已触发，结果及剩余缺口见[实施记录](docs/runtime/industrial-engineering-01.md)。
- 三层文档入口已建立；打印版、轻量帮助、诊断包等按实际进度分别标记。正式签名/设备/接口验收没有借用模拟PASS。

## 历史原件

原README和按日期排列的索引见[本轮整理前清单](docs/archive/pre-industrial-user-docs-01/manifest.json)。详细变更、实现与原SourceCommit仍由[工作包目录](docs/work-packages/README.md)、各运行记录和Git历史追溯，原始证据不改写。
