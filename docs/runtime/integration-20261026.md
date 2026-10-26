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

尚未完成：PR110新head CI、后续PR逐项同步和合并、最终干净main完整验证。

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
