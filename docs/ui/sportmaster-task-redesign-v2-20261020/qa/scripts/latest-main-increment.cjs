/* Necessary offline checks for the ba80d0ea design increment only.
 * No .NET Regex, WPF/Windows, device, network or production acceptance is implied.
 * UI_PROTOTYPE and UI_QA_OUTPUT may point to review paths; PROTOTYPE_BROWSER selects Chromium.
 */
const fs=require('fs'),path=require('path'),crypto=require('crypto'),assert=require('assert/strict');
const {pathToFileURL}=require('url');
const {chromium}=require('playwright');
const source=process.env.UI_PROTOTYPE||path.resolve(__dirname,'../..','Prototype.html');
const output=process.env.UI_QA_OUTPUT||path.resolve(__dirname,'../latest-main-increment.json');
const report={source,sourceCommit:'ba80d0ea90d86b24f9d9eebd20d075da34320972',prototypeSha256:crypto.createHash('sha256').update(fs.readFileSync(source)).digest('hex'),scope:'Only affected offline prototype tasks; no Windows, WPF, .NET regex, production transport, device or performance test',startedAt:new Date().toISOString(),checks:[],errors:[],externalRequests:[]};
const click=(p,a)=>p.locator(`[data-action="${a}"]`).first().click();
const field=(p,f)=>p.locator(`[data-field="${f}"]`).first();
const state=p=>p.evaluate(()=>getPrototypeState());
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.PROTOTYPE_BROWSER,args:['--no-sandbox']});report.browser=browser.version();
 async function check(id,title,scene,body){if(process.env.UI_QA_CASES&&!process.env.UI_QA_CASES.split(',').includes(id))return;const c=await browser.newContext({viewport:{width:1600,height:900},locale:'zh-CN'}),p=await c.newPage();p.setDefaultTimeout(7000);const r={id,title,status:'PASS',assertions:[]};report.checks.push(r);
 p.on('pageerror',e=>report.errors.push({id,message:e.message}));await p.route(/^https?:/,route=>{report.externalRequests.push({id,url:route.request().url()});return route.abort();});
 const ok=(v,m)=>{r.assertions.push({pass:!!v,message:m});assert.ok(v,m);};
 try{await p.goto(pathToFileURL(source).href+'?scene='+scene);await p.evaluate(()=>document.fonts.ready);await body(p,ok,r);}catch(e){r.status='FAIL';r.error=e.stack;await p.screenshot({path:path.join(path.dirname(output),id+'-failure.png')});}finally{await c.close();}}
 await check('V2-L01','指定左实例：输入错误、定位、修正、保存与待生效','driver-left',async(p,ok)=>{
  const before=await state(p);ok(await p.locator('.tabs button').count()===4,'四个参数类别明确可见');
  await field(p,'draft.left.ip').fill('192.0.2.999');await field(p,'draft.left.bcrCode').fill('DEST-LEFT-EXAMPLE');await click(p,'validate-config');await p.waitForFunction(()=>S.phase==='invalid');
  ok(await field(p,'draft.left.ip').getAttribute('aria-invalid')==='true','错误绑定左实例IP，不丢其他输入');ok((await state(p)).draft.left.bcrCode==='DEST-LEFT-EXAMPLE','同时编辑的取格口编码保留');
  ok((await state(p)).draft.right.ip===before.draft.right.ip,'右实例IP没有连带改变');
  await field(p,'draft.left.ip').fill('192.0.2.31');await click(p,'save-review');await p.waitForSelector('[role="dialog"]');
  await field(p,'reason').fill('本次离线验证：核对左实例地址与取格口编码');await click(p,'save-confirm');await p.waitForFunction(()=>S.phase==='pending');
  const after=await state(p);ok(after.saved.left.ip==='192.0.2.31'&&after.runtime.left.ip===before.runtime.left.ip,'保存候选与实际运行分别保留');
  ok(after.saved.left.heartbeatBcrCode===before.saved.left.heartbeatBcrCode,'修改取格口编码不覆盖心跳编码');
  ok((await p.locator('#config-state').innerText()).includes('等待重启生效')&&after.paused,'保存后明确待生效，接收仍暂停');
  await p.evaluate(()=>setScene('config-save-unknown'));const scope=await p.locator('.save-scope').innerText();ok(scope.includes('修改待核对')&&!scope.includes('未保存修改'),'未知写入结果不误称为已知未保存');
 });
 await check('V2-L02','本地条码样例：唯一匹配、旧结果过期、重复条目与清空校验','business-barcode',async(p,ok)=>{
  await click(p,'test-barcode-filter');let s=await state(p);ok(s.filterTest.selectedCode==='BC0003'&&s.filterTest.simulated,'预置模拟样例得到唯一选中码，保留模拟标记');
  await field(p,'filterExample').fill('BC0003\nBC0003');s=await state(p);ok(s.filterTest.stale,'修改样例后旧结果失效');
  await click(p,'test-barcode-filter');s=await state(p);ok(s.filterTest.matches.length===2&&s.filterTest.selectedCode===null,'重复两个条目不去重为一个有效码');
  await field(p,'draft.business.barcodePattern').fill('[');await click(p,'validate-config');await p.waitForFunction(()=>S.phase==='invalid');ok(await field(p,'draft.business.barcodePattern').getAttribute('aria-invalid')==='true','预置语法错误定位到规则字段');
  ok((await state(p)).filterExample==='BC0003\nBC0003','规则错误保留全部本地样例');
  await click(p,'barcode-preset');await click(p,'save-review');await p.waitForSelector('[role="dialog"]');await field(p,'reason').fill('本次离线样例规则修改');await click(p,'save-confirm');await p.waitForFunction(()=>S.phase==='pending');
  ok((await state(p)).saved.business.barcodePattern==='^BC[0-9]+$'&&(await state(p)).runtime.business.barcodePattern==='','新规则保存后仍不冒充运行规则');
  await field(p,'draft.business.barcodePattern').fill('');await click(p,'validate-config');await p.waitForFunction(()=>S.phase==='invalid');ok(!!(await state(p)).errors['draft.business.barcodePattern'],'已有策略清空报错，不当成关闭');
 });
 await check('V2-L03','筛选拒绝的当前结果、原始候选详情及另一位独立','work-filter-multiple',async(p,ok)=>{
  let s=await state(p);ok(s.left.task===null&&s.left.frozenRequest===null&&s.left.status==='filtered','筛选拒绝没有本次RCS请求与成功任务');
  ok((await p.locator('#lane-left').innerText()).includes('本次未请求 RCS'),'左位明确说明未请求，原因与RCS拒绝分开');
  ok((await p.locator('#lane-right [data-task="taskId"]').innerText())===s.right.task.taskId,'右位有效任务继续明确显示');
  await click(p,'input-details:left');const dialog=await p.locator('[role="dialog"]').innerText();ok(dialog.includes('BC0003')&&dialog.includes('BC0004')&&dialog.includes('demo-BC-rule-v1'),'详情含原顺序候选与规则指纹');
  ok(!(await state(p)).left.frozenRequest,'只读详情不会构造请求');await click(p,'dialog-close');
  await p.evaluate(()=>setScene('work-filter-none'));ok((await p.locator('#lane-left .chute-code').innerText())==='本次未取得','零匹配不把上一包格口带回当前区');
  await p.evaluate(()=>setScene('work-stale'));ok((await p.locator('#runtime-footer').innerText()).includes('运行配置待确认'),'快照失效时持续状态栏不冒称当前运行已确认');
  await p.evaluate(()=>setScene('work-filter-selected'));s=await state(p);ok(s.left.scanEvidence.candidates.length===2&&s.left.scanEvidence.selectedCode===s.left.barcode&&s.left.task.taskId,'多候选可经唯一选中后取得本次任务');
 });
 await check('V2-L04','系统/组件心跳归属、间隔校验与首配独立生命周期','system-station',async(p,ok)=>{
  const before=await state(p);await field(p,'draft.system.stationCode').fill('DEST-STATION-EXAMPLE');ok((await state(p)).draft.system.heartbeatStationCode===before.draft.system.heartbeatStationCode,'取格口站码与心跳工位编码分离');
  await click(p,'category:components');await field(p,'draft.rcs.heartbeatInterval').fill('0');await click(p,'validate-config');await p.waitForFunction(()=>S.phase==='invalid');ok(!!(await state(p)).errors['draft.rcs.heartbeatInterval'],'心跳间隔错误定位在唯一组件');
  ok((await state(p)).draft.system.stationCode==='DEST-STATION-EXAMPLE','跨类别校验保留关联编辑');
  await p.evaluate(()=>setScene('setup-system'));ok(await p.locator('.sidebar').count()===0,'首配没有日常侧栏');ok(await field(p,'draft.system.heartbeatStationCode').count()===1,'首配复用明确的心跳工位字段');
  await p.evaluate(()=>setScene('work-normal'));ok(!(await p.locator('.sidebar').innerText()).includes('首次配置'),'常规导航没有首次配置入口');
 });
 await check('V2-L05','心跳证据、只读连接检查与原件责任分开','diagnostics-outage',async(p,ok)=>{
  const before=await state(p);const text=await p.locator('.workspace').innerText();ok(text.includes('心跳')&&text.includes('HTTP'),'诊断显示明确的心跳HTTP层');
  await click(p,'aux-diag-check');await p.waitForFunction(()=>!S.aux.diagnosticBusy.rcs);let after=await state(p);
  ok(JSON.stringify(after.rcsHeartbeat)===JSON.stringify(before.rcsHeartbeat),'读取连接不会新造心跳回执');ok(after.left.status==='unknown'&&after.left.requestId===before.left.requestId,'检查不清偿未知原件');
  await p.locator('summary').filter({hasText:'原型故障注入'}).click();await click(p,'aux-demo-repair');await click(p,'aux-diag-check');await p.waitForFunction(()=>!S.aux.diagnosticBusy.rcs);after=await state(p);
  ok(after.rcsReachable&&after.left.status==='unknown','模拟传输恢复仍保留原件Unknown');ok(after.rcsHeartbeat.failure===before.rcsHeartbeat.failure,'旧失败证据保留等待新周期，不因修复按钮伪造200');
 });
 await check('V2-L06','同窗安装授权取消与未知回执核对，首配独立','install-review',async(p,ok)=>{
  await click(p,'aux-install-run');let s=await state(p);ok(s.aux.installPhase==='awaiting-authorization'&&!s.installDone,'本地摘要后才请求模拟授权，尚未安装完成');
  await click(p,'aux-install-cancel-authorization');s=await state(p);ok(s.aux.installPhase==='authorization-cancelled'&&!s.installDone&&!!s.aux.installReview,'取消保留审核摘要，没有成功回执');
  await p.evaluate(()=>setScene('install-receipt-unknown'));ok(await p.locator('[data-action="aux-install-run"]').count()===0,'回执未知不提供重复安装动作');
  await click(p,'aux-install-inspect-receipt');await p.waitForFunction(()=>S.installDone);s=await state(p);ok(s.installStep===3&&s.aux.installPhase==='completed','只读模拟回执核对后同窗完成');
  await click(p,'aux-install-setup');s=await state(p);ok(s.view==='setup'&&s.setupStep===1&&await p.locator('.sidebar').count()===0,'普通客户端首配作为独立流程演示');
 });
 await browser.close();report.finishedAt=new Date().toISOString();report.passed=report.checks.every(x=>x.status==='PASS')&&!report.errors.length&&!report.externalRequests.length;fs.writeFileSync(output,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify({prototypeSha256:report.prototypeSha256,checks:report.checks.map(({id,status,error})=>({id,status,error})),errors:report.errors,externalRequests:report.externalRequests.length,passed:report.passed},null,2));if(!report.passed)process.exitCode=1;
})().catch(e=>{console.error(e);process.exitCode=1});
