# BUNDLED-MVS-CAMERA-01

2026-10-27 用户明确纠偏：Sportmaster当前只有SDK连接，安装供包台即安装所需MVS运行依赖，不再要求另装签名驱动、库存/信任配置、退出首配再切换或TCP回退。版本仍1.0.0，起点main c5202ffc；本机原安装和当前未提交用户文件不覆盖。

## 当前唯一规则

- 标准型相机复用现有MVS真实采集模块及原生命周期/物理身份/资源守卫，以内置驱动随当前SM装配；左右首配与日常参数统一显示SDK字段，默认MVS，不显示TCP切换/端口/探测或驱动包安装流程。每台一个RCS共享编码。
- 用户本轮授权本机离线安装包内置实际SDK；只复制核查的Windows x64 GigE运行闭包，原库和许可告知随包，原生版本/hash/AMD64/保护目录/资源所有权检查保持。厂商文件不上传Git，不改变全机PATH、不依赖MVS客户端安装目录，也不编造Production投产批准。
- 公共工程与厂商资源分开保存不是现场操作流程。正式交付构建缺实际SDK应失败，验证环境如缺资源须明确限定验证产物，不冒充已内置正式SDK。
- RCS source标识调用系统，locale为协议语言标记，均实际进入请求和心跳，与UI语言/相机SDK无关。用户已明确source固定BAR、locale固定zh_CN；移除两项编辑/必填/参数模板，两个真实报文路径保持该固定值，原冻结请求字节不改。
- SDK采图不等于识码；尚缺正式算法组件时显示解码不可用并阻断正式业务。现有可信包裹边界、Unknown原请求、防重、Pending/Effective与受控重启继续有效，无旧配置转换或兼容迁移。

## 实施及验证

复用当前装配、参数共模、安装介质完整性与NativeSdkBoundary；验证首配/日常左右SDK字段和一个编码、保存启用/缺依赖、冻结安装内置字节/改坏拒绝、SDK真实初始化枚举和关闭；必要业务/资源及门禁复用。与真实帧/条码、RCS现场验收分开记录。


当前真实入口原SHA256 0CBCF8D1…5119B实际返回0x04080201、文件4.8.2.1。原0x04080202文档/常量记录错误，首次内置加载严格拒绝，保留失败日志；本包按同一DLL/hash/头文件数据布局修正单一精确API常量，不加入旧SDK版本兼容。


已完成当前实现：内置MVS复用原实际采集源，首配/日常直接SDK，完整候选保存左右参数、共享相机编码，RCS固定BAR/zh_CN；安装输入强制真实14库/告知文件，包内保护/hash/AMD64与原生命周期不降级。真实内置目录加载、版本、初始化/枚举/结束通过，设备0，未打开/触发相机。应用RCS/心跳、参数、延后/Unknown、P0八组合、SDK硬化及原生SDK/完整参数V3通过，原FAIL分别保留。生产算法/实际帧/条码/RCS现场未验。工程验证细目见[evidence](../runtime/evidence/bundled-mvs-camera-01/verification.json)。


首轮f52f825c远端五项构建相关门禁停于同一Application.Verification锁文件缺新增MVS项目引用；原job122206482407/282/002/406/496保留。同步测试默认packages.lock并以locked重建通过；不改测试或放宽locked。原生SDK参数门禁首轮通过。本机f52冻结实际SDK介质与后续仅测试锁修正分开记录。

本机f52实际SDK ZIP已形成，原始单EXE封装在Installer自身新增MVS传递引用的RID锁处拒绝；同步Installer准确锁闭包后重建新源码介质，不复用混源EXE，首个失败日志保留。

f2cd1428已成功形成含实际SDK的单EXE，保留其SourceCommit与原日志。远端AQA/default暴露多Application动态id测试仍读取生产空白首配，现仅把Sportmaster合成场景明确指向当前synthetic夹具，守卫及断言不变。Windows AQA01全项通过；default合成业务段通过后在既有Production段受未安装管理授权服务阻止，完整default等待Linux门禁，不记Windows全套PASS。同步介质启动说明，当前SM公共配置不再称Simulation参考。
