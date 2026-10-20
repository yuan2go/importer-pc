# 并发任务整合报告 · 13:09续轮

起始main `9e5b6ab5be9cf70284acdbaa84b84994ca2377e9`；开始北京时间2026-10-20 13:09。前轮#60/#61已完成，本轮单独统计，不把前轮18笔增量相加。当前为调查实施记录；最终回执在本轮实际提交/合并后固定。

## 原任务、成果与证据

以下原用户消息和后继任务记录已实际读取，不从PR标题反造要求。全部源任务查询时idle。11个worktree及所有refs已快照至Git外 `D:/projects/ipc-reconciliation-snapshots/20261020-1309`；独立整合树不覆盖共享工作区。唯一未提交为旧sm-ui四文件，前轮已原字节归档，再次备份；不可把它们替换已完成#62。

|任务|原要求与后继指令|分支/起始HEAD/产物|已有证据与本轮处理|
|---|---|---|---|
|恢复原型设计质感|按原HTML与Impeccable，后明确“减少无关信息，把必要信息做大、做清楚”；再要求所有页面图|#62 codex/sportmaster-clear-workstation / 6e73250f56e09cfa1eaac9f7b8df650bbabdfd4f；3笔，UI/夹具/截图/运行报告；Git外36图图集|0e79e73同版100项原生检查；CI39365283961视觉失败；1条P2校验拒绝误标。图集诊断弹窗失败未造图，需保留并修复实际代码|
|海康驱动|接入已连接相机、按IP配置与左右无关；后要求真实读码测试|#63 codex/hikrobot-reader-01 / cff767b25b9655583d428f49db84170a406bc0bd；3笔，独立驱动/资格化原生边界/自包含修复/签名包规范/探针|ef55327冻结、26项驱动/5组政策及CI39367874458通过；真机连接释放，0帧0码，真实读码未证实；保留不把相机连接当读码通过|
|更换软件图片和名称、单EXE|用户更正名称为“菜鸟供包台”，明确允许图片及图标公开；后明确仅一个可双击EXE交付现场|#64 codex/cainiao-product-brand / 7bbc1b84574f8e4c9be2374132c68e956f401512；7笔，品牌图/图标、准备启动器/封装器、原生证据；本地172MB EXE|a7de058单EXE原源码、27准备/5封装/17原生；原品牌38项；CI39372834903视觉失败。新组合不可沿用旧EXE身份|
|简化无人值守安装设计|删除无人值守和批量要求，收敛普通安装包/必要说明，检查类似过度设计|#65 codex/simplify-installation-docs / 37a46cee9a66838aac099f7fcad3c9dc9a24ed40；1笔，13个文档/清单文件|11文档/336引用、本轮前CI39371238086通过；普通人工安装与后续单EXE要求兼容，合并时统一主入口|

四PR均非Draft，base为本轮起始main；共同基线/全部独有历史已核。源PR起始唯一提交集合14笔（含#62一次整合merge），不能用各branch tracking ahead相加。现有更早商业化/发行/架构成果已在起点，不重新计入新增。

## 裁决与验证边界

先治理文档，再编码；[本包](../work-packages/WP-IPC-PARALLEL-TASK-RECONCILIATION-20261020-02.md)及[编码前裁决](../design/parallel-task-reconciliation-20261020-02.md)是当前实施入口。旧报告/PDF、原始失败、D/RV和前轮收窄保留原形成时身份。本轮最终覆盖/验证/提交与合并回执逐项追加，不以无Git冲突或单PR局部PASS作为完成依据。

## 调查补充与实际冲突

#65已实际合并，merge `288ca6fed7bb2cd788675464f47bb04ae6f749e0`。#62原P2已以bf3f68f修正并解决评审线程；进一步a0ee213修复最小窗口与合成旧schema夹具。初始CI #62失败为1028 DIP客户区无法容纳1100目标，#64失败为定时刷新覆盖显式投影；原失败日志/结果收于[evidence](evidence/parallel-reconciliation-20261020-02/)。尝试调整CI显示模式因无可枚举模式失败，未计PASS；后改测试专用原生窗口尺寸处理，产品守卫不变。

原报告标明的classic最小窗口操作越界已实际复现并修复；保持紧凑状态区局部滚动及原件明细，默认UI在登录和50条长原因下也保留最小窗口条码/格口/操作。开发扩展原生200项PASS（a0ee213内容），不是现场通过。业务状态机/发送器/存储迁移未改。

[36张原生历史图集](../archive/sportmaster-native-gallery-20261020-0e79e73/README.md)与原导出器已取回，逐图SHA256相符；35PNG+1JPG。排除临时数据库、机器私有输出及构建缓存。5份既有PDF保持Git字节和原基线。

执行期间品牌源任务被新用户消息唤醒，新增107b82e（文档）、de105a5（安装选项/窗口衔接）及d10c51a（#65合并）。新用户原文要求安装目录与两个快捷方式选项，以及修复安装后遗留技术窗口；已读记录与源码，不从标题推断。初始快照之外再次保全Git外refs.bundle、暂存/未暂存补丁和冲突文件；已发支持的任务消息请源任务停止写入并交接。该后继成果纳入本轮，旧EXE不重标。

组合中间点2b216de（尚未包含上述新安装选项）已构建0警告/错误、驱动26项及5组政策、安装准备27项与封装5项PASS，原stdout保留；完整最终冻结验证另记，不能据此宣告最终通过。首次封装测试用错discover文件名导致0项/exit5，已用test_installer_package.py重跑5项PASS，不把空运行算通过。

## 已确认要求与最终实现位置

|确认要求|最终实现/来源|验证口径|
|---|---|---|
|Platform、Application、Driver、Component四类职责；一业务一实例、多页共享|既有framework/architecture22；ParcelRuntime、各独立Flow、Presentation；#62/#63不新建通用流程|源码对照；原生共享Application、辅助窗口/关闭重试断言|
|Sportmaster左右reader隔离，扫码到WCS格口，无PLC/图片展示或长期存储|SportmasterFlow、ParcelBindings、SupplyStationPosition；#62/63|双位延迟/Unknown核对、原请求归属、观察/准入拒绝/格口失效原生及核心冻结回归|
|DirectPlc、WcsToPlc独立单相机单投递位；DirectPlc UI延期|原独立项目/Flow及发布选择，源码未改变边界|源码核实；本次不重复现场测试/扩展开发UI|
|可选认证/称重/上传组合；业务语义在Application|原能力factory/ports/独立流程；#63 reader仅IP和SDK观察|factory原资格、驱动26断言及5组签名政策；Production缺资格拒绝|
|恢复/持久化/配置激活不回退|原DB8、schema3、ABI1.10.0、ParcelTargets、Startup/ExtensionStore；#63精确扩展边界|核心冻结恢复/防重/旧请求/配置/生命周期，UI失败不清原件|
|精简放大专业双位UI且设备诊断可达|#62布局、SupplyStationViewModel/Diagnostics、ShellWindow紧凑状态区|原生最小客户区、长内容、诊断OneWay、拒绝区分、焦点/选择/滚动/草稿|
|驱动与左右无关，只配置IP|#63 Ipc.Drivers.Hikrobot及原机级资源租约/资格化装配|同触发多帧/同码新触发/旧epoch/队列满/Stop失败/健康失败；真读码仍缺证据|
|菜鸟供包台名称及获准公开图片|#64 ProductBrand、cainiao.png/icon及桌面/安装资源|同源发布资源和实际原生启动；原图片/图标来源许可见源报告|
|一个可双击EXE、可选目录和两个快捷方式、完成正确关闭/启动|#64 Ipc.Installer、原StartupSetupWindow/ParcelStartupServices/DeploymentShortcuts、后继de105a5；整合767bf30保留已改指向入口|准备27/封装5、选项18及实际临时COM；最终EXE黑盒与真实安装另列，不能借合成完成页通过|
|仅普通人工安装，不要求无人值守/二次选型|#65文档及本轮现行操作入口|逐项文字/链接/manifest核实，保留高级已有机制和数据保护|

源任务已确认暂停写入并交接，新增三个源提交未推送的成果已保全并合入#64；d10c51a遗留manifest.sha256冲突标记及新代码记录缺失已由整合重建，未在远端合入失效清单。后继包装继续在独立目录进行，Git/PR由本整合任务统一负责。
