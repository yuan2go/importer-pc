# 文档索引

当前规则只看仓库根目录的 [AGENTS.md](../AGENTS.md)。本页只列当前还在用的入口。

## 现场与实施

- [供包员作业卡](operator/quick-guide.md)、[部署卡](operator/deployment-card.md)、[实施与维护手册](operator/implementation-guide.md)、[故障目录](operator/fault-codes.md)

## 开发与运行

- [运行入门](runtime/getting-started.md)、[D 盘环境](runtime/d-drive-storage-20261021.md)
- [当前代码与验证边界](runtime/current-contract-cleanup-01.md)、[当前格式与版本](runtime/version-compatibility-01.md)
- [SM参数、默认与保存](runtime/parameter-inheritance-02.md)、[首次配置与日常设置](runtime/parameter-guidance-startup-01.md)
- [管理模式](runtime/management-mode-01.md)
- [安装、卸载与生命周期](runtime/lifecycle-ux-current-guidance-01.md)
- [MVS 软件识别](runtime/mvs-software-reader-01.md)
- [海康原生识码与安装依赖](design/mvid-native-reader.md)
- [SM RCS 参数生效与原请求](design/rcs-parameter-description-compatibility.md)
- [长稳采集、完整备份分析与证据边界](runtime/win11-24x7-stability-remediation-01.md)
- [双相机性能测量、采集与统计](design/sm-dual-camera-performance.md)
- [现场打印稿生成与排版核查](runtime/industrial-print-guides-01.md)

## 设计

- [SM 包裹轮次与原请求生命周期](design/sm-parcel-rounds.md)
- [SM DB12 到 DB13 离线接续](design/sm-db12-continuation.md)

- [框架](design/framework.md)、[框架决策](design/framework-decisions.md)
- [SM RCS 取格口任务](runtime/sportmaster-rcs-task.md)
- [字体规范](design/typography-01.md)
- [WPF 工业桌面 UI 审查方法（Hallmark-WPF）](design/wpf-ui-review.md)
- [作业卡状态与现场验收](design/work-card-states.md)
- [供包台导航、设备诊断及安装呈现](design/operator-navigation-20261103.md)
- [工业化交付（安全恢复、生命周期、诊断、发布门禁）](design/industrial-engineering-01.md)
- [企业打包选型与新版本提示](design/enterprise-packaging-07.md)
- [代码签名证书接入](design/code-signing-onboarding-06.md)

## 公开范围

- [PUBLICATION](baseline/PUBLICATION.md)

## 历史

早期验收只对应各自来源提交。工作包、验证记录、评审和旧索引可在 [archive](archive/) 里，只作背景，不是工作指令：

- [历史工作包](archive/work-packages/)
- [规则收口前的完整索引](archive/INDEX-pre-20261102.md)

`docs/manifest.json` 只在正式出发布包时用 `scripts/refresh-integration-manifest.py` 刷新，日常提交不需要更新它。
