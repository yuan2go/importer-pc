# IE01正式交付签名门禁

2026-11-03，当前为工程门禁增量；没有取得正式证书、发布者资料或签名服务授权，没有签署/发布正式安装包。

## 检查范围

`scripts/verify-distribution-signature.ps1`现在递归枚举完整介质中的所有`Ipc.*.exe/dll`，明确包含helper/服务及嵌套目录，并要求提供最终单EXE；拒绝重解析路径、缺少入口、不完整介质、未签名/未批准签名者及缺时间戳。批准者来自外部公有`.cer`，以证书SHA256身份核对，不在仓库硬编码公司或证书，不接收私钥。

每个实际文件保持只读共享句柄，验证期间禁止替换/写入。PE证书表有边界、数量、对齐和嵌套深度预算；CMS主签名/嵌套自有签名须SHA256，自有签名者须属于批准集合。RFC3161的消息摘要和TSA签名摘要均要求SHA256或更强（SHA384/512），直接解析TSTInfo，不能用较强的TSA签名掩盖SHA1消息摘要。Windows Authenticode与SDK SignTool随后负责密码学/信任策略验证，使用`verify /pa /all /v /tw`，非零退出（含警告）均阻断。最后对同一实际文件对象计算SHA256与大小，输出逐文件清单。

依据：[Microsoft SignTool参数与退出码](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)、[Windows PE证书表](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format)、[.NET PEHeader](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.portableexecutable.peheader)、[PowerShell Authenticode](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/get-authenticodesignature)。覆盖[微软RFC3161计数签名OID](https://learn.microsoft.com/en-us/windows/win32/api/wincrypt/ns-wincrypt-crypt_algorithm_identifier)与标准CMS时间戳属性；TSTInfo通过[.NET解析器](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.pkcs.rfc3161timestamptokeninfo)读取。自有脚本的元数据策略不替代Windows签名验证。

## 独立资格

- `PASS`只表示列出的实际字节通过该次签名门禁；不授予投产批准。
- 输出明确保留吊销在线/离线矩阵NOT_RUN；Windows当次信任策略结果不能冒充独立完成吊销矩阵。
- SmartScreen下载信誉NOT_RUN，不能承诺“有签名就永不拦截”。
- 签名工具不证明单EXE载荷属于哪个SourceCommit。正式交付新增必需`installer-bytes`证据项：最终签名后的下载SHA256及载荷/冻结介质绑定。仅签名通过不能代替该项，不能拿另一个同公司签名的旧安装器放行。
- 需要公司提供的资料、签名顺序和证书轮换见[代码签名证书接入](../design/code-signing-onboarding-06.md)。
- ARP正式Publisher及完整签名流水线尚待真实授权资料、签名阶段的同源字节闭环与最终安装包验证；当前包工具继续如实报告NOT_SIGNED，未加入任何发布secret。

## 运行方式

先加载项目D盘环境。全部输入为实际获批准产物/公有证书；下列占位目录不能当作已提供资料：

```powershell
. ./scripts/project-environment.ps1
./scripts/verify-distribution-signature.ps1 -Media D:\reviewed\medium `
  -ApprovedSigner D:\reviewed\public-signer.cer `
  -Installer D:\reviewed\final-installer.exe `
  -SignTool D:\tools\WindowsSDK\signtool.exe
```

`check-commercial-delivery.py --level production`现在同时要求`--approved-signer`和`--installer`；没有实际证书/最终EXE时返回BLOCKED，不能用介质中的手填PASS替代。公有证书轮换由外部批准集合控制，原证书不能在无批准情况下继续接受。

## 本轮验证

- PowerShell解析检查通过；`python -m unittest discover -s scripts -p test_commercial_delivery.py`5项通过，覆盖缺证书、缺最终EXE及证据边界。
- `scripts/test-signature-policy.ps1`14项策略边界通过：内存临时CMS的SHA256/未批准者/SHA1/嵌套深度/损坏结构、微软RFC3161属性/TSTInfo消息摘要与TSA签名摘要分别核验，以及基于实际已构建PE布局的证书表/类型/越界检查。临时密钥不导入证书库、磁盘或Git；合成CMS不是可信产品签名，不进入正式交付证据。
- 同一策略单测已接入无发布secret的PR CI。[CI41513895922](https://github.com/yuan2go/importer-pc/actions/runs/41513895922)在`5ff10416ace8437dd1c4f3a6a4cdee174b997b0c`通过14项策略及5项Python测试，同时8组核心回归、三套构建/格式/文档门禁通过；仍只证明对应工程边界。
- 实际正式签名、SignTool全链/时间戳、证书轮换、离线吊销、最终下载及SmartScreen矩阵均NOT_RUN。缺条件不改成模拟PASS。
