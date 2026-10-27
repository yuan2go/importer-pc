# 独立审查回归

`Program.cs` 保留2026-10-27对 `e15631fe` 的20项原始审查断言，源内基线注释也保持原样。修复前16通过/4失败；本项目通过 `ReviewedRoot` 引用**本次工作树已构建**的Platform、通用驱动和两个SDK驱动，并非复用基线DLL。默认根为项目目录的上两级；可用MSBuild全局属性显式指定另一份已构建源码。

先按运行说明构建Solution及SDK Probe，再以Release/RestoreLockedMode构建此项目，执行输出的 `Ipc.Drivers.Hikrobot.Probe.dll`。程序集名复用现有测试friend边界，不修改生产公开API。

全部设备输入为合成端口、TCP为loopback。四个原失败断言没有删除、跳过或放宽。日志隐私断言在本轮用户授权修复后采用遮蔽诊断副本的规则；业务原账和原请求字节另行验证。
