# 相机读码闭环21 · 合并前证据

最终产品/测试源`9628b1fbde3a242f2fffaaa4a9cc6a0be163e486`，main基线f2055ace/PR172，继续PR171。三个Release构建0警告0错误；26套件810项PASS：Application 13套496项、原生WPF 13套314项。`summary.json`逐套记录实际命令、退出码和原始本机目录；全部26个唯一套件均退出0。后续文档/证据提交不改变这些产品/测试字节。

`application-camera-chain-closure.log`新增20项检查：左右入口终止唤醒、满载停止、64组取消竞争、不能满足的预留、未发布原帧所有权、真实池桶字节上限、背压后只识别最新视频、严格中文编码及真实反色搜索。该64组竞争只计为一项汇总断言，不用循环次数增加PASS数量。

`native-camera-chain-reliability-ui.log`19项：原有终态与空框恢复，再加长波次换行、1366/800稳定任务/结果几何，隐藏旧任务及旧格口、匹配完成后恢复，逐次真实HTTP不合并。`native-operator-ux-18.log`保留原36项断言，包含短格口实际44DIP、四字段可见、800紧凑异常至少一整行和失效处理。SDK API为显式合成，ZXing/SQLite/内置RCS回环及WPF/SendInput为实际运行；不是设备或客户协议验收。

`negative-original-owner.log`与`negative-source.json`：原6185d0f6产品未修改，新增测试真实复现原帧预留方法接受不完整长度；`negative-fixture.cs.txt`保留当次准确测试，`negative-build.log`保留构建。此处证明内部预留守卫缺口，不宣称真实SDK已经送入该非法帧。

`intermediate-fixture-capacity-boundary.log`：初版夹具在HTTP到达前断言，后改为明确等到第三观察仍由源持有且首请求已到达回环对端；未弱化断言。`intermediate-compact-error-space.log/png`保留处理中几何预留挤压终态原因的原失败；已改为仅处理中保留，错误/暂停/离线/失效释放空间，用原断言通过。`intermediate-shared-lease-conflict.log`是先前ec908e4a验证遇到设备租约占用；未删除租约、未结束其他进程、未改变本机管理根/权限，释放后原构建重跑49项通过，最终9628b1fb又完整重跑通过。

`intermediate-continuous-foreground.log`保存最终源一次原生按键注入被真实HWND守卫拒绝：激活动作落在上一用例退场窗口。等待前一窗口退出、选择新返回的唯一合成窗口后，同一构建重跑完整35项通过。没有用RaiseEvent替代该逐键输入，也没有削弱前台/焦点/修饰键检查。`native-progress.log`保留失败与重跑顺序；统计只计summary中的成功套件，不累计失败中的局部PASS。

`assembly-hashes.json`记录自有程序集的真实版本和指纹。`publish.log`、`publish-smoke.json`及`publish-environment.json`记录同源Sportmaster Release输出的framework-dependent `dotnet publish --no-build --no-restore -p:BusinessModule=Sportmaster --self-contained false`及真实EXE的`--environment-report`退出0。`hashes.json`覆盖本目录除自身外的证据字节。未上传厂商SDK、字体字节、真实条码/照片、凭据或生产账。

复验先执行`. ./scripts/project-environment.ps1`，运行本目录`verify.ps1 -Workspace <D盘绝对工作树路径>`；也可按`-Section Core`、`-Section Native`分段，同一提交与构建，不能混用来源。原生套件内部串行；环境需要真实前台时只激活选定的合成测试窗口。实际执行中Core与原生使用独立夹具根和报告，汇总前核对同一源和套件唯一性；没有在同一WPF窗口并发操作。

物理MVS/SDK现场、客户RCS、物理PDA/音频、服务器安装和现场长稳NOT_RUN。播放检查证明API调用，不证明听到声音；环境报告启动不证明安装、SDK加载或业务连接。当前报告绑定分支验证源，实际合并以[PR171](https://github.com/yuan2go/importer-pc/pull/171)记录为准；main合并后的产品树核对与构建/启动另记，不能把这些PASS改标为main实跑。
