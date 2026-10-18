# WP-IPC-ACQUISITION-LIFECYCLE-P2

2026-10-18用户授权独立复核、修复及main PR。起点为已合并PR #22的main `2126ac2c06a0c352d96afbbf13ec1a719c352738`，无开放PR；独立工作树保留主工作区受限未跟踪资料。原入口见[原字节](../archive/pre-acquisition-lifecycle-p2/README.md)。REQ/BR/CAPI/D/RV、SQL1～3及历史运行/发布证据保持。

## 设计与取舍

固定容量或仅在回调成功后登记均不足：前者可能淘汰在途原件，后者与异步消费者竞态；只有移除容量上限则无法回收。本包采用显式所有者登记：reader只有绑定复用采图时记录完整AcquisitionIdentity；在调用回调前暂存观察所有者，false/throw只撤销该所有者。成功入队不是业务准入。

业务准入为作业增加独立所有者，消费者finally释放观察所有者。拒绝、重复、过载、未启用图像不留无用记录。持久ImageReleased先于作业释放；Unknown/Conflict/Prepared、原回执责任或在途阶段均保留。采图调用单独固定记录至实际返回；同reader重连不清旧所有者，epoch/原件身份不替换。Stop只停来源，完整排空后Dispose才终结此进程内登记；重启缺原件必须明确失败/核对，不能从新epoch重造。

复用reader的契约增加RetainAcquisition(identity, owner)及ReleaseAcquisition(identity, owner)，不向驱动传业务状态；业务决定何时释放，驱动决定原采集资源如何保有。观察/作业所有者使用不同前缀，释放幂等，完整identity防串实体/设备/代次。未实现生命周期的旧复用提供者不能装作兼容；精确契约指纹改变，需新不可变版本重新构建/签名。独立相机仍通过冻结请求及返回Acquisition精确验证，不声称真实SDK已支持。

## 实施计划（writing-plans，当前会话执行）

- [x] 从最新main核验入口/工作包/框架/D/RV/公开范围及并行修改；保护要修改文档原字节，清理相关当前指引。
- [x] 在既有Application.Verification增加`--acquisition-lifecycle`，修复前运行一条接受+5000拒收后原图采集，确认IOException。
- [x] 修改Application.Contracts的reader采图生命周期、Drivers的所有者/在途登记及ParcelFlow的观察到作业交接/持久释放；保留Unknown守卫。
- [x] 复用现有AA22/能力/可靠性验证，补必要的慢采图双位过载、重复/拒绝、重连、异常/恢复/终态回收用例；记录红绿输出。
- [x] 核验真实/扩展驱动接入，必要构建/发布启动冒烟；更新运行/扩展兼容说明、manifest与结果；提交推送main PR，不合并。

## 验收边界

参考合成设备与macOS验证不等于Windows实际、真实相机SDK/WCS/PLC或现场验收。Production缺真实能力仍拒绝。只交授权通用实现和合成证据，不带客户原稿、SDK、字体、生产数据或凭据。

实现及验证已完成，见[结果报告](../runtime/acquisition-lifecycle-p2.md)。源码固定`ca35b16a0ef883126b39640c19ea6f840e5a9716`；最后远端PR交付另记，未授权合并main。

已创建并读回[PR #23](https://github.com/yuan2go/importer-pc/pull/23)，base=main，OPEN；源码提交`ca35b16`，结果/证据提交`8415e9c`。未合并。最后交付记录只补远端回执及清单，不改变运行源码或候选。
