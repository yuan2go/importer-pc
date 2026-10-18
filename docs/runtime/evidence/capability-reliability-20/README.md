# 可靠性20 · 合成验证与候选证据

最终运行源码a147eac7e4bafb70e16b2c677c2a2933909234d3；执行基线08286542bbd007433d85be2cb8d129b366b007e0。全部外呼为独立合成接收端或测试端口，没有生产设备/数据。Windows实际NOT_RUN，真实认证/SDK/服务/现场BLOCKED。

- [checks.json](checks.json)：命令、结果及实际日志hash；logs/ipc20-a147-*为最终冻结源。候选打包/启动日志在同目录。
- [candidate-inventory.json](candidate-inventory.json)：四ZIP完整SHA/长度/源提交/解包检查；二进制未入Git。三个portable及一个WCS→PLC Windows闭包。
- smoke-{sportmaster,direct-plc,wcs-to-plc}-default.json、smoke-wcs-to-plc-attached.json：实际解包进程旅程和独立接收事实。
- [signed-reference-identity.json](signed-reference-identity.json)：实际签名1.3.1身份、冻结契约和源码。私钥/信任配置不入Git。
- [signed-schema.py](probes/signed-schema.py)：独立签名metadata探针，仍是合成参考DLL，声明endpoint schema后只测试宿主地址校验；不是自称真实服务或生产资格。真实参考链用原正常参数执行，HTTPS探针不访问外部地址。探针仅用于临时测试store，实际ID/版本/hash以静态检查日志为准，不作为分发产品。
- [original-boundaries.json](original-boundaries.json)：原SQL1、Supply源码/SQL和D/RV原字节比较。
- [superseded-97d9efc-candidates.json](superseded-97d9efc-candidates.json)：迁移前守卫补齐前诊断候选，非本次最终产物；原字节本地保留。

logs/diagnostic-*保留基线红灯及夹具排空/过早Pause、schema未声明endpoint的负证据，不计成需求通过。基线无图取消日志明确为Abandoned/Capture Cancelled且未释放，非仅未完成取消。系统Python最初缺Pillow的结果另有本地原件；最终[完整资料核验](archive-checks.json)使用带Pillow环境，仍为FAIL：265个原PPTX媒体类型和2个原UI元数据标记；新增身份/路径/链接错误0，未修改原件或校验器。

后续文档/manifest/PR回执提交不改变冻结源码与候选身份。完整逐项结论见[实施回执](../../capability-reliability-20-results.md)，操作与复现见[指南](../../capability-reliability-20.md)。
