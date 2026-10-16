# importer-pc

C#/.NET 10 工位运行基座与供包**独立参考产品**：Generic Host、WPF、多页面/布局共享业务、实际SQLite事务/Outbox/恢复，两类模拟读码源与独立持久接收账loopback夹具。不是既有现场工程迁移，也未接入厂商SDK或真实WCS。

[运行与发布](docs/runtime/operations.md) · [实施记录](docs/runtime/implementation.md) · [本轮工程交付与验证边界](docs/runtime/engineering-closure-07.md) · [统一入口](docs/INDEX.md) · [框架设计](docs/design/framework.md) · [独立静态复核](docs/reviews/main-static-review-02.md) · [当前工程工作包](docs/work-packages/WP-IPC-ENGINEERING-CLOSURE-07.md)

工程收敛07从main `6d4063e`实施，代码源提交`493416f`：10项工程缺口已实现，均待运行验证。StorageVersion为3，旧1/2须先原格式备份并显式升级；Windows需维护安装的固定账户/机级租约策略。Windows06仍NOT_RUN，SDK/WCS/生产规则05仍BLOCKED。本轮用户要求不测试，未运行测试、构建、程序、验证器或发布，未生成新二进制；以下仅为已有操作入口。

```sh
# .NET SDK 10.0.402 + Python3
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll .
# 本地模拟接收端：保持运行
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

Windows桌面和离线win-x64发布命令见运行说明。macOS能运行核心/夹具和持久化验证，并交叉编译WPF；Windows桌面、DPI/触控、提示声、SDK/WCS与现场验收均须另验。Production必需能力缺失时报NotReady，不回退模拟成功。

仓库public。[公开范围](docs/baseline/PUBLICATION.md) 持续有效：仅提交新的通用实现和合成数据，不上传客户原稿、私密来源、凭据、生产数据、SDK或字体。完整客户归档仍为PUBLIC_SUBSET_ONLY，D未批准、RV-01～17保持OPEN。新参考实现的验证不表示旧HTML已修复。
