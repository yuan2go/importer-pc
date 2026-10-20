/* node source/render_screens.cjs [--first] — requires Playwright, no network. */
const fs=require('fs'),path=require('path');
const {pathToFileURL}=require('url');
const {chromium}=require(process.env.CODEX_PRIMARY_RUNTIME_NODE_MODULES?process.env.CODEX_PRIMARY_RUNTIME_NODE_MODULES+'/playwright':'playwright');
const root=path.resolve(__dirname,'..');
const specs=[
 ['01-left-camera','left','左相机：对象、连接与编码'],['02-system','system','系统：供包台身份'],['03-business-request','request','业务模块：取格口请求'],['04-rcs-component','rcs','组件：共享 RCS 连接'],
 ['05-validation-error','invalid','字段错误：保留其他输入'],['06-save-review','review','统一保存审阅'],['07-saved-pending','pending','已保存：运行版本未变化'],['08-connection-failed','connection','连接检查：分层结果'],
 ['09-automatic-backup','backup','自动备份与完整备份'],['10-template-preview','template','安全模板导入预览'],['11-search','search','设置搜索与对象定位'],['12-unsaved-close','leave','关闭前处理草稿'],
 ['13-save-unknown','unknown','保存结果待确认'],['14-camera-advanced','advanced','识别、编码与缓冲'],['15-barcode-filter','filter','条码筛选与本地样例'],['16-work-rcs-fault','work-fault','作业：RCS 异常影响范围'],
 ['17-work-normal','work','作业：相机、扫描、任务三组'],['18-data-records','data','数据位置与记录'],['19-processing','processing','处理参数'],['20-display','display','本机显示偏好'],
 ['21-driver-unavailable','missing','驱动不可用：保留原参数'],['22-legacy-conflict','legacy','检测到旧设置冲突'],['23-saving','saving','正在保存与核对'],['24-save-failed','save-failed','已确认未保存：保留输入']
].map(([file,scene,title])=>({file:'screens/'+file+'.png',scene,title,width:1600,height:900,scale:2}));
specs.push({file:'screens/25-1366x768.png',scene:'left',title:'布局：1366×768 逻辑像素',width:1366,height:768,scale:2});
specs.push({file:'screens/26-1092x614.png',scene:'left',title:'布局：1092×614 DIP；对应约1366×768 /125%可用空间',width:1092,height:614,scale:2});
specs.push({file:'screens/27-800x420.png',scene:'left',title:'布局：800×420 最小工作区',width:800,height:420,scale:2});
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.PROTOTYPE_CHROME||undefined,args:['--no-sandbox']});fs.mkdirSync(path.join(root,'screens'),{recursive:true});
 const errors=[],requested=[];
 const selected=process.argv.includes('--first')?specs.slice(0,4):specs;
 for(const spec of selected){
  const page=await browser.newPage({viewport:{width:spec.width,height:spec.height},deviceScaleFactor:spec.scale,reducedMotion:'reduce'});
  page.on('pageerror',e=>errors.push({scene:spec.scene,error:e.message}));
  page.on('request',r=>{if(/^https?:/.test(r.url()))requested.push(r.url());});
  await page.goto(pathToFileURL(path.join(root,'Prototype.html')).href+'?scene='+spec.scene);
  await page.evaluate(()=>document.fonts.ready);
  await page.screenshot({path:path.join(root,spec.file),fullPage:false});
  spec.geometry=await page.evaluate(()=>({documentOverflow:document.documentElement.scrollWidth>innerWidth,bodyOverflow:document.body.scrollWidth>innerWidth,mainScrollHeight:document.querySelector('.main-scroll')?.scrollHeight||null,mainClientHeight:document.querySelector('.main-scroll')?.clientHeight||null}));
  await page.close();
 }
 await browser.close();
 if(errors.length||requested.length)throw Error(JSON.stringify({errors,unexpectedNetworkRequests:requested}));
 if(!process.argv.includes('--first')){
  fs.writeFileSync(path.join(root,'screens.json'),JSON.stringify({kind:'synthetic-browser-prototype',nativeWindows:false,baseLogicalViewport:[1600,900],pixelExport:'2×',screens:specs},null,2)+'\n');
  const gallery=`<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>参数设置 V3 · 高清界面</title><style>body{font:16px 'Microsoft YaHei UI',sans-serif;color:#20324b;background:#eef2f7;margin:0 auto;padding:32px;max-width:1500px}h1{font-size:26px}p{color:#5b6c82}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(360px,1fr));gap:26px}a{color:#1758aa;text-decoration:none}img{width:100%;display:block;border:1px solid #c7d2e1}figure{margin:0}figcaption{margin:10px 0 0;line-height:1.6}small{color:#5d6b80}</style><h1>参数设置 V3 · 高清界面</h1><p>24 个界面状态 + 3 个尺寸检查；全部为模拟数据。点击逐张查看原始 2× PNG。浏览器图不代表 Windows 原生验收。</p><p><a href="Prototype.html">打开离线交互原型</a></p><div class="grid">${specs.map(s=>`<figure><a href="${s.file}"><img src="${s.file}" alt="${s.title}" loading="lazy"><figcaption>${s.title}<br><small>${s.width*2}×${s.height*2} PNG</small></figcaption></a></figure>`).join('')}</div></html>`;
  fs.writeFileSync(path.join(root,'Gallery.html'),gallery);
 }
 console.log(JSON.stringify({screens:selected.length,pageErrors:errors.length,unexpectedNetworkRequests:requested.length}));
})().catch(e=>{console.error(e);process.exit(1)});
