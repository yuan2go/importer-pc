# 本轮公开参考验证证据

固定运行源码`c78746a8f4417136f8244b514326a83bba58345d`，基线main`08286542bbd007433d85be2cb8d129b366b007e0`。[报告](../../backup-health-recovery.md)、[源文件及日志hash](checks.json)、[三个portable候选](candidates.json)。日志中的本机工作树/SDK/合成临时根被替换，判定与失败栈行号保持。数据全为通用合成输入，无客户资料、生产数据库或私钥。

- main-baseline：原main加同一风险选择器，启动headers/body挂起、停机错误健康事实及超大健康响应失败。原P1 Windows路径是源码确认，Windows实机NOT_RUN。
- related-red2：关联缺陷修复前，旧loopback吞调用取消和PLC查询取消擦除已接受状态均FAIL。
- main-core17-baseline：原main的既有AC17测试在备份守卫失败。测试只等待两条入库、忽略Pause回执；本轮等待两个业务阶段终态并明确断言暂停/维护认证，原业务守卫及原断言不削弱。
- frozen-build/targeted/core17/core18/core19：固定源码重新构建并执行，均有完整PASS标记。
- package-*：三个portable ZIP实际构建；smoke-*：真正解压后启动、持久化、重启及停止。两个wcs-to-plc可选旅程使用独立HTTP接收进程，分别验证前置和附加上传、丢响应原查询及每个接收账执行一次。

Windows实机NOT_RUN；本次未修改UI，未重复WPF交叉构建。真实SDK/设备/协议/现场BLOCKED。候选仅本地保留于工作树`.artifacts/bhr-c78746a/`，没有发布为已验收Release。签名包/旧请求实现身份/迁移SQL未重写。
