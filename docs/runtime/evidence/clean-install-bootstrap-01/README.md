# INSTALL-BOOTSTRAP-01 · SM1.11.1实际交付

冻结源码及本地介质、单EXE、原生验证均为14d3e38ce461d38e900af9d8c584970ee2ff4e25；[范围摘要](SUMMARY.json)、[安装器封装回执](INSTALLER.json)保留来源。安装器SHA256：f62c9b65a9cec82e183d09103451f8f3cb25e961956701a15bf819318b545063；738个介质文件，未签名。

- [只读检查5项](READONLY.log)：真实登记D根不存在时，读取位置/未安装状态/路径预览均不创建目录，普通用户不能开通受保护目录。
- [授权隔离检查8项](ELEVATED.log)、[退出和正式根未创建](ELEVATED.json)：每级新目录ACL、现有权限不改写、同名文件、不安全ACL及链接拒绝。仅在隔离验证目录操作并清理，没有正式安装。
- [冻结WPF回执](RESULT.json)、[8项原生记录](WINDOWS-WPF.log)：使用同提交实际发布DLL，D根不存在时选项和确认可达；截图为[安装选项](clean-install-options.png)、[确认安装](clean-install-confirmation.png)。通用runner原Scope文字为多模式共用，本次没有注入业务身份或构建业务端口，只运行干净安装分支。
- [独立EXE实际打开](STANDALONE.json)、[确认页原生可访问性文本](STANDALONE-CONFIRMATION.txt)：从D盘最终交付EXE实际启动，内嵌校验完成后核对选项及确认页，取消后退出；未点击“安装”，D部署根、服务和安装登记仍不存在。

首轮测试工具的预期异常类型及编译别名错误在冻结前修正，局部失败原件仍保留在本机忽略目录。SM1.11.0旧故障安装器与清理证据保留原来源，不能当作1.11.1通过。[CI88](https://github.com/yuan2go/importer-pc/actions/runs/39714858200)在本次记录时已通过新增ACL回归和真实System边界，整轮仍在运行；不称整体PASS，也不与本地二进制互换。

本机新交付目录为D盘“重新全流程测试_SM1.11.1”；旧重测目录已标记历史故障包。正式新安装、首配、真实设备/RCS和现场流程留给用户本轮测试；本次验证后没有恢复旧配置或设置管理密码。海康组件未操作。
