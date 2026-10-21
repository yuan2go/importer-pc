# STARTUP-UX-02 验证回执

日期：2026-10-21（北京时间）。开工main `3b883fe`，继承PR83 `0fea1b5`，本轮业务版本1.8.1。产品源码、冻结介质和提交回执单独记录；下面不是旧1.7.0或PR83介质的验收。

| 检查 | 实际结果与范围 |
|---|---|
| Sportmaster Desktop / UiVerification / Application.Verification Release构建 | 零警告、零错误；[桌面](../evidence/startup-experience-02/desktop-build.log)、[原生项目](../evidence/startup-experience-02/ui-build.log)、[核心项目](../evidence/startup-experience-02/core-build.log)。 |
| 首配专项 `--startup-experience` | [52条PASS](../evidence/startup-experience-02/STARTUP-WPF.log)，真实WPF/Dispatcher加合成维护契约；导入与不兼容拒绝、空筛选、菜单可达、五步同编辑器、窗口内底栏、小窗滚动、检查失败/写入前失败/结果未知防重提、正常回执及回执丢失后的原次核对、模态启动交接、未完成续配、安装页中文与分组。 |
| 完整 `--parameter-v3` | [219条PASS](../evidence/startup-experience-02/PARAMETER-WPF.log)，含原作业/参数体验/参数继承/保存审阅回归。运行时125%，800×420及其他客户区为原生验证尺寸；不声称实际物理屏幕是1600×900或已测100%。 |
| `--parameter-refactor`（包含继承） | [75条PASS](../evidence/startup-experience-02/parameter-core.log)，空规则、仅排除、歧义/重复码、预算、原请求冻结、候选和恢复边界。 |
| `--configuration-confirmation` | [41条PASS](../evidence/startup-experience-02/configuration-confirmation.log)，原权威账、CAS、确认、草稿及原件责任。 |
| `--version-compatibility` | [57条PASS](../evidence/startup-experience-02/version-compatibility-final.log)，编译版本/嵌入声明、精确提供者、篡改与混源拒绝。过时的业务1.5.0与RCS1.2.1固定断言改为对照当前工程声明，拒绝断言保留。 |
| 随包说明 | [2项PASS](../evidence/startup-experience-02/release-documents.log)，同源文档及失效/越界引用拒绝。 |
| 离线恢复 `--startup-deployment` | [15条PASS后权限拒绝](../evidence/startup-experience-02/startup-deployment.log)。普通账户不能创建要求提权的维护工作区；未提升权限、未跳过守卫，后续完整恢复未完成。 |

首轮原生复现了空条码规则仍阻止下一步，保留[原失败](../evidence/startup-experience-02/initial-blank-rule-failure.log)。修复在同一有界筛选器和首配入口完成。后续旧断言依赖英文变量名/旧组件身份文案的问题，按新中文可见文案重写定位；仍验证真实错误跳转、原对象/焦点/选区/未保存输入，不删断言换PASS。截图初轮因只绘制带Margin的Content而出现裁切，最终按整个WPF Window绘制，并加入真实窗口内底栏边界断言；不以裁切图证明布局。

本轮用本地临时HTTP只读服务实际打开V2离线原型，走查首配进入和系统步骤；查看67张V2及27张V3设计图的联系表、交接与字段/操作迁移。原型仍为设计证据，Windows实现以上述原生结果为准。临时页和服务已关闭。

## 当前原生图（合成配置）

- [安装选项](../evidence/startup-experience-02/installer-options.png)、[高级项分组](../evidence/startup-experience-02/installer-advanced.png)、[安装确认](../evidence/startup-experience-02/installer-confirmation.png)。安装确认使用合成审阅请求，未执行实际安装/UAC。
- [首配第一步与导入](../evidence/startup-experience-02/startup-step-1.png)、[系统](../evidence/startup-experience-02/startup-step-2.png)、[驱动](../evidence/startup-experience-02/startup-step-3.png)、[组件](../evidence/startup-experience-02/startup-step-4.png)、[末步](../evidence/startup-experience-02/startup-step-5.png)、[小窗](../evidence/startup-experience-02/startup-small.png)。空配置显示缺失项，不冒充现场已连接。
- [未完成配置后的继续入口](../evidence/startup-experience-02/configuration-paused.png)。这是正常首配未完成状态，真实启动异常仍进入原恢复页。

这些是原生窗口程序化事件和渲染证据，不声称已做物理键鼠、IME、触控、目标工控机全DPI或现场验收。真实SDK/RCS、生产认证/实体资格、实际安装升级/重启/双账号、完整恢复后业务核对仍待验。D/RV与历史发行SourceCommit保持。原用户截图不上传。

实施提交：`3f7e785ae313c0b4203c147a0f8113f27b6950bd`。后续首轮CI补修与最终冻结身份见下方回执，不将中间介质改标为最终版本。原生日志记录源码级验证，不声称与随后冻结发行二进制逐字节相同。


## PR85 首轮远端检查与后续

[首轮CI](https://github.com/yuan2go/importer-pc/actions/runs/39569567903)固定GitHub合并测试源`ccf7884cb5af50fdb6d4474ccf81de08a3b22f07`，不是本机源20171ab。冻结发布、核心和卸载内核验证通过；首配窗口测试仍按旧标题和分开的保存/启动入口断言，导致WPF及工程门禁FAIL，后续视觉/安装器步骤未运行。原[失败日志](../evidence/startup-experience-02/ci-first-WPF.log)、[WPF回执](../evidence/startup-experience-02/ci-first-WPF-result.json)和[工程拒绝](../evidence/startup-experience-02/ci-first-engineering.json)保留。

后续断言改为检查可见且可用的“保存并启动”，不恢复多按钮。另修正本轮精简引入的诊断提示缺口：加密草稿解密失败或原配置无法读取时，主状态必须明确提示不可用并保留原文件；完整原因仍在诊断中。保留原DPAPI原字节、可编辑字段、焦点/未保存关闭保护断言。新版源码、复验及CI结果分别以实际后续回执记录，不将首轮FAIL覆盖。

清单核对：[原始结果与比较](../evidence/startup-experience-02/archive-comparison.json)。两份Windows git archive受autocrlf转换，原全树检查分别FAIL 4320/4336，其中4022/4038项身份噪声已逐项证明仅为CRLF变化；没有修改校验器或添加豁免。另从Git对象逐项核验，基线5507项与候选5533项canonical SHA256/大小/Git blob均一致；原298条结构/政策问题完全相同（277个去重报告），新增0。全仓库仍不记为PASS。


本机后续复验：商业原生页[31条PASS](../evidence/startup-experience-02/COMM-WPF-FIX.log)，包含真实DPAPI拒绝后原密文不变、明确草稿不可用提示、输入/焦点和关闭保护；首配专项[52条PASS](../evidence/startup-experience-02/STARTUP-CI-FIX.log)；视觉原生[119条PASS](../evidence/startup-experience-02/VISUAL-CI-FIX.log)。均为合成契约与原生程序化交互，不扩张为实际安装或现场证据。


最终首配补充：[56条专项PASS](../evidence/startup-experience-02/STARTUP-FINAL.log)与[31条商业原生PASS](../evidence/startup-experience-02/COMM-FINAL.log)。增加末步数值错误和异步正则错误自动返回真实字段、精确输入与键盘焦点保留；定位在操作结束、控件重新启用后执行，输入已变化时不抢焦点。恢复页从未完成态返回真实失败态时恢复原最小高度。之前4b5924c的[远端必需CI](https://github.com/yuan2go/importer-pc/actions/runs/39570927095)已全部通过；最终定位补丁的CI与冻结介质以之后回执为准，不将此PASS扩张到后继树。


## 最终冻结交付

最终产品源为`e3941dc871816c1c028209368237413c4f066591`，SM 1.8.1。对应[必需CI](https://github.com/yuan2go/importer-pc/actions/runs/39573180019)全部通过，CI实际构建GitHub合并测试源`9975747f847ed6574dad6fd57819cbeda3baa1b4`；[原身份记录](../evidence/startup-experience-02/CI-FINAL.json)。[CI可安装介质](https://github.com/yuan2go/importer-pc/actions/runs/39573180019/artifacts/12333770382)保留至2026-11-20，CI介质与下面本机介质分别保持源码和二进制身份。

本机同源Windows介质737文件，ZIP SHA256 `fa950af5fc2d2fb2998f327562ca9e508b3335e9e6cac17481e5455dc58229a4`，见[介质回执](../evidence/startup-experience-02/MEDIA.json)。单EXE名`CainiaoSupplyStation_SM_Setup_v1.8.1_win-x64.exe`，SHA256 `e249274be4d5bdc8179c3c18e464aec7c58dfca7fbef01e2b8d8c23a608d5cbd`，见[安装器回执](../evidence/startup-experience-02/INSTALLER.json)。封装过程校验全部payload清单及每个共享DLL与安装器构建字节一致，没有覆盖中间候选。

对最终发布DLL单独执行原生商业页面验证，[31项PASS](../evidence/startup-experience-02/FROZEN-WPF.log)，[冻结源/夹具源/实际DLL身份](../evidence/startup-experience-02/FROZEN-WPF-RESULT.json)均为e3941dc，并验证介质未被测试改变。发布CLI业务身份及Desktop只读启动[冒烟通过](../evidence/startup-experience-02/ENTRY-SMOKE.json)；没有启动真实设备或执行实际安装。最终65项变更记录的Git原字节SHA256、大小和blob[全部匹配](../evidence/startup-experience-02/final-records.json)；此数字是回执入库前的变更文件数，不是测试项数。

本机标准NuGet缓存位置与SDK实际缓存不同，首轮封装因找不到license nuspec正确拒绝；按实际project.assets中的`D:/DevCache/nuget/packages`显式设置NUGET_PACKAGES后，使用原许可校验与原封装脚本成功，没有伪造许可证或放宽准入。使用仓库现有脚本从已提交源码冻结：先`package-application.py --application sportmaster --target win-x64`，再以生成的同源ZIP调用`package-installer.py`。实际运行命令及dotnet路径见[当前说明](../startup-experience-02.md)。

这是未签名候选安装包，不是main正式发行；PR85仍为依赖PR83的草稿。实际Windows安装/UAC/升级/重启/双账号、普通账户下完整恢复、真实SDK/RCS/现场及目标IPC触控继续待验。早期20171ab及4b5924c介质/CI保留原身份，不作为最终推荐包。后继文档提交仅补本段和原回执，不重标e3941dc的SourceCommit。
