# DELIVERY-12 Implementation Plan

Goal: 在既有单所有者工位完成受控后装与同版参考交付，按原AC逐项保留证据和未验边界。
Architecture: Platform声明契约与签名静态校验；Product安装登记、加载和资源；原Supply事务配置为唯一部署修订；原WPF维护抽屉生成参数。
Tech Stack: 锁定.NET10/WPF/SQLite，BCL RSA-PSS/ZIP/ALC；不新增未锁定第三方包。

- [x] S0：核对main/并行PR、隔离工作树、原字节归档、F-04/F-06/F-10与工作包；Windows入口缺失登记。
- [ ] S1：Platform ExtensionContracts/ExtensionPackages/ExtensionLoading，Product组合清单与构建裁剪；负例验证路径、签名、依赖、配额及兼容。
- [ ] S2：Product ExtensionManagement + installer CLI + 受限Windows broker；日志阶段和中断恢复/重复/卸载测试，不加载代码。
- [ ] S3/S4：StationConfig统一部署，原SaveConfig/ActivateConfig复核实例/绑定/包，最终来源+包/设备租约；升级失败保旧，旧请求核对和资源故障回归。
- [ ] S5：原MainWindow维护页连接同一管理契约，动态分组表单、版本/有效对照；修首次Loaded暂停，继承页面/焦点/历史策略。
- [ ] S6：冻结宿主后仅引用其公共契约独立构建合成来源插件，签名后装并进入真实来源/Outbox/loopback路径；两种组合与负例。
- [ ] S7：定向Verifier、必要构建/发布/解压冒烟；Windows可执行验证入口和实际环境分项；远端未验预发布上传/下载SHA与内容核对；main PR。

不重复未受影响全量。共享配置/加载/生命周期涉及E08/E11及相关原AC/T1/T2/T3时执行对应风险链。运行命令和实际结果写12 checks；不事先填PASS。Windows未取得时06/AC08/09保留OPEN/NOT_RUN，真实05/D/RV不变。
