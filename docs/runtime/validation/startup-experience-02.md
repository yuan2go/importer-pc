# STARTUP-UX-02 验证回执

日期：2026-10-21（北京时间）。开工main `3b883fe`，继承PR83 `0fea1b5`，本轮业务版本1.8.1。产品源码、冻结介质和提交回执单独记录；下面不是旧1.7.0或PR83介质的验收。

| 检查 | 实际结果与范围 |
|---|---|
| Sportmaster Desktop / UiVerification / Application.Verification Release构建 | 零警告、零错误；[桌面](../evidence/startup-experience-02/desktop-build.log)、[原生项目](../evidence/startup-experience-02/ui-build.log)、[核心项目](../evidence/startup-experience-02/core-build.log)。 |
| 首配专项 `--startup-experience` | [52条PASS](../evidence/startup-experience-02/STARTUP-WPF.log)，真实WPF/Dispatcher加合成维护契约；导入与不兼容拒绝、空筛选、菜单可达、五步同编辑器、窗口内底栏、小窗滚动、检查失败/写入前失败/结果未知防重提、正常回执及回执丢失后的原次核对、模态启动交接、未完成续配、安装页中文与分组。 |
| 完整 `--parameter-v3` | [219条PASS](../evidence/startup-experience-02/PARAMETER-WPF.log)，含原作业/参数体验/参数继承/保存审阅回归。运行时125%，800×420及其他客户区为原生验证尺寸；不声称实际物理屏幕是1600×900或已测100%。 |
| `--parameter-refactor`（包含继承） | [75条PASS](../evidence/startup-experience-02/parameter-core.log)，空规则、仅排除、歧义/重复码、预算、原请求冻结、候选和恢复边界。 |
| `--configuration-confirmation` | [41条PASS](../evidence/startup-experience-02/configuration-confirmation.log)，原权威账、CAS、确认、草稿及原件责任。 |
| `--version-compatibility` | [57条PASS](../evidence/startup-experience-02/version-compatibility-final.log)，编译版本/嵌入声明、精确提供者、篡改与混源拒绝。过时的业务1.5.0与RCS1.2.1固定断言改为对照当前工程声明，拒绝断言保留。 |
| 随包说明 | [2项PASS](../evidence/startup-experience-02/release-documents.log)，同源文档及失效/越界引用拒绝。 |
| 离线恢复 `--startup-deployment` | [15条PASS后权限拒绝](../evidence/startup-experience-02/startup-deployment.log)。普通账户不能创建要求提权的维护工作区；未提升权限、未跳过守卫，后续完整恢复未完成。 |

首轮原生复现了空条码规则仍阻止下一步，保留[原失败](../evidence/startup-experience-02/initial-blank-rule-failure.log)。修复在同一有界筛选器和首配入口完成。后续旧断言依赖英文变量名/旧组件身份文案的问题，按新中文可见文案重写定位；仍验证真实错误跳转、原对象/焦点/选区/未保存输入，不删断言换PASS。截图初轮因只绘制带Margin的Content而出现裁切，最终按整个WPF Window绘制，并加入真实窗口内底栏边界断言；不以裁切图证明布局。

本轮用本地临时HTTP只读服务实际打开V2离线原型，走查首配进入和系统步骤；查看67张V2及27张V3设计图的联系表、交接与字段/操作迁移。原型仍为设计证据，Windows实现以上述原生结果为准。临时页和服务已关闭。

## 当前原生图（合成配置）

- [安装选项](../evidence/startup-experience-02/installer-options.png)、[高级项分组](../evidence/startup-experience-02/installer-advanced.png)、[安装确认](../evidence/startup-experience-02/installer-confirmation.png)。安装确认使用合成审阅请求，未执行实际安装/UAC。
- [首配第一步与导入](../evidence/startup-experience-02/startup-step-1.png)、[系统](../evidence/startup-experience-02/startup-step-2.png)、[驱动](../evidence/startup-experience-02/startup-step-3.png)、[组件](../evidence/startup-experience-02/startup-step-4.png)、[末步](../evidence/startup-experience-02/startup-step-5.png)、[小窗](../evidence/startup-experience-02/startup-small.png)。空配置显示缺失项，不冒充现场已连接。
- [未完成配置后的继续入口](../evidence/startup-experience-02/configuration-paused.png)。这是正常首配未完成状态，真实启动异常仍进入原恢复页。

这些是原生窗口程序化事件和渲染证据，不声称已做物理键鼠、IME、触控、目标工控机全DPI或现场验收。真实SDK/RCS、生产认证/实体资格、实际安装升级/重启/双账号、完整恢复后业务核对仍待验。D/RV与历史发行SourceCommit保持。原用户截图不上传。

实施提交：`3f7e785ae313c0b4203c147a0f8113f27b6950bd`。之后仅补测试文案、manifest顺序与交付证据；冻结发行身份另以回执为准。原生日志记录源码级验证，不声称与随后冻结发行二进制逐字节相同。
