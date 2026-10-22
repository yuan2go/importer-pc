# 经典窗口与参数文案：前后原生证据

2026-10-23，Windows 原生 HWND/Dispatcher，系统 DPI 125%，合成账号显示、配置及端口；非网页或原型图片。before 运行的是增量父提交 `9900cbe9` 的业务/界面源码，新增 fixture 只负责采集。after 为本次实现。既有操作员与管理模式已经分离，本次保留该事实，不把 PR88 的早期描述作为当前缺陷。

[源码映射/集成说明](../../parameter-shell-polish-02.md) · [验证及剩余项](../../validation/parameter-shell-polish-02.md) · [来源/结果](RESULT.json) · [修改清单](MODIFIED-FILES.txt) · [字节哈希](FILES.json)

| 场景 | 改前 | 改后 |
| --- | --- | --- |
| 数据页、1366×768 DIP | [原生截图](before/data-1366.png) | [原生截图](after/data-1366.png) |
| 作业登录、1366×768 DIP | [原生截图](before/operator-session-1366.png) | [原生截图](after/operator-session-1366.png) |
| 1092×614 DIP | [原生截图](before/parameters-1092.png) | [原生截图](after/parameters-1092.png) |
| 最小尺寸请求 | [改前实际为800×720](before/parameters-800.png) | [改后800×420](after/parameters-800.png) |

改前长账号把横向顶栏撑高，800×420请求被内容约束为800×720，不能冒称同尺寸对照；1366×768是同尺寸可核验对照。改后800×420保留原生参数滚动、身份入口与固定保存。逻辑尺寸不等于切换物理分辨率或其他 OS DPI。

- [管理模式原生菜单与真实到期时间](after/management-menu.png)
- [实际软件暂停状态](after/paused-1366.png)
- [退出后回到普通模式](after/revoked-800.png)
- [实际本地授权到期分支](after/expired-800.png)

到期采用受控本地授权测试时钟推进，执行真实服务撤权与直接保存拒绝；不是等待15分钟的人工测试。原始错误/键保留在诊断，普通参数反馈用中文；配置候选在仅布局/文案/换页后字节不变。没有安装、推送、发布或生产设备调用。
