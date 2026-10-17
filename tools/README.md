# 资料校验

本目录只检查资料，不构建或运行供包业务。使用 Python 3.10+；有图片的受限本地包还需 Pillow。没有图片时不需要该库。工具不联网、不解包、不改文档；错误返回非零，结果写到标准输出。

在仓库根目录执行：

```sh
python3 tools/verify_archive.py --root . --scope public
python3 -m unittest discover -s tools -p 'test_*.py' -v
```

受限本地包：

```sh
python3 tools/verify_archive.py --root "$ARCHIVE_ROOT" --scope local --registry docs/baseline/reference-policy.json
python3 tools/verify_archive.py --zip "$INPUT_ZIP" --sha256 8685ba48f19aa087541598b0efc4e56a04f017e56251197bd53b9318d2c3302a
```

路径参数由使用者设置。校验模式不能生成 manifest 或自动修复。manifest 中仅排除自身及其校验文件，校验文件记录 manifest 的 SHA-256；远端复核另外逐文件比较这两个文件。所有其余交付文件必须列入清单，禁止任意扩张排除范围。RUNTIME-03仅新增精确的生成物规则：十个已登记C#项目（含13新增Contracts/Assembly/Inspection）自身的bin/obj，以及根目录.artifacts，不属于归档输入；docs下同名目录、其他工程目录和所有源码仍逐文件检查。规则由对应失败用例保护。`.git` 和 Python 字节码缓存属于运行环境，不是交付资料。

检查包括逐文件大小/哈希、JSON、Markdown 相对链接与标题锚点、HTML 属性与 CSS 资源、图片完整解码、PPTX CRC/XML/内部关系/外部关系/嵌入字体、画板尺寸及 SVG/资产引用、敏感标记。外链只记录指纹，不自动访问。行内代码路径、历史 JSON/脚本中的运行环境路径须由证据定位表另行登记，不把文本中的每个文件名自动当成当前链接。

`reference-policy.json` 的 `exceptions` 必须精确记录 `source`、`target`、`reason`、`status`；多余或未使用的豁免也报错。`locators` 可记录 `path`、`sha256`、`line_end`，核对历史证据身份和行号范围。已登记缺失会单列输出；整体 PASS 只表示未发现未登记的交付错误，不能解释为全部原始资产已取得、获准公开或业务通过。

PPTX 必需部件、包内 SVG/CSS/位图也检查。原件若将 SVG 字节命名为位图，只能通过 `package_exceptions` 逐项登记 `container`、`part`、`sha256`、`reason` 及 `status=HISTORICAL_FORMAT_MISMATCH`；字节变化或多余登记仍报错。登记不豁免 SVG 内的资源错误，不修改原件，不把这些部件计为可解码位图。

敏感标记扫描是补充检查，不能证明所有权、公开许可或完全没有秘密。准备公开材料仍须逐文件审查，包含嵌入内容、来源元数据和实际暂存差异。
