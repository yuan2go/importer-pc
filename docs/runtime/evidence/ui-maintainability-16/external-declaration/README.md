# 独立声明式扩展真实验证

宿主源089fd98；单独Git插件源e7b653fc23fcd092a41d1cca48fb77f885ca96b1，合成观察包wp16.external.reader2.0.0。两次真实独立编译、RSA-PSS签名、冻结宿主静态验收，原始manifest/payload完全一致；签名/ZIP随机字节分别记录。没有引用宿主ProjectReference或修改冻结宿主。

journey-b实际执行检查/安装→维护CAS SaveConfig→configs读取原修订ID→显式Activate Pending→受控停止/重启→Effective→两设备独立参数→合成作业接受2→受控停止，宿主字节不变。首轮journey-a仅验证脚本将紧凑日志State误当完整snapshot，原运行时已正常激活；保留原脚本/trace/日志，再加Requests谓词校正读者后新根通过，未改运行时代码或业务断言。

完整命令、原始/公开日志SHA、平台与独立来源/包身份见execution.json；内容只替换机器路径，原字节保留外部。外部源ZIP、两个签名包及identity由候选附件分发，不提交二进制入库。私钥不入源ZIP、证据或包。Windows实机、Job对象和PowerShell为NOT_RUN；真实SDK/WCS/身份/实体/数量及现场仍按独立门禁，不由本合成包放行。
