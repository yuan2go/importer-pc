# 启动与部署闭环 Implementation Plan

**Goal:** 用户经原生向导完成部署，启动失败仍可维护修复，基本配置不依赖可选扩展。
**Architecture:** Platform提供部署文件/ACL/登记机制；ParcelFlow拥有离线配置及恢复，Assembly装配；Desktop只调用维护契约与安装服务。沿用同一配置账和数据库，设备只在正常业务Start后启动。
**Tech Stack:** 既有.NET10/WPF/SQLite，自包含Windows候选；无新增NuGet、字体或SDK。

## Global Constraints

继承WP-IPC-STARTUP-DEPLOYMENT-02。用户已确认前轮方案并授权完整实现，本会话直接实施和验证；不再次请求执行选择，不修改并行PR45。新Windows行为只交叉构建，不冒充原生验收。

### Task 1: 可选库存独立
- [x] 增加实际Factory未配置/损坏库存回归，保留真实绑定Validate的拒绝。
- [x] `CapabilityCatalog`增加独立库存状态/诊断，`ParcelCapabilityFactory.Catalog()`返回内置提供者及显式库存诊断，UI显示原因。
- [x] 运行定向Application验证；不创建假信任或假包。

### Task 2: 离线维护应用与界面
- [x] 新增Contracts中的离线配置/恢复接口与ParcelFlow实现，Application所有权锁、CAS、原配置备份、仅Pending、读DB边界及完整备份隔离恢复。
- [x] Assembly提供维护装配及受限提升命令；Desktop新增原生维护窗口，失败页和显式`--maintenance`均可进入；提供参数编辑、文件夹选择、诊断导出、保存与重试，不重建活动连接。
- [x] 验证坏配置/占用/CAS/原字节和DB不变；编译实际WPF页面与事件处理。慢启动/部分停止的交互验证列Windows后验NOT_RUN。

### Task 3: 安装部署向导
- [x] 新增通用固定媒介校验/并排部署/最后提交登记及显式维护helper，权限分离、可选独立信任、保留已有数据。安装失败不切换旧登记。
- [x] 原生WPF多步向导及`Setup.cmd`，账户/数据选择、预检、结果、快捷方式及启动；安装中不加载插件或设备。升级沿原数据/配置，修复发布新目录。
- [x] 验证hash/额外文件/路径/失败保旧、打包入口及媒介精确依赖；Windows真实权限/快捷方式后验保留。

### Task 4: 交付
- [x] 更新现行指南/框架入口/manifest，保留原工作包与旧候选。
- [x] 定向验证、三个Application装配及WPF交叉构建，固定新源码生成portable/win-x64并检查启动及资源锁。
- [ ] 提交推送当前开发分支，更新PR44标题/描述/范围，实际提交与候选SHA分别报告，不合并main。
