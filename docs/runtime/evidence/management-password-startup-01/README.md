# MANAGEMENT-PASSWORD-STARTUP-01 本机证据

Windows 11 当前工作机，2026-10-29。原生窗口实际120 DPI；默认520×540及紧凑400×360 DIP是窗口尺寸，未切换系统缩放。PNG只含合成空密码及合成长诊断。用户原截图、事件原文、加密维护输入、当前凭据及机器数据不公开。

`windows-authority.log`：真实 NORMALUSER 子进程→同用户桌面 runas→管理员 helper→管道确认及退出；无部署、密码、设备写入。`windows-wpf.log`和三张PNG：实际 WPF窗口/Tab/长错误/滚动/动作及缓冲区检查。早期本机失败日志留在 D 盘，不删除断言或将它们替换为 PASS。

验证主体为本包开发树；最终同源介质身份与后续受保护服务测试分别补记，不能把原生合成通过当作真实产品首设或现场验收。所有历史证据保留原 SourceCommit。

最终冻结源码为 `4c65aebff7be80cb84bb8c93b923ce9628b2555d`。`frozen-wpf-result.json`记录同源夹具/介质、引用DLL哈希和清单不变；`frozen-wpf.log`为新增密码窗口用例，运行器沿现有工具生成，选择该用例。`frozen-system-broker.log`是实际SCM/System宿主与受限令牌的隔离密码服务验证；当前站点凭据未参与。`delivery.json`记录安装器/ZIP身份；`installer-startup-smoke.log`只确认真实EXE准备、识别更新及取消，不等于已更新当前站点。`python-packaging.log`为10项本机文档与安装包检查。PR148集成后的树与该冻结候选分开记证。
