# 当前格式与精确版本规则

项目只支持当前版本从零安装、从零建库，不提供自有旧版本迁移、转换或接管。非当前输入明确拒绝并保留原文件字节，请使用全新数据目录。冻结历史材料见[归档](../archive/pre-fresh-baseline-01/README.md)，当前工具不重验旧介质或旧资格记录。

业务版本以各业务csproj和config/compositions.json为准；扩展声明在config/compatibility中随业务程序集内嵌。发布工具强制二者一致。公共契约、模块及扩展按实际DLL精确哈希、签名、依赖闭包和独立信任校验；版本号相同不能替代字节身份。契约变化须重编译所有业务和扩展，生成新的不可变包身份，不覆盖历史签名包。

| 对象 | 唯一当前输入 |
|---|---|
| Parcel配置 | schema3，显式拓扑、重量有效期和当前管理模式 |
| Parcel数据库 | 当前格式9，空目录初始化或同格式再次打开；B3切换身份前不宣称新基线已完成 |
| Supply数据库 | 当前格式5，空目录初始化或同格式再次打开 |
| 正式Application发布 | application-release.json schema2，实际BusinessAssembly、BusinessVersion、CompatibilitySha256 |
| 当前组合验证 | schema3记录，实际CombinationInput、进程事件、候选及配置字节身份 |
| Windows和外部接口 | 继续执行Windows客户端门禁、RCS协议和厂商SDK要求；不是自有旧版本兼容 |

当前组合使用smoke-application.py，默认、optional-features、attached-upload分别产生新记录和原始events.jsonl。开始RUNNING，异常FAIL，实际完成才PASS；已有证据拒绝覆盖。核对命令、配置、snapshot、检查项、场景、实际peer身份与事件hash；参考进程不能取得Production资格。

```powershell
dotnet D:/new-release/app/Ipc.Assembly.dll --action business-identity
dotnet D:/new-release/app/Ipc.Assembly.dll --action deployment-check --file D:/candidate/business.json --data-root D:/new-preflight
python scripts/application_compatibility.py --record D:/evidence/current-run.json --package D:/new-release
```

源码开发允许无发布manifest；正式发布验证必须携带实际业务程序集。哈希证明内容一致，可信渠道、签名、运行测试和现场批准各自独立。新源码不能借用历史PASS。新增能力须同源发布、完整候选/CAS、受控重启及实际组合验证；原请求始终冻结原实现和报文。
