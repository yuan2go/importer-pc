> 2026-10-29 整合续做：ST08/ST14 的监督连续性与存储启动边界见[WT024收口](../runtime/integration-20261028-supervisor-storage.md)。下文原源、FAIL/PASS及外部资格保持形成时范围。

# 时间与存储故障分级 · TIME-STORAGE-FAULT-01

F0：2026-10-26 main a8cd431456f73e83040f75a0c5e3cc1423440d1b；3a5b1d13是原线索。PR108/110/111未合并，四份原工作区附件不动。独立D盘工作树codex/time-storage-fault-01。原DB9/SQL1–9、历史冻结报文/PASS/SourceCommit不改；新根Simulation/合成故障，不改系统时钟、机器政策或真实工位。

ST12先以可控UTC/单调时钟验证容量/保留节流。PR111已有部分容量单调实现但不属于main，不把其PASS移入本包。ST13先查明实时及恢复的原时间冲突规则；证明请求在途回拨，再讨论新证据如何跨恢复保留，不能仅放宽实时分支或解除旧Conflict。ST08先分别验证现有5秒BUSY预算、已知尚未提交的I/O失败与提交结果不明；不把所有IOERR统称可重试。WAL/FULL不变，不做业务请求重发。

ST14来源PR110，用户2026-10-26选择叠加依赖；PR112 base已改codex/p0-stability-01，93aea521保留父历史。父PR合并后再转main。先确认永久退出仍被监督启动3次（失败提交6d1d597e），再实现存储通知/快退/有限重启与永久Block。每问题先红后改，新增内部时钟接缝仅替换默认System取时入口，不提前改变算法。全仓UTC清单区分控制间隔、同进程寿命、绝对证据/跨重启期限和纯审计；只改证实有风险的位置。

目标26100+/4核8GB、真实SDK/RCS/设备、物理断电、Production安装、24h/72h、604800完整UAC恢复、2592000均不由本包短测授予资格。未执行项NOT_RUN。一个工作包一分支一PR，按实际源/环境登记验证和manifest，不强推、不合并。

交付源3965（产品最后改动0502、3965仅修夹具映像确认），本机8选择器/4 WPF/安装器编译及Linux/Windows冻结CI独立回执见[问题卡](../runtime/validation/time-storage-fault-01.md)。Windows P0步骤通过，但原非CJK快捷方式COM令job仍FAIL，保持草稿；未放宽断言或转记旧PASS。

当前集成：父980a1383的Unicode修复经255ccd4a继承，99a593b1本机8选择器/三ACP/四WPF/安装器编译重新通过；实际CI合并源b3606350的Linux、Windows P0和冻结安装器三job均SUCCESS。旧3965 COM FAIL保留，最终文档/manifest修订不转记这些PASS。完整边界及源码见最新问题卡。
