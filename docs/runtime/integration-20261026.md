# 2026-10-26整合执行记录（持续更新）

对应[意图台账](../work-packages/WP-IPC-INTEGRATION-20261026.md)。只记本轮实际执行，不授予现场或发布资格。原冻结回执保持原SourceCommit。

## 起点与文档收口

起点a8cd431456f73e83040f75a0c5e3cc1423440d1b；备份标签pre-integration-20261026。PR114以dea3adac843877f01b23b86a0dd7a7aebc52fee9合入，GitHub Actions 40267988148的Linux/Windows均成功。旧README、INDEX及两份运行说明按原Git字节归档；83条变更文档链接检查无断链。

## PR110同步及默认值修复

56a0ee30合入文档清理后的main。820ed4b6先加入默认监督关闭断言，实际运行失败；随后修复默认值并将启用场景明确设为true，禁用场景确认不创建子进程、不消费预算。保持健康窗口预算重置为当前进程已消费1次，TCP组件身份1.1.0。版本统一为SM1.17.0、DirectPlc/WcsToPlc1.11.0、Contracts1.21.0，DB9不变。锁文件通过真实restore生成。

下列本地执行以820ed4b6加记录的工作区diff为输入，不能称为820ed4b6干净树PASS。完整日志及每次输入/日志哈希保存在仓库外D盘审计目录。

| 执行 | 结果 | 输入diff SHA256 | 日志SHA256 |
|---|---|---|---|
| pr110-build-before-default | PASS (0) | `d39e2068e65d3488c71b9152652bf3e8b56fa2c9dc1199b30c299706b86fad5e` | `d90051db82d67fe6809a66da92d2fa688944cd43f819b448d44710b7c0343d2b` |
| pr110-build-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `a04e7dd8c613a3752f7b140e3ee1a17bb4d283b49248a028deb319a06ed069ed` |
| pr110-build-wpf | PASS (0) | `4eeda3df4b4a91a44fdfd8b8f7718db23a33892fefb49e091cc10c41733755da` | `e662c7280e3173c59812668cd96c2ca2638f551c893de8fd0e13f03de3824102` |
| pr110-compatibility-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `4de76dd29f474af870c1a9e12acb1b48406d31caf4cf1f0c3cd0fe1377a5af5f` |
| pr110-installer-change-guards-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `e979a6c1d9102e960cccf8b004127f578ec66a8dacd226247e4df71f456ec78d` |
| pr110-p0-cleanup-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `48a2853a474ab35f49149e9577d69b2a074671b366c56969920dbd4bb587f572` |
| pr110-p0-deferred-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `c6e859c45f18c6b351bb8d00e04c9bf68f970909c109708dbb254a022b808775` |
| pr110-p0-provider-identity-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `7b371e9f2ed62e5a4c55c0dbec5842b7755b6a694e544a0356818c67fb7e5f63` |
| pr110-p0-rcs-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `49a7a7e37c799ec13ecbd8342e2560a2490235fac605d57a8be68018deaedcd2` |
| pr110-p0-supervisor-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `739a35614ca6792ae54c37963a942a02041ae106b9e821dfb0494af715c2c7ea` |
| pr110-p0-tcp-components-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `4869d84aad8203a5c31bafb8605e4868bc5cf77868f39749b74a61ed773101a8` |
| pr110-p0-tcp-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `6b740c0678bcc8aa772d5104904ae87889d05fda4d82a55e6a0e8f54ab98a379` |
| pr110-p0-unknown-restart-green | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `5192287e876b58458291d8372f7cebd82646773eb35d5154f390fc74a8297fba` |
| pr110-recovery-default-red | FAIL (3762504530) | `d39e2068e65d3488c71b9152652bf3e8b56fa2c9dc1199b30c299706b86fad5e` | `cca934b7e228a392ab127e2060cc4313b708a0d5ba1774fe95d643f5a8a53f9b` |
| pr110-restore-DirectPlc | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `a1ff4b020fe48d464795e03be442535bdb3a22c86e7ec67b9c99ca3d69244240` |
| pr110-restore-installer-sportmaster | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `1ef814bf599c8682d25214dc2ba8a853a20b73c480508f1fd4e5a11b850d7e47` |
| pr110-restore-solution | PASS (0) | `8b2068462e582b85d62ed3de0ccaff6ff251415716a713dafb0b997c71e7153d` | `40dfc8f62de7ffe5f29fd0c8574836dad00b0a388105b4160f8369bcde6c698e` |
| pr110-restore-Sportmaster | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `7164ab1aacf7a14db4cf93c5f541b0cd8bcabb6169100df5b2442c2ff3e5ecb9` |
| pr110-restore-Supply | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `26a91ce3176826e55f1b71c0638c7e43ea1b4089624d2286e5b7021f7b3298ab` |
| pr110-restore-WcsToPlc | PASS (0) | `513ae962bbf2b5ece1472298f8ca92850edb0575c013b6f8c26ccf41b2b0fd00` | `a1d949dd4573d74bba7337237f90a39418a9157a8b39eea193126fff7d46ce04` |

本节记录PR110同步阶段；合并及后续实际源见下文，最终main完整验证另计。

## PR113组合预检

在d204bc587478b0f190b3814d7cccec47c132b571干净组合树运行，先继承PR110 5e4b85d5再保留PR113本身差量。冲突仅在索引、版本及锁文件：版本统一不再次提升，锁文件实际restore，清单重算。PR110原回执保持完整原字节，新状态只进索引/本记录。6260份已存在冻结证据、9份迁移SQL、两份字体和WAL/FULL均通过静态身份检查。

| 执行 | 结果 | 源 | 日志SHA256 |
|---|---|---|---|
| pr113-codepages | PASS | `d204bc587478b0f190b3814d7cccec47c132b571` | `47cdbcaa2dbd9a864a9234c02dac264731cce984a734d36d554c0bc0b2e2e0e9` |
| pr113-p0-provider-identity | PASS | `d204bc587478b0f190b3814d7cccec47c132b571` | `30b8b380489f4f5175c8e2b50f1c75c59652957104984ef46d0a403fceb4e266` |
| pr113-p0-supervisor | PASS | `d204bc587478b0f190b3814d7cccec47c132b571` | `20d2e887fe54256d004402245255fab23a8367d4b21d04cb49dbd040d1886659` |
| pr113-installer-change-guards | PASS | `d204bc587478b0f190b3814d7cccec47c132b571` | `e36c2ee0af6a94ebc8de234defa07aedf111f7e401e7d4d8f02a539cd9c3e972` |

三ACP均实际核对GetACP，产品Platform DLL SHA256严格相同；只使用全新D盘夹具根及真实COM，不登记机器安装、HKLM、任务或SCM。此结果尚不等于PR113合并或CI完成。

## PR110已合并及合并后回归

PR110在新头5e4b85d5的CI40269650794三个job全部success、未解决评审线程为0后，以c8dd514458bfcf4820ea587d97da7be64b7dca16合入main。新main Git树与被核验头完全一致。随后在干净main重建Application并重新执行8组P0/安装守卫，全部退出0；这批执行的SourceCommit确为c8dd5144，没有沿用父分支PASS。SQL1–9、6241份起点冻结证据、字体、WAL/FULL再次核对不变。既有CI同时覆盖AQA、保留/可靠性、参数首配及原生Windows回归；CI自带测试介质任务保留其来源，不作为本轮本机发布或现场验收。

| 执行 | 结果 | 日志SHA256 |
|---|---|---|
| post110-main-build | PASS | `b2f54484e67dc89ad331e084fa2c167eb1e981a486358b0b92a3eff30ec85623` |
| post110-main-installer-change-guards | PASS | `ebe2a4207d193ea260e1fecca332fdfa83d3823639a1f9771368919e8485ce6f` |
| post110-main-p0-cleanup | PASS | `f23411fb5efa7e773f9213cf26773c1a5070f2da708ad4aa0a7744f50484f89f` |
| post110-main-p0-deferred | PASS | `ff6b3f70d15ad9646b0b3299f70ceec8f1c6c88c4468420768ac18081716a1e6` |
| post110-main-p0-provider-identity | PASS | `4a760923e5a0cd6f917bcbf7b0216e5c1d6367418069a2f3aeacf7d41ecbeb16` |
| post110-main-p0-rcs | PASS | `97dc60847f593a2371dcfafcf02ba97ef8047037210c7dc2403fc21733c2d2e0` |
| post110-main-p0-supervisor | PASS | `f68d720f68d40e7fb20eeb7501c69b6efb4e0ccb8aad83f4d97e9584c868d813` |
| post110-main-p0-tcp | PASS | `09a4046e81064670bb45597ba76dde8582609685b2e45436705bda8ab6f51908` |
| post110-main-p0-unknown-restart | PASS | `62e95d83aba9ebec5a50dcd96b3d521a0af9b88f118d34ad24cc26ae367c92cf` |

PR113已merge上述新main。此前d204bc58的产品差量与当前组合一致，三ACP及正式TCP/预算回归各自仍只记d204源；PR113新head CI另行执行。

## PR112监督夹具的默认值适配

组合提交fcc1c2d2构建通过，原ST08监督夹具失败（实际启动次数0）。这是本轮Enabled默认false后旧夹具不再进入监督路径，并非永久故障守卫回归。只将两处监督场景显式设Enabled=true，保留首次永久退出仅一次启动、持久Block、恢复暂停及原请求不重放的全部断言；默认关闭断言仍由P0套件验证。原失败日志SHA256 addd05b9d61ed94993d11b718a59c0ab6998f6ac64372ac4edd247632bce92d8，后续结果独立登记。

## PR113已合并及合并后回归

PR113新头d88f6a69的CI40270929013三个job全部success；旧TCP身份P1在正式装配测试确认修复后解决，没有剩余评审线程。以209f31b2212b984ca2339a0a8c7ecf17427dc359合入main。随后在该干净main重新构建并运行三ACP、TCP身份、监督预算、Unknown重启、安装守卫，全部通过。与被检查头的Git树完全一致；相对前一main的6260份冻结证据、SQL1–9、字体及WAL/FULL未变。

| 执行 | 结果 | SourceCommit | 日志SHA256 |
|---|---|---|---|
| post113-main-codepages | PASS | `209f31b2212b984ca2339a0a8c7ecf17427dc359` | `47cdbcaa2dbd9a864a9234c02dac264731cce984a734d36d554c0bc0b2e2e0e9` |
| post113-main-installer-change-guards | PASS | `209f31b2212b984ca2339a0a8c7ecf17427dc359` | `08de91388c340084fa50d8aef8a29c236854a9f4fe8f9ecb2f9cf5f1aed78f95` |
| post113-main-p0-provider-identity | PASS | `209f31b2212b984ca2339a0a8c7ecf17427dc359` | `bc03e59aec93b5af6418dee21e43ca69e83b7f6ce35337e0d2d33d305fdbc8aa` |
| post113-main-p0-supervisor | PASS | `209f31b2212b984ca2339a0a8c7ecf17427dc359` | `6085bc8002cb4c7c4a13d92e0db42f961cb51ca0579de25c3d09dfd1a94f97c2` |
| post113-main-p0-unknown-restart | PASS | `209f31b2212b984ca2339a0a8c7ecf17427dc359` | `a34ff8d0486b7e413dfbdee6c2c106e3c4a667556a4b65135ec784cce291a10b` |

## PR112实际组合验证

678724b76ae0b17b098fad4355bd0033a7ef324b干净组合：时间/原子事务/永久故障监督/真实子进程四组，以及P0正式身份、监督预算、Unknown和部署守卫全部通过。真实子进程证明恢复先暂停，显式Resume不重放；已提交结果不明不再次执行SQL回调。三ACP同DLL及三个Parcel产品WPF锁定构建通过，SQL1–9不变。全是隔离Simulation/合成故障，不能代替物理盘或正式安装资格。

| 执行 | 结果 | 日志SHA256 |
|---|---|---|
| pr112-build-green | PASS | `900953f272816a8e8a20ddbc12394ed36e8b1d5111fc283f5eec10ce8a952490` |
| pr112-codepages | PASS | `47cdbcaa2dbd9a864a9234c02dac264731cce984a734d36d554c0bc0b2e2e0e9` |
| pr112-installer-change-guards-green | PASS | `3880571c50a87de0d76fa85f34ea944bf3384e1c60e7559f288cbc886bb620c1` |
| pr112-p0-provider-identity-green | PASS | `22bf82f39443ea3b49399cc1bb843db8e18e7a07896192686693559e0091e3c4` |
| pr112-p0-supervisor-green | PASS | `ef8f225f1c6ba3b26ea76252f2daf36656120acf09569add37336edea736bff3` |
| pr112-p0-unknown-restart-green | PASS | `66fb22b95e6d3b0bea44cde6f3515222bc436c84e20e7360693ec9a8157e6733` |
| pr112-time-storage-atomic-checks-green | PASS | `17cafae0a6b32b72c740b6fa7af33bf3510e84a3fee7444e0e5568c1aef145fc` |
| pr112-time-storage-fault-checks-green | PASS | `accdd02c97156ec5ac422b76f5ef8dae7e286bde847fd23951c3a7128916fc63` |
| pr112-time-storage-process-checks-green | PASS | `d3d3c866d18d718e2e273b7889ab4e8699ce6dff6b27cbe70f31fe0255b17384` |
| pr112-time-storage-supervisor-checks-green | PASS | `e3f6ff1c1b24e8ce1e666250babe8628dead09a212050f2c3b916ee675bbb6d6` |
| pr112-wpf-DirectPlc | PASS | `d13541ec4b5d4266cca8e92d50ff99a58bb7e933cc687df9d53e1a49cfbc2893` |
| pr112-wpf-Sportmaster | PASS | `4d3045a0927ebce1ca6dc45e00f4d4b92778e8d5f8bfbc3c5e4b68017de92a04` |
| pr112-wpf-WcsToPlc | PASS | `199a1c57b6caecb97c979d881ee4c4da5ef8b9433fb8ab70edde9dc6ecae255b` |

PR112已merge新main 209f31b2，准备转main并执行当前头CI；上述本机结果仍只属于678724b7，不转记新的文档提交。

## PR111合并时恢复ST12单调保留节拍

原容量文件负对照（4da6bf6e加组合暂存差异）实际失败：`ST12 retention notification interval also survives rollback`，日志SHA256 `6f55382939bb3866117fdd43138ae115fca1c70b80b56bce4eba22813586f550`。合并候选4e892a92保持该原文件以定位问题，随后单独恢复retentionCheckedTimestamp/GetElapsedTime，并保留新分级预警/有界预测；首次编辑构建因字段声明遗漏失败，修正后重新构建通过，未放宽测试。维护清理放入备份围栏内，启动计数在gate外且仍受存储故障分类和恢复暂停守卫保护。

下列PASS只属于4e892a92加工程差异 `ee4f0a92b8a0dad37d4aab8bf1070c6ea9a90e4d969d04d4c411785496261a12`，不转记最终main。

|执行|结果|日志SHA256|
|---|---|---|
|pr111-combined-installer-change-guards|PASS|`a2d5510bc4a94e2e1fe93c14944444f659eecee335bf8136ecb318960e57a851`|
|pr111-combined-latency-capacity-checks|PASS|`988ddc0dc574c93666dd40edd25e42b99a62f6cd444f7f0ac386c5d6ff0f131a`|
|pr111-combined-latency-details-checks|PASS|`e26842327b6b8d480cadfaf30e8dedc360ebc3e20372b68b13f54be6dcaa9ebb`|
|pr111-combined-latency-projection-checks|PASS|`f6a6b742c2929df645342daec65d1106723433ef58c943852300660790a0004a`|
|pr111-combined-latency-storage-checks|PASS|`191b48265b983658b0a253811f8c401ef1c1408e8decb872f18ed1f2ffeeff8a`|
|pr111-combined-p0-cleanup|PASS|`c5f12e5b041509a7dbe7265d59031a230b81770a7d91f06a58b072bbd919bdfb`|
|pr111-combined-p0-provider-identity|PASS|`b58bac1a131f6dd416946654cf5d386022ab5cf539b0114fbce77090d9efee6f`|
|pr111-combined-p0-supervisor|PASS|`85da2d5fd04c45c05aa3f0552473f9b15c1657ded0f481af991f7a74782f30c9`|
|pr111-combined-p0-unknown-restart|PASS|`e3a1e030a6d14067249c183f491dc553057958cb96eae221d046cb7b4e509c45`|
|pr111-combined-time-storage-atomic-checks|PASS|`f9c2bc8af157927d75d0bf710e31643c13d6fd685df0c2f2ba93c50488d1e4fd`|
|pr111-combined-time-storage-fault-checks|PASS|`1d2c894bd38492292e984c5f75a018839afe14f75eaf8a0c5ff65e8365d28ee6`|
|pr111-combined-time-storage-process-checks|PASS|`a7a95606674c3a0283ffc1003abad2594583a95e35b21d90fdf85eb58181721e`|
|pr111-combined-time-storage-supervisor-checks|PASS|`2dbfe11abd53e7e4d5b8b8f2c79547ecbe9b8eb1de50dc9733066feba7ce8774`|

## PR112重复回执身份根因修复

评审PRRT_kwDOVLQU8s6wJD82指出LocalClockEvidence随观察变化会破坏重复回执身份。新增测试提交9bfa1b0665e09c7d8d4bc32a264b4cae60e33cd2实际先红：相同回执查询、再次回拨和重启三条失败，日志SHA256 `6c9bbe7de747fd287425eb093ba6828449d18416dc47e24082dce5ed6ad1e76a`。修复从原operation/request/身份hash取得经字节/hash校验的持久原件，相同provider字段复用首次原件；内存投影缺失仍成立，不改首次raw、身份索引、SQL1–9或已有Conflict。不同字段及provider伪造本地证据有拒绝断言。

新代码构建、时钟/重复身份/重启、RCS Unknown守卫通过（9bfa1b06加工程差异58fe7d78d8527ee3b8e85b758a5f5e76825a57288bfd0704fb9d3439dcff2f0d）。本机receipt-custody和reliability-02尝试均被既有Windows指定账户/已安装机器政策入口阻断，失败日志保留，用例完整结果NOT_RUN；不改机器政策。receipt-custody已加入Linux CI，reliability-02本来就是该门禁；等待新头CI结果后判定。测试补充时一次可空类型构建失败已修正，未删断言。

|执行|实际结果|日志SHA256|
|---|---|---|
|pr112-repeat-reply-red-final|FAIL|`6c9bbe7de747fd287425eb093ba6828449d18416dc47e24082dce5ed6ad1e76a`|
|pr112-repeat-guards-build-2|PASS|`32d543dacc24fbf4993dbd84fb4ab23edc473fac675eba6e53e0e07591d4b787`|
|pr112-repeat-guards-time-storage-fault-checks|PASS|`0cca8d020b1637f531143573eb9bcc86c79b7da210aad5598ed5a807d4e004f0`|
|pr112-repeat-guards-receipt-custody|FAIL|`a16b1a3fc73c0004ec8e33b8a93171e2ee03bcc40929fb1b28c7ec35ad550ecc`|
|pr112-repeat-guards-p0-unknown-restart|PASS|`501d00c9dae58952ad60692dbaa9c9011eda4a08869fe86ef72b3cef54ad20a0`|
|pr112-repeat-guards-reliability-02|FAIL|`0a53bcf3899f047961dc7028764cef660686be55a955c1cff6cece4c0f711d93`|

## PR112合入main及PR111计划备份夹具复核

PR112新头000fb311的CI40274393725三个job全success，包含Linux receipt-custody/reliability-02；因此这两套不再笼统标NOT_RUN，区分Linux已执行和本机Windows安装策略入口阻断。重复身份评审线程已凭红绿结果解决，合入main a97d20088c78b55fffc2dde7911a58c11ba199e8。其干净main重新构建及11项时钟/事务/进程/P0/部署/启动参数回归全部通过，树与被检查PR头一致。

PR111源564fab6b复测时计划备份夹具实际失败，首轮COMPLETED计数0，随后一次计划检查完成1份备份。原夹具把到期检查当作当轮维护条件必然满足，未覆盖允许的Pending（在途健康调用等维护守卫）。改为5秒内驱动后续计划时刻并等待实际完成；份数上限检查以成功时刻为基准，还核对原路径和COMPLETED哈希不变，不改生产授权、暂停或在途调用守卫。失败日志SHA256 `bcdb3c8f089a8c1c346b4d00745d4701e66b5512a7731eafcdc7f869952c4f6b`；修正后的实际结果另计。用户要求复核旧NOT_RUN，后续按当前环境逐项判断，不继承旧标记。

## PR111当前组合的实际Windows验证

源5124b196baa00b6b320034307e9b8e22eb7da40f，干净树；计划备份修正夹具、在线取消/停止、P0 Unknown/监督/部署守卫、三个产品WPF及原生验证构建全部通过。60万合成历史预置279.42秒，随后六组各12秒；实际Window共享同一个Application。夹具PASS只断言稳态无重复详情计算/作业序列化，不表示统计不劣或长期资格；本次没有采集新的dotnet-counters LOH序列。原报告反向样本保留。

|驻留|窗口|刷新P50 ms|gate P50 ms|Dispatcher P95 ms|稳态作业序列化/详情次数|
|---|---|---|---|---|---|
|200|1|55.7213|27.1678|0.9899|0/0|
|200|2|62.6675|27.0085|1.0031|0/0|
|200|4|54.5091|20.3215|1.0584|0/0|
|800|1|102.3370|38.2865|1.0550|0/0|
|800|2|96.2622|37.6825|1.1449|0/0|
|800|4|93.3722|35.6435|0.9901|0/0|

完整MEASUREMENTS/IDENTITY与DLL哈希保存在D盘仓库外审计目录；完整60万count查询五次均8 VM steps，冷查询835.37ms、暖查询74.77–88.59ms，UI稳态不反复执行该count。

|执行|结果|日志SHA256|
|---|---|---|
|pr111-committed-installer-change-guards|PASS|`91415d98c6be6ee9586bb03a71343328752c650e179d0352c257ed0c789c2bbd`|
|pr111-committed-latency-capacity-checks|PASS|`dbddff4ede133ae64d4a9283599020cb5731c04f611ad2830939b62f48c72861`|
|pr111-committed-latency-storage-checks|PASS|`38fc4ef4896a036496f602af00def77a2c4b2f11dfdcea43675b5060e0b860cf`|
|pr111-committed-p0-supervisor|PASS|`20a3636cb747a7d82016b08b2a5b55b2c9c554f5839707c38ba9c8e21ddb7e4f`|
|pr111-committed-p0-unknown-restart|PASS|`97047a0f1e2bf1e8c3cb039c6548302f5477d12b0e87b15d1116f971f384b776`|
|pr111-final-native-600k|PASS|`c1e98a9fa52955fe4bf26b5713c7f87aafd7805c72e3899ea7796d8844d9cca7`|
|pr111-final-native-build|PASS|`03f238755d068bbb7f7ebd68331ac4dfd05e13efafa6c19b0745a09fe76b5346`|
|pr111-final-wpf-DirectPlc|PASS|`519ca71166164b1f4146eeb255812ef2cb1f2517a281856c38c8e641064312c7`|
|pr111-final-wpf-Sportmaster|PASS|`98e6dc93d774f18e2f08e76d8a8f491951c93c1562e4fc8639193fa48af72566`|
|pr111-final-wpf-WcsToPlc|PASS|`607004cee52f59a937c46d1e869e28016056b6368c8960094bb4ce0bf972bb07`|
|pr111-schedule-committed-build|PASS|`b1cf8cc1ca4d0e41dd12e612db64753f517eb911626089bcd197e49441bc3bed`|

## PR111合入与PR108语义整合

PR111头735391a5的CI40278220404三job success、无未解决评审，合入cc304049b7a561a9df34c817e7798ca2eb8b719f。该干净main重新构建及14组容量/投影/详情/备份/时间存储/P0/部署守卫全部PASS；6358份既有冻结文件、SQL1–9、字体及WAL/FULL不变。记录只属于实际main，不继承分支PASS。

PR108合并时同时保留：OperationId与Recovery的冻结计划；卸载剩余文件/发布根与恢复任务、WER/SCM/转储清理；缓存快照中的Pending及最近备份状态；后台备份所有权和最近成功；详情可见性缓存与管理权限。六组原生窗口流程覆盖生命周期、安装首配、参数启动、日志、管理模式和原请求链；800×420历史/经典壳实图已检查。

实际新增负对照：6b6c1bed直接调用完整详情方法在撤权后未拒绝（7147ed99…）；显式恢复配置的计划复核被错误判为变化（c69a737c…）。9b34ee19修正前后授权核对及原Recovery保留；详情绿，计划随后到达旧回执兼容用例并暴露null与默认Disabled的错误不等（cfd3f0cf…）。ae5ef217归一化两侧缺省，显式Enabled差异仍拒绝。8aa3d3f1同步新main后的16项真实只读部署预检全PASS，使用历史63795eba介质仅作输入；执行代码是当前DLL，不把这项写成新介质测试。未安装/修改HKLM/任务/服务。

一次组合构建引用不存在clock字段失败，改回既有UTC元数据来源；第一次原生生命周期调用漏设IPC_FROZEN_DESKTOP，在卸载页面前退出，实际WINDOWS-WPF.log保留。补齐当前同源Desktop路径并记录DLL哈希后六套全部通过，未改断言。8aa3d3f1的三产品锁定WPF构建也全部通过。当前头6362份此前冻结文件/SQL1–9/字体/WAL-FULL核对通过。

|执行|实际源|结果|日志SHA256|
|---|---|---|---|
|pr108-app-build-red-source|`6b6c1bed`|PASS|`41d32262b822cf1587972c5c10a2209b4928bb99fb9c05ca4eb4138a7e251017`|
|pr108-authority-green-build|`9b34ee19`|PASS|`54eb0b2a417882e42b2fca27481f69c6ca2df1ee8d9d5d2a598d883f890eaec4`|
|pr108-combined-latency-capacity-checks|`9b34ee19`|PASS|`01ffa2fe9f1346ac95fd8fa97dcb4609cfdd119eec1aa640babfc808162c2400`|
|pr108-combined-latency-details-checks|`9b34ee19`|PASS|`3b9376077ad8915dd8ccecfb1dbea917fa251e88da6f1aa87a4407effa43ea73`|
|pr108-combined-latency-storage-checks|`9b34ee19`|PASS|`42615990ccc5b164a54275742a7fe056e2c7fdf7bd27e6ebfc444f8668678f54`|
|pr108-combined-lifecycle-ux|`9b34ee19`|PASS|`8fa0c127cb85f287f582a79ba5778a6afc9f96c90a0aa70562fa904b22642683`|
|pr108-combined-time-storage-fault-checks|`9b34ee19`|PASS|`21e3a82a599bb9118302895e16362a8558a5d90a2fc8968ead14821a845d5d14`|
|pr108-deployment-build-red-source|`6b6c1bed`|PASS|`543897fbf76fc23c1822a476d60789f726d2f37725d3e00d77cd97ef9522af08`|
|pr108-final-deployment-build|`8aa3d3f1`|PASS|`10eca3a8b5114c8708a5dbdabcc54b4aed488cf4321ee880dceca518f0dd25b8`|
|pr108-final-deployment-cases|`8aa3d3f1`|PASS|`3eca2b6d216cc9fe8e0a933836a4b4fdd120b1df0ed57c0d8b8552354fbc421a`|
|pr108-final-native-build|`8aa3d3f1`|PASS|`d2a010890cfa78038daa3b909df8eb31dcfb1afd9fe0b13d15910a078ddb6234`|
|pr108-final-native-lifecycle-ux|`8aa3d3f1`|FAIL|`e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`|
|pr108-final-wpf-DirectPlc|`8aa3d3f1`|PASS|`9f049f3af383cf31649a624f23de39a4c73a42c3dafe63f9706b09deb5a8cd3f`|
|pr108-final-wpf-Sportmaster|`8aa3d3f1`|PASS|`f1c5981a322ddbf24d509239f09a26ffe55ff73d765664a2844b6bffefd909de`|
|pr108-final-wpf-WcsToPlc|`8aa3d3f1`|PASS|`d8704961264975e461f92f32fe9f30007584e8565a5bd7f2b4d7c131fd5bd89f`|
|pr108-full-copy-red|`6b6c1bed`|FAIL|`7147ed9915e9c508efee3d5922d69e46f43beb60d00a7f31d27cccc09a299c37`|
|pr108-merged-app-build|`41a3a064`|FAIL|`93335d9e1897d56ffef2399741397ce632e502a13cdb346cf73dbdced2191ac6`|
|pr108-native-desktop-identity-build|`8aa3d3f1`|PASS|`e6726ad258889bced74af7044f384c78cd71d7d0014e429dab322b56ce13438f`|
|pr108-native-fixture-identity-build|`8aa3d3f1`|PASS|`55c3acb1780997bf1ea24d154a46208b69a7280257e0ae7376dfcfff4f0484da`|
|pr108-native-qualified-installer-first-run-ui|`8aa3d3f1`|PASS|`82fd5d97377084ce4c5bd5e8106905b45a1026caad62f2ffd6d011545efc8a21`|
|pr108-native-qualified-lifecycle-ux|`8aa3d3f1`|PASS|`b772762b2a3f4186ae0f8587a01a378888451b39bbd8b7b01f1bda3c4e6ce086`|
|pr108-native-qualified-management-mode|`8aa3d3f1`|PASS|`16c6c5ad1915b5cbe3d816be72a5bd79dfb07250908e9097244d8cb704288b5a`|
|pr108-native-qualified-parameter-guidance-startup|`8aa3d3f1`|PASS|`859555497d30c454cd08525eea3ce4e4f5abd7137433f04e7d236d421fd6068b`|
|pr108-native-qualified-reliability-logs|`8aa3d3f1`|PASS|`c228676ca862eea6ac458c8f6b795db584b310ed1ff3690b17dbf0f4281b7397`|
|pr108-native-qualified-task-hierarchy-chain|`8aa3d3f1`|PASS|`bf8afaa1b7d32f2fee54eab5abf18fa2997172cfc1ff2fa5b435c26008d7d112`|
|pr108-recovery-green-build|`9b34ee19`|PASS|`7ad6fd4161017d29a6e7c3bd080e64c73b9c5ce1264da5f2c91521bb64a55c85`|
|pr108-recovery-green-legacy-red|`9b34ee19`|FAIL|`cfd3f0cf349f5ea9d3121993739c142be30b03e751082c9053157f03c4137e4c`|
|pr108-recovery-plan-red|`6b6c1bed`|FAIL|`c69a737c11ace53b9832dd29d0ff3711977c996ead2550501851afe360a94f1d`|
|pr108-restore-app|`665f43a9`|PASS|`8b4b076f1042c6c54346285657cb20285d2d108a17fd3e9d82d1f23900cf74cd`|
