# RCS 周期心跳增量 · 2026-10-20

用户要求在既有RCS组件中加入 `sorter.task.station_heartbeat`，默认每5秒一次并可配置。起点为最新main `6784d08`；开工工作区无修改、无开放PR，复用既有C#/WPF与签名组件，不新增框架或RCS服务端。不修改UI页面、旧接口原件、D批准、RV状态或历史验收。

## 配置与运行

| 所属位置 | 字段 | 含义 |
| --- | --- | --- |
| 系统完整配置 `business.json` | `StationCode` | 供包台业务编码，保留大小写，最多256字符；不替换内部 `StationId` 或数据目录。选择RCS时必须填写。 |
| Reader驱动实例 `Extensions.Instances[].Parameters` | `bcr-code` | 对应相机的业务编码，保留大小写，最多256字符；由Reader绑定选择该实例，不从IP/序列号/左右位置推测。各Reader编码必须不同。海康1.0.2的签名参数描述提供该字段。 |
| RCS组件 `Wcs.Parameters` | `heartbeat-interval-seconds` | 可省略，默认 `"5"`，整数1～3600秒。通过组件参数表单修改，沿原维护权限、完整修订CAS与受控重启生效。 |
| RCS组件绑定 | `Target`、`source`、`locale` | 复用现有地址及协议包头；固定POST `/rcs/sorter/api`、JSON、version=1。 |

系统配置示例使用[合成业务配置](../../config/sportmaster/business.json)中的 `StationCode`；组件样例见[参数](../../config/sportmaster/rcs.parameters.json)。真实Reader在其原驱动实例参数中填写 `"bcr-code": "SYNTHETIC-PICK-1"`，其他Reader填写各自不同编码。默认参考业务仍使用参考集成；安装RCS后才能启用真实HTTP客户端，不会将参考地址静默改为真实地址。

未安装新驱动参数描述的旧海康包不接受额外参数，必须按原签名/安装/受控激活流程升级到新不可变包；不能直接修改已签名manifest。新配置通过完整JSON维护入口保存 `StationCode`；本轮未扩展专用系统表单布局。内部工位/设备标识限小写，业务编码独立存放以支持外部大写编码。显式Simulation的manual Reader没有硬件参数，心跳使用其合成DeviceId；真实Reader缺 `bcr-code` 或系统缺 `StationCode` 均拒绝RCS启动，Production不会回退。

## 实现与语义

[可选运行配置契约](../../src/Ipc.Application.Contracts/WcsRuntimeConfiguration.cs)由[装配](../../src/Ipc.Assembly/ParcelCapabilityFactory.cs)在启动前提供同一Effective修订；[RCS心跳](../../src/Ipc.Integrations.SportmasterRcs/RcsHeartbeat.cs)只读取该修订中的供包台编码和Reader驱动实例参数，不另读配置文件、不持有第二套配置账。配置后复制编码，不受调用方后续修改字典影响。

一个RCS实例复用原HttpClient。启动后等待首个周期，每周期一个POST，将全部配置Reader放入 `data`，index为1、2……，每项命令均为 `sorter.task.station_heartbeat`。timestamp统一采用UTC Unix毫秒整数。重复Start不创建第二个循环；串行等待一次HTTP调用，单次超时不超过3秒且不超过周期。Stop取消并等待心跳退出及已有业务发送完成，Reconnect先排空再创建一个循环；换页不影响组件生命周期。

心跳requestId为当前进程单调递增的UTC毫秒数字，与持久取格口编号分开，不消耗T1编号；若与取格口声明范围重叠则拒绝该次心跳并显示故障，不外呼冲突编号。此编号不承诺跨工位/重启的全局幂等性；若服务端对心跳和业务请求有统一的全局编号要求，仍须按实际合同联调后调整，不能据此宣告生产资格。心跳不创建Outbox业务意图，不存为包裹接受事实，也不重放失败旧心跳；下一周期发送新时间和新ID。

HTTP失败、断连、超时或重定向报告到原CapabilityHealth，不跟随重定向、不重试旧报文；下一轮2xx可解除心跳传输故障。尚未收到心跳响应合同，因此2xx只记录HTTP交付，应用确认仍为unconfirmed。页面沿原“客户端/上游未确认”证据呈现；心跳不能清偿Unknown、代替Query、证明实体完成或发送成功。取格口 `Query=false`、冻结原请求/旧合同/目标、SQL与恢复责任继续保留；取格口旧左右映射参数不因心跳配置被改写。

## 版本与交付

RCS为1.2.0，公共Application.Contracts为1.11.0（新增可选运行配置端口及可省略StationCode，旧JSON缺省保持可读），Sportmaster为1.4.2。公共DLL指纹变化后新reference.parcel-capabilities声明2.2.6，DirectPlc/WcsToPlc兼容声明随新公共契约补丁到1.4.1；海康参数描述新包1.0.2。原包和历史介质不重签覆盖，旧请求继续引用其原精确包；不得用新包替代旧请求的原组件。Parcel仍DB8，无新SQL迁移。

构建和运行定向验证（仓库根目录，使用锁定.NET SDK10.0.402）：

```powershell
dotnet build tests/Ipc.Application.Verification -m:1 -nr:false
dotnet run --no-build --project tests/Ipc.Application.Verification -- . --rcs-heartbeat
dotnet run --no-build --project tests/Ipc.Application.Verification -- . --sportmaster-rcs
dotnet run --no-build --project tests/Ipc.Application.Verification -- . --version-compatibility
python -m unittest discover -s scripts -p test_application_compatibility.py -v
```

## 本次验证记录

Windows本机模拟验证已通过：心跳26项、既有RCS121项、版本兼容56项、Application架构63项；Python兼容17项、发行指南2项；海康fake API自测26项。验证工程、Sportmaster WPF Release及装配宿主构建均为0警告/0错误。心跳覆盖默认/配置周期、双Reader合并、编码/时间/索引、HTTP失败/超时/重定向、停止/重连、业务编号隔离和缺配置拒绝。签名装配和冻结发布启动尚待源码提交后验证，结果另补。

开工原RCS回归在SQL7断言失败：main实际已用SQL8，本次保留严格版本断言并更新为8。后续Windows迁移夹具的辅助SQLite连接默认池化导致文件哈希读取共享冲突，改用不池化的检查连接，保留原字节/hash/SQL历史断言。旧兼容测试仍写1.3.3且漏列已存在海康包，按当前精确声明更新，继续拒绝旧包及篡改。RCS恢复夹具旧流程试图在未交接的恢复根直接准入；按当前DB8语义改为严格验证Resume拒绝、观察不产生请求，并直接调用实际编号分配器确认原范围退役、原归档编号保留；未移除恢复围栏或伪造交接。Application架构的原SQL7升级断言同样更新为8。

额外 `--ux-commercial-presentation` 在NoRead显示断言失败；对未修改的最新main `6784d08` 单独导出、构建并运行，复现完全相同断言。该项记录为基线已有失败，不修改页面或删除断言，不纳入本轮PASS。Windows验证使用独立测试进程串行执行：机器级资源租约会拒绝并行占用相同合成设备；早期并行运行产生的租约冲突未作为功能失败或绕过理由。

真实RCS请求尚未执行；timestamp单位、心跳回包应用成功语义、服务端编号作用域仍待实际合同/联调。真实海康SDK、设备、生产资格、现场与Windows图形交互不在本次模拟PASS范围。
