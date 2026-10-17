# OPERATION-CLOSURE-15 UI映射与验收边界

实物来源SS-UI-VIS-001 v1.1保持原字节。已实际读取README/GALLERY、完整交接、HTML与分离CSS/JS/tokens，并查看正常工作台/记录/详情设计图；HTML嵌入CSS/JS分别包含分离原字节。它们是设计资料，实际WPF截图仍0。

|问题/页面|实现与操作|当前验证边界|
|---|---|---|
|UI01/UI06/UI-R02，PAGE03/05|Snapshot.BoxResult从原箱持久Context/CheckClose/Close请求/响应派生；箱状态、最后结果/原因/原ID、当前健康/阻断/投影与就近动作原因分别可见。CheckClose/Close拒绝仍Active；另一通道成功不抹箱拒绝。|真实SQLite/刷新/重启/权限策略验证，WPF交叉构建；实际长原因、客户区与DPI待Windows。|
|R01，PAGE04|ScanInputDraft + LaneControl真实SendAsync；每通道一次1条，处理/刷新期间只读；字符/粘贴/终止符显式拒收并反馈重扫；失败保留/选中，迟到不能消费新owner/version。|实际应用ScanRetained及同一输入策略/事件接线；真实HID/焦点/吞吐NOT_RUN。|
|A04-R1/UI-R03/A03/A04，PAGE08|当前草稿IntegrationTarget读取不调用Build；选择的成功/错误/finally用同一SelectionLoad；导入/新建/身份失效Invalidate。原统一草稿、当前页显式修订与应用CAS保留。|可控A慢失败/B快成功、取消代次、多设备服务/保存重读/激活核心；实际页面事件旅程NOT_RUN。|
|UI02～04，PAGE06|列表全宽、结果/接受量/对象/条码/通道/请求尝试接收摘要优先；箱会话不填Item字段。选择后打开详情，返回保留列表控件/条件/选中/滚动/页码；技术请求树默认折叠。可见筛选标签、本地日期+24小时时间、显式时区，非法/反向/夏令时反馈。|应用过滤/分页/鉴权/保留断言、时间转换和WPF构建；Windows实际列表/返回/滚动/日期控件待验。|
|UI05/UI07/UI-R04，PAGE03/04/06|只读主位置详情使用次动作色；移除重复详情按钮；处置仅在适用状态显示，ReviewFits不要求隐藏的次动作，但仍检查关键事实、波次差异、固定主动作及拆包输入。受影响组件复用Soft/Accent等tokens与原字体。|源/策略及交叉构建；实际尺寸/长字段/所有关键矩形与触控NOT_RUN，不缩字或降低保护换通过。|
|A01-R1/R2，PAGE01/06/07|恢复原请求旁显示独立接收ID/来源/时间/状态；详情/导出保留每次责任、原终态/唯一接受事实。无原件明确不可恢复，无通用解锁。|故障注入/重启/并发/真实Product与原合同，Windows显示NOT_RUN。|

同版Windows场景见[后验清单](../runtime/validation/operation-closure-15-windows.md)。不以纯策略、静态XML、HTML图、交叉构建或0张实图关闭UI实际验收；W01～06原支持/只读/保护/未运行矩阵、D/RV和现场批准全部保留。
