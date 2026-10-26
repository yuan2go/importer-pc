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
