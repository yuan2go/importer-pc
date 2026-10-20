> 现场系统已补充为Win10 22H2 Home/Pro x64为主，兼容目标及待实机项见[Windows兼容](windows-client-compatibility-01.md)。原7c/Win11验证记录保持历史身份。

> 后继安装体验以[单窗口与一次授权](installer-single-window-01.md)为准。下文7c构建和41项结果保持原历史身份。

# 菜鸟供包台安装、依赖与源码记录

本轮[工作包](../work-packages/WP-IPC-INSTALLER-RELEASE-INFO-01.md)继承已验证的129版安装器，新增英文发行名称、英文新装目录和随安装保留的源码记录。现场环境已明确为 Windows 11 64位、海康相机。原129 EXE及实际安装/升级结果保持原来源，不改名冒充新版。

## 现场需要准备什么

|项目|基础客户端|海康真实读码与现场业务|
|---|---|---|
|Windows|受支持版本的 Windows 11 x64，含正常桌面环境；安装时需管理员授权，日常用指定账户普通启动|同左；还需匹配设备与厂商驱动支持|
|.NET / WPF|EXE内已包含，无需单独安装SDK或Desktop Runtime|同左|
|数据库|SQLite及原生库已包含，无需SQL Server或数据库服务|同左，按原配置/业务数据保护规则操作|
|Git / Python / Node / PowerShell7 / WebView2|正常安装和启动均不需要|显式高级开发/签名脚本的工具不属于日常运行依赖|
|海康SDK及驱动|默认模拟配置不使用；未放入本EXE|从批准的厂商渠道安装匹配的IDMVS/MvCodeReader SDK及GigE驱动；当前适配2.0.0.4，不把普通MVS相机SDK或任意版本当成兼容|
|厂商原生运行库|基础客户端使用随包运行时及Windows系统组件|按厂商安装器要求准备其完整同位数依赖（包括厂商要求的VC++运行库等）；不猜测未提供的额外版本|
|扩展、资格和现场配置|安装后可以完成模拟首配并打开工作台|还需与实际宿主字节匹配的海康扩展、精确SDK资格、相机IP/设备绑定、真实WCS/RCS合同和访问配置；不能用模拟资格代替|

安装器及运行程序均为 win-x64 自包含；本机依赖核对确认含 coreclr、hostfxr、vcruntime140_cor3、WPF原生库和 e_sqlite3。基础客户端不需要联网下载运行时。基础软件可以拷贝此单EXE后离线安装并打开；这与海康设备和客户业务已经联调是不同事实。Microsoft当前系统支持依据为 [.NET10支持平台](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)及 [Windows .NET说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)，仅有“Windows11”名称不能替代具体系统版本仍在支持周期内。

厂商SDK没有获准再分发，不随基础EXE上传或捆绑。当前代码适配的库包括 `MvCodeReaderCtrl.dll`、`MVGigEVision.dll`、`MvSmtDevCtrl.dll` 及其完整同位数依赖；完整安装和资格说明见[海康驱动运行指南](hikrobot-reader-01.md)。原缺SDK、合同、资格时拒绝能力的规则不变。

## 英文安装包与目录

一个固定包只包含打包时选定的一个业务模块。正式文件名从该模块的简写和业务模块自己的版本声明生成（不是框架、安装器或驱动版本）：`CainiaoSupplyStation_SM_Setup_v1.4.1_win-x64.exe`，其中Sportmaster简写SM由用户明确确认，不在文件名加入commit。版本资源同步为业务版本及其四段文件版本（例1.4.1.0），分支/完整commit见下述记录。包装器拒绝错误版本名、混源码、缺分支来源或新旧记录不一致，保留旧输出，不覆盖历史EXE。后续发行按版本管理命名，每次构建的精确来源仍以记录中的完整commit核对。

新安装默认程序根为 `C:\Program Files\CainiaoSupplyStation`。实际发布放在其 `releases\sportmaster-<commit前12位>-<唯一值>`，程序文件夹均为英文。已安装版本仍带出原选项，用户可在升级时显式选择新的英文程序根；数据根和账户保持原值，不靠移动/清空数据换目录。旧版文件、维护入口和原受保护根兼容保留。

## 安装目录里的源码记录

实际发布根、`app` 和 `desktop` 各保存相同的 `BUILD-INFO.json` 与 `BUILD-INFO.txt`。从快捷方式选择“打开文件所在位置”即可看到 `desktop/BUILD-INFO.txt`，不需要安装Git。记录包含业务模块标识、模块简写、业务模块版本、目标架构、实际构建分支、完整commit、源码提交时间、实际打包UTC时间及公开源码仓库。

文件进入原 app/desktop 发布登记及完整介质 SHA256SUMS；安装沿原完整性和ACL机制复制，不能在准备后偷偷替换而保持成功。记录只供诊断，原受保护部署登记仍是唯一权威；不写入 `.git`、远端凭据、本机SDK路径、用户身份或生产配置。

`package-application.py` 在分支上构建时自动核对该分支指向当前HEAD。detached CI介质明确记为 `DETACHED`，不猜main；正式单EXE要求 `--source-branch <实际可核实分支>` 与当前commit一致。随后 `package-installer.py --media <同源码ZIP> --output <新的交付目录>` 自动生成规范英文EXE名，并再次核对分支/commit。源码与随包指南须先提交，任何代码变化都生成新的commit后重新出包。

必要验证与本轮精确交付身份见下节，不将原129的30项实际升级结果重新标为新源码。真实海康/RCS/现场仍按各能力证据单列，原D/RV不倒写。

## 本轮最终交付与实际结果（源码冻结后补记）

EXE：`CainiaoSupplyStation_SM_Setup_v1.4.1_win-x64.exe`，172493384字节，SHA256 `826f7ff47fd8bcddbe785a4a9e3cdbc92ec91cc512ca108e5de8050ca9a6b5f2`。源码 `7c384540393b1e77ceeba048a87a3916dd1af63c`，分支 `codex/installer-release-metadata`。文件版本1.4.1.0，业务模块版本1.4.1；精确来源记录位于安装发布根/app/desktop。签名状态NOT_SIGNED。

[逐项证据与边界](evidence/installer-release-info-01/README.md)、[交付身份](evidence/installer-release-info-01/delivery.json)和[41项实际结果](evidence/installer-release-info-01/RESULT.json)：以原Simulation部署实际升级到 `C:\Program Files\CainiaoSupplyStation`，原数据和配置保持、完整备份核对，两个图标真实创建，完成后和两个快捷方式各自正常启动新工作台并关闭。两处发布库存中的来源记录哈希和三处JSON/TXT实际读回一致。5+6+2+14项相关脚本测试PASS，冻结源码10个Git blob与manifest自校验PASS。

本机是Windows11家庭中文版23H2 build22631 x64，已不在微软当前支持周期；实际本机成功不能代替受支持系统资格或干净系统验证。现场应使用仍受支持的Windows11 x64具体版本，生命周期见[微软说明](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro)。本轮未在受支持的新系统、干净虚拟机或真实海康设备重验。EXE自带CLR/WPF10.0.13、SQLite，海康依赖仍按上文单列。

本节与证据在7c源码冻结后补充，不声称已写入此前生成的EXE随包指南；随包指南/源码记录保持7c原字节，原129成果及旧D/RV保留。

## 后续原字节公开上传

2026-10-20 用户另行授权发布既有任务产物。源码7c及EXE原字节已上传[新Release](https://github.com/yuan2go/importer-pc/releases/tag/installer-sm-v1.4.1-7c384540393b)；本次不重建、不测试，详细[PR合并与上传回执](publication-installer-release-info-20261020.md)与原验证记录分开。
