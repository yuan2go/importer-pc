# importer-pc

C#/.NET 10 工位运行基座与供包**独立参考产品**：Generic Host、WPF、多页面/布局共享业务、实际SQLite事务/Outbox/恢复，两类模拟读码源与独立持久接收账loopback夹具。不是既有现场工程迁移，也未接入厂商SDK或真实WCS。

[运行与发布](docs/runtime/operations.md) · [实施记录](docs/runtime/implementation.md) · [本轮工程交付与验证边界](docs/runtime/engineering-closure-07.md) · [统一入口](docs/INDEX.md) · [框架设计](docs/design/framework.md) · [最新独立静态复核](docs/reviews/main-static-review-03.md) · [下一工程工作包](docs/work-packages/WP-IPC-ENGINEERING-FOLLOWUP-08.md)

PR #6已合并至main `677a5ad`。最新[独立复核03](docs/reviews/main-static-review-03.md)：10项中8项指定整改路径静态补齐，E-06旧历史读取权限、E-10最终配置与租约集合仍有工程缺口；全部运行验证NOT_RUN。下一步为[工程增量08](docs/work-packages/WP-IPC-ENGINEERING-FOLLOWUP-08.md)与[参考验证09](docs/work-packages/WP-IPC-REFERENCE-VALIDATION-09.md)，均尚未执行。Windows06仍NOT_RUN，SDK/WCS/规则05仍BLOCKED。Storage3旧库需显式备份升级，Windows需固定账户/机级策略；没有新包。本轮按用户要求不测试、不构建、不运行验证器或程序；以下仅为已有操作入口。

```sh
# .NET SDK 10.0.402 + Python3
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll .
# 本地模拟接收端：保持运行
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

Windows桌面和离线win-x64发布命令见运行说明。macOS能运行核心/夹具和持久化验证，并交叉编译WPF；Windows桌面、DPI/触控、提示声、SDK/WCS与现场验收均须另验。Production必需能力缺失时报NotReady，不回退模拟成功。

仓库public。[公开范围](docs/baseline/PUBLICATION.md) 持续有效：仅提交新的通用实现和合成数据，不上传客户原稿、私密来源、凭据、生产数据、SDK或字体。完整客户归档仍为PUBLIC_SUBSET_ONLY，D未批准、RV-01～17保持OPEN。新参考实现的验证不表示旧HTML已修复。
