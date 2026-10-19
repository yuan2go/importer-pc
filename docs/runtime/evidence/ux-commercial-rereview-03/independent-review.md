# 授权续修的独立双轴复核

固定比较：`git diff 1a34a1e655a479c9692b85fc8a568aca6a813c17...6ce1d6d0b109bd6a3d9490fac176122b4670b90b -- src tests scripts config`。两条审查轴分别只读执行，不修改源码或代替Windows验收。

## Standards

无新增确定缺陷。

- ShellWindow.xaml保留紧凑布局的可展开诊断入口；操作员登录、登出及设备来源信息在源码路径上重新可达。
- ParcelViewModel.cs通过消息修订号区分旧消息与本次同文回执；无消息读取完成后保留完成反馈，不覆盖独立Pause结果或当前读取失败。
- UxCommercialClosure.cs检查实际MaintenanceScope、读码表格和保存控件禁用，并在finally释放及等待持有任务。新增原生检查覆盖紧凑诊断展开、口令焦点及真实Application登录/登出。

符合AGENTS.md §4/8/10与框架的视图职责、反馈和小客户区重排约束。未发现新增公开范围或版本身份违规；没有需要单列整改的判断性代码异味。Windows原生焦点、滚动、编辑及显示矩阵仍NOT_RUN。

## Spec

无新增确定缺陷或范围扩张。

- WP-03“完成失败反馈、Pause独立”：消息修订区分新回执，无消息成功读取写入实际绑定的Message，反例直接检查Feedback；同文回执、独立Pause和读取失败保留优先级。
- WP-03“实际控件、生命周期”：实际维护作用域/表格/保存控件断言及finally排空修复旧验证器错误对象和遗留任务。
- WP-05“紧凑客户区功能可达”：compact只默认折叠诊断及登录入口，原生脚本增加Application登录/登出、密码焦点、局部滚动和固定Pause检查，保留800×420及超界保护。
- 未修改原请求、Unknown、双位Owner或真实SDK/协议；版本和精确依赖属于同源候选范围。

这些是源码及定向参考证据结论。原生控件、焦点及实际几何仍NOT_RUN，不据交叉构建宣布通过。

两轴各0项新增确定发现；两轴均保留Windows实际执行门禁。
