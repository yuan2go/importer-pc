# 版本与组合验证证据

运行/发布闭包固定 **c4e131968964458467c3a03eddf90832ef85b309**，继承main9a5d87a（PR23/24/26）；随后仅备份测试夹具和文档/证据/manifest及main文档PR28整合。源码变化、实际候选和各次运行分别记录，旧候选/失败不覆盖。

- [组合索引](combination-index.json)：五条实际portable进程旅程、实际Release实现的签名能力组合、一个真实启动拒绝记录。ProviderSets是声明，只有实际执行记录才是局部参考PASS。
- [候选](candidates.json)：ZIP、发行清单、文件清单和源码SHA；二进制保留本地，不上传仓库。
- [检查](checks.json)：各组实际日志和哈希。Windows为交叉发布/构建，不是Windows执行。

五条进程记录的*.events.jsonl是原始stdout JSON；记录同时绑定输入配置和实际Effective配置，读者可用scripts/application_compatibility.py重验候选/配置/事件。signed-release-combination.json为SignedHostVerification：验证器DLL单列hash，加载的业务/平台/契约及驱动/组件来自实际Release目录，公共DLL与候选逐字节一致；签名包精确绑定该宿主。此记录不冒充普通portable冒烟或Windows结果。

unsupported-provider.json保留真实失败，Expectation在索引中单独标记为拒绝符合预期，不将失败记录改写PASS。初始编译、AA22本地维护守卫拒绝、环境和发布洁净检查失败日志均保留；未删除断言、关闭守卫或吞异常。

原SDK/真实WCS/PLC/认证/实体/现场BLOCKED；Windows实际NOT_RUN，Production资格不由上述记录授予。证据仅含合成数据、通用源码身份和本地参考路径，无私钥、凭据、客户原稿或生产数据。

公开组合JSON为显式PublicProjection：仅Command中的本机用户路径改为角色别名，记录OriginalRecordSha256。原始记录字节继续在本地候选证据目录保有，配置/事件及其hash完全未改；索引同时记录公开副本及原件hash。不能把公开命令别名当成原始argv字节。
