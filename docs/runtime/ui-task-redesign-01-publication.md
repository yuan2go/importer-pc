# UI-TASK-REDESIGN-01：设计入库与实施资料交付

日期：2026-10-20。用户明确授权将全部已交付UI设计产物提交 `yuan2go/importer-pc` 的 `main`，再按最新代码/文档给出下一步精简Codex提示。源码分析和清理起点为 `e2e69292899c709056ce9ab6159bf01f6f18de6c`；该起点已包含#62/63/64/66/67/68，本次不合并或覆盖其他运行工作。

## 1. 实际交付范围

- [完整设计入口](../ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)：45个命名界面状态，48张独立PNG、可点击原型、图集、完整交接、58项字段/状态与39项操作迁移、源码及原模拟QA。
- [最新代码分析与实施裁决](../design/sportmaster-task-redesign-01.md)：真实参数、对象/生命周期、保存与确认、诊断能力和已实现基座的具体继承关系。
- [UIR-00～06工作包](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)与[精简Codex提示](../work-packages/Codex-WP-IPC-UI-TASK-REDESIGN-01.md)：要求工业级完整实现、先读文档和清理过期指引、仅做必要验证。
- README、INDEX、AGENTS和UI STATUS改为单一当前UI目标；旧v1.1入口标明历史。清理前6份入口[原字节及哈希](../archive/pre-ui-task-redesign-20261020/inventory.json)保留，原设计/REQ/BR/CAPI/D/RV、旧失败和发行物不倒写。

**95是原交付目录总文件数，不是95份独立设计。** 它含48张图、9个主文件/说明、11个源码/字体/许可、13个迁移/来源材料、12个QA记录、1个图像索引、1个重复ZIP。仓库增补的索引、分析和归档包装不增加设计数量。

94个展开文件原字节直接保留；42,154,441字节ZIP因单次GitHub写入返回422“输入过大”，以11份最多4MiB的分片保留。已经实际[恢复并校验](../ui/sportmaster-task-redesign-20261020/bundle/README.md)，原ZIP SHA256为 `b6b7cfd3eb2567fb83cbae2dd15f21e85de703d07f3e3a635a398fe2047d5971`。这不是缺少整包内容，也没有把分片当新界面。

## 2. 最新源码复核的关键结论

复用现有共享Application、双位状态与旧格口失效、完整候选/唯一Revision/CAS/Pending/Effective、加密草稿、维护身份、原请求守卫、资源生命周期和原生单EXE。OneWay诊断及classic最小区修复已合入，不重复造轮子。

左右读码相机属于observation/Reader，不是采图Camera；海康真实5字段没有端口，原型9000不落入生产。RCS真实13项加Target完整配置，Query=false，Health仅本地；测试或核对原请求不得发送POST。配置Stage后可能返回普通Rejected，失败回执不等于未落盘；须由应用按候选指纹核账。Effective字段存在也不证明启动，成功activation/confirmation与当前健康、业务准入分别展示。

优先补对象导航、参数适用/分组/控件、结构化错误、完整差异、保存/待重启/应用失败/运行未知和带影响范围的诊断，再完成首配、记录、支持、模板/备份和安装衔接。真实安全模板排除设备地址/目标/秘密/编号；定向重连依然要求整站软件暂停，不能宣称另一位继续。所有细节与固定源码行号见实施裁决。

## 3. 本轮必要验证

只执行与文件入库有关的身份、引用、归档恢复及远端内容检查；未修改生产代码、业务测试或发行脚本，未构建运行客户端，未操作Windows安装、设备或WCS。

复核起点按现有工具运行：

```sh
python3 tools/verify_archive.py --root . --scope public
```

起点4515个文件，267项历史错误：265项旧PPTX嵌入媒体类型不匹配、2项旧v1.1资料的private_metadata_marker。保持原件和工具规则，不通过删除历史材料或放宽规则换PASS。

候选共4637文件，工具报告292项，较基准增加25项，已逐类核实；没有新增文件哈希、JSON、PNG解码、当前入口链接或manifest错误。94个展开文件身份、原ZIP恢复CRC/SHA256、6份归档和增量结果见[本轮检查清单](evidence/ui-task-redesign-01/publication-checks.json)。完整全仓检查仍为FAIL，不能写成全绿：

| 本次增加的报告 | 实际核对与处置 |
|---|---|
| 22项unsupported_reference_scheme | 原附件的source-gallery.md原文含22个D盘开发机路径；保留原字节作为来源证据，不作为当前可点击入口。对应原图已有[仓库可用图集](../archive/sportmaster-native-gallery-20261020-0e79e73/index.html)，新设计48图入口均是有效相对链接。 |
| 2项excluded_binary | Noto的400/700 WOFF子集，随附OFL和字体元数据，本次公开范围明确覆盖设计重建用途；原工具无条件排除字体，规则保持不变。 |
| 1项credential_review_required | 图集文件内14处大小写不敏感的字符匹配全部位于WebP图像的base64内容中，已核对图像数据范围及解码；不是凭据字段，图像数据之外没有该模式匹配。保留图集原字节。 |

上述处置不新增校验器豁免，不更改其原规则，也不修改旧图、旧PPTX或历史元数据来消除报告。源资料历史路径与现行可用入口区分，匹配偏移和哈希记录可复核。

提交使用读取到的main作为父提交，普通非强制更新并校验expected SHA。远端最终提交身份以该文档所在提交及用户收到的实际GitHub提交链接为准；未生成自引用的虚假“最终SHA”。发布后逐项读回提交树及本次文件Git对象身份。

## 4. 完成边界与下一步

本轮交付设计、代码裁决、文档清理和实施提示。UIR-00的实物入库及入口整理完成，58字段/39操作的实施级逐项处置仍由后续开工核对，不能因此把UIR-01～06写成完成。新目标UI尚未移植WPF，原型最后一次10项定向复核保留原SHA，不冒充本轮重跑。

既有233项WPF组合与原7c介质41项本机Simulation实际升级各属于原源码环境。海康0帧/0码连接记录不证明真实读码、双位实体或RCS端到端；目标Windows、DPI/输入和现场资格按真实证据登记。本轮不新增运行、性能或商业交付PASS。下一轮按精简提示持续实施全部工作包，以新源码自己的原生图和六任务证据收口。
