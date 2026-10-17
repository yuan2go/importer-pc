# OPERATION-CLOSURE-15 原始证据索引

冻结候选源码013862fc2d1769605624f8e10df5cc5b448a7954，macOS26.5.2 ARM64，SDK10.0.402/runtime10.0.13。全部业务数据为本任务独立Simulation合成与字面loopback；实际Windows截图0，真实SDK/WCS/产品/现场独立未决。

- `runs/`保留每轮实际SHA、命令、开始结束、退出码及完整scope完成标记。`logs/`为完整日志，包括未提交探索、首次失败、中间复验及最终候选运行；文件名中的final/stable不自动表示最终源码。正式采用范围见[checks](../../operation-closure-15-checks.json)。无完整标记不能只凭exit0判PASS。
- `source-evidence-comparison.json`逐源完整diff：最终运行时核心与110d相同；0cc只纠正并发测试等待及追加格式4 fixture；d2补UI箱身份；013纠正升级脚本SHA参数。每项仍报告实际执行SHA，未重复无关全量测试。
- 首次8df基线失败源于把活跃发送登记误当Unknown，110d修正守卫语义；110d operation失败源于测试误等可能同一任务，0cc等待两任务后通过，原断言不弱化。探索编译/旧format期望失败均保留。小写安装hash的旧升级失败在`upgrade/upgrade-journey`及原脚本副本中；013规范参数后最终旅程通过。
- d2 ZIP写入ENOSPC且runner记录写入失败，`final-package-portable.record.json`为事后恢复，明确开始时间不可用；原日志/部分ZIP本地保留。只将本任务571文件产物逐SHA校验后迁移到任务专用存储，未删除其他数据。重归档校验通过但d2被撤回；正式013双候选重新制作，见`withdrawn-*.json`。
- `upgrade/release-upgrade-journey/`保留完整旧1.0.1部署→清偿→合法过渡Effective→备份/显式3→5→新包/修订Effective→供包/追溯/停止重启原进程输出、升级/失败回执和最终结果；旧发布/seed/原包字节不变，旧读者拒绝。`legacy13-custody.json`绑定原13已公开附件SHA。
- `synthetic-databases.zip`含所采用选择器、关键失败及最终升级/冒烟的157份精确合成DB/WAL/SHM/raw接收文件，`synthetic-custody.json`逐文件hash；ZIP内另有SHA256SUMS。只解压到新目录，不修改原证据。DB原字节不做路径改写，非客户/生产库。
- `preserved-inputs.json`验证原SQL1～4字节及原件/D/RV/工作包/UI实物62项未变；`candidate-inventory.json`验证双候选全文件集/内层SHA/CRC与同一SourceCodeSha256。`release-plugin-reproducibility.json`保留两宿主、两插件各两次编译的精确manifest/payload/冻结宿主证据。PSS签名随机，ZIP hash不要求重建相同。
- `remote-readback.json`登记已公开最终附件完整下载hash、ZIP CRC/文件集/内层SHA；没有私钥/客户原稿/生产数据/SDK/字体。TestOnly公共信任不授权Production。

公开文本仅将本机工作树/SDK/临时根路径映射到`/reference/*`，不删事件/顺序/结果；`publication-provenance.json`逐文件原SHA/公开SHA及变换，运行记录中RawLogSha256保留原件、LogSha256对应公开完整副本。本机原件仍在任务.artifacts，路径映射不等于原字节相同。所有Windows同版后验见[清单](../../validation/operation-closure-15-windows.md)，UI图不替代客户端。
