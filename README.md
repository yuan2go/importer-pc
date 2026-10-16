# importer-pc

Windows工业工位框架与供包参考产品。当前仓库已形成目标架构与下一实施工作包；**尚无供包业务工程，已有代码为资料校验工具**，不能把设计入库解释为客户端已实现。

从 [统一入口](docs/INDEX.md) 阅读 [框架设计](docs/design/framework.md) 和 [WP-IPC-RUNTIME-03](docs/work-packages/WP-IPC-RUNTIME-03.md)。新参考实现采用C#/.NET10/WPF/SQLite的推荐路线；既有现场工程迁移、真实SDK/WCS和业务规则仍需各自核验。可开展隔离运行闭环，不以缺少局部现场资料阻止全部框架开发。

本仓库为public。[公开范围](docs/baseline/PUBLICATION.md) 和 [缺失登记](docs/baseline/MISSING.md) 继续有效；完整客户资料归档仍是PUBLIC_SUBSET_ONLY，不上传客户原稿、私密来源、凭据、SDK或字体。本轮新增的是通用技术设计，不代替客户批准，RV-01～17不因此关闭。

本轮只修订文档，不开发业务代码。后续按工作包在开发分支实施并提交PR；实际运行命令由真实工程提供，目前仅有 [资料校验工具用法](tools/README.md)。
