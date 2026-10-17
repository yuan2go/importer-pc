# CLOSURE-13 PR12复核与最终候选补充

核心工程完成；Mac参考验证PASS；WPF交叉构建PASS（0警告/0错误）。**WINDOWS_NOT_RUN**：实际WPF交互/截图、普通账户/UAC/ACL、DPI/跨屏/输入/听审/持续运行统一后置；真实SDK/WCS/现场资格继续BLOCKED。没有寻找/开通VM或runner，没有扩OAuth，没有合并PR或改变D/RV及12验收。

本轮先核验最新main仍为`feed6e9ce79449e48c667c17e2671939a86fb588`，相对指定基准无变化；发现同目标[PR12](https://github.com/yuan2go/importer-pc/pull/12)及工作包已存在。既有工作树干净且对应任务未在执行；从main创建`codex/closure13-verification-20261017`隔离分支，快进承接`451f7f5`并向同一远端PR分支快进交付，避免重复实现/重复PR。原main工作区及未跟踪客户材料保持原状；另一个工作树文件未修改。[开工记录](evidence/arch-extension-closure-13/recheck-20261017/opening.json)保留本轮观察，不倒写首轮“当时没有并行PR”。原[工作包](../work-packages/WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)、[原始要求](../work-packages/Codex-WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)、[首轮报告](arch-extension-closure-13.md)及全部历史证据继承；仍未收到额外独立附件。

## 实现与身份

- `06dc9c8`/`dc5b405`：继承的职责/构建装配、永久包身份、静态依赖策略、类型化集成、两类实例/部署、T1冻结原请求和资源所有权。首轮前置归档`2c04d69`保持原字节。
- `a1c41bc`：修复[PR12服务重命名P2](https://github.com/yuan2go/importer-pc/pull/12#discussion_r4301571017)并覆盖同类设备风险。`ExtensionsView.editedInstance`保留原选中身份；`ExtensionDraftEditing`原子替换原实例和绑定，拒绝重名、跨类型重名、占用端口及过期选择；显式“新建另一个实例”保留其他实例。实际UI调用同一Mac验证策略；保存/激活仍经原应用权限和CAS。两类新包改为1.0.1，组合清单驱动精确包版本和冒烟选择，保留原1.0.0。
- `8cb475b880e6cdd248db42bff74accf443c83519`：最终宿主/构建工具/配置冻结源码。`build-extension.py`解析macOS物理路径并映射整个临时根，防止随机PDB路径改变插件内容；新增真实重复构建验证。基线维护测试只对明确执行前Conflict重新读取投影，保留Persisted/失败恢复断言，并新增陈旧命令必须Conflict的断言；运行时CAS未改，未知结果/异常/业务拒绝不重试。

插件独立源码仍分别为读码`6507edb4c10699df3bdeb8f91da3bf31f81b94f9`、集成`01038cc5e21d200e4545c0a26594adc05eb62923`；分发版本均1.0.1，构建工具输入为8cb475b。ExtensionContract=1、Storage/MinimumReader=3/3，原SQL/checksum、HostContractSha256和StationConfig未删除或改主版本号。两目标公共契约SHA、模块/组合/包/原始manifest及ZIP哈希见[精确清单](evidence/arch-extension-closure-13/recheck-20261017/final-8cb475b/candidate-inventory.json)，[源码冻结核验](evidence/arch-extension-closure-13/recheck-20261017/final-8cb475b/source-freeze-integrity.json)证明后续仅文档/证据/manifest变化。

## AC13逐项交付

以下实现与首轮表共同构成完整AC13记录；所有本轮PASS绑定8cb475b，不把首轮结果冒称重跑。Windows列始终WINDOWS_NOT_RUN。

|AC|实现/commit|本轮命令与证据|结果及剩余|
|---|---|---|---|
|AC13-01|继承2c04d69归档/职责/决策/13工作包；隔离承接451f7f5，8cb补充失败解释|本轮opening；main/PR核验；原INDEX/framework/controlled-extensions/12证据/公开边界/UI实物继承；归档逐项对账|PASS；历史D/RV、12与原资料许可不改|
|AC13-02|Platform/BusinessAssembly，Ipc.Assembly，Ipc.Inspection，Product/ReferenceProduct；06dc9c8/dc5b405继承，8cb冻结|V2、V9～V11；实际两个发布组合核心进程启动、命令、查询共享RuntimeId、Inspection持久批次/采样/关闭、停止|CORE/MAC PASS；未造第二客户业务或完整UI，Windows未验|
|AC13-03|ExtensionStore、ManagedAssemblyPolicy、ExtensionPackages/Loading；06dc9c8及8cb构建器|V2/V3：跨卸载/退休/中断/并发的永久内容身份、精确重装/修复；缺实际引用、假System.*、版本冲突/歧义拒绝，合法依赖实际解析且预检不执行构造；V12八次真实重复构建|P2-01/P2-02 MAC PASS；没有清登记或放宽检查换PASS|
|AC13-04|Supply.Contracts/IntegrationExtensions、Product/IntegrationRuntime、独立IntegrationExtension；01038cc/06dc9c8继承|V2/V10：类型化编码/验回、发送/原查询/健康/取消/超时/Stop/Dispose/诊断；两个后装包进入实际供包→SQLite/Outbox→独立本地接收端|REFERENCE PASS，唯一接受2；厂商驱动/真实WCS仍需资料及后续准入|
|AC13-05|DeploymentExtensions/ExtensionDeployment、ExtensionsView/MainWindow、ExtensionDraftEditing、共同管理契约；a1c41bc/8cb|V1/V2/V3/V5；参数/目标隔离、角色/端口、schema、统一部署CAS；重命名/禁用/新建/重名拒绝及输入草稿不变的真实UI策略；[Windows场景](validation/closure-13-windows-scenarios.md)|ENGINEERING/MAC PASS；实际页面操作/异步回填/焦点 WINDOWS_NOT_RUN|
|AC13-06|StationApplication.Prepare T1冻结、IntegrationRuntime原身份路由，Product/Sender/Observation终止屏障；原实现继承|V2～V7：原Unknown查询/唯一接受账、缺旧能力阻断、构造/升级失败保旧、在途发送/查询、慢Stop/Dispose保留库/站点/设备/包与依赖卸载围栏|MAC PASS；不重写旧ID/报文/hash/合同/目标，不默认重发，不回滚业务事实|
|AC13-07|8cb最终源码，原有效Verifier断言及新增草稿/重复构建/陈旧版本断言|V1～V12；六个受影响选择器全PASS、工具10 PASS、冻结宿主后双插件独立构建/安装/运行哈希不变、两个发布业务真实运行|MAC PASS；初次失败与撤销候选保留，无跳关键恢复或删断言|
|AC13-08|8cb冻结四组合/插件1.0.1；后续报告/manifest与Windows增量输入|V1/V8～V13；12附件全量远端回读，ZIP CRC/内层文件与精确组合校验；[checks](arch-extension-closure-13-recheck-checks.json)|候选交付PASS，WPF_BUILD_PASS；实际Windows截图0/现场延期，不覆盖旧候选、不合并|

## 命令与最终原始证据

固定SDK10.0.402，`dotnet`指向本机该固定SDK入口；机器私有路径不作为公开交付身份。根目录执行；各最终日志及JSON均在[evidence目录](evidence/arch-extension-closure-13/recheck-20261017/final-8cb475b/build.log)同目录。

|编号|命令/结果|
|---|---|
|V1|`dotnet build ImporterPc.slnx -m:1 -nr:false -p:EnableWindowsTargeting=true`；0警告/0错误，含WPF交叉构建|
|V2～V7|`dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --architecture`、`--extensions`、`--baseline`、`--followup`、`--review-followup`、`--closure`；六个实际选择器全部exit0。日志依次architecture/extensions/baseline/followup/review-followup/closure.log|
|V8|配套含Pillow的Python执行`-m unittest discover -s tools -p 'test_*.py' -v`；10 PASS；tools.log。系统Python缺Pillow的原失败另保留，没有删断言|
|V9|`python3 scripts/package-reference.py --composition integration-reference --target portable`及`win-x64`，指定外部测试私钥/独立公钥政策、固定dotnet和全新输出；另`package-business.py --composition inspection-reference`及`supply-assembly`；四次exit0，package-*.log|
|V10|`scripts/smoke.py`与`scripts/smoke-extension.py`对冻结发布宿主执行；core-smoke.json七项PASS；extension-smoke.json两类包安装、实际来源/请求/接收账、接受2及Stop、宿主前后哈希不变|
|V11|两个发布`app/Ipc.Assembly.dll --config <app/config> --data-root <fresh> --view inspection-history或history --layout compact`，实际JSON命令/`snapshot`/`quit`；[命令、回执、查询和退出](evidence/arch-extension-closure-13/recheck-20261017/final-8cb475b/published-businesses.json)；exit0|
|V12|`scripts/verify-extension-reproducibility.py`，两个最终宿主contract-directory、两种kind各重复两次（8次独立构建），固定版本1.0.1和独立测试信任；reproducibility.json全部原始manifest/payload一致，宿主不变；签名随机性不要求ZIP相同|
|V13|`gh release download closure13-reference-8cb475b -D <fresh>`，逐附件完整SHA与大小、ZIP CRC、内层SHA256SUMS和插件文件校验；[回读](evidence/arch-extension-closure-13/recheck-20261017/final-8cb475b/remote-readback.json)；12附件全部匹配|

## 制品与运行

[新未验预发布](https://github.com/yuan2go/importer-pc/releases/tag/closure13-reference-8cb475b)：四个运行ZIP、宿主源码、两份独立插件源码、两个独立1.0.1包、单独TEST ONLY公钥政策、精确组合JSON及SHA256SUMS，共12附件。没有私钥、客户未许可资产、SDK、字体或生产数据。完整字节哈希与回读文件见上表，不用截短哈希代替精确身份。

供包portable需要固定.NET10运行时；Windows包自包含但未实际运行。供包沿用station.ps1或`dotnet app/Ipc.Product.dll --config app/config --data-root <fresh> --action serve`，维护入口/命令及安装、恢复、迁移与受控激活见[运维](operations.md)、[扩展开发](../extensions/developer-guide.md)。非供包按V11运行；其`config/business.json`为显式Simulation配置。外部独立公钥政策按文档配置，不能从包自带证书自动入信。Production仍拒绝缺必需能力。

旧dc5b405/DELIVERY-12 Release全部保留。跨宿主公共契约字节变化时，不用1.0.1替代旧请求要求的1.0.0实现；原请求缺精确旧能力保持阻断，先按原身份恢复/核对，不删除身份登记或换目标/ID。安装不等于激活，保存不等于生效。

## 保留的失败与遗留

a1c41bc候选在发布前发现两目标同版本插件字节不同，**未上传**；[精确原包身份与原因](evidence/arch-extension-closure-13/recheck-20261017/withdrawn-a1c41bc.json)保留。最小复现中相同宿主路径的重复构建也不同；PE CodeView包含随机/private/var路径，定位到PathMap逻辑路径失配。8cb物理路径修复后，用V12对最终宿主8次实际构建证明收敛。没有改变包接受规则来适应错误包。

首个补丁构建缺using/nullable错误、系统Python缺Pillow、维护基线一次真实CAS冲突的日志均保留；CAS修复仅在验证端，显式旧版本冲突断言和原防重/失败保旧/所有权断言保留。最终六个选择器重新按8cb绑定，不将失败日志改为PASS。

资料归档校验依然FAIL：基准与当前均267项既有资产问题，无新增、无豁免、未降低断言；[逐项对账](evidence/arch-extension-closure-13/recheck-20261017/final-8cb475b/archive-comparison.json)。这不等于业务测试失败，也不能忽略这些历史资料问题。Windows/现场、D/RV与AC12旧状态按原登记继续；不因本PR/候选交付关闭。
