# CLOSURE-13 实施与交付

基线main feed6e9c；[工作包](../work-packages/WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)、[本轮原始指令](../work-packages/Codex-WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)、[设计/职责](../design/controlled-extensions.md)、[开发/安装/恢复](../extensions/developer-guide.md)。原12记录、原候选、原始要求与D/RV均保留。本轮用户决定Mac开发与WPF交叉构建，Windows实际验收后续统一进行；未寻找VM/runner，未扩OAuth，实际截图0。

当前核心实现与Mac参考定向验证完成，WPF交叉构建0警告/0错误。候选生成及远端回读正在执行，尚不作为已完成交付；逐项最终状态随后补入本报告及checks。Windows为WINDOWS_NOT_RUN，真实SDK/WCS/现场资格仍BLOCKED。

实现路径：Ipc.Platform包身份/AssemblyRef/依赖/契约注册；Ipc.Supply.Contracts公共契约；Ipc.Assembly与Ipc.Inspection真实业务选装；Ipc.Product.IntegrationRuntime及独立IntegrationExtension；WPF ExtensionsView/MainWindow业务门面与服务编辑。必要测试及初次失败原始日志保存在[evidence](evidence/arch-extension-closure-13/)。候选输入提交、精确版本/哈希、下载回读和AC13表在最终交付步骤登记，不以PR或源码存在代替验收。
