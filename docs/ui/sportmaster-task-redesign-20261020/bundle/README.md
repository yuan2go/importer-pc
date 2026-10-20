# 原始 ZIP 的完整分片归档

本目录保留 `Sportmaster-Redesign-Package.zip` 的全部原始字节。单文件42,154,441字节的GitHub写入请求返回422、提示输入过大；其94个展开文件均直接位于上级设计目录，查看原型或PNG不需要恢复ZIP。分片只是重复整包的存储形式，不是新设计。

[校验清单](bundle-manifest.json)按顺序登记11份二进制分片，每份最多4MiB。原ZIP的SHA256为：

```text
b6b7cfd3eb2567fb83cbae2dd15f21e85de703d07f3e3a635a398fe2047d5971
```

如需取得与原交付完全一致的ZIP，在完整下载或克隆仓库后，从仓库根目录运行（Python 3.10+，标准库，无联网）：

```sh
python docs/ui/sportmaster-task-redesign-20261020/bundle/restore_bundle.py --output ../Sportmaster-Redesign-Package.zip
```

输出目录须已存在。脚本逐片验证大小/SHA256，再验证整个ZIP，完成后才写出目标文件；已有同内容文件直接报告已验证，不覆盖不同内容文件。源码见[restore_bundle.py](restore_bundle.py)。如系统不支持同目录硬链接，脚本明确失败且不留下不完整目标，可改用支持硬链接的本地文件系统。

本次在交付环境实际恢复，并核对42,154,441字节、全量SHA256和ZIP内文件身份。该检查证明归档完整，不证明Windows客户端或设备链路通过。
