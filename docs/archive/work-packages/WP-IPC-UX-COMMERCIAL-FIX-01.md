# WP-IPC-UX-COMMERCIAL-FIX-01

用户在本次独立审查后明确要求“先修复问题，后面再验收”。开工main为8f886f44bec01779318ec078a569500ab1beab56，无开放PR；隔离分支codex/ux-commercial-fixes-01。原审查保存在仓库外同名任务报告，UXC-01～07及G-01/02编号不改。此包实施已确认的修复方向，必要开发验证不代替后续Windows/现场验收，不连接生产设备。

## 取舍与边界（业务代码前）

比较仅清UI旧值、另建输入/配置状态机、补齐既有Application投影三条路径：采用第三条。仅清UI无法解释重启与双窗，第二套状态机会分裂原件与配置责任。基座、三个独立Application、驱动/组件、原请求、Unknown、SQL1～7和单一配置账继续继承。

1. UXC-01：Application拥有独立最新观察事实，不因非法观察创建新Operation。新增可选ParcelSnapshot.Inputs（ParcelInputSnapshot数组），记录PositionId、原Observation、Outcome（Admitted/Rejected/Blocked/Duplicate）、Reason、OperationId、RecordedAt及原ReaderBinding。事实进入既有parcel_audit的专用InputObserved事件，启动按持久seq重建每位最多一个投影；不新增表/第二账、不修改SQL和DB7。可信当前reader/epoch/sequence与来源时效先验证；旧注册/旧序列不能覆盖当前投影；来源事件/新鲜度/健康验证通过后才推进序列。设备完整绑定变化后旧终结结果留历史，不冒新来源。无码、多码、非法码的原来源和分类可追溯。最新观察未准入且旧主链已终结时，CurrentOperation为空，原作业仍在历史；原未决责任继续显示及受守卫，不被观察失败清除。两布局按同一投影展示，不用上件格口解释本次观察。
2. UXC-02/06：保留已批准的浅灰/白卡、蓝紫位标识和大条码/格口。正常、等待、处理中、拒绝、Unknown、失效使用文字+语义图标/颜色，普通RCS摘要不着错误色。健康展示保留证据范围，RCS仅本地lifecycle时明确“客户端已启动，上游未确认”，不造health请求或把POST当查询。
3. UXC-03：沿原schema、库存和CAS补schema3结构化编辑。所有reader/投递位/实例及本位秤/相机绑定保持完整，已安装提供者按精确身份选择；局部字段校验、完整候选校验、保存Pending、受控重启Effective分开。只读高级JSON仍可用，表单与高级JSON一次只以明确加载的草稿为准，冲突不覆盖。旧schema1/2兼容路径保留。安装/原操作恢复/卸载继续调用原Store，不引入第二安装设施。
4. UXC-04：在既有framework的800×420DIP重排目标内补真正紧凑布局；至少保留两位来源/当前状态/原码/格口和固定操作，详细标识和诊断可折叠/局部滚动。低于最低有效客户区仍暂停保护，恢复空间后人工确认且不Resume。既有1280×680是旧候选阈值，不作为缩减既有06矩阵的依据；本包扩展布局，不整屏缩字、不放松业务守卫。实际DPI/触控可用性后验。
5. UXC-05：启动先有真实阶段界面，业务允许与否仍由Application决定；失败可保留/复制诊断，不假进度。classic初始history/maintenance执行对应读取。历史加载/空/失败/更多页、时间与Operation→Request→Attempt/Reply层次可见，完整原JSON可复制。导航/刷新不丢原选择、草稿或新建runtime。
6. UXC-07：将已确认在桌面调用链同步执行的只读SQLite/文件及备份等服务调用放入有所有者的异步调用路径；UI投影/集合更新回UI上下文，原命令守卫、CAS、Busy、独立Pause和关闭排空继续有效。开发验证证明阻塞服务不占用调用线程，不宣称目标IPC延迟/SLA已验。

## 版本与交付

计划业务1.3.0、Application.Contracts1.9.0，DB仍7。新快照字段可缺省，旧请求/冻结配置/原回执字节不重写；真实旧未决请求仍须匹配原精确发布清偿，不据新UI改包。精确公共DLL变化须新不可变参考包，计划能力2.2.0、reader1.3.0、RCS1.1.0，不能覆盖旧身份。最终实际版本/构建SHA/包hash以交付记录为准。

现行UI/运行说明中受本包替代的阈值、表单和页面状态段落直接更新，并保留变更前字节或固定Git来源；不改原HTML、PNG、tokens、历史审查/PASS或D/RV。公开范围沿PUBLICATION：只发布获授权通用代码、合成证据及说明，不收入本地客户原HTML/PRD、密钥、SDK/字体或生产数据。

G-01真实Windows验收按用户要求后置；G-02缺真实SDK/协议/权威Unknown合同的部分继续BLOCKED，不编造接口/批准人或强制清Unknown。完成其余修复、必要定向回归、三选装交叉构建、不可变发布与解包参考启动，开发分支提交并创建main PR，不自动合并。

## 执行清单

- [x] 文档设计/计划与本包原审查映射先提交（943727e）。
- [x] UXC-01真实运行时和两布局反例先RED，再持久观察投影及重启/防重闭环。
- [x] UXC-02/04/05/06两布局状态、布局和页面状态接线。
- [x] UXC-03 schema3编辑、校验、CAS/激活与原Store管理路径。
- [x] UXC-07异步服务调用及关闭/暂停/草稿回归。
- [x] 版本、配置/依赖、随包指南、manifest、实际构建/参考媒介及证据，见[结果](../runtime/ux-commercial-fix-01-results.md)。
- [x] 已提交推送并创建[main PR #37](https://github.com/yuan2go/importer-pc/pull/37)，未合并；已修/待验/受阻见结果，后续验收不在本包宣告通过。
