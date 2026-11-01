# 相机链路可靠性20证据

本目录只包含通用规格、合成数据及本机生成证据；无厂商SDK、真实现场帧、凭据或私有访问链接。原main039ccdee的单帧失败、修复中的失败与最终回归分别记录，失败不计入最终PASS；具体产品源、命令及验证边界以summary.json和实施报告为准。

最终产品/测试源`d8984c60b3c1d3db8f2bd31d7322801256fe075f`，已整合main的PR172/`f2055ace`。三项Release构建0警告0错误，24套件758项PASS（Application451、原生WPF307）。`summary.json`保留各套命令、原始本机目录及真实退出码；`assembly-hashes.json`记录自有程序集版本/指纹；后续证据提交不改变这些产品/测试字节。`hashes.json`逐件记录本目录证据字节，排除自身；`verify.ps1`为同组复验命令。

`negative-main-single-frame.log`是未修改原产品代码的实际失败：lostPackets=1后相机Faulted。`negative-source.json`标记原产品源、测试身份和程序集指纹；`negative-single-frame.cs.txt`保留测试。其合成IMvsApi只增加LostPackets字段并在原MvsFrame构造的LostPackets位置注入，Program增加专项入口；不能拿此结果替代物理GigE测试。

`intermediate-*`保留测试夹具字段位置、准入ID格式、参数序列化断言、原生读查询容量、图片处理中资格和编译拼写错误；其中参数截图还记录实际渲染发现的控件类型名标题，已进入修复。Unsupported旧TCP提供者在当前SM目录为null，检查其原绑定保留，不能靠恢复旧TCP能力让旧测试通过。容量查询只允许有界重试只读查询，不重试业务命令。

原生用例的物理PDA、相机、声卡/扬声器、客户RCS、安装与现场长稳NOT_RUN。Windows SendInput须真实前台/焦点/无修饰键，不并行运行原生套件；终态Rejected等UI投影明确不生成业务请求。播放器计数证明API调用，不能证明现场听到声音。

`intermediate-native-foreground.log`和`intermediate-continuous-foreground.log`保存未获得真实前台时的原守卫失败。同一d898构建经Computer Use的`sky.activate_window`激活唯一返回的合成dotnet测试窗口后通过；没有伪造按键、跳过或降低守卫。`native-progress.log`保留这些尝试的时间顺序，最终统计仅采用summary中的24个唯一成功套件，不重复累加先前失败或局部成功。

`publish.log`、`publish-smoke.json`、`publish-environment.json`：从同源已验证Sportmaster Release输出执行framework-dependent `dotnet publish --no-build --no-restore -p:BusinessModule=Sportmaster --self-contained false`，真实EXE的`--environment-report`退出0。不是现场安装/SDK/真实RCS验证。原生截图为125%DPI、合成数据，已查看码制选择、紧凑四字段和主条码状态；不扩张为现场物理尺寸验收。
