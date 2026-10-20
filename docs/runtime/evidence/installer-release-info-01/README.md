# 英文模块版本安装包 · 精确来源与实际Windows验证

源码冻结 `7c384540393b1e77ceeba048a87a3916dd1af63c`，实际分支 `codex/installer-release-metadata`。当前固定包只有Sportmaster模块，用户确认简写SM，版本1.4.1来自该业务模块的csproj并与composition及app/desktop发布登记核对，不取安装器/框架/驱动版本。文件名不含commit。

精确交付见 [delivery.json](delivery.json)，41项实际安装检查见 [RESULT.json](RESULT.json)、[原始日志](E2E.log)和[实际工作台](actual-workstation.png)。本轮未改写129版EXE或原30项记录；安装数据为本任务原有独立Simulation数据，不连接实际设备。安装程序未做Authenticode签名。

## 复核与边界

- [完整原生测试夹具](Probe.cs.txt)和[项目](Probe.csproj.txt)在运行前原字节保存，SHA见delivery.json；相对旧夹具增加模块版本文件名、Windows版本属性、英文目录和安装后来源记录检查，保留原配置、防丢数据、完整备份、受保护登记、实际快捷方式和正常工作台断言。仅延长人工UAC等待上限，不替代Windows授权。
- 复核命令：先将夹具还原为Program.cs/Probe.csproj并用.NET10 Windows构建，再执行 `dotnet Probe.dll <本版EXE> <新的本地证据目录> 7c384540393b1e77ceeba048a87a3916dd1af63c --upgrade`。前提是原有本任务独立Simulation安装；不是任意生产环境自动化脚本。安装/预检查实际Windows UAC由用户点击。
- 41项为一次实际升级：原中文程序根迁入显式选定的英文程序根，沿原change plan并排发布；原数据根与seed/configuration ledger字节保留，旧版本保留，新增1份完整备份且所有恢复文件匹配COMPLETED.json。完成按钮、桌面和开始菜单分别真正启动新版本并以0退出，工作台双位快照正常；不把准备/预检查当实际安装。
- 安装后的release/app/desktop三处BUILD-INFO.json和txt实际读回一致，绑定分支、完整commit、SM、1.4.1和win-x64；app/desktop库存哈希匹配。原受保护部署登记继续作为唯一权威。机器SID、配置路径和实际用户目录仅保存在Git外INSTALLATION.json/选项目录截图，没有放入本目录。
- 现场正常运行不需要Git/Python/.NET开发环境。夹具运行需要开发环境与测试授权，与现场运行依赖分开。当前机器有开发工具/厂商环境，因此此轮不是干净Windows或海康开箱联调证明。
- 本机实际系统为Windows11家庭中文版23H2 build22631 x64。41项本机结果真实保留，但该版本已不在微软支持周期；生产现场需采用仍受支持的Windows11 x64版本。本机成功不扩大[.NET10支持平台](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)或[Windows生命周期](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro)。此轮没有在另一受支持系统/干净虚拟机重验。

## 其他必要核对

[来源测试](provenance-tests.log)5项、[单EXE封装测试](installer-package-tests.log)6项、[随包指南](guide-tests.log)2项、[统一归档边界](unified-package-tests.log)14项均PASS。后两项在本轮仅文件名末次改动前运行；文件名改动不涉及这些模块，未反复跑无关套件。来源/封装测试包含末次模块命名、模块版本和不一致来源的拒绝。

[冻结源码manifest](source-manifest-verification.json)验证10个新增/修改Git blob及自校验；后续证据文档提交独立核对，不重标7c源码。[随包记录与依赖核对](payload-metadata-verification.json)与[脚本原件](verify-payload.py.txt)核对三处记录和发布库存、单业务选择、自包含CLR/SQLite及桌面WPF依赖；首个补充核对器对console/WPF依赖位置的错误假设见[原失败及修正说明](supplemental-verifier-correction.txt)，未改生产代码或削弱运行断言。

海康SDK未获批准再分发，不捆绑厂商SDK/驱动；真实相机、资格、外部WCS/RCS与现场验收本轮均未运行/未批准。未更新历史D/RV。
