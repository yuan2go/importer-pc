# Application架构22证据

基线ea881293c888a824f26c9f9fa07352a22786206b；运行源码8981d54a3dcb45aa08ea8953f1dd9d4cd2f079f7。仅合成参考验证，无生产设备调用。Windows实际NOT_RUN，真实SDK/协议/现场BLOCKED。

[检查索引](checks.json) · [冻结输入](frozen-source.json) · [SQL原字节](migration-custody.json) · [四候选](candidates.json) · [新扩展身份](extension-identity.json) · [Debug检查宿主](debug-checker.json)。日志位于logs/；初始失败独立保留，不当作最终PASS。原D/RV及历史包不改。

signed-extension-build为独立TestOnly签名包1.4.0；检查宿主独立冻结目录，使用与验证宿主逐字节相同的公共DLL。它与Release候选精确指纹不同，不声称同一个包可以跨指纹安装。签名私钥和实际包均未入Git；新包本地保留，原版本不覆盖。

smoke-sportmaster证明解压后的两个reader真实执行；smoke-wcs-before/smoke-wcs-attached使用独立HTTP接收进程，核对原请求、单次执行、原图片bytes hash和Unknown查询。候选ZIP本地路径见结果报告，运行方式见包内APPLICATIONS.md。
