# 主条码最新观察19 · 本机证据

产品源`1740ab47eb36ebefbac35db4a488ddf89f66d60b`，基线main`039ccdee`。3项Release构建0警告0错误，16套件519条PASS断言（Application299、原生WPF220）。`summary.json`记录各套命令、退出码、源树身份及原始本机目录；`assembly-hashes.json`为本机自有程序集指纹。原始日志保留字节，失败不计入最终PASS。

- `negative-main-barcode.log`：原main产品＋新专项首断言实际失败，读到反馈未更新首次主框；源文件哈希见`negative-source-hashes.json`。
- `negative-main-task-fields.log`：独立未修改main实际复现800×420格口39 DIP；`intermediate-task-fields-failure.log`保留修复条码后发现同一问题的原失败。
- `native-camera-barcode-current.log`：21条新增原生检查，真实ZXing/Application/SQLite/内置RCS与合成SDK帧/回环HTTP，不是相机SDK实机验收。
- `publish-smoke.json`/`publish.log`：同源framework-dependent发布及实际EXE环境报告入口启动；不证明安装或完整业务启动。

截图为合成数据、125%DPI本机渲染；多码时主框不展示原候选列表，候选留在原观察证据。常规与800×420可核对新码、提示、四个任务字段和格口，物理尺寸、现场触摸及声音仍待验。

复验：检出产品源后先执行项目环境脚本，再运行本目录`verify.ps1 -Workspace <D盘绝对路径>`。不要并行运行原生套件；PDA用例保留真实前台/焦点/修饰键守卫。真实MVS、客户RCS、物理PDA/声卡、服务器安装与长稳NOT_RUN。后续纯文档/证据提交不能把这些结果改标为另一产品源的验证。
