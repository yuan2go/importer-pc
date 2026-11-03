> 最终运行源码：`7ed7862d4ab3a3f5bdcb88cd19633d4d75ce6806`，继承main `9a5d87ad41e3e936f56a7e58ec60de8f7aa53458`；最终结果见末节。此前候选与PASS保持原SHA。

# Sportmaster 第二套 UI · 实施与验证结果

2026-10-19；开工main `b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d`。用户确认本次衍生WPF代码可提交公开仓库，原HTML不上传。[运行指南](sportmaster-alternate-ui.md)与[Windows后验](validation/sportmaster-alternate-ui-windows.md)。

## 已实施

`--ui classic|supply-station` 在业务/设备创建前严格校验；默认classic。新Sportmaster原生窗口复用同一 `IParcelApplication`，左右稳定投影/草稿、原WCS格口/请求/原因、每位历史、历史/维护导航、辅助窗口、显式Simulation按位输入和主窗有界排空。业务Flow、ParcelRuntime、SQL、驱动和协议源码未修改；公共应用契约与Views/Layouts不变。

资源字典由独立位控件自己声明，避免构造时依赖未挂接父树；刷新失败及3秒到期标旧/禁用，成功命令刷新与轮询都有防旧失败覆盖；关闭快照排空5秒超时后恢复窗口并保留责任。静态审查发现的这些问题均已处理；Windows实际资源加载仍需后验。

## 实际验证

- UI定向：52项断言通过，使用真实SportmasterApplication/ParcelCapabilityFactory与独立参考WCS接收账，覆盖两套VM共享实例、双位同码不同实体、原请求一次执行、拒绝、提交后丢回复/按原件核对、schema2兼容、稳定草稿、快照失败/到期/乱序及命令刷新竞态。
- 既有架构22：60项断言通过，包括独立队列、恢复、部分构造失败、Stop排空/重试、原图/重量/上传及迁移屏障。
- Sportmaster、DirectPlc、WcsToPlc WPF编译及Windows专用烟测工程交叉编译：均0警告0错误。
- 随包文档渲染测试2项通过，发布脚本语法检查与diff空白检查通过。
- 初轮曾发现桌面项目缺少System.IO导入、Windows烟测装配选择及源链接导入问题，均已修复并重新编译；未删除断言或用跳过换PASS。

[checks](evidence/sportmaster-alternate-ui/checks.json)与同目录定向/回归/构建日志只包含合成参考信息，环境路径已规范化。日志里的临时数据不进入发布或仓库。

## 候选与交付身份

最新main整合前的已验证运行/候选源码：`911ed2433fc403a43189c00218e095dddbaff7a8`。该提交仅在前一实现基础上澄清图像未知阶段文案，最终UI定向52断言重新通过；两类候选按该冻结SHA重新构建，均0警告0错误。后续报告/manifest/PR回执不改变运行源码。

|目标|本地候选ZIP|SHA-256|字节|
|---|---|---|---|
|portable|`ipc-sportmaster-ui-911ed24-portable.zip`|`afe723bb841c62c0005ab20186e0e3ec77e88054f32e06be33dd21b43ff545ec`|18216418|
|win-x64|`ipc-sportmaster-ui-911ed24-win-x64.zip`|`744eabbfbe39a02063b60c51d72b3cefaabbd3504516d0a5201b354735f69038`|103761346|

27项[实际解包与启动检查](evidence/sportmaster-alternate-ui/package-verification.json)通过：SHA256SUMS、各release文件hash/业务/源码身份、两套BAML及本位/主题资源、随包指南/启动参数；portable解包后的实际宿主用双位同码不同Admission完成两条WCS链，均一次原请求且无PLC，正常排空退出。此处的启动指portable参考控制台，Windows桌面启动仍NOT_RUN。

复验入口为[同版探针](evidence/sportmaster-alternate-ui/verify-packages.py)：`--portable <ZIP> --windows <ZIP> --source 911ed2433fc403a43189c00218e095dddbaff7a8 --dotnet <固定dotnet> --report <JSON>`。

前一源码b60a516的候选/解包结果保留在[superseded记录](evidence/sportmaster-alternate-ui/superseded-b60a516-package-verification.json)，属于已通过但被本次文案修订替代的历史候选；没有用新SHA覆盖旧身份。

## 最新main整合

收尾时发现main已更新到 `4afe2afacde9e2acab7648fff3440fa3cc331b6e`（PR23及PR24）。以上911ed24候选和52/60/27项结果保持形成时身份。已在开发分支正常整合最新main：保留两边文档入口、manifest历史与全部验证选择器；新UI适配分页历史与详情选择、原件检查说明、已展示CAS、容量/附加责任。相对4afe2af的业务Flow、ParcelRuntime、SQL、驱动/外部协议源码无差异，未以UI任务重写业务。

全库资料校验与开工main b2dc55b对照：267个历史问题完全一致（265个PPTX媒体类型与2个元数据标记），本次新增0。未修改原资产或把全库FAIL记为PASS；详见[对照](evidence/sportmaster-alternate-ui/archive-comparison-b2dc55b.json)。

## 未执行与受阻

Windows原生窗口/启动/焦点/DPI/触控/截图实际 NOT_RUN；已提供可执行烟测工程，不将交叉编译、BAML或HTML截图当作真机PASS。真实SDK、WCS/PLC、设备/实体及现场验收 BLOCKED。Production继续按原守卫NotReady，不改变D/RV、旧HTML或历史验收结论。PR按用户授权创建main目标，不合并。

## 整合后验证

固定运行源码07e3d8c（最终报告/manifest/PR回执仅文档）：

- 最终UI定向66项PASS，使用 `--no-incremental` 强制编译并直接运行DLL，0警告0错误；新增真实双位重启、软件暂停/恢复、空检查说明拒绝、按本位原作业传递说明、历史选择一致、同件保留/换件清空检查草稿及并发修订返回Conflict。
- 最新main架构22定向62项PASS；PR24质量复审回归32项PASS。没有拿旧60项结果冒充整合后结果。
- DirectPlc/WcsToPlc WPF兼容编译各0警告0错误；Windows原生烟测工程跨编译，实际Windows执行仍NOT_RUN。
- 补充的恢复测试初次因配置库存不完整及忽略重启默认暂停而失败；按当前main的真实配置/恢复流程修正测试夹具，保留[失败日志](evidence/sportmaster-alternate-ui/integrated-test-setup-failure.log)，没有放宽业务守卫。

最终两类候选已按07e3d8c冻结源码重新生成并解包，27项检查全部PASS，实际portable宿主双位WCS链及正常停止通过；Windows实际UI继续NOT_RUN。

|最终目标|本地ZIP|SHA-256|字节|
|---|---|---|---|
|portable|`sportmaster-portable.zip`|`40a19959b2428b01291f43f21e79c561b6721d5ef201dfe1dcff5e38188aebec`|18362596|
|win-x64|`sportmaster-win-x64.zip`|`de4dd2936fc4c865183c7ef381a16ce0e0c248878f12b78ab944551e0a4dad45`|104056578|

本地完整产物位于 `Workspace/artifacts/importer-pc/sportmaster-alternate-ui/07e3d8c/`；不将候选ZIP/二进制、原稿或运行数据库提交Git。活动[候选/解包回执](evidence/sportmaster-alternate-ui/package-verification.json)固定07e3d8c；整合前911ed24回执另存为[历史记录](evidence/sportmaster-alternate-ui/pre-main-integration-911ed24-package-verification.json)。两个阶段不能共用结论或文件身份。

最新main4afe2af全库资料校验与当前比较：[267项历史错误完全一致，无新增](evidence/sportmaster-alternate-ui/integrated-archive-comparison.json)。manifest逐文件hash/大小/范围校验与本次文档链接一致；保留全库FAIL，不倒写PPTX/原设计资产。

## PR26合入main后的最终交付

PR创建后main合入PR26到9a5d87a；本分支再次正常整合，保留其资料入口、SQL5原件保有、旧配置确认与选装包发布机制，未修改其业务实现。最终源码固定7ed7862；本次UI/API形状不另改，精确公共DLL字节指纹仍以各候选release为准，后装包不能跨候选盲复用。此前07e3d8c候选及验证回执另存于[整合前记录](evidence/sportmaster-alternate-ui/pre-pr26-integration-07e3d8c-package-verification.json)，不覆盖历史身份。

最终复验：UI66、架构22回归62、质量复审32、配置确认32条断言均PASS；Sportmaster发布编译、DirectPlc/WcsToPlc兼容编译、Windows原生烟测工程跨编译均0警告0错误；随包指南2项及选装包辅助测试3项通过。最新共享维护页的旧配置确认/原接收责任入口随本UI继承。Windows实际UI依然NOT_RUN，真实SDK/WCS/现场依然BLOCKED。

实际最终媒介：

|目标|ZIP|SHA-256|字节|
|---|---|---|---|
|portable|`sportmaster-portable.zip`|`08dbe011e71dcee97785d29129c84d54b3fe595801dd94089a15db215788882e`|18373128|
|win-x64|`sportmaster-win-x64.zip`|`564ac7bab158b3ee7f24d65c9136292fb0eda25350f8ca75081ff26886fb87a9`|104110127|

产物目录 `Workspace/artifacts/importer-pc/sportmaster-alternate-ui/7ed7862/`。27项[解包/资源/启动检查](evidence/sportmaster-alternate-ui/package-verification.json)PASS，包括真实portable宿主双位WCS链、一次原请求、正常停止；不把该启动当作Windows桌面启动。

已创建并附加[main PR #27](https://github.com/yuan2go/importer-pc/pull/27)，不合并。源码7ed7862后的报告/manifest提交仅交付回执，不改变冻结运行源码。

收尾再次继承main `baac260214f910dca9ae4628ba6449b25188e2ac`（PR28，仅文档发布回执）；相对9a5d87a无src/scripts/config变化。正常合并保留双方入口和manifest，无需重建已冻结7ed7862运行候选；上述最终验证仍绑定7ed7862。全库资料校验对照见[最终记录](evidence/sportmaster-alternate-ui/final-archive-comparison.json)，历史FAIL与本次新增分开。PR27保持OPEN，不合并。

最新main baac260的完整扫描为268项（267项历史设计资产问题加1项manifest校验和）；合并后重新生成manifest校验和，当前267项历史问题保持，新增0、移除1项校验和错误。完整资料状态仍FAIL。

## PR27独立审查后四项P2整改（2026-10-19）

独立审查固定head7d47a19、main baac260，原4项P2保持原审查身份；[审查与整改复核](../reviews/pr27-ui-independent-review.md)分别列出源码修复、参考验证和Windows未验项。用户授权后冻结修复源码 `bf16bd1079128b4d235819878af18443a848943a`；后续报告/manifest提交不改变此运行源码。7ed7862的[检查回执](evidence/sportmaster-alternate-ui/pre-p2-7ed7862-checks.json)、[候选回执](evidence/sportmaster-alternate-ui/pre-p2-7ed7862-package-verification.json)及原日志/候选保留，不重新赋予新修复结论。

四项修复：所有桌面快照读取共用默认行为不变的扩展点，新UI在相关失败时标旧并退休旧轮询；诊断区132/96 DIP局部滚动保留全部原原因；小客户区1280×680边界以下保护并调用既有Pause，恢复空间后需有效暂停快照与人工确认，Resume保持显式；失效最后读取时间常驻。主窗关闭同时有界等待保护Pause。业务Flow、Runtime、应用契约、SQL、驱动、协议、配置相对baac260无差异。

最终参考验证：UI86、架构62、配置确认32、质量复审32断言PASS；Sportmaster、DirectPlc/WcsToPlc（使用现有锁定文件）及Windows烟测工程编译均0警告0错误；指南2项、发布辅助3项PASS。修复前新增时间断言确实失败，保留[失败回执](evidence/sportmaster-alternate-ui/p2-pre-fix-last-read-failure.log)。复核另发现“旧轮询成功覆盖较新Pause读取失败”，已补失效退休及Pause/ConfirmConfiguration两条回归；[独立原探针](evidence/sportmaster-alternate-ui/p2-independent-ordering-probe.log)复跑保持Fresh/CanAdmit为false。其余日志为同目录p2前缀，路径已规范化。

|目标|新ZIP|SHA256|字节|
|---|---|---|---|
|portable|`sportmaster-portable.zip`|`3db6057bb6fd49423b265e90d054c2a1687f048de58b0f4b0a85467894d240c1`|18373853|
|win-x64|`sportmaster-win-x64.zip`|`85a9c73720672bbbbb0b2c5dea05f71d2c3ee3cfdaf46ae15c844a87918e5f86`|104114359|

新产物目录 `Workspace/artifacts/importer-pc/sportmaster-alternate-ui/pr27-p2-bf16bd1/`。[实际解包/资源/宿主启动检查](evidence/sportmaster-alternate-ui/package-verification.json)27项PASS，含portable双位原WCS链、一次原请求和正常停止；不将其称为Windows运行。原生烟测新增50条合成诊断、长原因与固定动作几何检查，以及小客户区保护/人工确认，但Windows实际执行、DPI/多屏/触控/截图依然NOT_RUN，真实SDK/WCS/设备/现场依然BLOCKED；D/RV不变。

[同一main PR27](https://github.com/yuan2go/importer-pc/pull/27)继续用于本次修复审查，不合并。原HTML没有进入提交或候选。全库资料保持历史设计资产FAIL，最终对照见[资料回执](evidence/sportmaster-alternate-ui/final-archive-comparison.json)；当前逐文件身份与新日志随manifest更新。

本次工作目录资料扫描因新烟测工程的ignored bin/obj生成物额外报告164项（109未登记、51二进制、4生成路径标记），见[工作区扫描记录](evidence/sportmaster-alternate-ui/p2-worktree-build-output-scan.json)。这些文件不进入Git/候选公开清单。公开资料对照另在Git文件加待提交非忽略新增文件的精确源码快照执行，校验器与例外政策未改，不用工作区生成物结果冒充公开源码校验。

## 226ac9b复审后的读取/窗口竞态整改（2026-10-19）

[第二轮原复审和整改复核](../reviews/pr27-ui-independent-review.md)保留两项原P2：旧成功先完成后，新暂停读取失败仍被忽略；主窗停止失败重启已关闭子窗计时器。两项新增回归先在原源码失败，见[r2读取RED](evidence/sportmaster-alternate-ui/r2-pre-fix-read-failure.log)和[r2窗口RED](evidence/sportmaster-alternate-ui/r2-pre-fix-window-failure.log)，没有删断言或跳过失败。

修复按每次读取的单调请求ID判断：只有ID更大的成功能覆盖较旧失败，禁止发布早于相关失败的结果，但新于失败的在途读取成功可直接恢复。共享桌面的默认钩子不改变classic行为。窗口Closed是终态；停止失败只恢复仍登记/未关闭窗口；子窗失败不提前解除主窗正在进行的排空。原实体、请求、CAS和业务守卫不改。

先在ee56123402cba4d284186a06aa61c7a478193560验证96/16/62/32/32，整合前日志以r2-pre-main-ee56123前缀保留。随后正常继承main `713f23d7c39743762adb884fdffa18745cad08f8`（PR31恢复整改），保留两边入口、manifest与全部验证选择器。整合后重新验证同组用例并补47项升级冲突回归。最终冻结运行源码 `e86d664c78418d82bdd49b1abaacc148f38c2d35`，后续交付回执仅文档。相对该main，业务Flow/Runtime/应用契约/SQL/配置/驱动/协议无差异；PR31恢复改动来自main，非UI重新实施。

整合后验证：UI96、实际窗口代码控制流16、架构62、配置确认32、质量复审32、升级冲突47项PASS；三类客户端和原生WPF烟测工程锁定构建均0警告0错误；指南2/发布辅助3项PASS。窗口控制流使用实际code-behind及最小事件/计时器替身，覆盖主子重叠关闭、刷新/保护Pause挂起、失败重试、子窗失败与5秒超时；不能替代HWND/原生Dispatcher。独立保留的[读取原探针](evidence/sportmaster-alternate-ui/r2-independent-ordering-probe.log)复跑Fresh/CanAdmit为false且入场调用0，[关闭原探针](evidence/sportmaster-alternate-ui/r2-independent-window-probe.log)复跑子窗计时器始终停止。旧临时事件替身编译警告原样保留；正式验证工程和WPF项目构建0警告。两轴源码复核未发现剩余确定阻断项。

|目标|新ZIP|SHA256|字节|
|---|---|---|---|
|portable|`sportmaster-portable.zip`|`054c8acb9165f3608347fd3d20dcaf56613e0efe7c2e64750a5bf092c7e90110`|18377583|
|win-x64|`sportmaster-win-x64.zip`|`e64cdee3ffcc5797cc40641917ba7c6bd000d8947aeebe6fc78a87c589dd1b6c`|104121535|

目录 `Workspace/artifacts/importer-pc/sportmaster-alternate-ui/pr27-r2-e86d664/`。[新候选实际解包/启动](evidence/sportmaster-alternate-ui/package-verification.json)27项PASS，实际portable双位原WCS链、一次请求和正常停止通过。bf16bd1的[检查](evidence/sportmaster-alternate-ui/pre-r2-bf16bd1-checks.json)和[候选](evidence/sportmaster-alternate-ui/pre-r2-bf16bd1-package-verification.json)另存历史，未覆盖旧ZIP或赋予旧候选新结论。

原生烟测已增加主子重叠关闭、停止失败与重试，但实际Windows窗口/Dispatcher/DPI/触控/截图仍NOT_RUN；真实SDK/WCS/设备/现场BLOCKED，D/RV不变。main原始失败日志有一处尾空白，保留原字节；本PR相对main的diff空白检查PASS。公开源码快照资料对照见[最终记录](evidence/sportmaster-alternate-ui/final-archive-comparison.json)，不扫描忽略的本地编译生成物充作公开内容。

同一[PR27](https://github.com/yuan2go/importer-pc/pull/27)更新审查与验证记录，不合并。原HTML仍未上传。
