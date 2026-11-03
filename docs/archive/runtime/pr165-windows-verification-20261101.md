# PR165 Windows 补证

2026-11-01。产品/测试源码 `3e79ec7a04f6acc818efc687589898cf0ad792b7`，整合 main `aebd476d`（PR163/164）。原 Linux 证据、NU1102 和 DPAPI 拒绝保持原轮次，不改记 Windows 成功。修改前索引/报告原字节见[归档](../archive/pre-pr165-windows-20261101/manifest.json)。

先在未修改的 PR165 前产品上仅加入新增 UI 夹具，实际运行 Windows HWND/WPF。原7项断言通过后，迟到持久化结果离页清理断言失败，复现原问题。修复版核心与 WPF 全量 Rebuild 均0警告/错误；原生三种迟到结果（离页、重进、另一窗口新轮次）均通过，旧未发送意图审计取消，其他窗口新轮次保持可发送，HTTP次数不变。

18项生命周期回归通过，使用显式Simulation、真实SQLite及内置RCS loopback，涵盖容量1、普通新准入仍拒绝、明确替换、ID真实耗尽形成Wcs=null、幂等清理、Accepted/Unknown保全和原始回执待T3拒绝取消。原11项人工调试断言保持；Production配置的manual-supply与manual-rcs-debug套件、camera-rounds及整合PR163后的shutdown-resources全部通过。Production配置使用显式合成相机端口，不代表真实SDK合格。

未修改产品补丁，未绕过DPAPI、管理权限或原请求守卫。仅为原Linux日志和归档补充二进制保真属性，并按已有Git blob恢复工作副本，防止Windows换行转换；原字节与哈希保持。原生截图已查看，人工来源和无相机照片状态、冻结请求与原响应分别可见，compact准备入口回归通过。未新增视觉设计或浏览器验收。

精确断言数、原始合成日志和哈希见[summary.json](evidence/pr165-windows-20261101/summary.json)。真实SDK/客户RCS、系统安装、目标机/现场均NOT_RUN；CI停用、D/RV不变。必要Windows补证完成后可按正常PR保护合并；PR162三个原审查问题已有代码及实际回归依据。
