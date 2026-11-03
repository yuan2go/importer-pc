# 代码签名证书接入（P06）

状态：**阻塞，等待公司提供签名资格**。本文写明需要谁提供什么、拿到后按什么顺序签名和核验。仓库里没有证书、私钥、签名服务凭据或正式发布者名称，也不会为了演示而编造。门禁本身见[发布策略](../runtime/industrial-release-policy-01.md)。

## 需要公司提供

| 项目 | 由谁提供 | 说明 |
|---|---|---|
| 正式公司名称（发布者） | 法务/商务 | 用于证书主体和“程序和功能”里的发布者，必须和营业执照一致 |
| 代码签名证书（OV 或 EV） | 公司向公共CA申请 | 私钥只能在 HSM、云签名服务（如 Azure Trusted Signing、CA 托管签名）或受控 USB 令牌中，不能导出成 .pfx 交给开发 |
| 公钥证书 `.cer` | 证书管理员 | 只含公钥，作为门禁的批准签名者输入；轮换时提供新旧两份 |
| 时间戳服务地址 | 随证书由CA提供 | 必须支持 RFC3161 与 SHA-256 |
| 签名机/签名账号 | IT/安全 | 一台受控 Windows 签名机或签名服务账号，安装 Windows SDK 的 SignTool；不放进 PR CI 或普通仓库 secret |
| 下载渠道 | 实施负责人 | SmartScreen 信誉按真实下载渠道取证 |

## 签名顺序

签名必须在字节清单之前完成，签完不再改任何程序集、资源或清单：

1. 在签名机上取得同一 SourceCommit 构建的不可变候选介质（未签名）。
2. 签所有自有 PE：介质内全部 `Ipc.*.exe/dll`，含 helper 和服务；第三方 SDK 保持厂商原签名，不改签。
   `signtool sign /fd SHA256 /tr <时间戳地址> /td SHA256 /sha1 <证书指纹> <文件>`（使用 HSM/云服务时按其 CSP/插件参数）。
3. 重新生成介质哈希和安装库存，构建单 EXE。
4. 签最终单 EXE（同上参数）。
5. 计算最终 EXE 的 SHA-256 和大小，写入发布清单。
6. 在另一台干净 Windows 上，用下载到的同一字节运行门禁：

```powershell
. ./scripts/project-environment.ps1
./scripts/verify-distribution-signature.ps1 -Media D:\release\medium `
  -ApprovedSigner D:\release\signer.cer -Installer D:\release\Setup.exe `
  -SignTool "C:\Program Files (x86)\Windows Kits\10\bin\<版本>\x64\signtool.exe"
```

门禁通过后，`check-commercial-delivery.py --level production --approved-signer ... --installer ...` 才不再返回 BLOCKED。

## 证书轮换

- 新证书生效前，批准签名者同时给新旧两份 `.cer`；旧版本安装包仍按旧证书核验。
- 新版本全部改用新证书签名后，从批准集合移除旧证书。任何时候都不在代码里写死证书指纹。
- 证书吊销或私钥泄露时，立即从批准集合移除，并按发布策略撤回受影响版本（在新版本说明的 `Revoked` 里列出，见[实施手册·新版本提示](../operator/implementation-guide.md)）。

## 仍然阻塞或未运行

- 正式证书、发布者名称、签名机：未提供。
- 真实签名、SignTool 全链与时间戳核验、最终下载字节核验：NOT_RUN。
- 吊销在线/离线矩阵、SmartScreen 信誉：NOT_RUN；签名不保证永不被拦截。
- 当前构建工具如实报告 `NOT_SIGNED`，未签名候选不作为正式交付。
