# CODE-RELIABILITY-HARDENING-02 编码前归档

保存被替代现行入口和日志读取/呈现说明的原字节；身份及 SHA256 见 FILES.json。旧报告、SQL/checksum、D/RV 和历史验收不改写。新入口为 [工作包](../../work-packages/WP-IPC-CODE-RELIABILITY-HARDENING-02.md)。

原始三份快照保留Windows检出CRLF字节与原sha256，FILES另列对应SourceCommit的canonical_git_sha256；正文仅行尾不同。新增稳定性运行稿直接保留Git原blob。归档与运行证据均设为-text，避免Git自动行尾转换破坏原字节核验。
