# PR29独立审查P1/P2整改结果

用户要求修复并推送原PR29，不合并。开工核对main **baac260214f910dca9ae4628ba6449b25188e2ac**、原PR29 **d954ba6bac2610592512405eee9a96bfc5b9bcb3**；并行PR27 **226ac9bfdde08a99f5ce93e55c4eb0a0d277b7fa**、PR30 **822ce92f01e01487fdf037eca9219a601250d458**、PR31 **f068156ed9d4a0ffe4feee94b5c11e966aaa366b**均未合入。本任务复用原codex/version-compatibility工作树，不修改main工作区、未跟踪资料或其他工作树。

## 修复

P1：新组合记录schema2在运行前保存CombinationInput输入/候选身份事件；finish写PASS前与重验时都核对该事件和实际进程snapshot的Effective配置、ApplicationId、重启RuntimeId、默认/BeforeMain/Attached场景及OperationId/最终State。不能只重算记录自带配置和provider hash来复用另一组合的原输出。旧schema1记录保留原字节，只根据其已有snapshot/场景/结果重验；缺独立输入事件仍是其历史边界，不伪造补齐。

P2：ParcelCapabilityFactory.ReadPublishedRelease统一安装、恢复和卸载的发行校验，传实际业务程序集；不放宽schema或绕过原manifest/字节检查。正确schema2通过，错业务版本、schema1降级和外部声明/manifest一起修改仍拒绝。

原独立审查原件及失败SHA回执、red/green日志和新运行见[证据](evidence/version-compatibility-01-rereview/README.md)。[整改前当前指引原字节](../archive/pre-pr29-p1-p2-d954ba6/README.md)已保有；原c4e1319发行和历史签名证据未改。

## 实际验证

修复运行/发布闭包 **5e3282b02616d905a628a6cbeeb9a7fbe4228a79**。之后仅报告、证据、索引和manifest，不改变实际候选源码身份。业务实现、业务版本1.2.1、兼容资源、公共接口与旧请求未改，发行身份仍由SourceCommit和精确文件SHA区分。

- 原P1探针在旧实现接受默认输出被标为optional；新实现对原探针及新schema2同类攻击均拒绝。12项定向Python、全38项Python（含指南/选装包/构建版本）通过。旧五条组合记录按snapshot一致重验通过，字节不变。
- 三个业务的C#版本/兼容/真实维护草稿与共享维护发行检查通过，正确schema2可继续，原拒绝约束保持。三种WPF均按已有精确lock和RestoreLockedMode交叉构建通过。首次WPF构建生成的无连字符lock已保存在本地额外证据，重跑时显式使用已有准确锁名，未提交这些临时锁。
- 新WcsToPlc portable及Sportmaster win-x64从固定Git快照构建，ZIP解压后逐文件及业务程序集元数据/内嵌声明核对通过。新portable默认、BeforeMain、Attached三条真实进程/重启/原查询旅程及schema2重验通过。
- 新portable实际发行Ipc.Assembly.dll的共享维护校验方法动态通过（未构造设备）。Windows app/desktop实际DLL静态IL证实安装与恢复/卸载都经过同一入口，且加载实际businessAssembly字段调用四参验证器；这只是静态检查，不能认作Windows运行。
- Darwin arm64尝试加载win-x64宿主报FileLoadException架构不兼容，原输出保留；该项BLOCKED，不把portable探针冒充Windows提权通过。

## 边界

Windows实际启动/交互/UAC/ACL/安装恢复卸载流程仍 **NOT_RUN**；真实SDK/WCS/PLC/认证/实体/现场 **BLOCKED**；Production仍NotReady。新候选的签名扩展激活本次未重跑，c4e1319/2.0.1旧签名PASS不适用于新宿主精确字节。未执行全量C#历史回归。事件关联及自带SHA证明一致性，不自行证明发布者；同时替换全部记录/事件需独立受信索引或渠道检测，现场批准不由此产生。

初始Python/C#失败、原审查原件与架构加载失败均保留。资料检查保留原267项资产失败，最终新增错误0，不宣称整体资料PASS。首次扫描早于最后WPF日志写完，曾产生1项文件identity不符；日志完成后重建manifest，首次检查原件及SHA回执保留；原始red日志行尾空白保留。无客户资料、生产数据、私钥、SDK、字体或二进制进入提交，未改D/RV或旧验收。发布事实以实际Git提交和PR29远端head读取为准，不合并main。
