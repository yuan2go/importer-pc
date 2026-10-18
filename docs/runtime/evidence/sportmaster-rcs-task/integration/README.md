# RCS最终main整合验证

整合main `baac260214f910dca9ae4628ba6449b25188e2ac`（PR26/28）。组件最终新不可变版本1.0.1，公共契约1.7.0，Parcel SQL6；main SQL1-5原样，原件核对/独立接收资料责任及原旧ABI恢复路径继承。

[定向与回归结果](checks.json)：RCS、AA22、AQ、review-closure、receipt-custody、configuration-confirmation均PASS；构建0警告/错误。Python工具26+10项PASS。新增SQL5->6原件/回执/hash保有、SQL6不降级及重复原Query复用、NEED_UNPACK被污染后恢复仍保持本位阻断。诊断失败和夹具修正原因保留，未删除原断言、未改旧业务PASS边界。

此前父目录4c9d51c/1.0.0候选只表示整合前原身份。此目录为整合候选的验证与交付入口；最终媒介以固定源码和实际delivery身份读取。Windows实际NOT_RUN，真实RCS/SDK/设备/现场BLOCKED，不关闭历史D/RV或旧HTML问题。

最终固定源码 **9050bdd53e7f60882a9418c1c04e04307a1b8f1d**。实际签名扩展安装/装配/三种回包及未知重启不重发PASS；解压portable进程启动/双位原链/重启保有/Stop PASS；实际CLI安装、配置候选、部署检查与防覆盖PASS。Windows候选逐文件hash及app/desktop与portable精确契约相同，仅为交叉发布/档案校验，不是Windows运行验收。详见[交付身份](delivery.json)、[签名包身份](sportmaster-rcs-task-1.0.1.ipcx.identity.json)及同目录日志。

源main baac260的全量资料检查为268项历史问题：265项PPTX媒体类型、2项来源元数据、1项manifest checksum。当前清单重建修复checksum；历史资产不改，新材料使用相对路径公开投影，原绝对路径JSON只在Git外按SHA保管，验证器规则未放松。当前267项历史资产问题继续FAIL，不宣称全量资料通过。

合并时main原始initial-probes/original-output.log的一处尾空白原字节保留；只对本分支相对整合main的修改执行diff检查。旧日志不为满足格式检查被重写。

已创建并读回[main PR #30](https://github.com/yuan2go/importer-pc/pull/30)，OPEN/CLEAN/MERGEABLE，未合并。[公开回执](publication.json)绑定创建时head与固定运行源码；后续回执提交不改变9050bdd运行/包身份。
