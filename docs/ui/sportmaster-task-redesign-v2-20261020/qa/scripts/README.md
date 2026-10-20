# 已执行的必要走查脚本

本目录保留本次实际执行的三个脚本原文，便于审查断言和范围。它们记录当时scratch目录及浏览器路径，**不是开箱即用的生产测试套件**，也不会被产品加载。相关hash/时间以各自JSON回执为准；未复制中间冻结HTML或失败截图为正式设计产物。

若需要复现某个必要场景，先调整脚本中的SOURCE/OUT和已安装Chromium路径，输出到独立临时目录，再仅选择受影响任务。核心脚本可用UI_V2_QA_ONLY，辅助脚本可用UI_V2_AUX_ONLY筛选；最后修正脚本只包含3项必要检查。不要覆盖已有回执或将新跑结果冒认为原版证据。

可移植的正式原型构建与全图导出入口见[源文件说明](../../source/README.md)。所有验证限定离线模拟；需拦截外部请求，不连接设备。

## 最新 main 增量

`latest-main-increment.cjs` 使用同目录上两层的 `Prototype.html`；也可用 `UI_PROTOTYPE` 指向原型、`UI_QA_OUTPUT` 指定结果、`PROTOTYPE_BROWSER` 指定本机Chromium。`UI_QA_CASES=V2-L01,V2-L03` 只运行指定受影响任务。需已安装 Playwright；脚本不访问生产服务。实际两次调用与版本见 `../latest-main-increment.json`。
