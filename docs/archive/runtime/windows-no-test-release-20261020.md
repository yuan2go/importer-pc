# 2026-10-20 Windows 不测试构建与新版本发布

**结果：三套 Windows 自包含安装 ZIP 已构建并上传新 Release；测试未执行。**

源码提交：`939462bee40c9eebe8eb2aa60860217afd95fcdd`；SDK `10.0.402`；业务 `1.4.0`、Application.Contracts `1.10.0`、Parcel DB8。此源继承用户指定 aaa30b5、后续 PR53 及本轮已合并 PR52/54，覆盖构建冻结时全部 main 成果。

[新 Release](https://github.com/yuan2go/importer-pc/releases/tag/windows-main-not-tested-939462bee40c)；资产均为新名字/新 tag，不覆盖旧版本。下载对应业务 ZIP 后完整解压，运行 `Setup.cmd`；原生安装及运行遵循产品操作指南和指定账户政策。

|业务|安装ZIP|字节|SHA256|
|---|---|---:|---|
|sportmaster|[sportmaster-win-x64-939462bee40c.zip](https://github.com/yuan2go/importer-pc/releases/download/windows-main-not-tested-939462bee40c/sportmaster-win-x64-939462bee40c.zip)|104281746|`8bcd0c69729f43e170d3aef5c3b8f3d868bb0a88db332e9a528541bfa2364470`|
|direct-plc|[direct-plc-win-x64-939462bee40c.zip](https://github.com/yuan2go/importer-pc/releases/download/windows-main-not-tested-939462bee40c/direct-plc-win-x64-939462bee40c.zip)|104197153|`11a1459c98591aea433d2948b075f7cb348e26838e7e358f6c27eba435d33dd5`|
|wcs-to-plc|[wcs-to-plc-win-x64-939462bee40c.zip](https://github.com/yuan2go/importer-pc/releases/download/windows-main-not-tested-939462bee40c/wcs-to-plc-win-x64-939462bee40c.zip)|104240537|`8c7f1eff4e4b39763efe184adcef63e0a00155f7a1036ae8724e4c87e71d3571`|

三条 package-application.py --target win-x64 命令均实际退出0；控制台/helper 与真实 WPF 桌面从同一固定源码构建。构建保留既有 ABI/业务版本、选装闭包和公共 DLL 字节门禁，没有执行测试套件、业务程序、安装或启动冒烟。固定源码完整导出，未复制本地报价表、私钥、客户原稿或生产数据。

每包均为 reference core 组合，不捆绑真实厂商 SDK/生产信任或沿用旧签名扩展资格。DirectPlc 完整 UI 延期仍保持。Windows 安装/UAC/ACL/另一账户/中断恢复/目标屏幕及真实设备现场仍 NOT_RUN/BLOCKED，没有 Authenticode 证书，也未宣称商业投产合格。

实际日志、来源/命令/退出码、精确版本、上传大小及SHA256见[证据入口](evidence/windows-no-test-release-20261020/README.md)。旧 bd86fbb、cfe6c0e、7acbb2c 发行保留原字节与原结果，不重标为本源；旧 .zip.partial 保持不可安装中断产物。

后续可从该精确提交干净检出，运行：

```powershell
python scripts/package-application.py --application <sportmaster|direct-plc|wcs-to-plc> --target win-x64 --output <全新目录> --dotnet D:/tools/dotnet/dotnet.exe
```

下载及上传身份与运行验收分开；本次未运行测试。交付回执 PR 仅新增文档/日志/manifest，运行源码仍为上述固定 SHA。实际合并 SHA 与提交数量以 GitHub/API 完成后回读为准。

## 构建前范围及合并裁决（保留形成时记录）


用户要求基于 aaa30b5 后继最新 main，合并全部开放 PR，实际构建全部合并成果的 Windows 安装包并上传新 Release，不测试、不覆盖旧版本。开工远端 main 为 fe9e5bb15cd0e380c3dbd2c0a5369afaee9325c4；开放 PR52 与 PR54。现有根目录报价表排除。

先按保护规则实际合并，再从完整固定源码构建 Sportmaster、DirectPlc、WcsToPlc 三个独立 win-x64 自包含 ZIP。复用 package-application.py，不运行测试、业务程序、安装或冒烟；构建内置的合同/组件/文件散列门禁仍保留。不会把旧 cfe6c0e、7acbb2c 或 bd86fbb 字节改标为新源码。

PR52 与最新 main 仅 manifest 冲突：保留最新商业实现和复审的当前元数据，按路径纳入52的历史八包证据及手动 Actions工作流，原形成时结果与新构建分开。仅为 workflow_dispatch 的流程本轮不触发。历史 D/RV、Windows未验与真实 SDK/设备/现场 BLOCKED 保留。

本节为构建前范围及合并裁决；实际源码、包、日志、Release 和下载身份在构建上传后补充，尚不表示构建成功。

PR52 已实际合并 d3870322127ab6118d47e50c0996123813a14279。PR54 与最新 main 仅 INDEX/manifest 冲突：现行入口保留 PR53 修正和本次新出包状态，cfe6c0e 的已发布资产与原测试日志按旧来源继承；不将其当作本轮新运行验证。PR54 的 portable 注释及现有身份夹具定位修正按原差额继承，本轮不执行夹具。
