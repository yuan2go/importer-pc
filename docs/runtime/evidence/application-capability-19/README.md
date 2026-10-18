# 19执行证据

最终运行/候选源码：`c213aad54f5d48687efc0b56428991b6bf4f3537`；开工main：`08b8067aa6cba6a148252296c7a5b18a27b3be65`。SDK10.0.402，macOS arm64。所有数据/身份/图片/对端均为显式合成参考。

|证据|范围|
|---|---|
|[source-freeze.json](source-freeze.json)|运行输入与验证源码hash、验证DLL、原SQL字节比较；Supply与Storage源码未改|
|[candidate-inventory.json](candidate-inventory.json)|三个portable/三个win-x64 ZIP、全文件hash/CRC、真实解压、选中闭包；Windows未执行|
|[independent-receivers.json](independent-receivers.json)|六次独立HTTP接收端的原请求、身份/重量/采集关联、PNG字节和一次执行事实|
|[signed-release-journey.json](signed-release-journey.json)|真实冻结发布程序安装并激活精确Release签名包，严格WCS实测/图片输入、原PLC及查询|
|[Debug签名身份](signed-debug-identity.json)、[Release签名身份](signed-release-identity.json)|不同精确公共DLL指纹对应独立签名包；测试私钥/凭据未上传|
|[log-custody.json](log-custody.json)|最终及早期失败日志原hash/公开副本hash；仅替换本地路径根|
|[superseded-candidates.json](superseded-candidates.json)|早期候选原SHA/ZIP hash保留；不是最终交付资格|

最终日志为`logs/ipc19-c213-*`：core19、core18、core17、signed-debug、baseline、architecture、六候选构建、九次解压冒烟。其余`delivery`、`first`等日志属于前序实施/失败记录，不能替代最终c213身份。

默认上传组件没有声明重量输入，可以在测量前冻结不带重量的上传请求；WCS/PLC仍必须携带配置所要求的重量。声明必须实测的上传组件由core19单独验证“先关联原图、重量就绪后才冻结/发送”。

原失败保留：空引用/属性关键字编译拒绝、夹具未构建超时、图像核对后状态未复位、旧构建器拒绝新增声明字段、未指定Application静态检查入口、原合同指纹包被新宿主拒绝、图像释放与继续命令的CAS竞争。实际产品/工具问题已修，旧合同拒绝未放宽；清理导致的明确CAS Conflict只刷新本地命令，不重试未知外呼。旧签名包没有重签覆盖。

本地候选位于开发工作树`.artifacts/capability19-c213aad/`；未将二进制、私钥、客户原稿、生产数据、SDK/字体上传。可用[运行指南](../../application-capability-19.md)重新构建。签名发布链的完整可复跑探针：[signed-release.py](probes/signed-release.py)。

Windows实际WPF/交互/截图/DPI/HID/ACL/UAC：NOT_RUN。真实SDK、身份/实体关联、WCS/PLC/上传合同及现场：BLOCKED。Production拒绝条件与D/RV保持。
