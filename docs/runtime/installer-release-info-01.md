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

正式文件名自动从实际业务版本和源码生成：`CainiaoSupplyStation_Setup_v1.4.1_win-x64_<commit前8位>.exe`。版本资源同步为业务版本及其四段文件版本（例1.4.1.0），完整commit见下述记录。包装器拒绝错误版本名、混源码、缺分支来源或新旧记录不一致，保留旧输出，不覆盖历史EXE。

新安装默认程序根为 `C:\Program Files\CainiaoSupplyStation`。实际发布放在其 `releases\sportmaster-<commit前12位>-<唯一值>`，程序文件夹均为英文。已安装版本仍带出原选项，用户可在升级时显式选择新的英文程序根；数据根和账户保持原值，不靠移动/清空数据换目录。旧版文件、维护入口和原受保护根兼容保留。

## 安装目录里的源码记录

实际发布根、`app` 和 `desktop` 各保存相同的 `BUILD-INFO.json` 与 `BUILD-INFO.txt`。从快捷方式选择“打开文件所在位置”即可看到 `desktop/BUILD-INFO.txt`，不需要安装Git。记录包含产品/业务版本、目标架构、实际构建分支、完整commit、源码提交时间、实际打包UTC时间及公开源码仓库。

文件进入原 app/desktop 发布登记及完整介质 SHA256SUMS；安装沿原完整性和ACL机制复制，不能在准备后偷偷替换而保持成功。记录只供诊断，原受保护部署登记仍是唯一权威；不写入 `.git`、远端凭据、本机SDK路径、用户身份或生产配置。

`package-application.py` 在分支上构建时自动核对该分支指向当前HEAD。detached CI介质明确记为 `DETACHED`，不猜main；正式单EXE要求 `--source-branch <实际可核实分支>` 与当前commit一致。随后 `package-installer.py --media <同源码ZIP> --output <新的交付目录>` 自动生成规范英文EXE名，并再次核对分支/commit。源码与随包指南须先提交，任何代码变化都生成新的commit后重新出包。

必要验证与本轮精确交付身份随后记录，不将原129的30项实际升级结果重新标为新源码。真实海康/RCS/现场仍按各能力证据单列，原D/RV不倒写。
