# 稳定性整改证据身份

本目录只收录获授权合成测试、Windows主机规格和原生截图，不含生产数据、凭据、厂商DLL、字体或客户原稿。原审查ZIP保持原路径和哈希。development/中账号SID已在一份公开失败日志中排除，原始SHA及替换规则见ORIGINALS.json；其他日志保持原字节。原文件在D盘本地保管。

- development/：预冻结工作树的受影响回归与259.2万索引规模记录，不能冒充固定发布验证；其完整二进制集合未留存。
- first-freeze-7017899/：原第一候选源码7017899c1657a2fd6ca2f38adfc97c32e0d5a299的构建/启动/原生结果。其采集器结束报告有后续修复，不作为长稳采集交付。保留内部UI方法不可访问导致的首次测试编译失败；后续仅用反射调用该原方法，保留所有断言及异常语义。
- fixed-c728754/：采集器修复后、整合PR97之前的实际候选，SourceCommit c728754e2156a8616cba8bf0bdb40f2f95135086。ZIP SHA256 812D8B910B61464F0696963D1875850CC6321606BE77CEF0C6F2C5CD558921C1。Win-x64参考组合，实际EXE环境报告.NET10.0.13，原生/责任夹具宿主.NET10.0.11；两者分别计证。冻结EXE只读启动与相同发布DLL的原生/责任测试通过，未安装或发布。

2026-10-24随后main合入PR97（c310705），以上结果不自动扩张至整合后的树。最终整合记录另列。[结果与边界](../../validation/win11-24x7-stability-remediation-01.md) · [运行指南](../../win11-24x7-stability-remediation-01.md)。真实设备/RCS查询合同/8GB目标机/多Windows版本/24h/72h均未获得资格。


最终整合固定源码 **ac085f37c0ed04569d9bbf0c35c7ead226bc0ecc**，ZIP SHA256 **2387E07531885FBE1F3B6812F818E44429173C1855DB5B358F9C62267B613A55**，记录见[RESULT.json](RESULT.json)和[integrated-ac085f3](integrated-ac085f3/BUILD.json)。该候选继承已合并PR97；PR98未由本任务合并。开发回归与固定DLL复核分别保留，固定启动/原生稳定性/原生任务链/持久责任全部通过。全仓原CI门禁保留，CI状态以本PR精确head及其实际合并源码为准。

运行 `scripts/verify-commercial-wpf.ps1 -Media D:/DevCache/importer-pc/stability-release-main97 -Output D:/新目录 -Win11Stability` 或 `-TaskHierarchy` 可复核实际发布DLL。custody目录保存实际编译项目和最小访问适配源；适配只通过反射调用发布UI内部共模函数，原始异常原样重抛，生产DLL及断言未修改。源测试来自固定commit，引用全部固定DLL并另存每个哈希；不能将夹具进程称为已安装生产EXE。运行数据和完整合成库留在D盘，不上传Git。

归档目录的Git属性明确关闭换行归一化，Git blob也已核对FILES.json原哈希。FILES.json只覆盖本证据目录并排除它自身；原件/旧失败/先前冻结结果不倒写为最终SourceCommit。没有长稳PASS，也没有真实查询合同或设备验收结论。
