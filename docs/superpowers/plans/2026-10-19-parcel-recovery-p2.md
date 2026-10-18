# Parcel恢复P2实施计划

范围：用户授权修复独立复审的S1/Q1与custody测试竞态。基线main baac260214f910dca9ae4628ba6449b25188e2ac，PR26已合并。其他PR与工作树不改，不合并。先提交现行文档收敛，再实施以下顺序；原FAIL不改写。

1. 阅读现行设计/维护与原复审，归档被改入口，明确原件、投影和永久身份边界（本提交）。
2. 在现有Verification加入定向选择器：65条非终结回复后重复ID矛盾，分别外查及RawDurable重放；保持同字节幂等。加入超过64条独立Committed Query的裁剪/重启/候选激活检查。捕获修复前FAIL。
3. 复用OriginalReplyHash，旧格式读取原occurrence。统一离线两条入口提交前校验；HasReceiptOutcomeBasis以持久原件和终结一致性证明，不要求存在于投影。保持SQL/ABI。
4. 可控Health调度建立确定性拒绝和成功窗口；核对无Query/无修订变化、外呼排空后成功和原件保有，保留原测试所有恢复/归档断言。
5. 定向恢复、配置及受影响资源/扩展回归；冻结源码后使用新不可变包身份构建核心、选装和Windows交叉发行物，解压/安装/绑定/激活/维护和旧发布CLI路径按影响验证。Windows真实运行及设备仍NOT_RUN/BLOCKED。
6. 记录源码/产物SHA和原始输出、更新manifest、复核diff，推送新codex分支并建main PR，不合并。

实现文件：ParcelReplyIdentities/ParcelHistory、ParcelLegacyReceiptRecovery、ParcelReceiptCustody；测试沿ArchitectureQuality分部与Program选择器，不建新验收框架。测试数据为新合成原件，不改真实旧库版本伪装历史发布。
