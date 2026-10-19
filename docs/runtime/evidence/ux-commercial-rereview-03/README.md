# UX独立复审续修03证据

[结果](../../ux-commercial-rereview-03-results.md) · [ARTIFACTS](ARTIFACTS.json) · [实际远端字节回读](remote-publication.json) · [独立双轴复核](independent-review.md)。固定产品6ce1d6d0b109bd6a3d9490fac176122b4670b90b，UI功能源码203c1a8，其后仅版本/兼容声明与版本测试同步；当前业务1.3.2。原f1a655c和两次未发布本地尝试保持原身份。

- final-checks.json / 对应日志：六组定向参考回归、验证工程/原生工程交叉构建，以及Python17+2项。tested-source.json记录测试时源文件与冻结Git完全相同的输入字节；生成程序集的SourceRevision元数据与实际Release分别读取。
- wpf-builds.json / 三个wpf日志：冻结提交的三业务WPF交叉编译。原生Windows实际执行、截图、DPI/键盘/HID/普通账户/负载全部NOT_RUN。
- feedback-red.log为修前真实绑定反馈失败；python-compatibility-first-failure.log和version-compatibility-first-failure.log保留两次本地版本尝试失败；native-verifier-build-first-failure.log保留一次缺using的编译失败。没有删断言、跳过关键验证或将其改为PASS。
- publish-checks.json / media-verification.json：实际五ZIP、签名包和固定源SHA；smoke-results.json及selected/rcs旅程摘要对应同版解包运行。原CombinationRun及事件、完整selected-journey.json在raw ZIP。
- source-parity.json核当前生产/配置/构建/随包指南与冻结产品输入；后续仅状态及证据文件。
- log-custody.json逐文件登记公开衍生副本/原字节SHA。路径替换不修改原ZIP；原事件hash、原记录执行身份应在raw-verification-evidence.zip中复验。该ZIP还保留attempt-203c1a8、attempt-7036488负证据，不与final的通过结果混用。
- archive-delta.json记录同一有Pillow环境的干净归档比较，继承错误不倒写，整体FAIL不改为PASS；没有扩张工具例外。

复跑源入口：固定SDK后build tests/Ipc.Application.Verification，以其dll加源码根及上述六个flag执行；Windows实际控件使用Ipc.Desktop.UiVerification。发布入口仍为package-application.py和build-extension.py，选新隔离输出目录；参考包版本/公共DLL必须精确匹配。Windows/真实SDK/权威协议/现场资格分开。
