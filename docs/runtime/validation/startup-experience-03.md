# STARTUP-UX-03 实施与验证

2026-10-21，继续PR85，开工main3b883fe、继承5587921，PR83/82仍为依赖，PR84及其他工作副本未改动。产品SM 1.8.2，TCP 1.0.0，RCS 1.3.1。当前操作见[说明](../startup-experience-03.md)，设计见[裁决](../../design/startup-experience-03.md)。最终冻结SourceCommit、安装包及SHA256由交付回执追加；不把旧e3941dc/1.8.1的CI或安装器重新标记。

## 当前验证

所有截图为实际WPF HWND/Dispatcher渲染的合成数据。Windows主机125%缩放，不声称目标工控机、物理100%缩放、触控或现场验收。

| 实际执行 | 结果与证据 |
|---|---|
| Sportmaster Desktop、原生夹具、核心夹具Release构建 | 最终全部零警告/零错误；[构建](../evidence/startup-experience-03/desktop-build.log)及同目录ui-build/core-build |
| --startup-experience | [87项PASS](../evidence/startup-experience-03/startup-experience.log)：含6类loopback TCP合同、严格UTF8/换行/有界拒绝、同码分实体、断线不重连、停止排空；真实TCP/RCS静态闭包；底部导入/草稿、无工具菜单、错误定位/输入焦点、CAS/未知/失去回执核对、安装删项/范围/完成页 |
| --parameter-v3 | [225项PASS、60次CAPTURE](../evidence/startup-experience-03/parameter-v3.log)：共享候选、原角色/搜索通知、字段归属、RCS五种事实状态、焦点/选择、保存核对、800×420等原生尺寸 |
| --comm-native-pages | [31项PASS](../evidence/startup-experience-03/comm-native-pages.log)：真实Windows DPAPI拒绝保留原密文、草稿不可用提示、未保存关闭保护；独立维护目录/原检查失效守卫，日常页/历史/帮助复用一个业务实例 |
| --parameter-refactor | [75项PASS](../evidence/startup-experience-03/parameter-refactor-final.log)：条码、初始配置、保存与恢复、继承、原请求冻结及适用业务重量范围 |
| --version-compatibility | [57项PASS](../evidence/startup-experience-03/version-compatibility-final.log)：精确兼容、编译/发行身份、拒绝改写声明及旧版 |
| --rcs-heartbeat | [27项PASS](../evidence/startup-experience-03/rcs-heartbeat-final.log)：原唯一循环、驱动编码映射、独立编号、失败及不重发 |
| --runtime-identity | 26项逻辑PASS：共享Users主体、缺成员/deny-only/提权/服务账户拒绝；实际Windows双账号安装另验 |
| Python release_documents / application_compatibility | 2 / 17项通过，包含当前版本和签名包声明、不重标旧RCS包 |

首轮参数测试仍按旧参考草稿只填写正则，新的真实TCP/RCS必填字段为空，原[失败](../evidence/startup-experience-03/initial-bootstrap-failure.log)保留；改为显式合成IP/端口/协议字段后完成原保存断言。首轮兼容/继承夹具使用固定参考DeviceId，与机器上已占用资源争锁，原[租约拒绝](../evidence/startup-experience-03/initial-lease-contention.log)保留；只隔离合成标识，实际工厂/机器级租约及互斥守卫继续启用。商业原生旧断言依赖已删除的首配技术目录和独立检查按钮，原权限/路径变更/检查失效断言移到独立维护窗口，没有删除守卫断言。

首次集成构建暴露测试源链接遗漏及类型冲突，按原工程链接实际RCS源/资源修正；一次构建与正在运行的原生夹具争DLL，原窗口退出后重新构建，最终零警告。未使用删断言、跳过或吞异常替换成功。

## 现场与安装边界

新装默认所有Windows用户共享同一数据根、机器政策及资源租约；实际Users组ACL/UAC/双账号/安装升级/重启尚未执行。旧账户、数据根和模拟/生产身份升级时保留，不自动迁移至新空库。内置TCP默认仅观察，包裹关联须现场确认；真实认证、生产资格及原业务责任未满足继续拒绝业务，不将合成/本地就绪当在线或生产成功。

真实相机/IP网络、真实RCS、SDK及目标IPC/现场仍待验；未控制真实设备、未上传用户截图/客户原稿/生产数据/SDK，D/RV未关闭。公开manifest原有历史问题不在本包扩张为修复。

## 冻结前资源键核对

8955bb1保留本轮主体、87项初验及中间介质身份；该中间ZIP不作为最终推荐安装器。核对发现端口前导零可形成不同文本资源键，后续统一使用规范数值端口构造物理目标键。新增真实ResourceLease断言：不同ReaderId、80/00080不能同时取得同一目标；原租约释放后才可再次取得。新的[89项首配专项](../evidence/startup-experience-03/STARTUP-FINAL.log)通过，最终两份构建日志零警告/错误，原87项及原图不覆盖。[最终相机页](../evidence/startup-experience-03/final-startup-step-3.png)修正TCP包裹关联说明，[完成页](../evidence/startup-experience-03/final-installer-complete.png)保持底部运行选项。后继仅用新提交重新冻结，最终产品SourceCommit和安装器另列。
