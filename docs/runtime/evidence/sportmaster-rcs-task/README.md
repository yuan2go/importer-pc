> 当前整合结果见[integration](integration/README.md)。以下4c9d51c/组件1.0.0记录为整合前历史候选，保持原SHA与原字节；不充当新main候选证据。

# RCS组件01证据

本目录只使用合成数据。执行基线main `4afe2afacde9e2acab7648fff3440fa3cc331b6e`；PR26独立保留。当前已执行结果见[checks](checks.json)和同目录原日志。

- 固定main基线AA22：PASS；当前RCS定向104条PASS（含完成行）、AA22/AQ/review-closure回归：PASS。
- 实际WPF win-x64交叉构建：PASS，0警告/错误；Windows实际NOT_RUN。操作端投影在参考运行中验证，未声称Windows截图/交互验收。
- 资料工具单测10项、随包指南单测2项：PASS。系统Python缺Pillow的失败日志保留；改用已有bundled Python后通过，未更改失败断言。
- 两次无效夹具/命令诊断原日志保留，其原因见checks；不能当业务失败或业务通过证据。

固定实现源码 `4c9d51c2c017b7be74cc45156c9df9cd97a5fae9`；[交付身份](delivery.json)记录实际包/签名/精确契约哈希。签名扩展安装与真实应用工厂装配、三类响应和重启无重发全部PASS；[冻结宿主身份](signed-host-custody.json)逐个核对12个DLL。解压portable实际进程冒烟PASS；实际CLI安装、候选生成、部署检查与禁止覆盖PASS。Windows候选仅完成交叉发布、解压hash及app/desktop/portable契约一致性校验，实际运行NOT_RUN。

读取UI v1.1 README/GALLERY、原HTML/分离CSS/JS、完整交接及normal/split PNG作为实物基线；只在现有状态/详情投影增添RCS结果，不改XAML/样式/原资产，不关闭旧HTML缺陷或历史RV。

资料全量检查对固定main与本包各保留267项历史错误（265项PPTX媒体类型、2项原来源元数据标记）；本包新增错误0，未修改原资产伪造全量PASS。CLI一次小写expected SHA被既有安装规约拒绝，改用原指南规定的大写值后实际通过，原拒绝日志保留。真实RCS/设备/现场BLOCKED；不能将参考HTTP夹具或交叉构建解释为真实验收，不关闭D/RV。
