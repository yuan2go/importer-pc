# STARTUP-UX-03 实施与验证

2026-10-21，继续PR85，开工main3b883fe、继承5587921，PR83/82仍为依赖，PR84及其他工作副本未改动。产品SM 1.8.2，TCP 1.0.0，RCS 1.3.1。当前操作见[说明](../startup-experience-03.md)，设计见[裁决](../../design/startup-experience-03.md)。最终冻结SourceCommit、安装包及SHA256由交付回执追加；不把旧e3941dc/1.8.1的CI或安装器重新标记。

## 当前验证

所有截图为实际WPF HWND/Dispatcher渲染的合成数据。Windows主机125%缩放，不声称目标工控机、物理100%缩放、触控或现场验收。

| 实际执行 | 结果与证据 |
|---|---|
| Sportmaster Desktop、原生夹具、核心夹具Release构建 | 最终全部零警告/零错误；[构建](../evidence/startup-experience-03/desktop-build.log)及同目录ui-build/core-build |
| --startup-experience | [87项PASS](../evidence/startup-experience-03/startup-experience.log)：含6类loopback TCP合同、严格UTF8/换行/有界拒绝、同码分实体、断线不重连、停止排空；真实TCP/RCS静态闭包；底部导入/草稿、无工具菜单、错误定位/输入焦点、CAS/未知/失去回执核对、安装删项/范围/完成页 |
| --parameter-v3 | [225项PASS、60次CAPTURE](../evidence/startup-experience-03/parameter-v3.log)：共享候选、原角色/搜索通知、字段归属、RCS五种事实状态、焦点/选择、保存核对、800×420等原生尺寸 |
| --comm-native-pages | [31项PASS](../evidence/startup-experience-03/comm-native-pages.log)：真实Windows DPAPI拒绝保留原密文、草稿不可用提示、未保存关闭保护；独立维护目录/原检查失效守卫，日常页/历史/帮助复用一个业务实例 |
| --parameter-refactor | [75项PASS](../evidence/startup-experience-03/parameter-refactor-final.log)：条码、初始配置、保存与恢复、继承、原请求冻结及适用业务重量范围 |
| --version-compatibility | [57项PASS](../evidence/startup-experience-03/version-compatibility-final.log)：精确兼容、编译/发行身份、拒绝改写声明及旧版 |
| --rcs-heartbeat | [27项PASS](../evidence/startup-experience-03/rcs-heartbeat-final.log)：原唯一循环、驱动编码映射、独立编号、失败及不重发 |
| --runtime-identity | 26项逻辑PASS：共享Users主体、缺成员/deny-only/提权/服务账户拒绝；实际Windows双账号安装另验 |
| Python release_documents / application_compatibility | 2 / 17项通过，包含当前版本和签名包声明、不重标旧RCS包 |

首轮参数测试仍按旧参考草稿只填写正则，新的真实TCP/RCS必填字段为空，原[失败](../evidence/startup-experience-03/initial-bootstrap-failure.log)保留；改为显式合成IP/端口/协议字段后完成原保存断言。首轮兼容/继承夹具使用固定参考DeviceId，与机器上已占用资源争锁，原[租约拒绝](../evidence/startup-experience-03/initial-lease-contention.log)保留；只隔离合成标识，实际工厂/机器级租约及互斥守卫继续启用。商业原生旧断言依赖已删除的首配技术目录和独立检查按钮，原权限/路径变更/检查失效断言移到独立维护窗口，没有删除守卫断言。

首次集成构建暴露测试源链接遗漏及类型冲突，按原工程链接实际RCS源/资源修正；一次构建与正在运行的原生夹具争DLL，原窗口退出后重新构建，最终零警告。未使用删断言、跳过或吞异常替换成功。

## 现场与安装边界

新装默认所有Windows用户共享同一数据根、机器政策及资源租约；实际Users组ACL/UAC/双账号/安装升级/重启尚未执行。旧账户、数据根和模拟/生产身份升级时保留，不自动迁移至新空库。内置TCP默认仅观察，包裹关联须现场确认；真实认证、生产资格及原业务责任未满足继续拒绝业务，不将合成/本地就绪当在线或生产成功。

真实相机/IP网络、真实RCS、SDK及目标IPC/现场仍待验；未控制真实设备、未上传用户截图/客户原稿/生产数据/SDK，D/RV未关闭。公开manifest原有历史问题不在本包扩张为修复。

## 冻结前资源键核对

8955bb1保留本轮主体、87项初验及中间介质身份；该中间ZIP不作为最终推荐安装器。核对发现端口前导零可形成不同文本资源键，后续统一使用规范数值端口构造物理目标键。新增真实ResourceLease断言：不同ReaderId、80/00080不能同时取得同一目标；原租约释放后才可再次取得。新的[89项首配专项](../evidence/startup-experience-03/STARTUP-FINAL.log)通过，最终两份构建日志零警告/错误，原87项及原图不覆盖。[最终相机页](../evidence/startup-experience-03/final-startup-step-3.png)修正TCP包裹关联说明，[完成页](../evidence/startup-experience-03/final-installer-complete.png)保持底部运行选项。后继仅用新提交重新冻结，最终产品SourceCommit和安装器另列。

## 981c7da候选交付与远端视觉断言后续（后续候选已替代）

最终产品源码固定为`981c7da5b74c4e788dbc82362e09a45267cd9ebc`。本机标准脚本生成SM 1.8.2同源介质及单EXE；[介质](../evidence/startup-experience-03/MEDIA.json)、[安装器](../evidence/startup-experience-03/INSTALLER.json)保留原SourceCommit。安装器SHA256 `346d5a9e28e2983038b589e588ef52595f93be41c34989920aaa5a24500bbd96`；ZIP SHA256 `d616fe3a3c9634d75c75897147cffde71519ea999f76986048e34180bc71976f`。共737文件（736条文件清单加SHA256SUMS自身）。封装校验全部共享DLL字节与介质一致。未签名，实际安装NOT_RUN。

对最终发布DLL重新执行[225项原生V3](../evidence/startup-experience-03/FROZEN-V3.log)和[89项首配/TCP](../evidence/startup-experience-03/FROZEN-STARTUP.log)，均通过。两份回执的产品源/夹具源均为981c7da；736条介质文件全部重新验hash，未被验证修改。[冻结源80项当前变更的canonical清单](../evidence/startup-experience-03/FINAL-RECORDS.json)全部匹配，不把旧全仓库归档问题称为通过。

产品源981c7da的[CI66](https://github.com/yuan2go/importer-pc/actions/runs/39583740263)实际构建合并测试源`8f6d6d7f41d1cacc3e5fd965a64bbcc9eb29bd4e`。政策、冻结发布、核心/原生/卸载与生产拒绝阶段通过；视觉阶段仍按旧按钮“保存首配待生效”断言而FAIL，后续参数/安装器阶段未执行。原[失败日志可读副本](../evidence/startup-experience-03/CI66-VISUAL-FAIL.log)、[原始ZIP字节](../evidence/startup-experience-03/CI66-FAILURE.zip)、[原身份/转换说明](../evidence/startup-experience-03/CI66.json)与[回执](../evidence/startup-experience-03/CI66-VISUAL-RESULT.json)保留，不记为绿灯。

后续仅修改视觉夹具：检查新的保存并启动与无配置工具；更多菜单断言没有辅助窗口，通过程序化第二视图继续检查共享Application/连接生命周期。未恢复已删入口，未删除原共享责任断言。本机更新夹具[120项视觉PASS](../evidence/startup-experience-03/VISUAL-FIX.log)，零警告构建；新的远端结果单列。最终产品源码、配置、脚本和随包指南不变，后继夹具/交付回执提交不重标981c7da安装器SourceCommit。

最终发布CLI业务身份及Desktop只读环境入口[冒烟通过](../evidence/startup-experience-03/ENTRY-SMOKE.json)，退出码0、stderr为空；没有运行设备或执行实际安装。

## 旧模拟配置连接选择补充

复查发现旧模拟配置已绑定参考集成时，组件页误报RCS未安装，且未提供真实服务选择。补充显式“服务连接方式”，旧参考显示“本地模拟（旧配置）”，可选择随包RCS并填写现场地址；新装真实RCS无需多余选择。保留原站、用途、数据和严格生产守卫。本次包含产品修改，981c7da安装器不再是本轮推荐交付；原回执、哈希及验证不改写。后继候选从新SourceCommit冻结。

修改后本机[90项首配](../evidence/startup-experience-03/LATEST-startup.log)、[225项参数V3](../evidence/startup-experience-03/LATEST-parameter-v3.log)、[31项商业原生](../evidence/startup-experience-03/LATEST-comm-native-pages.log)、[120项视觉](../evidence/startup-experience-03/LATEST-visual-productization.log)全部PASS；Desktop及原生夹具Release构建零警告/错误。测试增加明确切换RCS后可编辑真实地址的断言，未自动转换原用途。[CI67](https://github.com/yuan2go/importer-pc/actions/runs/39584824727)已成功，仅覆盖e8b368b，不扩张为本次后继修改验收。

## 本轮推荐安装器：e55a63c

产品SourceCommit为`e55a63cdcde5cb14c18a74d42f970ea2f6cd45ee`，单EXE `CainiaoSupplyStation_SM_Setup_v1.8.2_win-x64.exe`，177848366字节。SHA256 `fc4e109fb4ed3fbec653839681b2949a5d1d8bcb8dd96d1269df82e0f8f16083`。介质ZIP SHA256 `e61688b39b08425dd3b6debf286798d3da54d2ae18fa625cfe7a8696fe461430`。[安装器回执](../evidence/startup-experience-03/RELEASE-INSTALLER.json)与[介质回执](../evidence/startup-experience-03/RELEASE-MEDIA.json)取代981c7da作为本轮推荐，旧文件不覆盖。后继交付文档提交不重标产品SourceCommit。

发布DLL与同源固定夹具执行[225项参数V3](../evidence/startup-experience-03/RELEASE-FROZEN-V3.log)、[90项首配/TCP](../evidence/startup-experience-03/FROZEN-RELEASE-startup-experience.log)、[31项商业原生](../evidence/startup-experience-03/FROZEN-RELEASE-comm-native-pages.log)、[120项视觉](../evidence/startup-experience-03/FROZEN-RELEASE-visual-productization.log)，共466项PASS。[V3回执](../evidence/startup-experience-03/RELEASE-FROZEN-V3-RESULT.json)记录产品与夹具均为e55a63c；各专项回执在同目录。全部736条介质文件验证后hash保持一致，清单SHA256为`27d5ce98120c14433085b1d86f4a4d5f77033dc393dfaa4f43aa81f9e37bbde2`。封装脚本确认共享DLL逐字节一致。[只读CLI/Desktop入口](../evidence/startup-experience-03/ENTRY-RELEASE.json)退出码0、stderr为空；[101项本包当前变更记录](../evidence/startup-experience-03/RELEASE-RECORDS.json)与冻结Git字节、长度及blob完全一致，不扩张为全归档验证。

[CI68](https://github.com/yuan2go/importer-pc/actions/runs/39585823307)关联PR head e55a63c，已成功完成政策、冻结、原生/核心/生产拒绝、视觉及安装器阶段。远端PR合并测试树与本机e55a63c介质验证保持各自身份；不把后继文档head尚未执行的CI记为通过。

第一次冻结因C盘TEMP空间耗尽而失败，部分产物未交付；保留[失败日志](../evidence/startup-experience-03/BUILD-SPACE-FAILURE.log)和[原日志哈希/重试说明](../evidence/startup-experience-03/BUILD-SPACE-FAILURE.json)。未改变源码，以D盘TEMP/TMP和新的不可覆盖输出重跑成功。自动审批拒绝清理本次C盘生成的build-intermediates目录，仅返回blocked by policy，目录仍保留；没有清理其他任务或用户数据。

安装器未签名；实际Windows安装/升级/UAC/双账号、真实相机/RCS与现场仍NOT_RUN，本轮没有生产资格或现场验收通过声明。
