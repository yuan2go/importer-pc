# PR81 合并前角色目录审查补修

2026-10-21，基于 main `20251b4c10db76a37f4d204332e3e6cf68340429`。PR80已合并；原PR81体验记录中的“PR80仍开放”保留为形成时快照。此次处理[自动审查P2](https://github.com/yuan2go/importer-pc/pull/81#discussion_r4333532956)，实现及测试提交为 `75b47d49a8c5ee790b90c595c21525123ac09644`。

更改“实例与角色绑定”后，旧目录名称曾继续指向旧实例，新绑定实例未显示对应业务角色。修复使对象标题通过INotifyPropertyChanged同步目录及页头，监听当前候选的角色选择，并刷新搜索上下文；更换模型时注销旧订阅。原表单、对象引用、草稿、选区和焦点保留，不重新加载配置或重建业务。

新增回归先在原产品2215487复现：原33项通过后，角色目录更新断言FAIL，见[原失败](../evidence/parameter-settings-v3-experience-01/role-review/before.log)。修复提交重新Release构建0警告/0错误，本机Windows125%专项38项通过，涵盖角色重绑/解绑、搜索定位与草稿/焦点/选区，见[回执](../evidence/parameter-settings-v3-experience-01/role-review/RESULT.json)、[原生日志](../evidence/parameter-settings-v3-experience-01/role-review/WINDOWS-WPF.log)和[原生截图](../evidence/parameter-settings-v3-experience-01/role-review/role-labels.png)。未删断言、吞异常或跳过原检查。

现有`--parameter-v3`仓库门禁纳入完整字段体验回归；最终集成结果以[PR81检查](https://github.com/yuan2go/importer-pc/pull/81/checks)的实际提交和CI产物为准，不改写上述本机提交身份。本机日志为真实WPF HWND/Dispatcher和显式合成配置；没有发送业务请求或提交保存。

继续使用项目UI/UX Pro Max独立`--stack wpf`通知/绑定建议与Impeccable Operate/WPF既有界面基线。限定修复现有名称投影，不新增平台、依赖或设计体系。未运行网页/CDP作为原生验收。

PR80原1.7.0介质保持`bfec0a5672d846a865db62af9ef6a573a953f914`及原EXE/ZIP哈希，本次不替换。原体验e544492、本机输入95ad990及初次PR81 CI c3455eff各保留身份。目标工控机、完整IME、触摸、真实SDK/RCS、现场及包含本补修介质的实际安装启动仍待验证；本局部结果不扩张为旧包或现场验收。
