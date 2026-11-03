# PDA Backspace 原生输入完成确认

2026-11-01。基线main `3ef78950a40da0aebd8656b4ebd98287cd060633`。本轮问题是测试时序缺陷：原断言早于该次Backspace到达WPF。诊断时产品源码不变；补丁只加合成输入事件追踪和失败后观察，原即时断言仍失败，没有把迟到的正确结果改记为即时成功。

## 实测顺序

| 相对毫秒 | 事件 | 文本 / 光标 |
|---|---|---|
| 1836.759 | 注入Backspace前 | `0001293`，6/0 |
| 1838.716 | 原Flush(ApplicationIdle)返回，原即时断言读取 | `0001293`，6/0；Backspace尚未进入WPF |
| 1841.681 | WPF PreviewKeyDown:Back | `0001293`，6/0 |
| 1843.971 | WPF TextChanged | `000123`，5/0 |
| 1857.495 | WPF KeyUp:Back | `000123`，5/0 |
| 2100.109 | 仅诊断排空后的观察 | `000123`，5/0；原即时断言保持FAIL |

这说明该次删除由WPF正确执行；SendInput返回及单次Dispatcher空闲不能用作Windows输入已处理的证据。日志还显示原Left/9之后的焦点切换可发生在这些按键处理前。因此修复覆盖所有原生注入批次，而非给Backspace断言添加固定睡眠或按期望字符串轮询。

## 修复范围

`BarcodeNativeKeyboard.SendAsync`在SendInput之前订阅目标TextBox的实际WPF KeyUp，按虚拟键顺序确认每一键后再继续，并执行一次Dispatcher空闲排空当前事件后续。包含handled/read-only事件；超时、错误按键、前台/焦点/修饰键不满足继续明确失败，finally注销监听。测试只在注入已经处理后移动焦点、刷新或断言。

保留原19项值/状态/命令/HTTP/冻结字节断言，新增16轮不同光标位置的Home/Right/插入/Backspace，交替焦点切换及快照刷新；输入保持真实Windows SendInput，不整体赋值Text，不自动修正光标以满足断言。产品 `src/` 无改动，Application/RCS准入及Unknown/原请求责任不变。

## 验证状态与边界

最终代码提交 `85d15e231c142648313748544eaf4cd64fba4de3`。WPF构建0警告/错误，连续8轮真实SendInput回归均通过，每轮35项，共280项断言；既有 `--workstation-barcode-ready` 回归59项通过。最终8轮没有环境拒绝或业务/编辑失败。

修复原型首轮35项亦通过；随后一次重复在第2轮编辑后被前台条件守卫拒绝，恰逢用户确认/清理交互，原失败保留并单独标识，不算入连续通过。精确源码、各轮计数、原始日志/补丁长度和哈希见[summary.json](evidence/sm-pda-input-ack-01/summary.json)。

原发布失败、原断言仍失败的诊断日志、仅追踪补丁及构建记录见[证据目录](evidence/sm-pda-input-ack-01/)。旧安装包和历史PASS不倒写。首配、安装、真实SDK/客户RCS、目标机与现场分别验收；本轮软件合成证据不扩张现场资格。CI停用，D/RV不变。
