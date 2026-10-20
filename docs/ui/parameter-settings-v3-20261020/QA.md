# 参数设置 V3 · 验证记录

本记录仅覆盖本轮实际执行。初始读取main `837370e`，完成阶段已三方整合main `bf61337` 的PR76会话时长修复及PR77发布。V2/1.6.1原生和发行证据保持原SourceCommit，不算作V3通过。

## 已执行与未执行

| 层次 | 本轮结果 | 证据与限制 |
|---|---|---|
| 实际输入截图 | 已查看用户 `download.png`，1708×960 | 只证明可见布局/文字，不能证明旧按钮行为与性能；未公开原图 |
| 商业软件参考 | 已阅读3组官方资料、查看8张官方图片 | 见REFERENCES；未安装操作参考软件 |
| 界面导出 | 27 PNG；无页面脚本错误、无HTTP请求 | `screens.json`；24个命名状态+3个尺寸，全部离线合成数据 |
| 视觉走查 | 27张均实际查看 | 两轮覆盖全部图片；已纠正相机连接来源和保存中暂停文案；最小窗允许纵向滚动 |
| 交互原型 | 10组必要任务 PASS | `BROWSER-WALKTHROUGH.json`记录实际时间、浏览器、原型SHA；0页面错误、0HTTP请求 |
| 字段/契约静态复核 | 已完成 | 49条字段/聚合映射；海康6项×2、RCS14项、系统/业务/通用组件兜底；不是49个独立可编辑控件 |
| .NET锁定还原与受影响交叉构建 | PASS，0警告、0错误 | SDK10.0.402；Sportmaster Desktop与UiVerification。`SOURCE-STATE.json`、`CROSSBUILD.json/log`界定源码与命令 |
| Windows原生执行 | **NOT_RUN** | Linux环境不能运行本轮WPF；新增/适配测试仅完成编译，没有冒充Windows通过 |
| 工控机、Windows DPI/键鼠、SDK/真实RCS、现场 | **NOT_RUN** | 必须使用本轮最终源与确切环境分别验收；原型不连接真实服务 |

交叉构建初次默认并行进程遇到环境限制；使用单进程、关闭共享编译器/构建服务器后完成。期间修正了一处新窗口Thickness构造的编译错误，并在整合最新主线后再次构建最终源码，最终日志如上。没有通过放宽断言或吞错误得到PASS。

## 六项任务结果

| 用户任务 | 本次原型走查 | 原生需要的同源验收 |
|---|---|---|
| 左右连续处理，当前结果不串位 | 两个固定相机/扫描/任务组；各自条码、任务、格口对应；RCS异常退出当前格口，历史独立 | 实际独立到达/迟到响应及刷新；共享阻断按Application，不凭相机绿灯推断可作业 |
| 找到指定相机并修改连接 | 左/右对象切换、独立草稿、技术键搜索定位通过 | 真实安装角色、同窗控件和焦点；继承PR76已绑定登录字段的实际适用性 |
| 非法输入定位且保留其他值 | 非法IPv4定位原字段，序列号及其他输入保持；修正后可保存 | 原生数值Binding转换失败、隐藏高级组错误、JSON编辑来源仍走真实原控件 |
| 保存后判断是否运行 | 保存13、运行12分别展示；Unknown禁重提交，核对使用原冻结候选；撤销草稿仍保留Unknown | 原Revision/RuntimeId/CAS、Stage后异常、结果核对、Pending及受控启动实际结果 |
| WCS/RCS异常诊断及恢复 | 共享RCS影响、分层检查、参数定位、未验证业务响应清楚 | 原生已有Health/心跳与受控重连；没有草稿Probe或Query合同则不测试业务、不伪造恢复 |
| 离开未保存参数 | 返回作业不弹窗且保留无效输入；实际关闭可继续、保留或放弃 | 加密保留失败、多窗取消、重读原稿、返回后实际键盘输入和滚动不丢失 |

另走查了安全模板预览只改允许草稿、本地样例异步执行及结果过期、Ctrl+F/Enter/Escape、三个小视口的底栏/输入/保存审阅可达。原型JS有界样例使用可终止Worker，不代表.NET正则25ms/100ms预算被验证。

## 六项商业质量的证据边界

1. **重点清楚**：对象、修改、保存和运行状态有层次；作业例图保留当前包裹与大格口。视觉及模拟已查看，真实扫码另验。
2. **组织一致**：一个目录、四类归属、统一名称和保存方式；当前入口及字段迁移已核对。
3. **密度合适**：表单按填写组织，作业按快速阅读组织；左对齐、按内容设置输入宽度，27图走查无主要遮挡。
4. **细节完整**：错误、关联冲突、未修改、未保存、忙碌、未保存失败、待生效、Unknown、缺驱动、空搜索和关闭选择已覆盖。
5. **操作连贯**：原型任务通过；原生保留缓存控件/同VM/异步通道/独立暂停的代码路径已复核，真实Dispatcher和输入仍需Windows执行。
6. **真实显示可靠**：目前仅原型1600×900、1366×768、1092×614、800×420逻辑视口检查；实际Windows工控机与DPI未通过，不以网页几何替代。

## 复现

直接离线打开 `Prototype.html`。用底部场景选择器切状态，“重置演示”只重置模拟数据。操作员日常页面不会出现这些演示控件。

重新导出需要Node、Playwright及本机浏览器；复用本包字体无需下载。项目根目录执行：

```sh
python docs/ui/parameter-settings-v3-20261020/source/build_prototype.py
node docs/ui/parameter-settings-v3-20261020/source/render_screens.cjs
node docs/ui/parameter-settings-v3-20261020/source/walkthrough.cjs
python docs/ui/parameter-settings-v3-20261020/source/build_field_inventory.py
```

浏览器位置可用 `PROTOTYPE_CHROME`指定。编辑新增中文字符时，使用具有再分发许可的Regular/Bold源字体重新生成子集；不能把缺字当最终图交付。

本轮必要构建：

```sh
dotnet restore tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -p:BusinessModule=Sportmaster -p:EnableWindowsTargeting=true --locked-mode
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -p:BusinessModule=Sportmaster -p:EnableWindowsTargeting=true -p:UseSharedCompilation=false --no-restore --disable-build-servers -m:1 -nodeReuse:false
```

Windows下一步使用现有受影响的parameter/task-redesign/login-session及必须门禁，针对实际风险执行；不要无理由扩大完整套件。原生运行结果、截图、键鼠/DPI记录及发布启动必须明确对应冻结源码。
