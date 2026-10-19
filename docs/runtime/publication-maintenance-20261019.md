# 维护P2与SID既有成果不测试发布

用户本轮要求提交推送代码、报告、文档及刚刚任务执行产物，并合并所有开放PR。本发布流程的测试、构建、打包、签名、启动及仓库验证器均为 **NOT_RUN_BY_USER_REQUEST**，Git hooks禁用。原客户资料、私密配置、运行lease及本地重复文档保持原样，无关工作树未改。

## 输入、合并与清单

开工远端main `be7956447054054dfdcfce725e8fbbbdfc87407a`；PR41原head `e274b5f9ca3ad645c4071eef480a3fe00c4b4bcb`，PR42原head `92834054fb583a3393b573d8a20d6e1a6f676341`。PR41已按精确head合并为`9f4b32f319d3a66d993cfbafcdda90656e7fd687`。PR42再纳入最新main，以原PR追加正常整合提交，不强推、不关闭或替代原PR。实际最终merge及数量随后由Git/GitHub记录。

两边功能文件无重叠。SID的脚本、装配/CLI、MachineRuntimePolicy及ParcelRuntime入口保持PR41原字节；维护草稿/关闭/双参数表/list修复、版本和相应用例保持PR42原字节。只有manifest JSON及摘要冲突，按当前实际文件合并双方清单，不删除历史证据；INDEX和Windows指南自动合入双方正文。当前README/INDEX/框架版本同步1.3.3，Windows当前操作说明明确组合媒介尚未生成。全部功能路径及原Git blob见[静态整合裁决](evidence/maintenance-publication-20261019/merge-resolution.json)，原PR42说明另存，不运行代码来取得这些身份。

[SID原报告](runtime-identity-preflight-01.md)及[维护P2原报告](ux-maintenance-p2-04-results.md)保留原源码、形成时测试及Windows后验；原D/RV、Unknown/原件守卫、SQL1～7、原失败和历史267项资产错误不倒写。没有新业务规则或新UI设计。

## 两组原字节执行产物

- [维护1.3.3参考发布](https://github.com/yuan2go/importer-pc/releases/tag/ux-reference-ff31b0e)：既有15个附件，固定源码`ff31b0efaaebd7e5fae4254c7184e0640e2eea81`，含维护P2，不含SID前置修复。原Windows ZIP与精确扩展不重签或覆盖。
- [SID portable参考发布](https://github.com/yuan2go/importer-pc/releases/tag/runtime-sid-reference-f354d3f)：从原本地现成归档上传，固定源码`f354d3ff8d8a46d3eaca02db61877be1fd506785`；ZIP SHA256为`39c4373a4bdf655b982283342e4a961bddf440b7dbdb9f7def0c2a38d9fe3048`。附SID-ARTIFACT与SHA256SUMS，不重建、不重打包。仅Sportmaster portable开发参考，非Windows门禁验收；不含维护P2。原报告“仅本地”保留其形成时间，新上传由本次用户授权。

**当前main包含两组源码，但本轮未产生同时包含二者的新媒介。** 不能把上述任一旧包、Windows ZIP或扩展标为已包含全部合并修复；下一次发行须固定新的组合源码、生成全新可信媒介并核对精确扩展依赖及指南。当前合并后的运行/打包/组合状态 **NOT_RUN**。

Windows令牌/SID/ACL/UAC及WPF原生交互、普通账户安装更新、目标负载仍 **NOT_RUN**；真实SDK/正式协议/权威Unknown/设备/现场 **BLOCKED**，Production **NOT_READY**。非WindowsPortableDevelopmentOnly/WindowsPolicyChecked=false不表示Windows已验；参考发布和Git合并不授予商用或现场资格。
