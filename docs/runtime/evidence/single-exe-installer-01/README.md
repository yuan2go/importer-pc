# 单 EXE 交付证据

运行源码与内嵌 Windows 介质固定为 `a7de058d5877b8fa797e2292271181b4272ac244`；后续提交只记录文档和证据。继承 PR64 品牌，开工 main 为 `9e5b6ab5be9cf70284acdbaa84b84994ca2377e9`，未合并并行 PR62/63/65。

现场文件为 `D:/releases/cainiao-single-exe-01/菜鸟供包台安装程序.exe`，172,462,152 字节，目录中仅此一个 EXE；SHA-256 为 `2284d542f65299f53b2e6cec10e6ac7e1e52ee59dfc2ff9c487fc4deb27f0c9f`。它是封装构建输出的原字节副本，实际原生测试另将同样字节放入仅含单个 EXE 的新目录运行。精确内嵌 ZIP、747 项归档条目（含 SHA256SUMS）、源码和散列见[交付记录](delivery.json)。外部文件不需要伴随 ZIP、DLL 或运行脚本。

## 已验证

- [启动器 WPF 构建](installer-build.log)：0 警告/错误。[同版介质构建](media-build.log)、[单 EXE 构建](single-exe-build.log)成功；后者原 stdout 中文路径受当前 shell 编码影响，文件身份以 UTF-8 的 delivery.json 和实际 SHA-256 为准。
- [27 项准备机制检查](payload-tests.log)：完整字节、中文路径、单调实际进度、禁止覆盖、损坏、大小/数量边界、取消、路径越界/Windows 别名、重复/链接条目、缺失/清单外文件、单项哈希和混版拒绝。
- [5 项封装器测试](package-tests.log)、[随包指南 2 项](guide-tests.log)通过。首轮恶意路径夹具被 Python ZipInfo 的 Windows 归一化改变，原失败见[初次记录](package-test-first-fixture-failure.log)；修正夹具保留原始字节，并补充拒绝 orig_filename/filename 差异，未降低原断言。
- [实际单 EXE 17 项结果](native-result.json)、[公开逐项日志](native-public.log)：欢迎页、小窗口、取消欢迎、取消实际准备、内嵌字节准备进度、原向导衔接、目录/账户选择、源身份、正常退出和本次临时介质清理通过。通过子进程环境将共享 .NET 根设为不存在目录，仍正常运行；实际核心库从单文件运行缓存加载。
- 实际 HWND 截图已查看：[欢迎](welcome.png)、[较小窗口](welcome-small.png)、[准备进度](preparing.png)。125% DPI 原生截图，不是 HTML 或画稿。安装目录/账户选择页也实际检查，但原图含本机账户/机器默认值，仅保留本地，没有上传或改图冒充模拟图。
- [原生探针源码](native-probe.cs.txt)、[项目参数](native-probe.csproj.txt)可存回 .cs/.csproj 后以 .NET10/WPF/WindowsForms 编译；传入 EXE、新证据目录和上述源码 SHA。初期要求单独 coreclr 模块的探针假设不适用，见[探针修正](probe-corrections.json)；改为观察实际 System.Private.CoreLib 加载路径，仍保持不依赖开发机共享框架的断言，未修改安装器。

公开 native-public.log 仅去掉模块清单及含本机缓存路径的两行，其余逐项结果保持原文；原完整日志本地保留，SHA-256 记于 delivery.json。参考 iFinD EXE 仅查看文件元数据和用户截图，没有运行、拆取或复制其安装资产。

受影响的[工程输出目录用例](generated-output-path-test.log)单独通过。payload-tests.log 中 `a/file ` 的末尾空格是被拒绝的原测试输入，原字节保留；全量 `git diff --check` 因此报告该日志一行末尾空格，源码/文档及其他日志的定向格式检查通过，没有增加资料校验豁免或改写该断言。

## 未通过或未执行的边界

[原部署基线](startup-baseline-environment-block.log)15 项通过后，在原维护数据库副本步骤要求提升权限而被拒绝；没有改维护权限守卫。[资料工具](archive-tools-environment-block.log)启用 Pillow 后 6 项通过、1 项因 WinError1314（不能创建符号链接）环境错误；受影响的精确新增工程输出路径用例单独通过。首次未加载 Pillow 的工具输出本地保留。它们均未被标记为整套 PASS，也未删除或跳过断言。

本次只检查安装准备及原向导的前两步，没有点击授权安装，没有创建机级政策或操作设备。真实 UAC 安装、并排升级/卸载、重启、额外 DPI、真实 SDK/设备/RCS 和现场验收均 NOT_RUN；不授予 Production 资格。没有提供签名证书，实际 Authenticode 为 NotSigned，没有生成自签“可信”证书。原 D/RV 和旧 ZIP/旧 PASS 保持原身份。
