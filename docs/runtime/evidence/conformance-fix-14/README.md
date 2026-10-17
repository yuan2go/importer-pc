# FIX-14 新证据

冻结源码 `68cdc36f7821825c1ee9e53d866015be5ecf3044`；macOS 26.5 / ARM64，SDK10.0.402/runtime10.0.13。原审查是Linux/8cabf06d，未覆写或迁移其结论。所有运行只用独立合成数据和字面loopback，无生产WCS/设备动作。

- `runs/final-*.record.json`记录实际命令、开始/结束、平台、源SHA、退出码、完整scope标记及原日志SHA。最终--conformance、--baseline、--followup、--closure、--architecture均以完整尾部PASS为准，不能只看退出0。
- `logs/`保留开工红探针、全部构建/测试失败和最终完整日志。冻结前日志来自当时未提交工作树，不能冒称每轮均为68cdc36或用于最终同版验收。原审查日志另在原目录保持原字节。
- 健康探针首轮校正判定仍使用原串行完成假设，主完成时辅助仍Unconfirmed，exit3；v2仅增加等待独立辅助探针完成，不改业务或原探针。两版源码/差异说明/失败与成功日志均保存。新结果为FIX_VERIFIED，副作用请求0。
- 核心冒烟首个收集记录错用了不存在的`status`字段，exit0但marker=false，原记录保留FAIL；`final-smoke-core-evaluated.record.json`按同一原输出与七项结构结果补做完整性判定PASS，不重跑、不更改原日志。
- `synthetic-databases.zip`是本轮五个最终选择器保留的合成数据库/存在的WAL/SHM/接收围栏，含每项SHA256SUMS；`synthetic-custody.json`登记原字节。只解压到新目录，不直接写原证据。
- `probes/`只含映射后的原探针副本；原工程绝对路径映射到本工作树，矛盾探针增加实际验证SHA，健康探针改变修复判定并等待独立健康完成。原探针、原数据库和失败保持原目录。
- `candidate-inventory.json`记录三个候选的源/Release/Storage/模块与全文件SHA；`plugin-reproducibility.json`证明两个冻结宿主、两类插件各两次独立编译的manifest/payload一致，宿主未变。PSS签名随机，不宣称ZIP必相同。
- `legacy13-custody.json`及`legacy-compatibility.json`绑定原13下载SHA，实际旧读者拒绝4且库字节未变，原1.0.1包在新公共指纹下拒绝；没有改旧包/原请求来伪兼容。`preserved-inputs.json`确认原SQL、合同样本、D/RV、12/13工作包与UI实物保留。
- 远端附件完整下载、CRC/精确文件集/内层SHA结果在`remote-readback.json`。Windows实际UI/扫码器/触控/声音/ACL继续NOT_RUN，现场与真实SDK/WCS继续BLOCKED。

公开副本仅将本机用户/工作树/SDK绝对路径映射为`/reference/workspace`、`/reference/dotnet-10.0.402`等别名，事件/结果和完整顺序未删。原字节仍在本机.artifacts，逐文件原SHA/公开SHA及映射见publication-provenance.json；runs中的log_sha256对应公开副本，raw_log_sha256保留原件。重现时将别名映射到自己的固定SDK/仓库，不能把路径变换当作原字节相同。
