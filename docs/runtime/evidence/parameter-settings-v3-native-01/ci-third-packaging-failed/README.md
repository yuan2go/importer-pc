# PR80 第三次 CI：原生通过，安装器打包拒绝

运行：https://github.com/yuan2go/importer-pc/actions/runs/39535603858 。PR head 为 `bf7edd4c50744acc328a8484df8f977c79624aa5`，冻结 SourceCommit 为 `dce379844f8e3ed8f899025b0f237267cc9e26ee`。

核心、原生页面、移除恢复、视觉交互及 V3 专项均通过；安装器对共享 DLL 的逐字节比对拒绝发布。原始日志显示 Python TEMP 使用 `C:\Users\RUNNER~1`，MSBuild 使用展开后的 `C:\Users\runneradmin`。`release_build.build_properties` 未规范化路径，使嵌入调试信息包含不同临时目录。后续修复统一规范路径，并加入真实独立目录编译的 DLL 字节比较；没有删除或放宽原安装器一致性断言。本目录不是最终可安装交付证据。
