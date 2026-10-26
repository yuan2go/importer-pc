# 稳定性补修的当前整合记录

> PR115评审更正：下文9f5435d8关于保留全部原断言的叙述不准确；ST03“备份存储阻塞期间快照可读取”断言被遗漏。旧运行结果保持原记录，但不能证明该项。现恢复原断言，受控阻塞反例与正常场景重新执行后另记证据，合并前须重新通过CI。

原本地42e0205a（基线1dfb676a）的F01～F03在独立新分支承接；已merge最终生命周期main ab7e31a9。旧现场回执、机器资料、原分支及外部备份未上传或删除。原文档先于实现提交，当前运行指南的DB8误述改为DB9；SQL1～9未改。

## 实现与先红后绿

- F01：保留同一Installed登记、原配置目录/数据根/运行账户、固定审阅hash且非RepairSeed的原种子；支持的schema、应用/站/模式仍需合法。保留原字节仅允许进入既有维护计划，不能批准Production业务。无库孤立WAL/SHM、恢复/过载标记拒绝；原Unknown、完整备份和CAS仍裁决。4ac7f8e1原种子及孤立WAL实红；5ab5be54补齐。
- F02：安装选项失败保留路径和快捷方式，显示只读可复制诊断；敏感字段隐藏，实际重试先清旧诊断，再根据本次结果投影。4ac7f8e1原生缺详情实红；4e0c00f0实现。05a1dd88原生实测失败→再次失败→成功预检、键盘焦点、输入/账户保留，以及Unknown不重投全部通过。Review测试入口默认仍调用真实服务，没有产品假预检。
- F03：DB7～9合并为逐件原件/永久所有权事务校验；数字正值、非空请求、唯一性、原冻结请求hash、归档hash、存储行的作业/实体/观察身份均保留或补强。失败不部分补录所有权；DB1～6保留旧验证，SQL1～9和WAL/FULL不变。4ac7f8e1空所有权表下的存储行身份错配实红；10d06261修复。17类损坏反例、原子回滚、永久编号下限、孤立责任及既有Unknown升级守卫实绿。

2000条合成T1的交错配对：4e0c00f0旧组合路径669.21～881.85ms，新入口366.25～405.14ms。旧组合是原重复SQL校验加同一当前原件/所有权遍历，不是旧二进制；不代表完整启动、600000/2592000完整原件、目标IPC或24h资格。原JSON及Runtime SHA在本次外部证据保存。

## 额外失败与边界

4e0c00f0容量夹具等待备份进入限速钩子超时，未捕获当时任务终态；不宣称查明了产品缺陷。156098ef改为同时观察真实进入或提前终态，复跑通过。静态核对确认维护守卫可在后台健康工作占用时返回无副作用Rejected；9f5435d8夹具仅对已确认Rejected且没有数据库/恢复目录产物的情况有界等待，异常、未知或部分输出立即失败，失败路径释放限速钩子。全部性能、停止取消、容量、导出及备份集合断言保留，生产没有新增重试。

05a1dd88的两条stability-followup执行使用156098ef已构建Application夹具（PE ProductVersion为1.0.0+156098ef），当时仅CI/包装脚本变化；这两项不作为05a新二进制PASS。9f5435d8重新构建并实际重跑同两项，另跑容量及全部补修，均通过。原始记录不回填。

原生截图installer-preflight-details.png（05a1dd88）已实际查看：880×560 DIP，真实125% DPI；详情可见、可聚焦，正文滚动且底部操作保留。截图SHA256：7043bb2193eceec5e92accb01d456a4d9c90f43272558b307b793e6265248ace。没有把逻辑视口矩阵当作更改系统DPI。

Linux/Windows CI增加stability-audit-fixes；现有冻结介质CI增加原种子、安装首配和生命周期原生流程。当前须等新PR的检查通过，不能以本地PASS代替CI。

## 本轮实际记录

日志和JSON在仓库外 D:/audits/pre-integration-20261026/runs；记录源、命令、工作树状态、单调耗时、退出码和日志SHA。

|执行|记录源|结果|日志SHA256|
|---|---|---|---|
|stability-backup-diagnostic-build|`156098ef`|PASS|`20ce1245b714b303899c24e3abbcf4b14fbf546130e23b867fec141b8c941671`|
|stability-backup-diagnostic|`156098ef`|PASS|`9a8952794e2d950ac1ef4f7b521d43c072586dda8bfa09ef51fc1e1fa49efcdb`|
|stability-followup-p0-unknown-restart|`05a1dd88`|PASS|`cc3936297af0e116f726d9269fb577b9f586720d6ff3145a13b4355a21860483`|
|stability-followup-time-storage-fault-checks|`05a1dd88`|PASS|`b99b857c84d962e003a42e49c8f1759416202cf5a16cf66a26043e4f299985e4`|
|stability-green-app-build|`4e0c00f0`|PASS|`492e168a19c9d494f79052f5e154c722e2ac96f98e997bc08eec7cdc042da638`|
|stability-green-desktop-build|`05a1dd88`|PASS|`9cda0c8a02f375e53dce394b043662d455f69ea672cbd42bfcaac694131a86de`|
|stability-green-installer-change-guards|`4e0c00f0`|PASS|`aba75ba966b4fb3193377d07f13bf157575d1d1f6dbe8a3380a9bc929c840b45`|
|stability-green-latency-capacity-checks|`4e0c00f0`|FAIL|`23673ec782d9a1b00dd0a1c66f02620c379463e6bad6455d5fe1b1d704ee97da`|
|stability-green-latency-storage-checks|`4e0c00f0`|PASS|`0af9e4e34c0b1dbb174e28675dca374fd902e130465aadc32372adf36a8be90a`|
|stability-green-native-build|`05a1dd88`|PASS|`e9787bbb139b55814652771e4489bf9bcba07214e14f1251e150d28d3b9bb6ef`|
|stability-green-native-installer-first-run-ui|`05a1dd88`|PASS|`584e51827e8129272d89365403eddb5a7194ec783cc067268dfdbfb9efe2ec75`|
|stability-green-native-lifecycle-ux|`05a1dd88`|PASS|`7275ef24f06a9c2d6484f0d987ecb303783a1df194f1a0273f7de40cde6f691c`|
|stability-green-native-stability-seed-upgrade|`05a1dd88`|PASS|`c877bdf32395eea93d057dd3adf0d4c875d989c1f1b6366ee8b231d45887eeca`|
|stability-green-stability-audit-fixes|`4e0c00f0`|PASS|`f088b55eabbc3b06279c16d6d596c924d79b17d5b2227aeb92a7a80effe2dca1`|
|stability-history-red|`4ac7f8e1`|FAIL|`0f4395396d4d860f900ef8f60bd93f48245727d7056483aa6c8e9ad9d3ad1435`|
|stability-maintenance-red|`4ac7f8e1`|FAIL|`cb8f6f48175e974ba03942eafee7af6a0857727038d1eb86c855619939f28118`|
|stability-qualified-app-build|`9f5435d8`|PASS|`802306f654bd279f712f906ecb613163720e9cfb44fc6d207a1680009ccabc25`|
|stability-qualified-latency-capacity-checks|`9f5435d8`|PASS|`b400f5cde2cb04a8ead475ebe34f0a6df294d8f4533e2e13492d9f0868ae7725`|
|stability-qualified-p0-unknown-restart|`9f5435d8`|PASS|`6dede36557db4b8ba3dda26abe52108debce1e8c8483a8a58d23c3af071b6f94`|
|stability-qualified-stability-audit-fixes|`9f5435d8`|PASS|`3d0bde66341a2f37b9c96c75f8cf7f175d5ea7c3178fdf86cdd6a24525ec7ee7`|
|stability-qualified-time-storage-fault-checks|`9f5435d8`|PASS|`482da9f29cba86b2d70a5ce3af9c62fea353afc7d86d27c2f6465f7f65bc5e42`|
|stability-red-app-build|`4ac7f8e1`|PASS|`891fddfe26a53d277c87b16c74cab3d517284b7aacf8e455342eab25729057d2`|
|stability-red-desktop-build|`4ac7f8e1`|PASS|`992b2df7c288ccee41bc042f872cbf563d89c09b8d6257759a924f8e89c8c48d`|
|stability-red-native-build|`4ac7f8e1`|PASS|`6cefa622c037bb5fc82fa790a9da08aea8faddefa61f0787b19f5f06e60aa0b1`|
|stability-red-native-installer-first-run-ui|`4ac7f8e1`|FAIL|`28406497f5d77944c329d285fe1d1e32ff4d26bb2608257789f5c46bbdf1d69b`|
|stability-red-native-stability-seed-upgrade|`4ac7f8e1`|FAIL|`7dffc5c50fe92199ff7cbec57a93bdee8de26edde9f9a0cde2bd78a017c7ad0f`|

系统安装/UAC/HKLM/SCM/任务/WER往返、设备/SDK/RCS、目标IPC、24h/72h及物理断电仍需独立证据。本轮运行的是隔离合成数据、真实SQLite/WPF及受控故障，不生成本地交付介质、不安装、不控制设备。TCP自动重连策略、G01权威结果/人工处置、G02密码恢复仍按原外部条件，不引入重发或假恢复。最终main完整验证另记，不把分支结果改记为main。
