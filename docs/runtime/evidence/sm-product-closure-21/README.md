# SM产品闭环21验证证据

最终产品、测试与复验脚本 SourceCommit 为 `4119f23ac50f2d22539496c09eb1513a702b11e6`，完整继承 main `ecec9640c0dbc91925b1a9c4a27632120b505c09`（含 PR171 最终相机链路修复）。后续证据提交不改变这些源文件。用户取消集中看板；本次本机统计、班次采用用户确认的自然日初始班次、站点时区及识别/请求/格口取得分别计数，不宣称实际包裹件数。

`summary.json` 保留每项真实退出码、PASS 数及本机原生证据目录。三项 Release 构建全部退出0；14套应用测试559项、9套原生WPF测试292项，共23套851项PASS。只计本次冻结源的唯一成功套件；中间尝试不累计。`build-*.log` 与 `app--*.log`/`native--*.log` 是原始输出，原生日志取实际 `WINDOWS-WPF.log`，不以空的控制台输出替代。无跳过关键测试或删断言换PASS。

复验从干净提交运行 `. ./scripts/project-environment.ps1` 后执行 `./scripts/verify-sm-product-closure-21.ps1`；也可指定 `-Phase application|native|desktop`。脚本在开始及每项前拒绝未提交的 src/tests/scripts 改动，所有缓存、构建、临时数据及证据位于D盘。原生套件顺序执行，不并行争抢前台和WPF所有者。

`publish.log`、`publish-smoke.json`：同源 Sportmaster Release 执行 framework-dependent `dotnet publish src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:SelfContained=false --no-build --no-restore -o <D盘输出>`，随后实际 EXE 执行 `--environment-report`；发布和启动均退出0，程序集信息版本包含本次完整SourceCommit。报告在 Windows 11 x64 / .NET 10.0.13 本机运行；原始环境信息仅保留D盘，不发布主机硬件信息。`assembly-hashes.json` 是该发布输出的自有程序集指纹，未上传程序集、厂商SDK或字体文件。

五张 `pc21-*.png` 均由本次原生合成用例生成，已查看标准诊断/异常/统计、紧凑统计及首次配置界面，管理模式、待配置、远端未确认及校验错误均按实际状态显示。尺寸为WPF逻辑尺寸，截图含本机125%DPI，不代表目标IPC物理尺寸验收。

## 闭环及证据定位

|工作包项|本次直接证据|
|---|---|
|PC21-01 连接与配置启用|camera-chain-reliability、camera-chain-closure、mvs-offline-activation、mvs-software-flow、sm-original-frame、shutdown-resources；继承PR171并在整合树重验，不以页面状态冒充设备连通|
|PC21-02 安全动作、旧模块、常规日志|rcs-timeout-errors、manual-supply、sportmaster-rcs、product-closure-21；未知请求不重发，独立同码输入仍独立提交；常规RCS结果日志、旧人工隔离控件移除|
|PC21-03 操作路径|原生 product-closure-21、parameter-guidance-startup、parameter-v3-only、debug-log-live、query-filters、operator-ux-18；首配五组、待配置组合、自动查询、日志冻结、待重启与管理撤权|
|PC21-04 自动清理|应用 product-closure-21、retention-ingress、storage-schema-only；完整备份、排空、读容量竞争延后、时钟跳变停止、可选状态损坏保留原件、已清理统计不重计|
|PC21-05 规则热生效|应用 product-closure-21、ux-commercial-configuration；完整候选/CAS/管理授权/无在途门禁，原作业冻结配置及原请求字节保持|
|PC21-06 诊断包|应用 product-closure-21；白名单/文件哈希/无覆盖/取消/撤权及发布前再授权，不自动上传|
|PC21-07 维护中心|原生 product-closure-21、operator-ux-18与桌面构建；场景入口复用现有恢复/备份合同及停止后离线交接，原有恢复边界由retention-ingress、shutdown-resources等守卫复验|
|PC21-08 统计班次|应用及原生 product-closure-21；跨日/夏令时/覆盖校验、事实分计、调试排除、单快照/时间预算、清理前后汇总一致、班次保存重启与草稿保护|

## 中间失败保留

`intermediate-product-core-1.log`：未授权只读投影已脱敏，测试错误地用它比较完整原请求；修正为有效管理授权下取得原件，再比较FrozenConfiguration和原报文字节。没有取消原字节不变断言。

`intermediate-product-core-2.log`：启用自动清理的合成候选没有明确7天保留期，实际校验正确拒绝；夹具补齐保留期后仍走完整配置保存/重启流程。

`intermediate-product-core-4.log`：制造的已接受作业缺原件保管依据，原清理内核正确拒绝；改用真实完整备份/恢复产生合格保管链，再验证事务删除、保留统计、重跑防重计。

`intermediate-sm-build-1.log`：诊断文件缺StageState命名空间导致编译失败，已补齐引用。其他开发中失败、构建及原生尝试的原始输出保留在D盘 `.artifacts/pc21` 与早期按提交分组的目录；这些未冻结工作树输出不冒称最终源证据。历史参数测试改为当前显式延后目标的合同，日志测试等待真实初始查询完成后再操作，冻结/取消/查询范围守卫继续保留。

## 验证边界

本次实际运行 SQLite、内置RCS环回、真实ZXing软件引擎、合成MVS端口及Windows原生WPF。物理MVS/SDK与GigE拔插、客户RCS、物理PDA、实际声卡/扬声器、目标IPC、受保护安装/现场配置以及24×7长稳均 **NOT_RUN**；未部署或修改现场。当前数据库格式11，DB10及更早拒绝直接打开并保留原件，没有旧库升级器；格式拒绝不是完成现场迁移。自动清理默认关闭且有备份数量上限，详见运行说明。历史D/RV、旧介质和既有验收保持其SourceCommit。本目录仅含合成数据、通用规格和本机生成截图/日志，不含原审查文档、客户原稿、真实业务数据、凭据、SDK或受限衍生资产。

`hashes.json`逐件记录本目录证据字节，排除自身；归档原当前指引的哈希另见 `docs/archive/pre-sm-product-closure-21/manifest.json`。
