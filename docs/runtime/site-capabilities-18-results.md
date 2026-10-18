# 场地能力18验证结果

开工 main `d3a757d6314a2d690bcfc4e7763cfd4849b97319`，PR17已合并；设计前置提交 `63e04b1`，开发分支 `codex/site-capabilities-18`。源码矩阵及边界见[实际能力指南](site-capabilities-18.md)。

运行源码冻结 **08595e9243ccd9981fc419b8074f67c836644248**。源码已补齐三场景可选登录、重量和采图上传，并完成以下参考验证。后续提交只增加测试覆盖、报告、清单及证据，不改变该运行源或候选字节。

|验收|实际结果|证据|
|---|---|---|
|AC18-01|PASS：从实际main发现PR17已实现三链，复核Application/Driver/Component/Platform/UI/装配，不复建框架|[源码能力矩阵](site-capabilities-18.md)、[输入与源码指纹](evidence/site-capabilities-18/source-freeze.json)|
|AC18-02|PASS：三场景×登录开关×称重开关×传图开关共24组合；关闭功能不构造对应端口；未登录观察拒绝进入队列；实测等待/错误重量不触发后续|[24组合](evidence/site-capabilities-18/optional-matrix.json)、[实际执行](evidence/site-capabilities-18/logs/image-recovery-final.log)|
|AC18-03|PASS：原操作员/会话快照进入原payload；WCS原格口和请求进入PLC；采图/上传/PLC落账丢回执后重启原查询；图像原回执T3失败本地重放；每阶段一次尝试；图像备份恢复不复活登录或重传|同上及[原Application恢复/资源回归](evidence/site-capabilities-18/logs/application-serial.log)|
|AC18-04|PASS：三个WPF选择交叉构建0警告/错误，Windows依赖锁更新；三个ZIP实际解压后默认链与全部可选功能HTTP链各一轮；独立服务持久账/PNG字节；错Application配置在建库前拒绝；旧baseline/architecture与15个发布工具测试|[候选](evidence/site-capabilities-18/candidate-inventory.json)、[独立服务接收事实](evidence/site-capabilities-18/independent-http-receivers.json)、[错误组合](evidence/site-capabilities-18/wrong-composition-rejection.json)、[全部日志](evidence/site-capabilities-18/README.md)|
|AC18-05|源码、配置、指南、清单和公开合成证据已形成；PR回执在创建后登记|[checks](site-capabilities-18-checks.json)|

三个portable候选均来自08595e9，ZIP CRC、全文件SHA、选中Application闭包与实际启动/退出已核验。三个候选的Platform、Application.Contracts与Components字节分别完全相同；DirectPlc不含Integrations DLL。候选保留在工作树 `.artifacts/site18-08595e9/`，没有将二进制或客户资料混入Git提交。

|候选|SHA256|
|---|---|
|sportmaster.zip|`8b49b71f9642e98496a9451580bc178407c84ac0682cb3ade4db2252a01a492c`|
|direct-plc.zip|`969d315efd2cf58ffe9d075921d84e7f996d92466464737754a100e923cdbac2`|
|wcs-to-plc.zip|`5b50f9b7dcfc9bed98eb0dc4e076abe9cc317f2ead3b337b3020064cecc4620d`|

独立Ipc.Fixture进程通过literal-loopback HTTP接收WCS/PLC/上传，服务根与客户端根分开。每个原接收账Executions=1，PLC含原WCS格口/请求与原操作员、重量、图像；服务实际PNG长度/hash与采图一致。上传落账后返回503，客户端先持久Unknown并阻断PLC；停进程重启后原查询不自动发送，下游只经显式继续。该结果不是内存mock调用计数，也不是生产网络/PLC协议资格。

首次失败没有删除：空引用警告导致编译拒绝、测试嵌套类型名错误、图像备份误复制活动租约、旧测试子进程找不到SDK、并行MSBuild节点退出以及资料校验缺Pillow/清单校验文件名错误。代码或配置原因已修复；定向核心采用单节点构建后直接运行冻结DLL，两个scope均通过。资料错误在最终清单阶段修复后重新核对，历史源资产问题单列。[日志原hash与公开副本hash](evidence/site-capabilities-18/log-custody.json)保留，公开副本仅替换工作树及SDK绝对路径。

**Windows实际客户端/交互/截图/DPI/HID/ACL/UAC：NOT_RUN。真实身份、相机/秤SDK、上传合同、WCS、PLC控制授权/安全联锁与现场：BLOCKED。Production仍NotReady，D/RV不变。** 新登录、采图、上传抽象和内建参考适配器不能作为“真实协议已接通”或“任意插件已可安装”的证据；新可选能力的真实签名适配仍需合同和受控加载政策。旧原包/原发布恢复边界不降低。

资料校验使用有Pillow的既有工作区Python后，当前结果为 **FAIL（原资产）**：265个原PPTX媒体类型问题、2个原UI元数据标记。新增内容身份、清单、链接错误 **0**；未改原件或放宽检查。完整结果见证据内archive-final.json；清单新增证据后另作最后内容/引用核对。
