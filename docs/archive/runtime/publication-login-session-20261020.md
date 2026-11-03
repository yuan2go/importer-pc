# 登录会话时长修复与 SM 1.6.1 Windows 发布

[下载1.6.1安装包](https://github.com/yuan2go/importer-pc/releases/download/installer-sm-v1.6.1-92586bf44cde/CainiaoSupplyStation_SM_Setup_v1.6.1_win-x64.exe) · [Release及校验文件](https://github.com/yuan2go/importer-pc/releases/tag/installer-sm-v1.6.1-92586bf44cde) · [固定源码](https://github.com/yuan2go/importer-pc/tree/92586bf44cde7efb6cec1538d7b9654ee518c0fb)

用户授权基于最新main修复日常参数页已绑定登录却隐藏会话时长的问题，保持唯一配置账与命令守卫，只做相关必要验证，提交并合并PR后重新发布同源Windows安装包。开工main为`837370e3c21a4b83ca0f20dc9a606e760ba9acac`，独立工作树干净，没有其他开放PR；用户原工作区未切分支或覆盖修改。

[修复PR76](https://github.com/yuan2go/importer-pc/pull/76) head为`280f76883c32d1c68cc7f029b6918d5eb1a65bb2`，普通merge后的实际main为`92586bf44cde7efb6cec1538d7b9654ee518c0fb`，文件树与已验证head一致。安装器及内嵌客户端都从该合并提交的不可变Git导出构建，后补发布文档不改包或移动tag。

## 修复与使用

在“参数配置 / 系统 / 数据与记录”维护“操作员会话有效时长”，单位ms、范围100～86400000。日常schema-3直接读取原`ConfigurationRoles`登录选择，首配继续读取自己的`operator-login/Bound`草稿。绑定、解绑、重新绑定复用同一控件和原始输入；只有维护认证时不显示操作员会话字段。已有校验错误即使解绑也保留可定位入口，以便纠正完整候选。

字段仍双向绑定同一`ConfigurationPolicyDraft.SessionLifetimeMs`，保存沿完整候选、CAS、暂停/维护授权、原责任守卫与回执核对；只保存Pending，受控重启前Effective不变。没有新增配置账、UI直接落盘或设备命令。详细[实现与复现](login-session-lifetime-fix-01.md)保留修复前失败、夹具比较基准纠正和开发阶段证据；原PR74审查与1.6.0未修复记录保留形成时身份，此版解决该具体意见，不宣称关闭所有历史D/RV。

## 精确安装包

| 项目 | 本次身份 |
|---|---|
| 文件 | `CainiaoSupplyStation_SM_Setup_v1.6.1_win-x64.exe` |
| 字节数 | 177645964 |
| SHA256 | `97e32a18c85f9fb7c0d304fab74e4c7f7acdf1cd31fccd4e02a2ccc9be41609b` |
| SourceCommit | `92586bf44cde7efb6cec1538d7b9654ee518c0fb` |
| 构建引用 | `origin/main`，构建时核验确实指向上述提交 |
| 文件/产品版本 | `1.6.1.0` / `1.6.1+92586bf44cde7efb6cec1538d7b9654ee518c0fb` |
| 内嵌介质 | 106392414字节ZIP，734文件、解压242336766字节 |
| 内嵌ZIP SHA256 | `9c59f74032ea02746877ef4dc0f7095579e9172e1e46d184e7ab3c50915669cb` |
| 发布资格 | 未签名、GitHub prerelease、Simulation reference；未取得生产/现场资格 |

[BUILD-INFO](evidence/login-session-publication-20261020/BUILD-INFO.json)、[DELIVERY](evidence/login-session-publication-20261020/DELIVERY.json)、[PUBLICATION](evidence/login-session-publication-20261020/PUBLICATION.json)及[SHA256SUMS](evidence/login-session-publication-20261020/SHA256SUMS)与EXE同时发布。基础客户端自包含.NET/WPF/SQLite，安装仍沿[原单窗口流程](installer-single-window-01.md)；海康及现场依赖见[安装说明](installer-release-info-01.md)。未另上传便携ZIP，EXE是当前安装入口。

Sportmaster补丁为1.6.1，共享桌面消费者DirectPlc/WcsToPlc声明为1.4.4，但本轮没有发布这两种业务安装包。Application.Contracts仍1.13.0，数据库及SDK/外部协议版本未变；未重新构建签名扩展闭包，旧扩展不能据版本名称推断与新宿主字节匹配。

## 必要验证

| 范围 | 实际结果 |
|---|---|
| 代码与配置 | [开发阶段聚焦证据](evidence/login-session-lifetime-01/VERIFICATION.json)：24项真实WPF/Dispatcher合成端口检查通过；三种受影响桌面构建均0警告0错误，版本/兼容17项、随包文档2项通过。覆盖未授权拒绝、授权单修订、Pending/Effective及不产生业务发送。 |
| 固定源构建 | SDK10.0.402、locked restore、不可变Git导出，app、真实WPF及单EXE构建成功；[734文件介质核验](evidence/login-session-publication-20261020/medium-verification.json)通过，CLI business-identity实际读回SM1.6.1；app/desktop共用12个、installer/desktop共用15个Ipc DLL原字节一致。 |
| 最终发布DLL | [最终聚焦24项](evidence/login-session-publication-20261020/WINDOWS-WPF.log)通过；[10个实际加载的相关生产程序集](evidence/login-session-publication-20261020/frozen-verification.json)与介质哈希一致。测试宿主定义源280f，编译发生在提交前、基准main837370e；[夹具身份](evidence/login-session-publication-20261020/fixture-identity.json)明确记录与生产92586bf分别计证。 |
| 实际Windows输入 | 本机Windows11 Home 10.0.22631 x64、桌面125%。实际键盘将600000改为610000，Ctrl+F搜索SessionLifetimeMs得到一项，回车返回实际字段仍为610000。该手工输入未再保存；授权保存守卫由上述隔离合成运行核验。 |
| 最终EXE冒烟 | 实际EXE自动准备后显示1.6.1安装选项，取消正常退出，准备目录清理且原部署登记哈希不变。未观察/采样整个准备转场，不宣称高频防闪动、UAC或完整安装升级通过。 |
| 上传与下载 | [远端上传回执](evidence/login-session-publication-20261020/artifact-upload-receipts.json)逐项核对大小/摘要；[匿名下载核验](evidence/login-session-publication-20261020/public-download.json)完整回读EXE，字节数与SHA256一致，公开tag指向92586bf。 |

[同源高清WPF图](evidence/login-session-publication-20261020/daily-session-lifetime.png)、[实际输入](evidence/login-session-publication-20261020/native-session-edited.jpg)、[搜索](evidence/login-session-publication-20261020/native-session-search.jpg)、[回车后留值](evidence/login-session-publication-20261020/native-session-return.jpg)、[安装选项](evidence/login-session-publication-20261020/installer-observed.jpg)均保留原始像素。[原生输入记录](evidence/login-session-publication-20261020/native-input.json)、[介质构建](evidence/login-session-publication-20261020/media-build.log)、[安装器构建](evidence/login-session-publication-20261020/installer-build.log)分开记载方法与边界。

本次按实际风险执行聚焦验证；实现提交明确使用`[skip ci]`避免重复整套条码/RCS/其他UI流程，未把旧CI改标为新CI，也未绕过保护规则或强推。后补文档PR同样不重复业务套件，清单、自哈希、回执原字节和更新链接另行核对。

## 待验与历史边界

完整新版本安装/UAC/升级/回滚、目标工控机、Win10 Home/Pro、其他DPI/触摸/CPU/干净受支持系统、精确签名扩展闭包、真实SDK/相机/RCS及现场物理验收 **NOT_RUN**。原生WPF与本机实际输入证明该字段和记录的交互，不证明生产资格。未分发SDK、额外字体、客户原件、生产数据/凭据或测试信任，Production缺必需能力继续拒绝。

原1.6.0、1.4.1及各历史D/RV、图集、介质和失败证据保留原来源；不将旧安装58/41项或其他业务测试记为本版通过。发布记录和下载入口通过独立文档PR入main，EXE随包`LOGIN-SESSION-FIX.md`保持92586bf时已提交的指南字节，本页及上传后回执是其后补充。
