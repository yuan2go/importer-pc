# 审查02 N01–N07原始参考证据

最终Release/指南/打包源码2ad633578141db4ef34e255217bc8815634ffd0a；业务代码bab9c3012652c3b8aae1d90627953734a1de29b8不变；继承PR24而未合并main。[checks](checks.json)、[最终源文件](final-source-files.json)、[测试宿主](test-host.json)、[候选解包](candidate-readback.json)、[原件保管](baseline-custody.json)。

最终AQ读取logs/final-quality.log；Release AA22/资源、签名扩展和七模块分别读对应log。quality.log保留首个新增上传夹具漏能力库存导致的拒绝；build-final/build-receipts保留新断言空值编译失败。quality-final/quality-receipts曾在失败构建后继续运行旧入口，只保留为NOT_FINAL，不计最终PASS。之后采用build成功才run并固定最终宿主/源码，未删断言或降守卫。

[selected旅程](selected-journey.json)对应[参数化重放版本](selected-release-journey.py)：仅合成数据，实际解包、安装、绑定、重启激活、双reader/WCS、显式驱动启动失败保旧及安全退休。[实际CLI恢复](recovery-cli.json)保留DB3及原责任；[核心进程](smoke-core.json)和[独立HTTP Attached旅程](smoke-http-attached.json)使用原smoke-application入口。包身份JSON与实际Release公共DLL精确匹配；私钥、信任原文和运行二进制未入库。

Windows实际NOT_RUN，真实SDK/协议/实体/现场BLOCKED；没有生产资格或旧HTML/RV关闭结论。日志中的来源/路径/条码均为本轮合成环境，不含客户原稿或生产数据。

selected-journey.json为公开可移植投影，仅替换本机artifact根路径，原JSON与原执行脚本保持在本机授权artifact目录；原字节SHA分别登记于PublicProjection和脚本首行。投影不是原请求或原配置，禁止用于重放。

最终能力包1.7.2/观察包1.0.12与2ad Release公共DLL匹配；早期1.7.0/1.0.11及bab9候选/宿主按*.original.json及Git原记录保留。最终退出码见process-exits-2ad.json，全部参考进程0；Windows实际未验。
