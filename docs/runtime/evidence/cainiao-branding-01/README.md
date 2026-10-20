# 品牌替换证据

最初品牌源码为 `afc52a469e2f084cbfaa2def4f7d66a34444e491`，起点 main `9e5b6ab5be9cf70284acdbaa84b84994ca2377e9`。实际 EXE 冒烟发现文件说明仍显示程序集内部名，随后修正 `AssemblyTitle` 并另冻运行源码；最终身份见下文。

- [Sportmaster 开发构建](development-build.log)：0 警告、0 错误。
- [Supply 隔离构建](supply-build.log)：0 警告、0 错误。
- [开发期原生 WPF 检查](development-wpf.log)：33 项 PASS，使用显式合成身份/端口，不安装政策或操作设备。
- [标准客户区](workstation-1366.png)、[800×420 客户区](workstation-minimum.png)：当前 WPF/Dispatcher 的客户端渲染，125% DPI；本次上传标志与五字名称均完整显示。不是原 HTML 的重新导出，也不是现场验收。
- [首次打包失败原记录](package-first-file-lock-failure.log)：构建已完成，清理其临时源码时与并行资料校验发生 WinError32 文件占用；该部分介质不作为成功发行。后续改用新目录并按顺序检查，不修改打包器掩盖失败。
- [原资料校验编码失败](archive-initial-encoding-failure.json)：本机默认 GBK 读取 UTF-8 文本失败；后续 UTF-8 全量校验因上述文件占用中断，未形成有效资料结论。最终只对本次交付的 manifest 项和 Markdown 引用进行定向核验。
- [首次 EXE 元数据失败](exe-metadata-first-failure.json)：原断言发现 ProductName 正确、FileDescription 为 `Ipc.Desktop`；补上 `AssemblyTitle` 后重新打包，未删除或降低断言。
- [实际 EXE 冒烟脚本](exe-smoke.ps1)：检查产品名称、文件说明、从 EXE 提取的图标像素和原生安装向导标题，正常关闭且不执行安装或设备操作。

最终运行源码与同版夹具为 `967ebc8daf377a99561914d57bd807017dcf2d14`。后续提交仅补充本次文档、证据和 manifest，不改变被冻结的运行源码。

- [最终介质](package-result.json)、[最终打包日志](package-final.log)：`D:/releases/cainiao-branding-03.zip`，746 项库存哈希、ZIP CRC 通过；ZIP SHA-256 为 `c6a31dcd1ff90979cfc767748c6bbb6ea12f5c9b8f90dcd0b878844474503579`。
- [同版原生结果](frozen-wpf-result.json)、[38 项检查日志](frozen-wpf.log)：PASS，SourceCommit 与 FixtureSourceCommit 完全一致，验证前后介质库存不变。
- 最终渲染：[标准工作台](final-workstation-1366.png)、[800×420 工作台](final-workstation-minimum.png)、[首次配置](final-first-configuration.png)。原生 125% DPI 合成截图，已查看最终工作台的标志、名称及最小布局。
- [实际 EXE 结果](exe-smoke-result.json)：ProductName/FileDescription 均为“菜鸟供包台”，图标像素匹配，精确 HWND 标题为“安装与部署向导 · 菜鸟供包台”，退出码 0。[开发期探针失效与修正](probe-development-notes.json)保留了误读启动所有者标题及附着进程退出码不可用的原因；最终新启动进程保留句柄，仍要求原精确标题、图标和正常退出。

同版验证复跑：`scripts/verify-commercial-wpf.ps1 -Media D:/releases/cainiao-branding-03 -Output <全新证据目录> -Dotnet D:/tools/dotnet/dotnet.exe -VisualProductization`。EXE 探针记录本机精确介质路径；移动介质时需对应调整该路径。

安装/UAC、真实 SDK/设备/RCS、额外 DPI 和现场资格不由此次品牌检查授予。Supply 此次只有构建，其他业务完整运行未验。历史 D/RV 不变。全量历史资料未形成有效新结论，仅定向校验本次 manifest 和新增 Markdown 引用。
