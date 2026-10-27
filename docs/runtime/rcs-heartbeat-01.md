# RCS 心跳：当前相机单一编码

2026-10-27起以[CAMERA-SDK-SINGLE-CODE-01](../work-packages/WP-IPC-CAMERA-SDK-SINGLE-CODE-01.md)为准。修改前说明及历史SDK/验证身份见[原字节归档](../archive/pre-camera-sdk-single-code-01/manifest.json)，不把原PASS扩张到当前源码。

每台相机只填写一个相机编码，在参数“左相机/右相机”页唯一编辑，取格口请求与心跳共同读取同一映射。当前规范保存位置是已选RCS组件的left-bcr-code/right-bcr-code；驱动仅保存连接、采集和识别参数，不再声明bcr-code。两个用途没有各自的可写副本，不从IP、序列号、条码或DeviceId推测业务编码，模拟Reader也使用同一配置来源。

相机和RCS都已启用时，该相机编码必填且左右不能重复；明确延后或停用的相机不参与心跳，延后字段可留空。所有相机均延后时不发空心跳。启用相机前，完整候选静态校验及实际RCS运行配置均核对原拓扑位置、编码、重复和设备身份；缺失拒绝，不回退至旧驱动编码。SDK切换只重置本相机连接参数，相机编码保留。取格口准备仍固定本件的原RCS绑定和编码，新的Pending编码不会覆盖原请求，Unknown不会自动重发。

供包台编号读取系统StationCode，并沿既有完整候选统一到取格口映射。接口仍为sorter.task.station_heartbeat，沿当前RCS端点与source/locale，version=1、数值requestId及UTC Unix毫秒timestamp。默认5秒，参数范围1～3600秒。心跳编号不消耗持久化取格口编号范围；冲突拒绝。HTTP成功只代表传输，未提供应用层确认合同，不能标为业务接受；失败不重发旧心跳，不重发Unknown业务请求。

沿用唯一RCS实例和生命周期。运行中的实例禁止重配；受控停止排空定时任务及HTTP调用，保存后Pending与Effective分离。真实SDK、真实RCS、现场验收分别记录；环回HTTP和原生UI验证不是现场证据。
