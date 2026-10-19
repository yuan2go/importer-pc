# 商业化整理前的原字节与元信息

日期：2026-10-19。来源为 main [b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5](https://github.com/yuan2go/importer-pc/commit/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5)。本目录保留本次收敛前七份当前入口/运行指南的完整原字节，供追溯；它们是历史快照，不是现行实施指令。

当前入口见 [INDEX](../../INDEX.md)、[商业化工作包](../../work-packages/WP-IPC-COMMERCIAL-PRODUCTIZATION-01.md)和[基线核对](../../reviews/commercial-productization-01-baseline.md)。采用 .txt 后缀保留历史相对链接和语境，不将旧路由再次作为当前导航。

|原路径|原字节快照|字节数|SHA256|
|---|---|---|---|
|README.md|[快照](README.md.txt)|4880|f5fe29e236b6c3ab0b8c31175466e1f6bc2c02d27c02be83c9e192e4b1af4a4e|
|docs/INDEX.md|[快照](docs/INDEX.md.txt)|11524|994a14874ef743b215ad452f4f5af963081e4d9039e3814106695493f9ae3545|
|docs/design/framework.md|[快照](docs/design/framework.md.txt)|39261|52f6915ac9e1b87f70677547c3f196235c856e455f05e2acf8454966181b2c48|
|docs/design/framework-decisions.md|[快照](docs/design/framework-decisions.md.txt)|22512|a6b6d6318d672e3ccb1d0eec55f1b70b3da667c8c1e2e4fa11040a3bd5ea3d7f|
|docs/runtime/getting-started.md|[快照](docs/runtime/getting-started.md.txt)|5808|467f6ce05149b040e867fa9018e8d60d41790182e906db0f771914106887a7c8|
|docs/runtime/windows-application-delivery.md|[快照](docs/runtime/windows-application-delivery.md.txt)|12509|c5ffceb09a50b7a3d1604190295111bd214919b6cac52e1812cc11f35458ac02|
|docs/runtime/architecture-quality-closure-01.md|[快照](docs/runtime/architecture-quality-closure-01.md.txt)|24266|ac4fcbb3924ff6ebb5536798be72810910afb8ad40fd672b8f7e05af355eb20d|

[metadata-before.json](metadata-before.json)保留原 manifest 中选定的当前入口/源码/发布元信息，历史发布对象中的版本、结果和介质身份保持原义。完整原 manifest 可从[固定提交原文件](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/docs/manifest.json)核对：Git blob e648404a8f9f8c93deba4c135831a5367ce5f490，SHA256 fdd76679c95a1f9335f874cea78b3957184da341db81bb26d46b74dcc2d2ccae。

原校验文件的目标文本为 docs/manifest.json；现行资料校验器要求在 docs/manifest.sha256 中使用文件名 manifest.json。本次随清单更新修正该目标文本，原校验文件可按上述固定提交核对，不更改校验器或新增例外。

所有原审查、结果、D/RV、旧发布回执和更早 pre-* 归档仍保持原字节。本目录只保存本次实际收敛的原文件，不复制整个仓库或重复历史发布包。
