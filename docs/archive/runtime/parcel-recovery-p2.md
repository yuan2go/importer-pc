# Parcel恢复P2与custody竞态修复

基线为执行时main `baac260214f910dca9ae4628ba6449b25188e2ac`；PR26已由外部授权合并（head `78298b5dc7ccdcb7bb08e857d2d94f3681182fde`，merge `9a5d87ad41e3e936f56a7e58ec60de8f7aa53458`）。本补丁源码冻结于 `fe4a4a2198874a8a5250ce67d50c849ba833c7c0`，后续提交仅整理证据/入口/manifest。PR23/24/26的已有配置、原件、归档、维护和扩展机制继承，不作为本次新增实现。开工后复核main仍为baac2602；并行PR27/29/30以及随后发布的PR31未合并、未改动。本分支只修独立复审S1/Q1和custody夹具，PR31的DB4预迁移/旧冲突工作另审。

## 实际变化

|缺陷|修复与验证结果|
|---|---|
|S1：离线原发布Query遗漏永久回复身份|外查和本地RawDurable重放均在业务接受前查首次原hash。DB4+复用永久索引；DB2/3沿原occurrence原件顺序读取，无新账。65条非终结回复后重用已裁剪ID的不同字节进入LegacyQueryConflict；DB3/4、外查/本地两入口均保留原Awaiting、图像、完整备份、原请求/次数和原版本。相同ID/相同字节的两入口仍可幂等升级。|
|Q1：Query依据随64条投影裁剪丢失|改以持久关联、Committed Query原件SHA、冻结请求/payload/配置、永久身份与当前终态一致性核验。67个缺raw发生分别核对后全部依据有效；实际重启及候选激活成功，67份原责任仍Awaiting，原图仍保有。相反终态、改冻结配置和改raw均拒绝。|
|custody：Wait可用到Command之间TOCTOU|实际Monitor在Configure=true快照后占用Health外呼；命令明确Rejected，修订/责任/owner不变且无Query。释放同一外呼后成功核对；后续探测在预约前受测试异步闸门控制，无时间碰运气窗口。生产CanConfigure、排空及命令守卫不变，不重试未知外呼。原恢复、原图释放与归档断言全部保留。|

没有改公共Contracts/Platform、SQL1～5、配置格式或已有包身份。internal Monitor调度缝默认为空，实际运行保持原探测/资源围栏。没有改页面、三个Application的业务边界或DirectPlc延期范围。

## 原FAIL及原因

[证据目录](evidence/parcel-recovery-p2/README.md)保留原始并发FAIL（SHA256 `9f2b79663b4ce0b04d80087447dce90ba002d9ca38012b3f5639c33e384f10c3`）、独立重跑PASS和仅增加Wait后的PASS，各自字节不变。原FAIL直接证明未形成Query；原Awaiting、owner、原图及WCS事实仍在。按原命令序列、审计和原库排除其他正常提前返回后，剩余正常拒绝来自Monitor所持externalWaits围栏。这是基于源码和持久事实的推导：原运行没有记录命令返回值、瞬时计数及具体port，不能冒充原始trace。

独立旧DLL受控探针和本补丁测试则直接观测了完整占用→拒绝→排空→成功序列。参考Health即使同步完成，ExternalAsync仍释放gate再排队重取，计数归零前有窗口；不需要假设对端慢。原FAIL不是已证明的custody丢失、死锁或设备故障，独立重跑PASS不能解释它。本次受控测试并行于配置/维护回归时也通过，结论依据调度与原件断言，不依据重复运行数量。

本次补码前RED保留6项失败（四种旧格式/入口及依据/重启）。实现期间的编译失败和随后误用旧输出目录再次得到6项FAIL的输出同样保留，未记为业务测试PASS；修正局部变量名并完整构建后再执行最终结果。原独立S1/Q1探针FAIL也保持原字节；Q1原日志的FirstLost误标签指向原Submit，判断以LostBases及原独立SQL核对为准，新测试明确验证被裁剪的中间Query。

## 定向验证

以下实际命令组均exit0，原输出及哈希见证据：

- Release Verification构建；`--parcel-recovery-p2`、`--receipt-custody`（受控及并行运行）、`--configuration-confirmation`、`--review-closure`。
- 受影响`--architecture-quality`（配置断进程、旧状态、责任、容量、冲突）、`--application-architecture`（三Application/资源与拓扑）。没有循环跑全库。
- `--legacy-custody-upgrade ORIGINAL_RELEASE ACTUAL_OLD_FIXTURE`：真实8981d54发布DB3，维护子进程在原Query刷盘后exit76，重试本地完成、不再外查，升级DB5且不清原责任。新建旧格式的身份反例另标合成，没有冒称真实历史库。
- 新宿主精确合同签名包的`--quality-extensions`，包含Effective/Pending/Frozen、仅Pending备份消费者、未知消费者与安全卸载守卫。
- 新核心及选装ZIP实际解压核SHA，选装包安装/原操作恢复、两独立reader/WCS绑定、完整候选激活、启动失败保旧/保Pending、选中服务维护、解绑与安全卸载。
- 解压核心CLI跑真实旧DB3→匹配原发布Query→DB5→维护确认旧账→当前配置过渡→right新作业；原WCS/Upload接收方Executions均1，原业务文件不变，仅两份运行所有权lease变更。该旧库使用内置provider，包pin=NOT_APPLICABLE；不得冒充扩展包迁移，包引用另由签名组验证。
- 解压核心实际Attached可选能力冒烟：登录、同件重量、PNG上传、丢回复后重启核对、主链独立完成和有序停止。
- Windows x64选装发行物含实际WPF交叉构建；核包清单、SHA和与便携宿主逐字节相同的公共DLL，静态包检查成功。Windows实机/交互/UAC/ACL为NOT_RUN；真实SDK/协议/设备/现场为BLOCKED。

公开归档检查仍为FAIL：267项既有历史错误，与PR26原检查逐项相同，新增0；没有加例外、删历史失败或弱化守卫。manifest按当前全部受控文件重算，原FAIL日志的原字节保留。

规范与需求两条独立静态复核未发现新增可确认缺陷；其结论仅覆盖baac2602…fe4a4a2，不能替代上述运行证据。

## 不可变发行物

运行源码均为fe4a4a2；[完整身份](evidence/parcel-recovery-p2/release-identity.json)包含大小、精确公共DLL、manifest和包hash。产物保存在本任务本地artifact目录，未发布或覆盖既有Release；本PR交付源码、指南和可核验指纹。

|媒介|SHA256|
|---|---|
|core.zip|`92ac82e52374070c8b83e9380fd9422bfcb3bfa9ec40e8a80d201825ca2845f7`|
|selected.zip|`65fca269a3f06360b0f9174f60f144f3232ac813fe282c056371bb46d7b1ccf7`|
|windows-selected.zip|`b21bd763078b331cd2493e8686f107decde4abc5dd928f22f1e89cee85d7b3a6`|

核心0包；选装两包为reference.parcel-capabilities 1.8.2与reference.barcode 1.1.2。扩展声明作用域源码仍为 `1291ee6f02d63a33f45e0ce73adad5c76c6ca389`，未伪标为宿主源码；包针对新精确公共DLL新签名，不覆盖旧1.8.1/1.1.1身份。

## 升级及恢复操作

现行DB5可用本补丁发布正常重启，无schema迁移；原缺raw仍独立待恢复。按[维护指南](architecture-quality-closure-01.md)先停止旧Owner、核DB/WAL和新完整备份，旧库由匹配原发布执行recover-legacy-receipt。不同字节重复ID输出Conflict时不升级、不删原件/责任、不盲重发；保留原发布/备份/LegacyQueryConflict审计调查。已知业务结果不能补造原Submit raw。只有实际恢复该发生的原字节并核原SHA，才能按原策略完成本地T3与原图释放。

配置仍沿同一envelope及三段Owner维护确认；候选只在资源全部成功后确认Effective。选装沿原Store、compositions与消费者登记，随包不等于已安装/激活。回退使用匹配完整备份及原发布，并核对期间外部事实，不能倒退业务账。
