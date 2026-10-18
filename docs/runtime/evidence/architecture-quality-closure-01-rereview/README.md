# PR24 五项复审整改证据

审查输入a2e987a，main b2dc55b；固定整改/测试/候选源码7850fcc02b04d1b572fcb89215945ee0f60be28c。checks.json区分实际命令、返回码、原失败与未验证边界；runtime-source-files.json核对冻结源码，signed-test-host.json固定签名验证宿主。原编译测试输出未覆盖；独立签名测试副本仅使用逐字节匹配冻结checker的公共/核心DLL。

input/review-result.original.json是独立审查JSON原字节，保留历史CHANGES_REQUIRED，不是原始审查01 PDF。log-custody.json绑定本轮原日志和公开路径规范化日志的两个hash；只规范化私有绝对路径，原诊断与断言保持，原字节另存本地不可变artifact。编译失败后执行旧DLL的日志不能作为该失败源码的PASS。历史失败及b82失败候选目录不删除。

candidate-readback.json是两个新ZIP分别独立解压的清单，原3d候选不覆盖。smoke-*.json来自实际选定Assembly进程，WcsToPlc两旅程使用原独立HTTP参考接收进程；e1接收器hash见checks，不冒称本轮重建。精确Debug1.6.8/Release1.6.9只提交.identity.json，不提交私钥、信任运行根或二进制。插件最后相关输入提交b82与785宿主构建分别登记。

compatibility.json核对SQL1～4、Platform/公共契约源及旧Supply源字节，外部NuGet版本未升级。archive检查按原267项(code,path)精确集合比较，工具没有降标。Windows实际NOT_RUN、真实SDK/合同/现场BLOCKED、原PDF01未取得BLOCKED；新结果待独立复审。完整解释见[整改结果](../../architecture-quality-closure-01-rereview-results.md)。
