# 菜鸟供包台 · importer-pc

面向供包工位的Windows客户端。SM（Sportmaster）使用内置海康MVS采图、进程内条码识别和内置RCS；一个业务实例服务全部页面。

## 安装与交付

安装介质由实施人员从可信渠道提供，并核对版本、源身份、哈希及实际验收范围。[GitHub Releases](https://github.com/yuan2go/importer-pc/releases)保留历史介质；历史资产不自动包含本轮安全修复，也不等于当前main已正式发布。

IE01正在[PR #184](https://github.com/yuan2go/importer-pc/pull/184)分阶段实施。当前SM源码版本1.0.1，正式公司发布者/证书、系统生命周期及真实设备/接口验收仍有未完成项，见[实施进度](docs/runtime/industrial-engineering-01.md)。

## 使用与维护

- [供包员作业卡](docs/operator/quick-guide.md)
- [实施人员部署卡](docs/operator/deployment-card.md)
- [实施与维护手册](docs/operator/implementation-guide.md)
- [故障目录](docs/operator/fault-codes.md)

相机自动来源与人工键盘/PDA分别判断可用性；同一次托盘放置只请求一次RCS。结果不明时保留包裹与原记录，不盲重发。程序、业务数据和开发缓存使用D盘，受保护根遵从管理员配置。

## 开发入口

[文档目录](docs/INDEX.md) · [架构与职责](docs/design/framework.md) · [决策](docs/design/framework-decisions.md) · [工作包目录](docs/work-packages/README.md) · [历史记录](CHANGELOG.md)

C#/.NET10/WPF/SQLite，Windows x64。先加载项目环境，再构建或运行标准核心回归：

```powershell
. ./scripts/project-environment.ps1
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1
dotnet test tests/Ipc.Core.Tests -c Release -p:RestoreLockedMode=true
```

PR/main自动CI与本机、原生Windows、系统安装、真实SDK/RCS和现场验证分别记证。遵守[项目规则](AGENTS.md)及[公开范围](docs/baseline/PUBLICATION.md)，SDK、凭据、生产数据和未授权资产不进入Git。
