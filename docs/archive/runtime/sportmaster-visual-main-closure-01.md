# Sportmaster 视觉产品化 main 续核结果

起点 main `e0beb5d32c6a24a7799ba698731566bca6a6833a`；整套首配、双位工作台、设置维护和帮助支持已由 PR58 合入，PR57 后继保留了该实现。本轮先提交[设计增量](../design/sportmaster-visual-main-closure-01.md) `86b9a06`，继承整套实现，再修复原生异常路径并补齐持续门禁。没有重建 UI、运行实例、配置账或部署权威。[证据与复核](evidence/sportmaster-visual-01/main-closure-01/README.md)。

## 四类界面的最终交付

|范围|实现与本轮核验|
|---|---|
|首配与失败修复|四步与独立恢复页、原目录显式重读、未完成草稿、未保存输入保护及检查失效继续有效。保存/启动沿原唯一配置账和完整守卫；本轮冻结首配断言通过。|
|工作台|标准/紧凑维持左右固定结构、条码/有效格口/下一步、固定动作和按需诊断。实际异步丢回执后只核对原请求；焦点、选择、展开、滚动、草稿和辅助窗共享实例通过。|
|设置维护|维护认证/保存与低频草稿重读分层；切页保留相同编辑器与独立草稿，CAS/暂停/在途保护保留。没有放宽权限或直接控制设备。|
|记录与帮助支持|日期、时钟和明确 UTC 偏移共用原生控件；完整原件与固定查询/导出保留。帮助可搜索、摘要本地预览。修复非法日期被日历关闭恢复成旧条件的错误。|

`DateTimeInput` 的原生复现：输入 `not-a-date` 时 Timestamp 为无效，但 DatePicker 保留原 SelectedDate；只打开再关闭日历就恢复为 `2026-10-19T08:09:10+05:30`。现清除陈旧原生选择，保留唯一 Timestamp 的无效值及明确的行内修复提示；打开/关闭日历、修改时钟或偏移均不能默默恢复旧日期。明确选择日期（包括原日期）、键入有效日期或清除才恢复。历史绑定和支持预览均有回归断言，支持摘要不能据错误日期导出。

仅新增共享控件的视图同步处理，没有新增业务日期解析或第二套过滤权威。Sportmaster 不增 PLC/图片；DirectPlc 完整 UI 继续延期。原请求、报文、目标、合同和 Unknown 不自动重发保持。指定 Windows 账户的非提权运行与按组合可选登录不变。

## 必要验证与精确身份

最终本地运行源码及原生夹具冻结为 `1744f398720b405448d975ab9d440cd24feeef74`，业务 1.4.0 / Application.Contracts 1.10.0 / DB8。后继仅改 CI 和交付文档/证据/manifest，冻结运行输入及测试仍须逐字节一致。

本地完整 win-x64：`D:/sm-visual-closure-media-01.zip`，104296788 字节，SHA256 `2642299524EFB2B51E9F3223C7EB1D97A7D3D9800CCA7D0E659E9EDD5B1FF40F`；库存 747 项，库存 SHA256 `5498BA2FC8F95AED3C1303DB74E0DC289970D66600005167513F14F3119D69F7`。完整原生入口、逐文件/ZIP 成员与 CRC、app/desktop 自有共享 DLL 和来源检查通过；[MEDIA.json](evidence/sportmaster-visual-01/main-closure-01/MEDIA.json)列实际引用路径和 DLL hash。本地验证候选未上传 Release，不覆盖历史介质。

|检查|实际结果|
|---|---|
|开发构建|Sportmaster UI 验证工程，0 警告/0 错误；开发态原生定向检查通过，不代替冻结结果|
|后继发行整合风险|统一发行器 14 项通过（本轮终端实际执行）；随包链接 2 项、商业门禁 5 项通过|
|同版首配/商业辅助页|24 项 PASS；读取不创建业务库、草稿/目录/检查守卫、帮助搜索及共享运行实例|
|同版完整视觉旅程|30 项 PASS；包含新增 6 项日期错误、关闭日历、选择/键入恢复、支持拒绝及显式清除|
|介质和源码|统一发行器原验证逻辑核完整包/库存/ZIP/来源/共享 DLL；原生验证前后库存不变；本轮范围的 Git/manifest/证据复核|
|Windows PR 门禁|现有同源发行及工程旅程后增加完整视觉旅程；复用同一个 win-x64，不生成第二候选；原始日志/结果/PNG 在失败时也保留。实际 CI 结果按 PR 精确 head 单独记录，不能从本地 PASS 推定。|

本轮没有修改业务、驱动或存储内核，未重跑无关全量测试。CI 自有工程门禁的执行与结果单独保管。证据整理助手首次错误要求不同目标的第三方 SQLite provider 字节也相同而失败；实际 deps.json 分别声明 net6.0 与 net6.0-windows7.0 资产，原 dfc2ab6 亦如此。已按现有夹具实际引用路径核验，全部自有共享 DLL、库存及 ZIP 的断言保留。该失败和修正见证据，没有删改产品断言。

首次 PR59 CI [39358622729](https://github.com/yuan2go/importer-pc/actions/runs/39358622729)在原生检查前 FAIL：PR57 继承的显式 `--source` 要求 main 祖先，而 PR 临时合并提交是 main 后继。保留[原始错误片段及身份](evidence/sportmaster-visual-01/main-closure-01/ci-first-failure.json)，该次原生检查为 NOT_RUN。现仅修正 CI 调用：使用发行器既有缺省模式构建当前检出的精确 HEAD，并额外断言 RELEASE.SourceCommit 等于 github.sha；历史来源发行的祖先限制、完整包/库存及业务断言保持。最终当前 head 的 CI 结果以 PR 检查为准，不能以本地 PASS 覆盖这次失败。

## 运行与复核

完整介质解压后运行 `Setup.cmd`，由原受保护安装机制登记账户/目录/权限。指定账户非提权从开始菜单或 `desktop/Ipc.Desktop.exe` 启动；Sportmaster 缺省新版 supply-station，显式 `--ui classic` 兼容。`Maintenance.cmd` 为独立启动修复；系统“已安装应用”或 `desktop/Ipc.Desktop.exe --product-maintenance` 为安装维护。真实未满足的 Production 资格继续拒绝。

```powershell
./scripts/verify-commercial-wpf.ps1 -Media D:/sm-visual-closure-media-01 -Output <全新目录一> -Dotnet D:/tools/dotnet/dotnet.exe
./scripts/verify-commercial-wpf.ps1 -Media D:/sm-visual-closure-media-01 -Output <全新目录二> -Dotnet D:/tools/dotnet/dotnet.exe -VisualProductization
python docs/runtime/evidence/sportmaster-visual-01/main-closure-01/verify.py --media D:/sm-visual-closure-media-01
```

验证夹具显式合成身份/端口，只读取冻结客户端 DLL；不配置机器政策，不操作真实设备。原生日志与布局图为实际 WPF/Dispatcher/窗口，本轮没有把程序化属性输入写成人工点击，也没有把 RenderTargetBitmap 图冒充现场屏幕照片。旧 PR58 人工窗口检查保持其 dfc2ab6 身份。

## 未验与资格边界

NOT_RUN：本机 Windows 22631 x64 / 125% DPI 以外的目标 Windows26100+安装/UAC/第二账户/重启/完整升级修复卸载迁移，100/150%及混合多屏 DPI，物理触控/手套/HID/读屏/声音/视距和长时负载。真实 SDK/WCS/设备/实体/批准合同、可信签名和现场放行缺条件仍 BLOCKED / NOT_APPROVED。

原 D/RV、原 HTML/CSS/JS/交接、历史失败及历史冻结介质不倒写。旧全树归档检查的 265 项 PPT 媒体类型和 2 项原元数据问题保持原 FAIL，本轮不重复无关全量检查或宣称全树 PASS。工程可执行范围完成不构成现场投产资格。
