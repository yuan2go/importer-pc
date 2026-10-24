# TYPOGRAPHY-01 · 受控原生对照与续修

2026-10-24。最新 main `c5ac51d`，开放前置 PR97 `a43ecb8` 已在隔离字体分支衔接；未合并任何远端 PR。先更新[诊断与验收条件](../../../../design/typography-comparison-01.md)，再修改生产代码。此目录独立于首轮证据；旧 PASS、原图和哈希继续保留其源码身份。

## 选择—依据—验证—限制

| 选择 | 依据 | 实际验证 | 限制 |
|---|---|---|---|
| 保留 Noto Sans CJK SC 2.004 Regular/Medium，中文回退 Microsoft YaHei UI | 官方完整静态字库、可固定离线字节和真实400/500。两个OTF共32,960,996字节；接受其包体成本 | 原生字体URI/实际字重、内嵌哈希、OFL/NOTICE、发布DLL探针；源码中文超集加混排样本782字符无缺字 | 不证明任意外部日志字符全覆盖；🚀专门验证主字库缺字时的系统回退 |
| 正文16/辅助14、标题≤20；条码28/24 Regular、有效短格口64/44 Medium | 基线过多粗体；A已减弱正常状态及条码强调，说明收益并非全来自换字体 | 相同文本、客户区和实际125%DPI三组图；A/B相同语义尺度和布局。条码均为Consolas，字体替换不改变其宽度 | 没有操作者误读率、耗时或偏好数据；不宣称效率提升 |
| 选择B，淘汰A为默认方案；A仍可作为回退观察 | A的Medium请求实际映射微软雅黑400；B实际加载Noto500，组标题/有效格口更稳定地分级，中文家族统一 | 800×420短格口A高度56 DIP、B64；14号下一步A17.6、B20。B更高，须由局部布局承担 | 不是“Noto天生更易读”的结论；视觉协调为工程判断，目标工控机及现场持续阅读未验 |
| 主窗口、菜单显式设置字体；紧凑条码同行、原因最多两行；记录标题与查询操作同行 | 真实属性发现派生Window默认仍是微软雅黑，紧凑菜单也覆盖家族；根字体修正后暴露两个真实空间失败 | 两宿主缩放后每个实际普通中文GlyphRun检查；最新97任务链、完整结果行等原断言保留并通过 | 不以独立24号字体小样替代生产页面验证；后续系统/字体版本仍需同类复核 |

Codex参考：本次原生工具返回的画面无法可靠绑定Codex主界面（实际为浏览器页面），故排除，不纳入公共图、不推定字体名称。采用用户要求的清晰克制方向及项目 Operate 原则，未把浏览器示意作为原生证据。

## 三组对照

- **基线**：PR97 `a43ecb8` 原生产源文件；仅加入同一比较夹具及入口。原 `src/` 与Git逐文件核对，文本按仓库CRLF规范归一，二进制原字节；构建显式设置SourceRevisionId。
- **A / system**：最终语义尺度/布局，只在测试夹具替换family；`TestOverrideFrom`保留覆盖前来源。Consolas仍用于标识。没有生产字体开关。
- **B / noto**：最终生产字体，无测试属性覆盖。

三组各10个场景：两宿主、1366×768/1092×614/800×420正常、800×420 Unknown与长码/长格口。所有比较使用同一冻结合成输入，`FixtureSha256`完全一致；实际DIP因125%像素取整可为1365.6或613.6，JSON记录实值。截图只截真实PageHost，排除随时间变化的窗口时钟；并非另造样板控件。A→B控制尺度/布局比较字体；基线→A包含尺度和必要布局的整体变化，不能分别估算它们的因果贡献。

| 同场景 | 基线 | A 系统字体 | B Noto |
|---|---|---|---|
| 默认位，800×420 | [图](comparison/baseline/baseline-supply-station-normal-800.png) | [图](comparison/system/system-supply-station-normal-800.png) | [图](comparison/noto/noto-supply-station-normal-800.png) |
| Unknown，800×420 | [图](comparison/baseline/baseline-supply-station-unknown-800.png) | [图](comparison/system/system-supply-station-unknown-800.png) | [图](comparison/noto/noto-supply-station-unknown-800.png) |
| 经典宿主，1366×768 | [图](comparison/baseline/baseline-classic-normal-1366.png) | [图](comparison/system/system-classic-normal-1366.png) | [图](comparison/noto/noto-classic-normal-1366.png) |

各组COMPARISON.json包含控件真实文本、属性Local/Style/Inherited来源、Binding标记、字形Unicode/索引、实际字体URI/字重、边界、换行/限高/滚动数据、DPI和程序集哈希。夹具只冻结呈现输入，不作为业务状态正确性的独立证明；业务证明另用97真实Application与loopback原回包链。

## 失败、修复及验证边界

1. `pre-root-fix/COMPARISON.json`证实两宿主普通中文仍用系统字体。`4818f4e`明确调用UiTypography.Apply。
2. `first-noto-failure`在紧凑菜单发现残留微软雅黑；菜单及ContextMenu补字体入口。原字号/业务状态没有被断言代替。
3. `chain-viewport-failure`：原Unknown责任与新未准入输入共存，条码控件略超出800×420位内视口；`648c92c`把紧凑条码标签和值同行，原因限高从32改48，保留下一步和原责任。修复图见[native](native/task-hierarchy/chain-blocked-after-unknown-800.png)。
4. `history-viewport-failure`：字体真实生效后结果区62.4 DIP，只见部分行；`e61e9f3`将标题/操作同行、筛选区60改72，保留完整表头/行及局部滚动。修复图见[记录](native/internal-pages/history-800.png)。
5. `baseline-refresh-failure`为夹具捕获早于冻结快照收敛，修为有界等待同一个快照对象，仍逐字断言独立条码预期。`portable-shim-failure`为可移植窗口替身缺新增Apply入口；`6ff61fa`只补替身，不能验证字体，原生验证仍是权威。托管首跑[40077954374](https://github.com/yuan2go/importer-pc/actions/runs/40077954374)在5cd156a也报告同一可移植构建失败，见verification/CI-FIRST.json；未删除或跳过Linux门禁。

8组原生检查合计697项PASS（226层级/真实任务链、9参数归属、25内部页、126视觉、23参数V3、28管理、88安装首配、172启动）；三组比较32/32/52项、实际发布DLL字体15项另列。受影响UI/可移植/安装器构建0警告0错误，win-x64本地发布、离线OFL/NOTICE核验和隔离environment-report成功；交付文档测试2项通过。具体来源分别见[RESULT](RESULT.json)和日志，不把后续文档/替身提交倒写为旧二进制身份。参数归属组来自648c92c；其余最终原生夹具来自e61e9f3；首配/启动组调用的发布入口为648c92c，最终e61e9f3发布DLL另经字体与隔离启动核验。e61只调整作业记录布局和身份宽度，未改首配/启动实现。

所有截图与原生证据为Windows 11 23H2、实际125%缩放及合成数据。Windows11实际100%/150%、目标IPC/代表性操作员交叉试验、持续阅读、物理扫码/设备/SDK/RCS、真实安装/UAC/现场验收均 **NOT_RUN**。托管Windows Server100%由当前[PR98 CI](https://github.com/yuan2go/importer-pc/pull/98/checks)独立记录，不能替代这些边界。未发布正式介质、安装字体或修改生产数据。

## 复现

在D盘Windows工作树执行；只生成本地隔离合成数据，不安装应用：

```powershell
. ./scripts/project-environment.ps1
./scripts/compare-typography.ps1
# 输出独立目录及三组证据指针；private-fixture.json含合成会话，只留本地。
$env:IPC_TYPOGRAPHY_CONTROLS='1'
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --internal-pages
# 字形源覆盖检查使用开发用fontTools；不进入运行/发布依赖。
python scripts/inspect_typography.py .artifacts/typography-coverage.json
```

完整其他模式、发布命令见[运行记录](../../../typography-01.md)。覆盖脚本需要开发环境fontTools，本机使用D盘开发缓存；不能把脚本未运行当成覆盖PASS。新增测试分别解决真实字体入口、缩放菜单回退、固定输入对照、字符缺失和可视几何风险，没有新增字号常量镜像矩阵。
