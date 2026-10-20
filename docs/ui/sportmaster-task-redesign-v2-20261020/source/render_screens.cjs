/* Render the offline prototype, using Playwright and an installed Chromium.
 * PROTOTYPE_BROWSER may point to a local Chromium executable.
 * No product device, service, Windows input or performance test is performed.
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const {pathToFileURL} = require('url');
const {chromium} = require('playwright');
const dir = path.resolve(__dirname, '..');
const scenes = JSON.parse(fs.readFileSync(path.join(__dirname,'scenes.json'),'utf8'));
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');

(async()=>{
  fs.mkdirSync(path.join(dir,'screens'),{recursive:true});
  fs.mkdirSync(path.join(dir,'qa'),{recursive:true});
  const browser = await chromium.launch({headless:true, ...(process.env.PROTOTYPE_BROWSER ? {executablePath:process.env.PROTOTYPE_BROWSER} : {}),args:['--no-sandbox','--lang=zh-CN']});
  const context = await browser.newContext({viewport:{width:1600,height:900},deviceScaleFactor:2,locale:'zh-CN',reducedMotion:'reduce'});
  const page = await context.newPage(), errors=[], external=[];
  page.on('pageerror',error=>errors.push(error.message));
  page.on('request',request=>{if(/^https?:/.test(request.url()))external.push(request.url());});
  await page.route(/^https?:\/\//,route=>route.abort());
  await page.goto(pathToFileURL(path.join(dir,'Prototype.html')).href);
  await page.evaluate(()=>document.fonts.ready);
  const cases = scenes.map(([id,title,description])=>({id,scene:id,title,description,width:1600,height:900,kind:'named-scene'}));
  cases.push(
    {id:'work-1366x768',scene:'work-normal',title:'紧凑显示 · 1366 × 768',description:'逻辑视口检查，不能代替实际 Windows DPI 验收。',width:1366,height:768,kind:'viewport'},
    {id:'work-long-1280x720',scene:'work-long',title:'长字段 · 1280 × 720',description:'约对应 1920 × 1080、150% 缩放的逻辑空间，非 Windows 实测。',width:1280,height:720,kind:'viewport'},
    {id:'work-minimum-800x420',scene:'work-normal',title:'最小逻辑窗口 · 800 × 420',description:'保留三组核心信息和本次操作，较低空间采用紧凑显示。',width:800,height:420,kind:'viewport'},
    {id:'display-protection-799x419',scene:'work-normal',title:'窗口不足 · 799 × 419 保护',description:'不支持的窗口显示保护提示；生产保留既有恢复确认。',width:799,height:419,kind:'viewport'},
    {id:'driver-invalid-1280x720',scene:'driver-invalid',title:'紧凑参数页 · 字段错误',description:'对象、类别及保存固定可见，滚动仅发生在当前表单内。',width:1280,height:720,kind:'viewport'},
    {id:'components-lower',scene:'components',title:'组件配置 · 下方参数与引用',description:'同一 RCS 页面向下滚动，仍显示对象头、分类与保存范围。',width:1600,height:900,kind:'scroll-detail',scroll:'.config-content'},
    {id:'system-backup-lower',scene:'system-records',title:'系统配置 · 下方计划备份',description:'同一系统页面向下滚动，完整显示计划开关、目录与周期。',width:1600,height:900,kind:'scroll-detail',scroll:'.config-content'}
  );
  // Preserve published image numbers when new named states are appended.
  const previousFile=path.join(dir,'screens.json');
  const previous=fs.existsSync(previousFile)?JSON.parse(fs.readFileSync(previousFile,'utf8')):[];
  const order=new Map(previous.map((entry,index)=>[entry.id,index]));
  cases.sort((a,b)=>(order.get(a.id)??10000)-(order.get(b.id)??10000));
  const output=[];
  for(let i=0;i<cases.length;i++){
    const entry=cases[i];
    await page.setViewportSize({width:entry.width,height:entry.height});
    await page.evaluate(scene=>setScene(scene),entry.scene);
    await page.evaluate(()=>document.fonts.ready);
    if(entry.scroll)await page.evaluate(selector=>{const node=document.querySelector(selector);node.scrollTop=node.scrollHeight;},entry.scroll);
    const geometry=await page.evaluate(()=>{
      const bounds=selector=>{const node=document.querySelector(selector);if(!node)return null;const rect=node.getBoundingClientRect();return {top:rect.top,bottom:rect.bottom,left:rect.left,right:rect.right,scrollHeight:node.scrollHeight,clientHeight:node.clientHeight,display:getComputedStyle(node).display};};
      return {bodyWidth:document.body.scrollWidth,workspace:bounds('.workspace'),left:bounds('#lane-left'),right:bounds('#lane-right'),saveBar:bounds('.save-bar'),configHeader:bounds('.object-header'),setupFooter:bounds('.setup-footer'),protection:bounds('.protect'),textOverflow:[...document.querySelectorAll('.chute-code,.scan-code,.task-item dd')].filter(n=>n.scrollWidth>n.clientWidth+2).map(n=>n.textContent)};
    });
    const file=`screens/${String(i+1).padStart(2,'0')}-${entry.id}.png`;
    await page.screenshot({path:path.join(dir,file),animations:'disabled'});
    output.push({...entry,file,pixelWidth:entry.width*2,pixelHeight:entry.height*2,sha256:hash(path.join(dir,file)),geometry});
  }
  await browser.close();
  const issues=output.flatMap(item=>[
    ...(item.geometry.bodyWidth>item.width?['horizontal page overflow']:[]),
    ...(item.geometry.textOverflow.length?['clipped task text']:[]),
    ...(item.geometry.left&&item.geometry.protection.display==='none'&&item.geometry.left.bottom>item.geometry.workspace.bottom+2?['current lane extends below workspace']:[])
  ].map(issue=>({id:item.id,issue})));
  const report={scope:'Offline browser render only; no Windows, hardware, RCS or performance acceptance',prototypeSha256:hash(path.join(dir,'Prototype.html')),locale:'zh-CN',scaleFactor:2,generatedAt:new Date().toISOString(),namedScenes:scenes.length,pngCount:output.length,errors,externalRequests:external,issues,screens:output};
  fs.writeFileSync(path.join(dir,'screens.json'),JSON.stringify(output,null,2)+'\n');
  fs.writeFileSync(path.join(dir,'qa/render-results.json'),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify({namedScenes:scenes.length,pngCount:output.length,errors,externalRequests:external.length,issues},null,2));
  if(errors.length||external.length||issues.length)process.exitCode=1;
})().catch(error=>{console.error(error);process.exitCode=1;});
