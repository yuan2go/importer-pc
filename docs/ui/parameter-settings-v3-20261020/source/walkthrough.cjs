// Focused task journeys. Browser simulation only; no Windows or hardware claims.
const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {pathToFileURL}=require('url');
const {chromium}=require(process.env.CODEX_PRIMARY_RUNTIME_NODE_MODULES?process.env.CODEX_PRIMARY_RUNTIME_NODE_MODULES+'/playwright':'playwright');
const root=path.resolve(__dirname,'..');
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.PROTOTYPE_CHROME||undefined,args:['--no-sandbox']});
 const page=await browser.newPage({viewport:{width:1600,height:900}}),results=[],pageErrors=[],network=[];
 page.on('pageerror',e=>pageErrors.push(e.message));page.on('request',r=>{if(/^https?:/.test(r.url()))network.push(r.url());});
 await page.goto(pathToFileURL(path.join(root,'Prototype.html')).href);
 await page.evaluate(()=>document.fonts.ready);
 const reset=scene=>page.evaluate(s=>window.prototype.setScene(s),scene);
 const snapshot=()=>page.evaluate(()=>window.prototype.getState());
 const click=action=>page.locator(`[data-action="${action}"]`).filter({visible:true}).first().click();
 async function check(name,work){try{await work();results.push({name,result:'PASS'});}catch(error){results.push({name,result:'FAIL',detail:error.message});throw error;}}
 try{
  await check('选择右相机、修改后切回左相机；对象草稿相互独立',async()=>{
   await reset('left');await click('page:right');await page.locator('#f-rightIp').fill('192.168.10.42');await click('page:left');
   assert.equal(await page.locator('#f-leftIp').inputValue(),'192.168.10.21');await click('page:right');assert.equal(await page.locator('#f-rightIp').inputValue(),'192.168.10.42');
  });
  await check('非法IP定位、修正、审阅、暂停、维护授权、保存；运行值保持原版本',async()=>{
   await reset('left');await page.locator('#f-leftIp').fill('192.168.10.999');await page.locator('#f-leftSerial').fill('HK-DEMO-00021');await click('save');
   assert.equal((await snapshot()).dialog,null);assert.equal(await page.locator('#f-leftSerial').inputValue(),'HK-DEMO-00021');assert.equal(await page.evaluate(()=>document.activeElement.id),'f-leftIp');assert.match((await snapshot()).errors.leftIp,/IPv4/);
   await page.locator('#f-leftIp').fill('192.168.10.31');await click('save');await page.locator('dialog[open]').waitFor();
   assert.equal((await snapshot()).authorized,false);await click('review-pause');await page.locator('#auth-proof').fill('synthetic-only');await click('authorize');await page.locator('#change-reason').fill('更换左相机，更新演示地址。');await click('submit');
   await page.waitForFunction(()=>window.prototype.getState().pending===true);const s=await snapshot();assert.equal(s.saved.leftIp,'192.168.10.31');assert.equal(s.running.leftIp,'192.168.10.21');assert.equal(s.revision,12);assert.equal(s.savedRevision,13);assert.equal(s.paused,true);await click('dismiss');
  });
  await check('按技术键搜索并展开真实字段；Ctrl+F/Enter/Escape可用',async()=>{
   await reset('system');await page.keyboard.press('Control+f');assert.equal(await page.evaluate(()=>document.activeElement.id),'settings-search');await page.locator('#settings-search').fill('queue.capacity');await page.keyboard.press('Enter');
   assert.equal((await snapshot()).page,'left');await page.waitForFunction(()=>document.activeElement.id==='f-leftQueue');assert.equal(await page.locator('#driver-advanced').evaluate(el=>el.open),true);
   await page.keyboard.press('Control+f');await page.locator('#settings-search').fill('不存在的演示字段');assert.match(await page.locator('.directory').innerText(),/没有匹配项/);await page.keyboard.press('Escape');assert.equal(await page.locator('#settings-search').inputValue(),'');
  });
  await check('返回作业保留无效输入；关闭草稿可取消或明确放弃',async()=>{
   await reset('left');await page.locator('#f-leftIp').fill('invalid-ip-demo');await click('leave');assert.equal((await snapshot()).view,'work');assert.equal((await snapshot()).dialog,null);await click('settings');assert.equal(await page.locator('#f-leftIp').inputValue(),'invalid-ip-demo');
   await page.getByRole('button',{name:'关闭前处理未保存更改'}).click();assert.equal((await snapshot()).dialog,'关闭前处理未保存更改');await click('dismiss');assert.equal(await page.locator('#f-leftIp').inputValue(),'invalid-ip-demo');
   await page.getByRole('button',{name:'关闭前处理未保存更改'}).click();await click('discard-and-leave');assert.equal((await snapshot()).draft.leftIp,'192.168.10.21');
  });
  await check('RCS连接检查保留编辑地址；不保存、不应用、不发送业务请求',async()=>{
   await reset('rcs');await page.locator('#f-endpoint').fill('http://192.168.10.60:58023/rcs/sorter/api');await click('check-connection');await page.waitForFunction(()=>document.querySelector('.test-result')?.textContent.includes('目标拒绝连接'));
   const s=await snapshot();assert.equal(s.saved.endpoint,'http://localhost:58023/rcs/sorter/api');assert.equal(s.running.endpoint,s.saved.endpoint);assert.equal(s.changes.length,1);assert.match(await page.locator('.test-result').innerText(),/未验证/);await click('connection-details');assert.match(await page.locator('dialog pre').innerText(),/BusinessRequestSent: false/);await click('dismiss');
  });
  await check('安全模板先预览再入草稿；不改变IP、站码、目标或运行配置',async()=>{
   await reset('left');await click('tools');await click('template');await click('apply-template');const s=await snapshot();assert.equal(s.changes.length,2);assert.equal(s.draft.history,'800');assert.equal(s.saved.history,'500');assert.equal(s.draft.leftIp,s.saved.leftIp);assert.equal(s.draft.station,s.saved.station);assert.equal(s.draft.endpoint,s.saved.endpoint);
  });
  await check('保存结果未知时禁止重复提交；核对后区分已保存与运行中',async()=>{
   await reset('unknown');assert.equal(await page.locator('.save-bar [data-action="save"]').isDisabled(),true);
   await click('undo');await click('confirm-undo');assert.equal((await snapshot()).unknown,true);assert.equal(await page.locator('.save-bar [data-action="save"]').isDisabled(),true);
   await click('inspect');const s=await snapshot();assert.equal(s.unknown,false);assert.equal(s.pending,true);assert.equal(s.saved.leftIp,'192.168.10.31');assert.equal(s.running.leftIp,'192.168.10.21');assert.equal(s.draft.leftIp,'192.168.10.21');assert.equal(s.changes.length,1);await click('dismiss');
   for(const scene of ['saving','save-failed']){await reset(scene);assert.equal((await snapshot()).paused,true);assert.match(await page.locator('.toolbar').innerText(),/已暂停新入场/);}
  });
  await check('左右当前条码、任务与格口对应；RCS异常不残留上一格口',async()=>{
   await reset('work');assert.equal(await page.locator('.lanes .lane').count(),2);assert.match(await page.locator('.lane').first().innerText(),/BC0003/);assert.match(await page.locator('.lane').first().innerText(),/1-01/);assert.match(await page.locator('.lane').last().innerText(),/BC0004/);
   await reset('work-fault');assert.equal(await page.locator('.chute').count(),0);assert.match(await page.locator('.work-content .banner').innerText(),/左右两路/);await click('diagnostic');await click('page:rcs');assert.equal((await snapshot()).page,'rcs');assert.match(await page.locator('.toolbar').innerText(),/RCS 连接异常/);
  });
  await check('本地条码样例异步检查与结果过期提示',async()=>{
   await reset('filter');await click('sample-check');await page.waitForFunction(()=>document.querySelector('#sample-result')?.textContent.includes('选中 BC0003'));await page.locator('#sample-codes').fill('BC0004');assert.match(await page.locator('#sample-result').innerText(),/已过期/);
  });
  await check('1366×768、1092×614和800×420逻辑视口保留底栏且无页面横向溢出',async()=>{
   for(const [width,height]of [[1366,768],[1092,614],[800,420]]){
    await page.setViewportSize({width,height});await reset('invalid');
    const box=await page.locator('.save-bar [data-action="save"]').boundingBox();assert(box&&box.x>=0&&box.y>=0&&box.x+box.width<=width&&box.y+box.height<=height);assert(box.height>=44);
    const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth||document.body.scrollWidth>innerWidth);assert.equal(overflow,false);
    await page.locator('#f-leftIp').scrollIntoViewIfNeeded();await page.locator('#f-leftIp').fill('192.168.10.31');await click('save');await page.locator('dialog[open]').waitFor();const dialogBox=await page.locator('dialog[open]').boundingBox();assert(dialogBox&&dialogBox.x>=0&&dialogBox.y>=0&&dialogBox.x+dialogBox.width<=width&&dialogBox.y+dialogBox.height<=height);await click('dismiss');
   }
  });
 }finally{
  const result={kind:'browser-simulation-only',timeUtc:new Date().toISOString(),prototypeSha256:require('crypto').createHash('sha256').update(fs.readFileSync(path.join(root,'Prototype.html'))).digest('hex'),browser:await browser.version(),nativeWindows:'NOT_RUN',realDevices:'NOT_RUN',pageErrors,unexpectedHttpRequests:network,journeys:results};
  fs.writeFileSync(path.join(root,'BROWSER-WALKTHROUGH.json'),JSON.stringify(result,null,2)+'\n');await browser.close();
  console.log(JSON.stringify({passed:results.filter(r=>r.result==='PASS').length,failed:results.filter(r=>r.result==='FAIL').length,pageErrors:pageErrors.length,unexpectedHttpRequests:network.length}));
 }
 assert.equal(pageErrors.length,0);assert.equal(network.length,0);
})().catch(e=>{console.error(e);process.exit(1)});
