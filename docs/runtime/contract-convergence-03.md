# 现行合同收敛03

基线 `c96cb382`，按[工作包](../work-packages/WP-IPC-CONTRACT-CONVERGENCE-03.md)继续核查02保留项。历史02报告、D/RV和验证记录不改写；CI停用，原工作区保留。

## B1：部署记录兼容重载

删除DeploymentExtensions双参数二进制构造器及双值Deconstruct。当前四参数记录的Services/Ports已有可选值，当前设备扩展和完整服务/端口部署仍由同一契约表达；本轮不删除这些能力。ExtensionContractCompatibility.Accepts及ExtensionPackages按实际Platform/公共契约字节拒绝旧包，不能用自有旧二进制兼容为桥接保留理由。原签名包及第三方SDK协议未修改。

现有ExtensionDraftVerification继续保护重命名、目标/设备/服务互斥、原候选不变及重复保存，增加同一完整候选与设备候选的JSON往返检查。独立--extension-draft入口运行同一个测试函数，不复制断言，也不启动无关HTTP夹具。Ipc.Verification与五种业务装配按锁文件Release构建，所有公共契约消费者须重编译，不借用旧包验证。

## B2：身份顺序检查改为行为检查

删除Python里Supply/Inspection运行入口的源码字符串顺序检查，保留独立维护入口的权限/返回分支检查。Ipc.Verification现有--runtime-identity入口同时运行实际ProductRuntime和InspectionRuntime：配置文件故意无效，注入身份拒绝必须先发生；Supply保持NotReady，Inspection保持异常；Dispose后原字节和目录文件集合不变，无数据目录/设备构造。生产默认仍是MachineRuntimePolicy.RequireRuntimeIdentity，注入仅为friend验证程序集可见的internal init属性，不新增部署开关或放宽策略。

Ipc.Verification locked Release构建、共享身份政策/两项实际入口检查和4项Python启动派发检查通过。验证工程增加Inspection项目引用，锁文件按当前SDK重算，没有包版本升级；全部产品依赖锁保持不变。原身份指南原字节已归档，当前入口说明新的验证边界，原Windows/ACL/系统安装记录不改写。

## B3：管理模式检查服从自动保存合同

删除OperatorAdministration中的两个过期检查：全模拟配置切换Production后期待旧认证异常；已授权SM仍因未手动暂停而拒绝保存。没有把异常类型改宽来凑通过，也没有把错误的拒绝断言改为保留旧分支。首次基线在前一检查失败，实际异常来自现行SM相机准入；原日志保留。

生产旧配置拒绝由sm-built-in-only保护，当前不暂停自动等待/保存/Pending/Effective由mvs-offline-activation保护。management-mode完整入口通过，包括撤权、过期、一次性授权、原密文保全、参数读取与保存权限、日志及异步授权代次。mvs-offline-activation、sm-built-in-only、runtime-identity-boundary均通过。原请求/Unknown及存储只读拒绝另外由audit03-rcs、storage-schema-only复验通过。

## 待完成

B4进程夹具、最终计数、验证边界和逐项保留定位在本轮后续批次补齐。厂商SDK、物理相机、客户RCS、原生WPF操作、系统安装及提权备份取消均未执行，NOT_RUN。
