# 代码可靠性02原始证据

本目录包含开发反证及冻结验证，二者不能混用。开发日志来自main 1dfb676a + docs提交c23997e及逐步未提交改动；不是每个阶段均保留了程序集hash，不能声称可由c23997e单独复建这些开发二进制。原始失败、初次夹具编译/选择器错误保留；final目录另以固定SourceCommit、程序集hash、命令和退出码建立可重现身份。

startup-before、log-time-before、health-before、storage-log-after及native-before-corrected保留实际缺陷反证。native-before为夹具未展开按钮的错误，不计产品缺陷；baseline-build/core-build等保留编译修正。core-after为首个索引实现每行重复权限检查导致性能变差的原结果，后续改为撤权token加100ms权限复核。原文、脱敏和提交前授权检查未降低。

crashes/regression-h02-crashes是外部父进程强杀后的核对；测试来源/服务端记录为合成，真实Application/SQLite不替换。data/security、账户、数据库、原始客户资产和二进制均不上传。仅复制允许的合成输入、模拟服务端记录、结果和日志。

Windows原生日志来自真实Dispatcher/HWND，合成数据和接口；不表示最终IPC、真实SDK/RCS、物理断电或24h/72h通过。WPF dispatcher_max包含窗口操作/首次布局，不等于纯格式化耗时。所有本机缓存和隔离数据在D盘。


`final/CORE-RESULT.json`与首个`final/RELEASE.json`固定51ad850；`native-management`为80dc783独立等待夹具配51ad850产品；`crash-disk`为c161b02（src/config等同51ad850）的磁盘逐笔对账补测。原生管理失败截图、并行资源租约拒绝、最终布局收口各保留实际身份。最终23af651介质另存`final/release-23af651`，不覆盖先前候选。

最终23af651同源portable/win-x64及三组冻结WPF均PASS；布局收口只涉及原文提示，不改变核心源码，因此核心51ad850全定向回归和c161b02磁盘逐笔强杀补测保持各自身份。EXE只读启动与原生DLL交互分开计证，正式安装未执行。

CI首轮40115179715的Windows真实身份拒绝与修复夹具本机复核保留于ci-40115179715；产品src/config仍与23af651相同。完整远端结论按PR104最终head检查，首轮失败不改写。
