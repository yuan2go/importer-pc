# PR26复核修复证据

最终运行、当前随包指南、打包源码为e914e75a109672cd1d52be91c40459f09231ea57；开工main b2dc55b6，收尾继承外部合并PR24的main4afe2afa及最终R03。原审查反例见[592d2bdd原复核](original-review-592d2bdd/evidence.json)，前次审查02证据不改。intermediate-1291ee6保存纳入R03前的构建/测试、1.8.0/1.1.0包及实际旅程；不能代替最终宿主。

[checks与退出码](checks.json)、[源码/指南逐文件hash](source-files.json)、[测试宿主](test-host.json)、[最终媒介/逐文件读回](candidates.json)、[旧发布升级](old-release-journey/result.json)、[选装实际旅程](selected/selected-journey.json)、[选装/库存指纹](selected/custody.json)。最终包1.8.1/1.1.1公共DLL与实际宿主匹配；PluginSourceCommit=1291ee6表示声明源码作用域最后修改，宿主SourceCommit=e914e75，含义分开。没有重签旧身份。

原构建/过程stdout、stderr与失败保留；一次并发custody测试exit134，两断言失败，同一二进制独立重跑exit0，不能抹掉原FAIL或事后猜唯一原因。夹具仅增加维护Availability等待及返回值输出，未删原断言或改业务守卫。实际旧Query维护也有RawDurable后子进程退出的负例，重试仅本地T3。实际选装旅程故意reader启动失败exit -6，保旧/不自动重试的断言通过；外层exit0不表示该子进程成功。最初跨schema探针被flattening守卫拒绝的夹具错误单独保留，最终三schema使用独立种子。

可重放的当前选择器为--inventory-closure、--review-closure、--architecture-quality、--application-architecture、--configuration-confirmation、--receipt-custody、--legacy-custody-upgrade、--prior-custody-migration、--quality-extensions和--signed-capabilities。旧输入需要相应完整原发布和真实形成fixture，精确原输入以原证据hash定位，不用手改新库schema冒充旧发布旅程。

只使用通用实现与合成数据。结构化资料中的本机绝对路径以公开投影替换，原始字节在本机artifact保留且Original SHA由public-projections.json登记；投影不是原配置/原请求，禁止用于原hash验证或直接业务重放。原输出log保持原字节。二进制、签名私钥、信任原文、SDK和客户资料未入库。旧真实fixture无包，pin为NOT_APPLICABLE；实际包保护由独立消费者/扩展组证明。原Query仅改变两份lease运行所有权元数据，源DB/config/raw/image/receiver JSON hash不变，变化逐项登记。

Windows实机NOT_RUN；真实SDK/协议/设备/现场BLOCKED，Production NotReady。资料校验保留历史267项资产错误，新增项另行比较，不放松校验器。
