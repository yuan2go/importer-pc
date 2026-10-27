# CAMERA-SDK-SINGLE-CODE-01

2026-10-27 用户截图纠偏；起点main d0a06313。当前运行介质进程来自bc771150测试构建；PR127统一版本及另一工作树海康加固并行，本分支不覆盖其修改，不改厂商ABI。

## 当前唯一规则

每台相机只有一份RCS相机编码；左右分别填写，取格口和心跳均读取同一映射。复用现有RCS组件的left-bcr-code/right-bcr-code，去除驱动bcr-code及其独立编辑/配置。仍在各自相机页唯一编辑，切换TCP/SDK只重置连接参数，不重置相机业务编码。原已冻结请求保留原报文、绑定和编码，Unknown不自动重发。当前代码不迁移旧配置，不补默认生产编号。

SDK经已安装的精确签名驱动包选择；源码合入或安装MVS客户端不等于驱动包已安装、已选择或已生效。MVS SDK仍按IP/可选序列号定位设备，SDK页不出现端口、TCP探测和静默时间。缺适用SDK包时明确显示未安装和实际安装入口，不增加伪提供者，不把当前TCP表单标成SDK。保存仍走完整候选、Pending/Effective及受控启用。算法缺失时保持仅采图/正式业务阻断。

本条明确替代旧“心跳与取格口编码分别配置”及相关旧测试预期；原设计、SDK缺组件、历史PASS/FAIL和原请求身份保持。修改前规范字节及哈希见docs/archive/pre-camera-sdk-single-code-01/manifest.json，旧原型和截图不倒写。本次用户截图及现场参数只留Git外。

## 验证与交付

执行后登记受影响参数、首配、心跳/取格口共同编码、原请求与SDK原生界面验证。工程验证与真机/真实RCS/现场分开；不把模拟HTTP或原生界面测试记作真机读码。


当前核验：HKLM指定的D盘机级目录没有Production扩展目录及独立信任文件，因而MVS独立包未在当前库存可选。主程序源码合入、MVS客户端安装及TCP旧选择均不等于SDK安装/选择/生效。本轮没有修改已安装bc771150测试介质或现场数据，没有伪造签名/资格使SDK进入Production。

实际改动：TCP/MvCodeReader/MVS当前描述删除独立bcr-code；RCS本位映射在对应相机页唯一编辑，心跳按明确拓扑位置读取同一映射，保留完整候选和冻结请求。相机或RCS延后时编码可暂空，启用后静态/运行校验必填和重复。RCS页只读引用标明左/右并可跳转，相机页缺SDK明确提示及日常安装入口使用既有安装管理工具，首配提示保存草稿退出，未添加虚构提供者或运行回退。

已执行：Sportmaster locked Solution及定向验证构建0警告/0错误；应用SDK编辑、参数共模、心跳实际环回HTTP、延后/Unknown原请求、RCS主链和8种延后矩阵通过。新增同一HTTP编码、倒置Reader数组仍按拓扑匹配，以及Pending更改相机编码不改Unknown原件的断言。原生SDK/缺包/单一编码、首配、延后首配和日常参数窗口通过，含1120×760、820×520及800×420 DIP，实际125%缩放。第一次原生测试因夹具在渲染后才注册事件找不到按钮，按实际页面先注册后设置模型修复后通过，原FAIL日志保留。回执和Git外日志哈希见[runtime/evidence](../runtime/evidence/camera-sdk-single-code-01/verification.json)。

工程参数/操作链范围已闭合；SDK真实图像→条码组件尚缺，真机帧/读码、真实RCS及现场可用保持BLOCKED/NOT_RUN；历史SDK枚举和旧PASS不转记本源码。用户截图、实际SDK与私密日志留D盘Git外。当前运行介质未被静默替换，保存也不会自动启用SDK。


源码提交55ae5867后冻结当前宿主，MVS、MvCodeReader和RCS三个当前独立工程签名包均通过PASS_FROZEN_HOST_EXTENSION_CHECK，宿主及检查器字节未变；读取实际签名包manifest再次确认两个SDK均不再声明bcr-code，RCS保留本位唯一映射。精确来源及哈希见[包回执](../runtime/evidence/camera-sdk-single-code-01/frozen-packages.json)。这些是Git外TestOnly/Simulation工程包，不是Production安装资格，不能直接装入当前Production代替正式SDK部署。本分支未覆盖并行PR127及海康加固工作树。
