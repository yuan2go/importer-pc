# 离线 Mono8 回放格式

`frames/` 每个 `.mono8` 是无文件头的行优先 8 位灰度数据，准确长度为 `Stride × Height`；`Stride >= Width`，末尾不附换行。这里的四份 8×8 数据只是可编程假解码器的灰度标记，不是面单或真实识码图。启动自检另由 `DecoderSelfTest.CreateFrame()` 生成 Code39 `123` 合成图；格式常量对照 [ZXing Code39 表](https://github.com/zxing/zxing/blob/master/core/src/main/java/com/google/zxing/oned/Code39Reader.java)，没有引入该库。

人工真值为 UTF-8 JSON 数组，字段与 `truth.json` 一致。`File` 仅允许当前目录下的文件名；不接受子路径、重复文件、链接或尺寸不符。`Text`/`Symbology` 同时为 null 表示人工确认无码；有码则精确填写文本和码制，前导零必须保留。每行代表一个独立测试帧。目录及真值须来自人工标注，不得用当前解码器输出反填真值。

`rules.json` 是版本化筛选规则；不代表生产默认。这个合成集预期 4 帧：正确 2、无码 1、错读 1，其中 Code39 的错读率 1/3，QR 正确 1/1。报告只输出聚合指标、规则身份及码制，不输出原始条码或逐帧文件名。

```powershell
. ./scripts/project-environment.ps1
dotnet build tools/Ipc.Mvs.Replay -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tools/Ipc.Mvs.Replay/bin/Release/net10.0/Ipc.Mvs.Replay.dll --frames tests/fixtures/mvs-replay/frames --truth tests/fixtures/mvs-replay/truth.json --rules tests/fixtures/mvs-replay/rules.json --decoder synthetic --output D:/new-replay-report.json
```

替换为 `--decoder <受信离线解码worker的绝对路径>`；各启动参数使用重复的 `--decoder-arg`。worker 在完成自身依赖/资格检查后调用 `ProcessFrameDecoder.RunWorkerAsync`，标准输入/输出专供版本 1 的长度前缀协议，不能混入日志。主进程每帧限时 2 秒，超时终止并等待本 worker 退出；异常样本计入 Errors，然后仅在离线工具中重新创建 decoder。无法证明终止则整个回放失败，不生成成功报告。

指标均为总帧数的比例（0～1）：ReadRate=(正确+错读)/总数，CorrectReadRate、NoReadRate、WrongReadRate、ErrorRate 分列。空真值却读到码属于错读。歧义/无匹配计无码；异常计 Error。分码制按人工真值分组，人工无码组为 `<none>`。P50/P95/P99 为每次解码调用的单调时钟耗时，nearest-rank，包含失败调用等待取消完成的时间；没有 CI 性能基准。真实样本、真值和报告留在获授权 D 盘仓库外，正确率不能由本合成集推断。
