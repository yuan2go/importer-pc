# SM-UNREAD-PACKAGE-27 验证

冻结产品及测试源：`c8dff2e7d92e381220167ca7002cc142cb331b8c`，基线 main `90ed7c859bef2e2df8c4f3df7a6a550c038a826d`。后续本目录、说明、属性和manifest提交只整理证据，不把历史验证转记为新的产品源。

三个Release构建均为0警告0错误。9套Application、5套原生WPF共325项PASS；发布和真实EXE `--environment-report`均退出0，产品信息版本含完整c8dff2e7来源。[逐套回执](summary.json)与原始日志、两张合成原生截图同目录保留。

| 范围 | 套件 | PASS |
|---|---|---:|
| 新增识别/无码闭环 | unread-package-27 | 12 |
| 链路/背压/帧拒收与原请求 | camera-chain-reliability、camera-chain-closure | 57 |
| 来源/码制/输入终态 | reader-feedback-22、reader-ingress-terminal-22 | 71 |
| 实际引擎、原图与关闭 | mvs-software-flow、sm-original-frame、shutdown-resources、camera-rounds | 103 |
| 新增原生无码、迟到、人工、重连 | unread-package-27-ui | 9 |
| 原生输入、主码、反馈与布局回归 | camera-chain-reliability-ui、camera-barcode-current、station-responsive-ui、reader-feedback-22-ui | 73 |

新闭环覆盖空主搜索后实际原生补充识别、暗码/完整多码/格式范围/原像素、空帧不建请求、旧帧/旧epoch/未来时间/矛盾元数据拒收、迟到回应、同码不同请求与原quantity、单帧拒收、SDK归还及反馈解除订阅。原生标准1366×768和紧凑800×420 DIP均检查提示可读，验证无码清退条码/任务/格口/图片、另一位不变、旧快照不复活、人工来源独立、新epoch需新读码。图片为Windows DPI125%的真实WPF合成夹具截图。

首次原生新增夹具误写人工提交方法名导致编译失败，原日志保留为 `initial-fixture-build-failure.log`；修正夹具API名称后才冻结产品与测试源。没有删除断言、降低持久/权限守卫或吞掉运行异常换PASS。

私有单张现场照片的旧源默认搜索重放没有有效候选。新源原图/75%/50%、全部码制/Code128各三次共18次，另13个旋转/亮度/3072×2048 Mono8布局变化均读出同一有效码并保留原像素。这31个离线用例来自同一照片，不能当作31件实物或现场成功率。另以该照片的3072×2048布局输入合成MVS API、实际驱动/识别/Application/SQLite/内置RCS回环：3帧形成3个独立请求、3次借帧全部归还。原图、变体、码值、请求体、账本与私有夹具失败/修正记录仅留D盘Git外，不进入本目录。

复验先切到指定产品源的干净D盘工作树，再执行 `. ./scripts/project-environment.ps1` 和 `./docs/runtime/evidence/sm-unread-package-27/verify.ps1 -Workspace <D盘工作树> -Evidence <D盘新证据目录>`。脚本包括源码清洁检查、三个构建、全部14套件、发布和EXE启动；脚本单独从本目录复制到指定源运行亦可。[制品哈希](files.json)校验公开原始字节，不包含二进制、SDK、数据库或现场照片。

实际引擎、真实SQLite/HTTP边界与合成SDK、回环RCS分开。物理MVS实拍/运动速度、曝光/焦距、实物扫码枪、客户RCS合同/去重/计件、目标IPC及现场长稳均NOT_RUN；没有安装或启动新现场版本、修改真实配置、控制真实设备或启用GitHub CI。软件侧已修复和照片回放通过，不声明实物过机或现场接受通过。
