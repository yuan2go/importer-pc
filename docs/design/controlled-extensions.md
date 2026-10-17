> 当前执行：WP-IPC-ARCH-EXTENSION-CLOSURE-13，基线 main `feed6e9c`。职责、装配、两项P2及类型化设备/集成接入正在实施；本段是执行范围，不代表目标已经完成。Mac核心验证与WPF交叉构建在本轮，Windows/W01～06、实际UI与现场统一延期，状态保持 `WINDOWS_NOT_RUN`；不寻找VM/runner，不扩OAuth。原当前指引见 [归档](../archive/pre-arch-extension-closure-13/README.md)。

# DELIVERY-12 受控扩展设计增量

本次目标见原字节工作包，起点 main 9f320459。F-04/F-06/F-10在framework-decisions登记；原固定注册仍是宿主核心的可信执行边界，新增受信任后装不是重新seal宿主。

包、模块、实例、设备、角色分别是签名分发单元、契约实现、参数和生命周期单元、稳定资源、业务用途。包及实例进入StationConfig扩展字段，与旧设备绑定/集成参数一起由原SaveConfig/ActivateConfig事务和版本守卫管理；不另设可生效配置账。安装只修改安装登记，不改业务事实或启动来源。仅受控重启选择Pending，锁定最终设备及包，初始化成功后才能Effective；失败保留旧Effective及失败回执，下一受控启动安全返回旧组合。

签名包为ZIP：manifest.json原字节+manifest.sig（RSA-PSS/SHA256）+精确Files。信任来自机级独立离线公钥策略，不读包内证书入信；TestOnly只在Simulation，Production缺批准信任及真实能力继续拒绝。校验身份、契约、runtime/RID、依赖闭包、路径（Windows规则）、配额、哈希、托管元数据后才暂存，操作日志绑定包SHA。临时/发布/登记阶段可恢复，安装过程绝不构造插件。

扩展代码仅使用类型化观察契约；无真实SDK/系统驱动随包执行，无通用脚本/XAML/表达式。参数schema是有限声明子集，分组、类型、范围、枚举、必填、说明、只读/生效级别按实际支持明确，未知语义拒绝；敏感字段仅凭据引用，不持久明文秘密。受信托管ALC共享Platform类型，包私有依赖只解析精确清单，系统框架由宿主共享。ALC不是安全沙箱，卸载不是停止证明，Stop未终止时保留来源和所有资源租约。

管理页在原PAGE-08同一低频抽屉，以真实管理服务提供包/信任/依赖、安装计划、实例/绑定/参数、草稿/有效对照、待重启/失败、审计。每窗拥有编辑草稿及查询代次，版本竞争走原应用CAS。未启动业务仍能查看核心管理诊断及OS授权的安装/恢复入口。

原PAGE01～08保留双通道固定动作、完整事实、波次差异、Unknown/关闭屏障、独立窗口和单提示消费者。首次Loaded前区域不足必须锁存应用Pause请求；放大窗口只解除区域限制，不能解除该暂停。06各格实际Windows仍待取得证据，保护拒绝不记适配PASS。

依据：[Microsoft插件加载](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support)、[协作卸载](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability)，2026-10-17读取；只用于限定工程机制，不代替本次Windows/设备验收。
