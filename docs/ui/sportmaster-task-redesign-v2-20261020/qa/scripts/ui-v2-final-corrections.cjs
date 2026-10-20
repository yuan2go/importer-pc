/* Targeted final V2 corrections only: read-only evidence, scroll/focus and long barcode geometry.
 * Never builds or edits the repository, opens the old prototype, or connects production services.
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { chromium } = require('playwright');
const ROOT = '/workspace/scratch/69927969969a';
const SOURCE = path.join(ROOT, 'importer-pc/docs/ui/sportmaster-task-redesign-v2-20261020/Prototype.html');
const QA = path.join(ROOT, 'qa');
const scratch = path.join(QA, 'ui-v2-final-correction-snapshots');
fs.mkdirSync(scratch, { recursive: true });
const bytes = fs.readFileSync(SOURCE);
const sourceSha256 = crypto.createHash('sha256').update(bytes).digest('hex');
const frozen = path.join(scratch, 'prototype-' + sourceSha256.slice(0, 12) + '.html');
fs.writeFileSync(frozen, bytes);
const env = JSON.parse(fs.readFileSync(path.join(ROOT, 'analysis/render-environment.json')));
const report = {
  scope: '仅最终三处修改的离线原型必要复核；未重跑完整 QA，未验证 Windows、WPF 或真实设备链路',
  source: SOURCE, sourceSha256, startedAt: new Date().toISOString(),
  checks: [], pageErrors: [], externalRequests: [], downloads: []
};
const readState = p => p.evaluate(() => JSON.parse(JSON.stringify(S)));
const control = (p, key) => p.locator('[data-action="' + key + '"]').first();
const field = (p, key) => p.locator('[data-field="' + key + '"]').first();
async function loadScene(p, scene) {
  await p.goto('file://' + frozen + '?scene=' + scene);
  await p.waitForSelector('#app .shell');
  await p.evaluate(() => document.fonts.ready);
}
async function run(browser, id, title, viewport, scene, body) {
  const result = { id, title, viewport, scene, status: 'PASS', assertions: [] };
  report.checks.push(result);
  const context = await browser.newContext({ viewport, locale: 'zh-CN', timezoneId: 'Asia/Shanghai' });
  await context.route(/^https?:/, route => {
    report.externalRequests.push({ id, method: route.request().method(), url: route.request().url() });
    return route.abort();
  });
  const p = await context.newPage();
  p.setDefaultTimeout(6000);
  p.on('pageerror', error => report.pageErrors.push({ id, message: error.message }));
  p.on('download', download => report.downloads.push({ id, suggestedFilename: download.suggestedFilename() }));
  const ok = (value, message, detail) => {
    result.assertions.push({ pass: Boolean(value), message, ...(detail === undefined ? {} : { detail }) });
    if (!value) throw new Error(message);
  };
  try {
    await loadScene(p, scene);
    await body(p, ok, result);
  } catch (error) {
    result.status = 'FAIL'; result.error = error.message;
    try { await p.screenshot({ path: path.join(scratch, id + '-failure.png'), fullPage: true }); } catch {}
  } finally { await context.close(); }
}
async function laneGeometry(p) {
  return p.evaluate(() => {
    const rect = e => { const r = e.getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width, height: r.height, top: r.top, bottom: r.bottom }; };
    const result = {};
    for (const side of ['left', 'right']) {
      const lane = document.getElementById('lane-' + side), barcode = lane.querySelector('.scan-code');
      const range = document.createRange(); range.selectNodeContents(barcode);
      result[side] = {
        barcodeText: barcode.textContent, barcode: rect(barcode),
        barcodeLineTops: [...new Set([...range.getClientRects()].map(r => Math.round(r.top * 100) / 100))],
        scan: rect(lane.querySelector('.scan-info')),
        task: rect(lane.querySelector('.task-info')),
        chuteRow: rect(lane.querySelector('.chute-row')),
        chute: rect(lane.querySelector('.chute-code'))
      };
    }
    return result;
  });
}
(async () => {
  const browser = await chromium.launch({ executablePath: env.executablePath, headless: true, args: ['--no-sandbox', '--disable-dev-shm-usage'] });
  report.browser = browser.version();

  await run(browser, 'V2-F01', '运行未知以只读核对为主操作，保留候选和软件暂停', { width: 1600, height: 900 }, 'config-runtime-unknown', async (p, ok, result) => {
    const before = await readState(p);
    const beforeStateText = await p.locator('#config-state').innerText();
    const primaryText = await p.locator('.save-bar .primary').innerText();
    ok(primaryText === '核对运行状态' && await control(p, 'runtime-evidence').isEnabled(), '未知状态的主操作是可用的“核对运行状态”');
    ok(await p.locator('[data-action="apply-review"], [data-action="apply-confirm"]').count() === 0, '未知状态没有默认重启或应用按钮');
    ok(before.ledger.confirmed === false && beforeStateText.includes('运行配置待确认') && !beforeStateText.includes('旧配置已确认') && !beforeStateText.includes('当前仍是上次已确认配置'), '读取证据前不宣称旧配置仍在运行', beforeStateText);
    await p.evaluate(() => {
      window.__finalMutationCalls = [];
      for (const key of ['stageCandidate', 'acceptSaved', 'applyCandidate']) {
        const original = window[key];
        window[key] = function (...args) { window.__finalMutationCalls.push(key); return original.apply(this, args); };
      }
    });
    await control(p, 'runtime-evidence').click();
    const dialogText = await p.locator('[role="dialog"]').innerText();
    ok(dialogText.includes('只读取现有启动与配置确认，不保存、不应用、不再次重启') && dialogText.includes('当前无法确认'), '核对窗口先说明只读范围并保留未知事实');
    await control(p, 'runtime-read').click();
    await p.waitForFunction(() => S.ledger.confirmed === true && S.phase === 'apply-failed');
    const after = await readState(p);
    ok(JSON.stringify(after.ledger.pending) === JSON.stringify(before.ledger.pending) && after.ledger.revision === before.ledger.revision, '只读核对后 Pending 内容与配置账修订保持不变');
    ok(after.paused === true && before.paused === true, '核对结束软件接收仍暂停');
    ok(JSON.stringify(after.runtime) === JSON.stringify(before.runtime) && JSON.stringify(after.saved) === JSON.stringify(before.saved) && JSON.stringify(after.draft) === JSON.stringify(before.draft), '核对没有保存、应用或改写编辑/已保存/运行内容');
    ok(JSON.stringify(after.audit) === JSON.stringify(before.audit) && JSON.stringify(after.left) === JSON.stringify(before.left) && JSON.stringify(after.right) === JSON.stringify(before.right), '核对没有新增配置审计或改写左右原件');
    const calls = await p.evaluate(() => window.__finalMutationCalls);
    ok(calls.length === 0, '只读流程未调用保存、接受保存或应用候选函数', calls);
    const afterText = await p.locator('#config-state').innerText();
    ok(afterText.includes('旧配置已确认') && afterText.includes('接收继续暂停'), '模拟读回以后才显示旧运行证据已确认，并继续标明暂停');
    result.before = { phase: before.phase, confirmed: before.ledger.confirmed, revision: before.ledger.revision, primaryText };
    result.after = { phase: after.phase, confirmed: after.ledger.confirmed, revision: after.ledger.revision, pendingRetained: !!after.ledger.pending, paused: after.paused };
  });

  await run(browser, 'V2-F02', '独立滚动表单中的错误定位、可见性及输入保留', { width: 1280, height: 720 }, 'driver-invalid', async (p, ok, result) => {
    await field(p, 'draft.left.bcrCode').fill('IS-L-L1-04-PICK-FINAL');
    const before = await readState(p);
    const scrolling = await p.locator('.config-content').evaluate(e => {
      e.scrollTop = e.scrollHeight;
      return { scrollTop: e.scrollTop, scrollHeight: e.scrollHeight, clientHeight: e.clientHeight, overflowY: getComputedStyle(e).overflowY };
    });
    ok(scrolling.scrollTop > 0 && scrolling.scrollHeight > scrolling.clientHeight && ['auto', 'scroll'].includes(scrolling.overflowY), '对象表单在独立 config-content 中确实产生滚动', scrolling);
    await control(p, 'locate-error:draft.left.ip').click();
    await p.waitForFunction(() => document.activeElement?.dataset.field === 'draft.left.ip');
    const after = await readState(p);
    ok(after.object === 'left' && await field(p, 'draft.left.ip').getAttribute('aria-invalid') === 'true', '错误摘要定位到左相机实际 IP 输入并保持错误标记');
    const geometry = await field(p, 'draft.left.ip').evaluate(e => {
      const c = e.closest('.config-content'), r = e.getBoundingClientRect(), box = c.getBoundingClientRect(), header = c.querySelector('.object-header').getBoundingClientRect(), save = document.querySelector('.save-bar').getBoundingClientRect();
      return { fieldTop: r.top, fieldBottom: r.bottom, contentTop: box.top, contentBottom: box.bottom, stickyHeaderBottom: header.bottom, saveBarTop: save.top, scrollTop: c.scrollTop };
    });
    ok(geometry.fieldTop >= Math.max(geometry.contentTop, geometry.stickyHeaderBottom) - 0.5 && geometry.fieldBottom <= Math.min(geometry.contentBottom, geometry.saveBarTop) + 0.5, '聚焦后的错误字段完整可见，未被固定标题或保存栏遮挡', geometry);
    ok(JSON.stringify(after.draft) === JSON.stringify(before.draft) && after.draft.left.bcrCode === 'IS-L-L1-04-PICK-FINAL' && after.draft.left.ip === '192.0.2.999', '错误定位保留其他输入和非法 IP 原文');
    ok(JSON.stringify(after.saved) === JSON.stringify(before.saved) && JSON.stringify(after.runtime) === JSON.stringify(before.runtime) && after.ledger.revision === before.ledger.revision, '错误定位没有保存或改变运行配置');
    result.geometry = geometry;
  });

  await run(browser, 'V2-F03', '1280×720 长条码换行不改变左右格口起始位置', { width: 1280, height: 720 }, 'work-normal', async (p, ok, result) => {
    const normal = await laneGeometry(p);
    await p.locator('#scene-select').selectOption('work-long');
    await p.evaluate(() => document.fonts.ready);
    const long = await laneGeometry(p);
    ok(normal.left.barcodeLineTops.length === 1 && long.left.barcodeLineTops.length > 1, '比较样例确实覆盖短条码单行与长条码换行', { normalLines: normal.left.barcodeLineTops, longLines: long.left.barcodeLineTops });
    const deltas = {};
    for (const side of ['left', 'right']) {
      deltas[side] = { taskTop: long[side].task.top - normal[side].task.top, chuteRowTop: long[side].chuteRow.top - normal[side].chuteRow.top, chuteTop: long[side].chute.top - normal[side].chute.top };
      ok(Object.values(deltas[side]).every(v => Math.abs(v) <= 0.5), (side === 'left' ? '左' : '右') + '位任务区、格口行与格口文字起始位置保持稳定', deltas[side]);
    }
    ok(Math.abs(long.left.chute.top - long.right.chute.top) <= 0.5, '长条码场景中左右格口文字仍在同一起始水平线');
    result.geometry = { normal, long, deltas };
  });

  await browser.close();
  report.finishedAt = new Date().toISOString();
  report.sourceStillSame = crypto.createHash('sha256').update(fs.readFileSync(SOURCE)).digest('hex') === sourceSha256;
  report.summary = { passed: report.checks.filter(c => c.status === 'PASS').length, failed: report.checks.filter(c => c.status === 'FAIL').length, pageErrors: report.pageErrors.length, externalRequests: report.externalRequests.length, downloads: report.downloads.length };
  const jsonPath = path.join(QA, 'ui-v2-final-corrections.json');
  fs.writeFileSync(jsonPath, JSON.stringify(report, null, 2) + '\n');
  const lines = ['# V2 最后三处修改的必要复核', '', report.scope, '', '- 原型 SHA256：`' + sourceSha256 + '`', '- 浏览器：Chromium ' + report.browser, '- 执行结果：' + report.summary.passed + ' 通过 / ' + report.summary.failed + ' 失败', '- 页面异常：' + report.summary.pageErrors + '；外部请求：' + report.summary.externalRequests + '；下载：' + report.summary.downloads, '- 运行结束时源文件哈希未变：' + report.sourceStillSame, ''];
  for (const check of report.checks) {
    lines.push('## ' + check.id + ' · ' + check.title, '', '**' + check.status + '** · ' + check.viewport.width + ' × ' + check.viewport.height + ' 逻辑视口 · ' + check.scene, '');
    for (const assertion of check.assertions) lines.push('- ' + (assertion.pass ? '通过' : '失败') + '：' + assertion.message);
    if (check.error) lines.push('', '失败原因：' + check.error);
    if (check.id === 'V2-F03' && check.geometry) {
      lines.push('', '| 位置 | 正常格口文字 top | 长条码格口文字 top | 差值 |', '| --- | --- | --- | --- |');
      for (const side of ['left', 'right']) lines.push('| ' + side + ' | ' + check.geometry.normal[side].chute.top + ' | ' + check.geometry.long[side].chute.top + ' | ' + check.geometry.deltas[side].chuteTop + ' |');
    }
    lines.push('');
  }
  lines.push('## 边界', '', '只复核根代理指定的最后三处修改，没有重跑完整交互套件或已有 800/799 几何检查。未知运行的核对是明确标注的模拟读回；测试验证其只读边界和状态表达，不证明真实运行证据已取得。几何为浏览器逻辑像素，不代表目标 Windows DPI、WPF 输入、设备/RCS 链路、安装或性能验收。', '', '脚本：`qa/ui-v2-final-corrections.cjs`；逐断言和几何证据：`qa/ui-v2-final-corrections.json`。冻结 HTML 与仅失败时的截图为内部 QA 留痕，不应重复计为设计页面。');
  fs.writeFileSync(path.join(ROOT, 'analysis/ui-v2-final-corrections.md'), lines.join('\n') + '\n');
  console.log(JSON.stringify({ ...report.summary, sourceSha256, sourceStillSame: report.sourceStillSame, failures: report.checks.filter(c => c.status === 'FAIL').map(c => ({ id: c.id, error: c.error })) }, null, 2));
  if (report.summary.failed || report.summary.pageErrors || report.summary.externalRequests || report.summary.downloads || !report.sourceStillSame) process.exitCode = 1;
})().catch(error => { console.error(error); process.exitCode = 1; });
