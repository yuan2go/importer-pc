# 本机边界回归证据

被测实现由`2f668db0a1bf6650e5088ac63dcf6158e52b81ac`标识；测试完成后提交相同源字节，后续只登记验证文档。逐项命令、退出码、日志原字节SHA256、环境和关键源Git blob见[verification.json](verification.json)，范围说明见[实施记录](../../hikrobot-boundary-regressions-01.md)。

`baseline-mvs-runtime.txt`是修改前`d506f77`的3项基线；其他日志对应本次实现。所有新增用例使用显式合成文件/字节，应用测试使用合成配置及必要环回RCS；没有加载正式SDK、连接/触发相机或操作现场设备。原日志未经改写，不将本地PASS扩张为真实识码、Production资格或现场通过。
