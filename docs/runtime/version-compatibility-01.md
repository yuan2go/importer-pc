> 本文保留形成时的设计及验证背景。当前 SM 实施及验收见[文档索引](../INDEX.md)与[实施手册](../operator/implementation-guide.md)。

> 2026-11-02 DB22用户最新授权：本机保留配置/历史更新，已知Parcel10→11允许在独占离线、严格结构校验、完整备份及原责任守卫下受控升级；其他旧格式仍拒绝，不自动降级。旧从零安装限制在本次范围内由[DB22工作包](../archive/work-packages/WP-IPC-DB11-COMPATIBLE-UPDATE-22.md)替代，历史证据不改写。

> 2026-11-02 PC21：Parcel格式11新增 `station_metric_buckets` 和三个有界统计查询索引。清理明细与累积汇总在同一事务；备份/恢复/发布manifest和长稳分析统一读取11。PC21形成时DB10及更早只读拒绝；本轮DB22仅为已核验10→11增加离线备份后的单步升级，不自动转换其他格式。本轮操作见[PC21](../archive/runtime/sm-product-closure-21.md)；原说明见归档。

# 当前格式与精确版本规则

2026-10-27 自有产品/公共契约/驱动/组件统一为1.0.0，见[首次交付版本说明](../archive/runtime/unified-version-1-0-0.md)。数据库格式、配置schema和外部SDK/协议版本保持各自身份；历史版本及冻结包不改标。

当前版本支持从零安装/建库及DB22获准的Parcel10→11保留数据更新。仅准确匹配原基线哈希和实际结构的格式10可在独占、完整备份及原请求守卫下升级；其他非当前输入明确拒绝并保留原文件字节，请使用全新数据目录。冻结历史材料见归档，当前工具不重验旧介质或旧资格记录。

业务版本以各业务csproj和config/compositions.json为准；扩展声明在config/compatibility中随业务程序集内嵌。发布工具强制二者一致。内置模块按实际DLL精确哈希和依赖闭包校验；外置扩展在适用业务还校验签名与独立信任。SM只使用内置MVS软件识别/RCS，不要求外置目录或按配置签署资格；版本号相同不能替代字节身份。契约变化须重编译所有业务和扩展，生成新的不可变包身份，不覆盖历史签名包。

| 对象 | 唯一当前输入 |
|---|---|
| Parcel配置 | schema3，显式拓扑、重量有效期和当前管理模式 |
| Parcel数据库 | 当前格式11；新库一次建基线，已核验10经离线备份后单步升级；运行只读门禁保持 |
| Supply数据库 | 当前格式6，空目录一次建基线；只读门禁后才能再次打开 |
| 正式Application发布 | application-release.json schema2，实际BusinessAssembly、BusinessVersion、CompatibilitySha256 |
| 当前组合验证 | schema3记录，实际CombinationInput、进程事件、候选及配置字节身份 |
| Windows和外部接口 | 继续执行Windows客户端门禁、RCS协议和厂商SDK要求；不是自有旧版本兼容 |

适用组合使用smoke-application.py产生新记录和原始events.jsonl，实际允许组合以对应业务内嵌目录为准；SM不保留外置签名组合或TCP/MvCodeReader生产组合。开始RUNNING，异常FAIL，实际完成才PASS；已有证据拒绝覆盖。核对命令、配置、snapshot、检查项、场景、实际peer身份与事件hash；参考进程不能取得Production资格。

```powershell
dotnet D:/new-release/app/Ipc.Assembly.dll --action business-identity
dotnet D:/new-release/app/Ipc.Assembly.dll --action deployment-check --file D:/candidate/business.json --data-root D:/new-preflight
python scripts/application_compatibility.py --record D:/evidence/current-run.json --package D:/new-release
```

源码开发允许无发布manifest；正式发布验证必须携带实际业务程序集。哈希证明内容一致，可信渠道、签名、运行测试和现场批准各自独立。新源码不能借用历史PASS。新增能力须同源发布、完整候选/CAS、受控重启及实际组合验证；原请求始终冻结原实现和报文。

基线等价证据见[SQLite结构比对](../verification/fresh-baseline-01/README.md)。PC21新增事务统计汇总及3个查询索引后，Parcel当前51个结构对象；原基线对照中的47个对象保持历史身份。Supply仍保留26个结构对象。历史格式标识表和多步执行器删除；platform_migrations/schema_history仍只存一条当前身份；获准Parcel10→11用同一事务添加统计结构、替换精确身份并记审计，不重写旧请求或配置。
