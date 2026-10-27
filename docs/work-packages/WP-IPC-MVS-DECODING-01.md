# MVS-DECODING-01

范围：MV-PD010003-06M-12C 的 MVS 采集后解码框架、显式合成验证及离线回放。基线 main `d506f77c`，保持当前 Observation/Application、SDK 边界、配置账和原请求责任。

[唯一设计](../design/mvs-decoding-01.md)定义决策、状态机、故障及取舍；[运行说明](../runtime/hikrobot-reader-01.md)定义配置和复测入口；[回放格式](../../tests/fixtures/mvs-replay/README.md)定义合成夹具与人工真值。文档清理单独提交，原始要求、D/RV、回执及原介质身份保留。

交付不包含厂商识别适配、SDK二进制、真实样本、生产资格或安装介质。当前内置装配保持无解码器、observe-only、businessReady=false。真实码率/错读率、触发计数及多帧语义、目标机长稳和真实设备/RCS闭环需要取得正式组件与现场证据后另验。
