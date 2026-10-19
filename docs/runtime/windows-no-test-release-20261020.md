# 2026-10-20 Windows 不测试构建与新版本发布

用户要求基于 aaa30b5 后继最新 main，合并全部开放 PR，实际构建全部合并成果的 Windows 安装包并上传新 Release，不测试、不覆盖旧版本。开工远端 main 为 fe9e5bb15cd0e380c3dbd2c0a5369afaee9325c4；开放 PR52 与 PR54。现有根目录报价表排除。

先按保护规则实际合并，再从完整固定源码构建 Sportmaster、DirectPlc、WcsToPlc 三个独立 win-x64 自包含 ZIP。复用 package-application.py，不运行测试、业务程序、安装或冒烟；构建内置的合同/组件/文件散列门禁仍保留。不会把旧 cfe6c0e、7acbb2c 或 bd86fbb 字节改标为新源码。

PR52 与最新 main 仅 manifest 冲突：保留最新商业实现和复审的当前元数据，按路径纳入52的历史八包证据及手动 Actions工作流，原形成时结果与新构建分开。仅为 workflow_dispatch 的流程本轮不触发。历史 D/RV、Windows未验与真实 SDK/设备/现场 BLOCKED 保留。

本节为构建前范围及合并裁决；实际源码、包、日志、Release 和下载身份在构建上传后补充，尚不表示构建成功。
