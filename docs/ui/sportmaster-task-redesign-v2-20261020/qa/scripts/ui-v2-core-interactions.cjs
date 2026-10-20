/* Necessary offline interaction checks for Sportmaster UI V2.
 * Reads only the NEW V2 Prototype.html; writes scratch QA evidence only.
 * No build, production HTTP, device, SDK, Windows or installation validation.
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { chromium } = require('playwright');
const ROOT = '/workspace/scratch/69927969969a';
const SOURCE = path.resolve(process.argv[2] || path.join(ROOT, 'importer-pc/docs/ui/sportmaster-task-redesign-v2-20261020/Prototype.html'));
const OUT = path.join(ROOT, 'qa/ui-v2-core');
const ONLY = new Set((process.env.UI_V2_QA_ONLY || '').split(',').filter(Boolean));
const RESULT_NAME = process.env.UI_V2_QA_RESULT || (ONLY.size ? 'results-targeted' : 'results');
if (!/^[a-z0-9-]+$/.test(RESULT_NAME)) throw new Error('QA report name must be a simple filename stem.');
if (!SOURCE.includes('sportmaster-task-redesign-v2-20261020')) throw new Error('Only the V2 prototype is permitted by this check.');
const bytes = fs.readFileSync(SOURCE);
const html = bytes.toString('utf8');
if (!html.includes('function renderBusiness') || !html.includes('function renderSetup')) throw new Error('V2 build is incomplete; build all source modules before running.');
fs.mkdirSync(OUT, { recursive: true });
const sourceHash = crypto.createHash('sha256').update(bytes).digest('hex');
const frozen = path.join(OUT, 'prototype-' + sourceHash.slice(0, 12) + '.html');
fs.writeFileSync(frozen, bytes);
const environment = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/render-environment.json')));
const report = {
  scope: '仅 V2 离线原型的必要交互；未连接 HTTP/设备，未做 WPF/Windows/DPI/安装/性能/现场验收',
  source: SOURCE, sourceSha256: sourceHash, startedAt: new Date().toISOString(),
  viewport: { width: 1600, height: 900 }, selectedChecks: [...ONLY], checks: [], pageErrors: [], externalRequests: []
};
const action = (p, key) => p.locator('[data-action="' + key + '"]').first().click();
const input = (p, key) => p.locator('[data-field="' + key + '"]').first();
const state = p => p.evaluate(() => JSON.parse(JSON.stringify(S)));
const phase = (p, expected) => p.waitForFunction(x => S.phase === x, expected, { timeout: 6000 });
const selectScene = (p, name) => p.locator('#scene-select').selectOption(name);
async function laneRequest(p, side) {
  await action(p, 'lane-detail:' + side);
  await p.locator('.dialog-body details summary').click();
  const text = await p.locator('.dialog-body pre.code').innerText();
  await action(p, 'dialog-close');
  return text;
}
async function run(browser, id, title, scene, body) {
  if (ONLY.size && !ONLY.has(id)) return;
  const context = await browser.newContext({ viewport: report.viewport, locale: 'zh-CN', timezoneId: 'Asia/Shanghai' });
  await context.route(/^https?:/, route => { report.externalRequests.push({ id, url: route.request().url() }); return route.abort(); });
  const page = await context.newPage();
  page.on('pageerror', e => report.pageErrors.push({ id, message: e.message }));
  const check = { id, title, scene, status: 'PASS', assertions: [] }; report.checks.push(check);
  const assert = (pass, message, evidence) => {
    check.assertions.push({ pass: Boolean(pass), message, ...(evidence === undefined ? {} : { evidence }) });
    if (!pass) throw new Error(message);
  };
  try {
    await page.goto('file://' + frozen + '?scene=' + scene);
    await page.waitForSelector('#app .shell');
    await page.evaluate(() => document.fonts.ready);
    await body(page, assert);
  } catch (e) {
    check.status = 'FAIL'; check.error = e.message;
    try { await page.screenshot({ path: path.join(OUT, id + '-failure.png'), fullPage: true }); } catch {}
  } finally { await context.close(); }
}
(async () => {
  const browser = await chromium.launch({ executablePath: environment.executablePath, headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage'] });
  report.browser = browser.version();

  await run(browser, 'V2-T01', '左右并行、旧任务整组失效与迟到响应隔离', 'work-normal', async (p, ok) => {
    const before = await state(p);
    await action(p, 'demo-scan:left');
    let s = await state(p);
    ok(s.left.status === 'processing' && s.left.task === null, '左位新包等待 RCS 时旧任务整组失效');
    ok(JSON.stringify(s.right) === JSON.stringify(before.right), '左位更新不改变右位包裹、任务或请求');
    const current = await p.locator('#lane-left .task-info').innerText();
    ok(!current.includes(before.left.task.chuteCode) && !current.includes(before.left.task.taskId), '左位可见任务区不泄漏上一件格口或任务号');
    await action(p, 'demo-scan:right');
    await p.waitForFunction(() => S.left.status === 'success' && S.right.status === 'success');
    s = await state(p);
    ok(s.left.barcode !== before.left.barcode && s.right.barcode !== before.right.barcode, '两位独立完成各自新包');
    const assigned = [before.left.requestId, before.right.requestId, s.left.requestId, s.right.requestId];
    await action(p, 'demo-scan:right');
    await p.waitForFunction(() => S.right.status === 'success');
    assigned.push((await state(p)).right.requestId);
    ok(new Set(assigned).size === assigned.length, '连续左右请求使用互不重复的编号', assigned);
    await action(p, 'demo-scan:left');
    await selectScene(p, 'work-processing');
    const next = await state(p);
    // Wait only for the known, bounded simulated response to complete.
    await p.waitForTimeout(1250);
    s = await state(p);
    ok(JSON.stringify(s.left) === JSON.stringify(next.left), '上一场景的迟到响应不能覆盖新的本位扫描/等待状态');
  });

  await run(browser, 'V2-T02', '四类编辑、保留错误、保存 Pending、重启确认与原请求冻结', 'work-normal', async (p, ok) => {
    const original = await state(p), originalRequest = await laneRequest(p, 'left');
    const parsed = JSON.parse(originalRequest.slice(originalRequest.indexOf('{')));
    const request = parsed.data ? parsed : parsed.request || parsed.body;
    ok(request && typeof request.requestId === 'number' && typeof request.data[0].params.zoneId === 'number' && typeof request.data[0].params.projectId === 'number' && typeof request.data[0].params.quantity === 'number', '预览请求中的整数保持数字 token，响应数量仍为字符串');
    await action(p, 'go:settings');
    ok((await p.locator('[aria-label="参数分类"] button').allTextContents()).length === 4, '日常参数有四个明确类别');
    await input(p, 'draft.system.stationCode').fill('IS-L-L1-05');
    await action(p, 'category:business');
    await input(p, 'draft.business.quantity').fill('2');
    await action(p, 'category:drivers'); await action(p, 'object:right');
    await input(p, 'draft.right.ip').fill('192.0.2.999');
    await input(p, 'draft.right.bcrCode').fill('IS-L-L1-05-PICK-2');
    await action(p, 'category:components');
    await input(p, 'draft.rcs.source').fill('PDA_REVIEW');
    await action(p, 'save-review'); await phase(p, 'invalid');
    let s = await state(p);
    ok(s.object === 'right' && Boolean(s.errors['draft.right.ip']), '从其他类别发起校验会定位错误的右相机实例');
    ok(await input(p, 'draft.right.ip').getAttribute('aria-invalid') === 'true', '错误标记落在实际输入字段');
    ok(s.draft.system.stationCode === 'IS-L-L1-05' && s.draft.business.quantity === '2' && s.draft.right.bcrCode === 'IS-L-L1-05-PICK-2' && s.draft.rcs.source === 'PDA_REVIEW', '一次字段错误保留所有四类其他输入');
    ok(JSON.stringify(s.draft.left) === JSON.stringify(original.draft.left), '编辑右实例没有修改左实例参数');
    await input(p, 'draft.right.ip').fill('192.0.2.32');
    await action(p, 'save-review'); await p.waitForSelector('[role="dialog"]');
    await input(p, 'reason').fill('核对四类对象并修改右相机；离线原型验证');
    await action(p, 'save-confirm'); await phase(p, 'pending');
    s = await state(p);
    ok(s.ledger.pending && s.ledger.revision > original.ledger.revision && s.paused, '保存形成一个 Pending 修订并保持整站软件暂停');
    ok(JSON.stringify(s.runtime) === JSON.stringify(original.runtime), '保存并未切换实际运行配置');
    ok(s.saved.right.ip === '192.0.2.32' && s.saved.system.stationCode === 'IS-L-L1-05' && s.saved.business.quantity === '2' && s.saved.rcs.source === 'PDA_REVIEW', '单次完整保存包含四类关联修改');
    await action(p, 'apply-review'); await action(p, 'apply-confirm'); await phase(p, 'clean');
    s = await state(p);
    ok(!s.ledger.pending && s.ledger.confirmed && s.paused && s.runtime.right.ip === '192.0.2.32', '受控重启取得新内容确认；新包接收仍暂停');
    await action(p, 'go:work');
    ok((await laneRequest(p, 'left')) === originalRequest, '应用新 system / business / driver / component 值后，旧包冻结请求保持原字节');
  });

  await run(browser, 'V2-T03', '已知数值边界与跨类别错误定位', 'system-station', async (p, ok) => {
    await action(p, 'category:business');
    await action(p, 'business-section:runtime');
    for (const k of ['queueCapacity', 'timeoutMs', 'maximumOperations']) await input(p, 'draft.business.' + k).fill('0');
    await action(p, 'category:system');
    await action(p, 'system-section:runtime');
    for (const k of ['residentHistory', 'minimumFreeBytes', 'backupInterval', 'backupMaximum']) await input(p, 'draft.system.' + k).fill('0');
    await action(p, 'category:components');
    await action(p, 'validate-config'); await phase(p, 'invalid');
    const s = await state(p);
    const keys = ['draft.business.queueCapacity', 'draft.business.timeoutMs', 'draft.business.maximumOperations', 'draft.system.residentHistory', 'draft.system.minimumFreeBytes', 'draft.system.backupInterval', 'draft.system.backupMaximum'];
    ok(keys.every(k => Boolean(s.errors[k])), '当前代码明确范围的七个字段不会让 0 被标为校验通过', s.errors);
    ok(s.category === 'business' && s.section === 'runtime', '错误摘要跳转到业务模块的作业处理分组');
    ok(await input(p, 'draft.business.queueCapacity').evaluate(el => document.activeElement === el), '首个错误输入在视口内并取得焦点');
    ok(s.ledger.revision === 12 && s.runtime.business.queueCapacity === '64', '校验错误不会保存或改变运行值');
  });

  await run(browser, 'V2-T04', '保存结果未知时锁定候选并禁止重复提交', 'config-save-unknown', async (p, ok) => {
    const before = await state(p);
    ok(await input(p, 'draft.left.ip').isDisabled(), '结果未知期间左相机编辑被锁定');
    for (const [category, key] of [['system', 'draft.system.stationCode'], ['business', 'draft.business.quantity'], ['components', 'draft.rcs.source']]) {
      await action(p, 'category:' + category);
      ok(await input(p, key).isDisabled(), '结果未知期间 ' + category + ' 也不可改写待核对候选');
    }
    const saveButtons = p.locator('[data-action="save-review"], [data-action="save-confirm"]');
    ok(await saveButtons.count() === 0 || (await saveButtons.evaluateAll(nodes => nodes.every(el => el.disabled))), '没有可重复保存的已启用按钮');
    await action(p, 'reconcile-save'); await phase(p, 'pending');
    const s = await state(p);
    ok(s.ledger.revision === before.uncertainSaved.revision, '核对收敛为已落盘修订，不创建额外修订');
    ok(JSON.stringify(s.runtime) === JSON.stringify(before.runtime), '核对写入事实不会冒充应用成功');
  });

  await run(browser, 'V2-T05', '后台更新保留焦点，未保存离开三种选择', 'driver-right', async (p, ok) => {
    const before = await state(p);
    await input(p, 'draft.right.bcrCode').fill('IS-L-L1-04-PICK-REVIEW');
    await input(p, 'draft.right.bcrCode').evaluate(el => { el.focus(); el.setSelectionRange(3, 10); });
    await p.evaluate(() => window.simulateScan('left'));
    await p.waitForFunction(() => S.left.status === 'success' && S.left.seq > 128);
    ok(await input(p, 'draft.right.bcrCode').evaluate(el => document.activeElement === el && el.selectionStart === 3 && el.selectionEnd === 10), '模拟后台结果更新后输入焦点与选区不变');
    ok(await input(p, 'draft.right.bcrCode').inputValue() === 'IS-L-L1-04-PICK-REVIEW', '后台刷新没有覆盖输入');
    await action(p, 'go:history'); await p.waitForSelector('[role="dialog"]');
    await p.locator('.dialog-footer [data-action="dialog-close"]').click();
    ok((await state(p)).view === 'settings', '继续编辑留在参数页');
    await action(p, 'go:history'); await action(p, 'unsaved-keep');
    let s = await state(p);
    ok(s.view === 'history' && s.retained && s.draft.right.bcrCode === 'IS-L-L1-04-PICK-REVIEW', '保留草稿后离开，编辑值没有投入运行');
    await action(p, 'go:settings'); await action(p, 'object:right');
    await input(p, 'draft.right.bcrCode').fill('IS-L-L1-04-PICK-DISCARD');
    await action(p, 'go:work'); await action(p, 'unsaved-discard');
    s = await state(p);
    ok(s.view === 'work' && s.draft.right.bcrCode === before.saved.right.bcrCode && JSON.stringify(s.runtime) === JSON.stringify(before.runtime), '放弃本次修改回到已保存值；运行配置未改变');
  });

  await run(browser, 'V2-T06', '状态失效阻断恢复，原件未知按位保留', 'work-stale', async (p, ok) => {
    await action(p, 'resume-review');
    ok(await p.locator('[data-action="resume-confirm"]').isDisabled(), '快照失效时恢复按钮不可用');
    await action(p, 'dialog-close');
    await selectScene(p, 'work-rcs-outage');
    // Synthetic transport recovery is not a business result or a replay.
    await p.evaluate(() => { S.rcsReachable = true; render(); });
    await action(p, 'resume-review');
    ok(await p.locator('[data-action="resume-confirm"]').isEnabled(), '共享条件恢复后允许当前已就绪的右位恢复，符合 schema3 按位可用性');
    await action(p, 'resume-confirm');
    const before = await state(p);
    await action(p, 'demo-scan:left');
    ok((await state(p)).left.status === 'unknown' && (await state(p)).left.seq === before.left.seq, '恢复操作不清左位未知原件，也不准入左位新包');
    await action(p, 'demo-scan:right');
    await p.waitForFunction(seq => S.right.status === 'success' && S.right.seq > seq, before.right.seq);
    ok((await state(p)).left.status === 'unknown', '右位完成后左位原请求仍保持未知，未自动重发或解除责任');
  });


  await run(browser, 'V2-T07', '计划备份关闭排除依赖，保存前重新开启保留输入', 'system-station', async (p, ok) => {
    await action(p, 'system-section:runtime');
    const original = await state(p);
    await input(p, 'draft.system.backupDirectory').fill('relative-backup');
    await input(p, 'draft.system.backupInterval').fill('0');
    await input(p, 'draft.system.backupMaximum').fill('0');
    await action(p, 'validate-config'); await phase(p, 'invalid');
    let s = await state(p);
    ok(['backupDirectory', 'backupInterval', 'backupMaximum'].every(k => Boolean(s.errors['draft.system.' + k])), '启用时相对目录和越界数值分别提示错误');
    await input(p, 'draft.system.backupEnabled').selectOption('false');
    ok(await input(p, 'draft.system.backupDirectory').isDisabled() && await input(p, 'draft.system.backupInterval').isDisabled() && await input(p, 'draft.system.backupMaximum').isDisabled(), '关闭后依赖字段明确禁用');
    await action(p, 'validate-config'); await phase(p, 'dirty');
    s = await state(p);
    ok(Object.keys(s.errors).length === 0, '关闭后的无效目录与数值不会阻断完整候选校验');
    ok(s.draft.system.backupDirectory === 'relative-backup' && s.draft.system.backupInterval === '0' && s.draft.system.backupMaximum === '0', '校验没有擦掉本次临时输入');
    const submitted = await p.evaluate(() => candidate());
    ok(submitted.system.backupEnabled === 'false' && ['backupDirectory', 'backupInterval', 'backupMaximum'].every(k => submitted.system[k] === ''), '关闭时保存候选排除全部依赖值，而非带入隐藏错误');
    await input(p, 'draft.system.backupEnabled').selectOption('true');
    ok(await input(p, 'draft.system.backupDirectory').inputValue() === 'relative-backup' && await input(p, 'draft.system.backupInterval').inputValue() === '0', '保存前重新开启能恢复临时输入');
    await action(p, 'validate-config'); await phase(p, 'invalid');
    ok(Boolean((await state(p)).errors['draft.system.backupDirectory']), '重新开启后恢复依赖校验');
    await input(p, 'draft.system.backupEnabled').selectOption('false');
    ok((await p.locator('.config-content').innerText()).includes('不删除已有完整备份'), '界面说明关闭只取消后续计划、保留已有备份');
    await action(p, 'save-review'); await p.waitForSelector('[role="dialog"]');
    await input(p, 'reason').fill('关闭计划备份；仅离线原型依赖核对');
    await action(p, 'save-confirm'); await phase(p, 'pending');
    s = await state(p);
    ok(s.saved.system.backupEnabled === 'false' && ['backupDirectory', 'backupInterval', 'backupMaximum'].every(k => s.saved.system[k] === ''), '关闭计划可保存为 Pending，已保存内容不混入临时字段');
    ok(JSON.stringify(s.runtime) === JSON.stringify(original.runtime), '保存关闭计划后仍清楚保持旧运行配置，等待受控生效');
  });

  await browser.close();
  report.finishedAt = new Date().toISOString();
  report.sourceStillSame = crypto.createHash('sha256').update(fs.readFileSync(SOURCE)).digest('hex') === sourceHash;
  report.summary = { passed: report.checks.filter(x => x.status === 'PASS').length, failed: report.checks.filter(x => x.status === 'FAIL').length, pageErrors: report.pageErrors.length, externalRequests: report.externalRequests.length };
  fs.writeFileSync(path.join(OUT, RESULT_NAME + '.json'), JSON.stringify(report, null, 2) + '\n');
  const lines = ['# Sportmaster V2 必要原型交互走查', '', report.scope, '', '- 原型 SHA256：`' + sourceHash + '`', '- 浏览器：Chromium ' + report.browser, '- 逻辑视口：1600 × 900', '- 结果：' + report.summary.passed + ' 通过 / ' + report.summary.failed + ' 失败', '- 页面异常：' + report.summary.pageErrors, '- 外部请求：' + report.summary.externalRequests, '- 运行结束时源文件仍相同：' + report.sourceStillSame, ''];
  for (const check of report.checks) {
    lines.push('## ' + check.id + ' ' + check.title, '', '**' + check.status + '**', '');
    for (const a of check.assertions) lines.push('- ' + (a.pass ? '通过' : '失败') + '：' + a.message);
    if (check.error) lines.push('', '失败原因：' + check.error);
    lines.push('');
  }
  lines.push('## 验证边界', '', '使用固定源文件快照，仅验证离线原型的可见状态与模拟交互。未执行真实网络、设备链路、WPF实际输入、Windows缩放、安装、性能或现场验收。页面导出的高清PNG也不构成上述验收证据。');
  fs.writeFileSync(path.join(OUT, RESULT_NAME + '.md'), lines.join('\n') + '\n');
  console.log(JSON.stringify({ ...report.summary, sourceSha256: sourceHash, sourceStillSame: report.sourceStillSame, failures: report.checks.filter(x => x.status === 'FAIL').map(x => ({ id: x.id, error: x.error })) }, null, 2));
  if (report.summary.failed || report.summary.pageErrors || report.summary.externalRequests) process.exitCode = 1;
})().catch(error => { console.error(error); process.exitCode = 1; });
