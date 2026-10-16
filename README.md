# importer-pc

C#/.NET 10 工位运行基座与供包**独立参考产品**：Generic Host、WPF、多页面/布局共享业务、实际SQLite事务/Outbox/恢复，两类模拟读码源与独立持久接收账loopback夹具。不是既有现场工程迁移，也未接入厂商SDK或真实WCS。

[运行与发布](docs/runtime/operations.md) · [实施记录](docs/runtime/implementation.md) · [验证与交付报告](docs/runtime/alignment-01.md) · [统一入口](docs/INDEX.md) · [框架设计](docs/design/framework.md) · [当前工作包](docs/work-packages/WP-IPC-ARCH-UI-ALIGNMENT-01.md)

```sh
# .NET SDK 10.0.402 + Python3
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll .
# 本地模拟接收端：保持运行
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

Windows桌面和离线win-x64发布命令见运行说明。macOS能运行核心/夹具和持久化验证，并交叉编译WPF；Windows桌面、DPI/触控、提示声、SDK/WCS与现场验收均须另验。Production必需能力缺失时报NotReady，不回退模拟成功。

仓库public。[公开范围](docs/baseline/PUBLICATION.md) 持续有效：仅提交新的通用实现和合成数据，不上传客户原稿、私密来源、凭据、生产数据、SDK或字体。完整客户归档仍为PUBLIC_SUBSET_ONLY，D未批准、RV-01～17保持OPEN。新参考实现的验证不表示旧HTML已修复。
