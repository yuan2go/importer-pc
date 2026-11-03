# IE01现场打印稿

这是当前候选的文档生成工具与本机排版核查，不是签名发布或现场资格。输入为[作业卡](../operator/quick-guide.md)、[部署卡](../operator/deployment-card.md)、[实施手册](../operator/implementation-guide.md)和[故障目录](../operator/fault-codes.md)，不使用客户截图、生产记录或远端内容。文案仍按真实实现说明未完成的功能，不用打印稿补造入口。

## 生成

工具只用于开发文档，不增加产品运行依赖。当前核查环境为Windows x64/CPython3.12、ReportLab4.4.9、fontTools4.68.0、pypdf6.10.0；Pillow12.3.0及charset-normalizer3.5.2一并固定。版本与PyPI发行字节的哈希见[依赖锁](../../scripts/operator-print.requirements.txt)，只允许wheel、启用require-hashes。其他Python/平台尚未核查。

```powershell
. ./scripts/project-environment.ps1
py -3.12 -m venv .artifacts/print-env
& .artifacts/print-env/Scripts/python.exe -m pip install --require-hashes --only-binary=:all: -r scripts/operator-print.requirements.txt
& .artifacts/print-env/Scripts/python.exe scripts/build-operator-print.py --output .artifacts/operator-print/output/pdf
```

输出目录必须是仓库`.artifacts`中的新目录，拒绝已有目录和重解析路径，不覆盖其他文件。失败保留该次生成物用于检查；更正后用另一个新目录重做。正式发布前必须从冻结的干净源码生成，读回清单和最终PDF；当前工具始终标记候选，不授予正式发布资格。

输出为两个A4单页及一个不超过20页的手册，附OFL和`print-manifest.json`。页脚从实际SM项目读取版本，绑定源提交及工作区修改标志；清单包含全部文案、工具、版本项目、字体和许可原件的SHA256，以及每个PDF的页数、大小、SHA256、正文片段核对数及字体嵌入结果。固定PDF及字体元数据时间用于复现，不表示实际制作或发布时刻。两次生成在相同源码、Git状态和工具版本下应得到相同字节。

使用获准随包的Noto Sans CJK SC Regular/Medium。ReportLab不直接嵌入原CFF轮廓，因此工具在内存中提取所需字形并转为二次曲线，保留字宽和行度量，轮廓误差上限为一个字体单位；临时子集另命名，不输出第二份可安装字体。两种子集都嵌入PDF，不依赖阅读器补字体。正文可搜索、复制；源文本先转义，不执行HTML/脚本或加载远端图片。实现依据为[ReportLab字体](https://docs.reportlab.com/reportlab/userguide/ch3_fonts/)、[中文换行](https://docs.reportlab.com/reportlab/userguide/ch6_paragraphs/)与[fontTools字形接口](https://fonttools.readthedocs.io/en/stable/ttLib/ttFont.html)。这不构成PDF/UA认证。

## 已执行的核查

2026-11-03本机：页数为作业卡1页、部署卡1页、实施手册6页；手册包含完整5个CP编号及安装/维护结果表。逐段读回核对分别为8、10、125段，两种字体子集全部嵌入。连续两次固定工具生成的三个PDF及清单字节一致；中间发现字体和PDF时间戳仍变化，已分别固定生成器元数据后重新核对，没有降低正文或页数检查。

Poppler100DPI渲染全部8页，逐页检查标题、正文、长路径/命令、跨页表头、字号、页脚、边距及示意图。修正了中文逗号等行首问题和无必要分页，没有裁切、文字重叠或空白页；没有把旧图当作原生WPF截图。最终本地候选位于`.artifacts/industrial-engineering-01/print-06/output/pdf`；其渲染像素与已逐页检查的print-04完全相同，字体/文档元数据修正另以完整PDF摘要核对。该候选基于a9f65cc9加本切片工作区，清单明确`sourceWorktreeDirty=true`，不能作为正式冻结介质。

首次生成曾因错误要求作业卡必须出现字母D而被拒；该卡无部署路径要求，改为逐段核对真实源文案，不插入无关文字让检查通过。试排13页和7页的原件均保留在本地，不覆盖失败/中间证据。

CI新增独立`operator-print`任务，以固定Action提交、Python3.12和哈希锁依赖生成、检查并保存三份PDF/清单/OFL；不上传临时字体、SDK或原始采集物。CI自动检查正文、嵌入、版本和页数，**不代替逐页视觉检查**。新任务的实际CI结果在实施记录中另记；本页不提前写通过。

后续实际结果：[CI41581640147](https://github.com/yuan2go/importer-pc/actions/runs/41581640147)在源3f1ac766全通过，独立PDF任务实际生成1/1/6页并保存附件。该干净源还在本机重新生成于`.artifacts/industrial-engineering-01/print-3f1ac766/output/pdf`，manifest明确未修改工作区，全部8页再次渲染检查通过；各PDF实际摘要见[实施记录](industrial-engineering-01.md)。这是本机排版与CI生成两层证据，仍未完成下述实体与现场验收。

仍未执行：实体打印、现场阅读距离/照明、新人30分钟旅程、同源最终安装包附带与离线打开、正式版本/签名绑定。当前仅是候选打印稿，不宣布整套IE01或历史RV已验收。
