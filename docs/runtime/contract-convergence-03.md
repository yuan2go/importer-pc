# 现行合同收敛03

基线 `c96cb382`，按[工作包](../work-packages/WP-IPC-CONTRACT-CONVERGENCE-03.md)继续核查02保留项。历史02报告、D/RV和验证记录不改写；CI停用，原工作区保留。

## B1：部署记录兼容重载

删除DeploymentExtensions双参数二进制构造器及双值Deconstruct。当前四参数记录的Services/Ports已有可选值，当前设备扩展和完整服务/端口部署仍由同一契约表达；本轮不删除这些能力。ExtensionContractCompatibility.Accepts及ExtensionPackages按实际Platform/公共契约字节拒绝旧包，不能用自有旧二进制兼容为桥接保留理由。原签名包及第三方SDK协议未修改。

现有ExtensionDraftVerification继续保护重命名、目标/设备/服务互斥、原候选不变及重复保存，增加同一完整候选与设备候选的JSON往返检查。独立--extension-draft入口运行同一个测试函数，不复制断言，也不启动无关HTTP夹具。Ipc.Verification与五种业务装配按锁文件Release构建，所有公共契约消费者须重编译，不借用旧包验证。

## 待完成

B2实际身份拒绝边界、B3管理/保存检查、最终计数、验证边界和逐项保留定位在本轮后续批次补齐。厂商SDK、物理相机、客户RCS、原生WPF操作、系统安装及提权备份取消均未执行，NOT_RUN。
