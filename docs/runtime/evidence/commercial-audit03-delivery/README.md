# 审查03交付证据

运行源码1401b7d3663bd7ca40aaa87773b023162178611c，业务1.2.7 / ABI1.8.0 / DB7；原报告固定ec698bd，原件位于审查03目录，未修改。总交付见[结果](../../commercial-audit03-delivery-results.md)。

- `inventory.json`：起点/当前main、原工作区保留、PR34及旧Release身份。`ARTIFACTS.json`、`media-verification.json`：最终13个ZIP/签名包/identity的hash、CRC/内层清单/指南来源与PE apphost位数。
- `release-input.log`、`release-snapshots.log`、`release-maintenance.log`：冻结发布Parcel DLL结合验证器执行；DirectPlc/WcsToPlc Flow保有同源验证构建字节，三条真正发布入口另见smokes。`verification-runtime-files*.json`分别绑定验证器及每个实际DLL；新版验证器仅增SQL6迁移夹具，不改发布DLL。
- `signed-t1.log`为首次双向反序T1参考结果；`signed-t1-migration.log`还要求实际图像责任释放、两原件确已归档，再核SQL6→7、原报文/数字ID不变与歧义只读拒绝。这里的SQL6为显式合成旧格式，不冒充旧Windows现场。
- `signed-original-unknown.log`、`selected-journey.json`、`rcs-journey.json`及`selected-journey.log`：实际签名加载与解压CLI的安装/绑定/CAS/激活/失败保旧/卸载，及RCS本机HTTP对端的Unknown、重启/Query/重连零重发。`selected-release-journey.py`继承PR34 c073044的既有工具，仅改不可变版本及避免把参考reader称为真实硬件；运行argv见pipeline及本结果说明。
- `smokes/`：9个schema3组合记录与实际argv/退出码，三个Application × default/BeforeMain/Attached；各可选场景另起真实独立本机peer进程。`cli/`：从/tmp启动实际解压DirectPlc CLI的Inspect/Export/Backup/PlanRestore及源DB/config指纹。
- 原回归日志各只解释其运行源码范围；AA22、RCS、BHR、P2、UI、quality与upgrade先在实施阶段执行，实际最终发布另以上述入口及DLLhash绑定。`resources.log`/`resource-observation.json`是有界队列、停止保有、重启及T3故障套件的耗用，不能当作目标节拍/离线时长验收。
- `input-red.log`为13bb1fd运行源码加新增反例的7项失败，测试正文保存在`input-red-source.cs.txt`（与fa022d4中该文件一致）。`input-setup-invalid-guid.log`是更早夹具错误，不当产品缺陷。原F03反例由原审查ZIP保留，F04和PR34过期1.2.4断言亦保留原审查身份。
- `aa22-old-callback-assertion.log`保留“回调接纳等于实体准入”的旧断言失败，修为核实体/外呼实际未准入；`bhr-original-confirmation-fixture.log`保留合成Mode变化未更新配置账指纹的失败；`upgrade-original-db5-fixture.log`及`quality-original-db6-reader.log`保留新DB7下的旧格式夹具/读者断言失败。`signed-t1-migration-original-unreleased-image-fixture.log`是额外迁移夹具在原图尚未持久释放时触发正确LegacyUnsettled，修夹具等待真正清偿及归档，不放宽迁移守卫。
- `public-path-normalization.json`逐文件绑定本机原始证据与公开副本的SHA256；仅将机器绝对路径替换为`${WORKTREE}`/`${DOTNET}`，原始字节保存在本地`.artifacts`，不入Git。原配置/事件中的hash仍对应替换前真实输入，不宣称由公开路径副本重算；原PDF/ZIP及发布二进制均未改。
- `publication.json`记录16个远端资产的state/size/GitHub SHA256与本地逐一相等，参考预发布不等于main合并或Windows验收。
- `reference-trust.json`只含本轮生成的RSA公钥，TestOnly=true；私钥始终在Git/媒介外。它是公开可复现参考签名输入，不是Production批准信任。
- `powershell-parse.log`仅PowerShell7.6.7 macOS解析；其官方归档SHA256 50dde99bb066adacbb9a748182d09eb9beff25119dc3b12e85c84b6b6b1c5da9，临时工具不随媒介分发。`powershell-host-rejection.log`证明非Windows拒绝，不能用来声称Windows分支、ACL/UAC或快捷方式已运行。

全为公开通用实现和合成数据；不含SDK/字体/私钥/凭据/生产数据。Windows原生、真实SDK/设备/合同/现场及目标负载/离线条件仍NOT_RUN/BLOCKED。全目录以最终manifest及发布回执定位，历史FAIL不删除或改成PASS。
