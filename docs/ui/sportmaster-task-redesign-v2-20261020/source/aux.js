/* Auxiliary task views for the V2 offline prototype.
 * All checks, authentication, installation and recovery are explicitly simulated.
 * No fetch, XMLHttpRequest, WebSocket, SDK or production filesystem API is used.
 * Root owns the application shell, core configuration state and delegated events. */
'use strict';

function auxiliary() {
  if (!S.aux) S.aux = {
    historyRows: null, historySnapshotAt: '16:06:14', historySnapshotId: 1,
    diagnosticResults: {}, diagnosticBusy: {}, diagnosticSerial: 0,
    diagnosticOrigin: null, diagnosticFeedback: '', reconnectObject: 'rcs',
    reconnectReason: '', reconnectBusy: false,
    supportPhase: '', supportData: null, supportStale: false,
    maintenanceTab: 'backup', backupPhase: '', backupTime: '',
    restoreSource: '', restoreDirectory: 'D:\\ImporterPC\\RecoveryDemo',
    restorePhase: '', restored: false, maintenanceReason: '',
    migrationStation: '', migrationAcknowledged: false,
    installDirectory: 'C:\\Program Files\\Cainiao\\SM',
    installDataDirectory: 'D:\\ImporterPC\\Data',
    installDesktopShortcut: 'yes', installMenuShortcut: 'yes',
    installPhase: '', installError: '', installReview: null,
    installPrepared: false, installSerial: 0, installOutcome: 'completed',
    installReceiptOutcome: 'completed', installReceipt: null, inputDetail: null
  };
  return S.aux;
}

function auxTabs(items, current, prefix) {
  return `<div class="subtabs" aria-label="任务分区">${items.map(([id, label]) =>
    `<button type="button" data-action="${prefix}${id}" class="${id === current ? 'active' : ''}" aria-pressed="${id === current}">${esc(label)}</button>`).join('')}</div>`;
}
function auxFocusField(path) {
  requestAnimationFrame(() => {
    const node = document.querySelector(`[data-field="${CSS.escape(path)}"]`);
    node?.focus(); node?.scrollIntoView({ block: 'center', behavior: 'auto' });
  });
}
function auxDisabled(condition, reason) {
  return condition ? `disabled title="${esc(reason)}"` : '';
}
function auxValue(value) { return value == null || value === '' ? '<span class="muted">未返回</span>' : esc(value); }
function auxConfig(category, object, sectionName) {
  closeDialog();
  S.category = category; S.object = object; S.section = sectionName;
  go('settings');
}

/* Freeze this observation when opening details; later scans never replace the selected evidence. */
function showInputDetails(side) {
  if (!['left', 'right'].includes(side)) return;
  const A = auxiliary(), lane = S.lanes?.[side] || S[side], binding = S.runtime[side];
  if (!lane) return;
  const fallback = !lane.scanEvidence;
  const evidence = copy(lane.scanEvidence || {
    candidates: lane.barcode ? [lane.barcode] : [], complete: true,
    selectedCode: lane.barcode || null,
    outcome: lane.barcode ? '旧版严格单码兼容 · 示例' : '尚无相机观察', ruleHash: null
  });
  const candidates = Array.isArray(evidence.candidates) ? evidence.candidates : [];
  const selected = typeof evidence.selectedCode === 'string' ? evidence.selectedCode : null;
  const complete = evidence.complete === true;
  const selectedOccurrences = selected === null ? 0 : candidates.filter(code => code === selected).length;
  const consistentSelection = complete && selected !== null && selectedOccurrences === 1;
  const waiting = !candidates.length && fallback || /尚无|待筛选|处理中/.test(evidence.outcome || '');
  A.inputDetail = { simulation: true, side, position: side === 'left' ? '左投递位' : '右投递位',
    deviceId: binding?.deviceId || '', instanceId: binding?.instanceId || '',
    observedAt: lane.time || '', observationSequence: lane.seq ?? null,
    evidence, legacyExample: fallback, displayedCandidateCount: candidates.length,
    originalCandidateTotal: complete ? candidates.length : null };
  const summary = !complete
    ? notice('danger', '候选不完整 · 本次未请求 RCS', '只能核对已保留的候选；不能从截断集合推断唯一条码或原始总数。')
    : waiting ? notice('info', '尚无可用于判断的筛选结果', '本页只读显示相机观察，不触发取格口请求。')
    : selected === null ? notice('warning', esc(evidence.outcome || '未选中有效条码'), '本次筛选没有产生唯一有效条码，未创建作业或请求 RCS。原件责任仍由原作业保留。')
    : !consistentSelection ? notice('danger', '模拟证据需要核对', '记录中的选中码与完整候选不一致，不能据此显示唯一匹配；重复条目未被去重。')
    : notice('info', '本次选中码已记录', '筛选通过后仍由 Application 判断准入。是否已请求或取得 RCS 任务，请查看本位作业状态。');
  const rows = candidates.map((code, index) => `<tr><td>${index + 1}</td><td class="mono" style="white-space:pre-wrap">${code === '' ? '<span class="muted">空字符串</span>' : esc(code)}</td><td>${consistentSelection && code === selected ? '应用选中码' : '相机原始候选'}</td></tr>`).join('');
  const list = candidates.length ? `<div class="table-wrap" style="max-height:270px;overflow:auto" tabindex="0" aria-label="按相机原顺序保留的候选"><table class="table"><thead><tr><th style="width:10%">序号</th><th style="width:66%">相机原始候选</th><th>记录用途</th></tr></thead><tbody>${rows}</tbody></table></div>` : notice('info', '没有已保留的候选条码');
  openDialog(`${A.inputDetail.position} · 本次扫描详情`, summary + facts([
    ['观察对象', `${esc(A.inputDetail.position)} · ${esc(A.inputDetail.deviceId)}`],
    ['观察时间 / 序号', `${esc(A.inputDetail.observedAt || '未记录')} · ${esc(A.inputDetail.observationSequence ?? '未记录')}`],
    ['候选完整性', complete ? `完整集合，共 ${candidates.length} 个原始候选` : `已保留 ${candidates.length} 个；原始总数未确认`],
    ['筛选结论', esc(evidence.outcome || '未记录')],
    ['应用选中码', selected === null ? '未选中' : `<span class="mono" style="white-space:pre-wrap">${esc(selected)}</span>`],
    ['原规则指纹', evidence.ruleHash ? `<span class="mono">${esc(evidence.ruleHash)}</span>` : fallback ? '严格单码兼容示例；没有正则规则指纹' : '旧记录未保存，不补造']
  ]) + section('按原顺序查看候选', list, '保留前导零、空白和重复条目；本页不重新执行当前规则。原始输入与已创建作业是不同证据。') +
    `<details><summary>冻结的模拟输入证据</summary><pre class="code">${esc(JSON.stringify(A.inputDetail, null, 2))}</pre></details>`,
    btn('返回作业', 'aux-close') + btn('复制本次输入', 'aux-input-copy') + btn('查看业务筛选规则', 'aux-input-rule', 'ghost'), true);
}

/* Records are an explicit query snapshot. Editing filters does not mutate the list. */
function auxHistorySnapshot(refresh = false) {
  const A = auxiliary();
  if (!A.historyRows || refresh) {
    A.historyRows = HISTORY.map((row, index) => ({ ...copy(row), date: row.date || '2026-10-20', recordIndex: index, operationId: `demo-operation-${index + 1}` }));
    if (refresh) { A.historySnapshotId++; A.historySnapshotAt = '16:06:30'; }
  }
  return A.historyRows;
}
function auxHistoryRows() {
  const filter = S.historyApplied;
  return auxHistorySnapshot().filter(row =>
    (!filter.query || row.barcode.includes(filter.query)) &&
    (filter.side === 'all' || row.side === filter.side) &&
    (filter.result === 'all' || row.result === filter.result) &&
    (!filter.start || row.date >= filter.start) && (!filter.end || row.date <= filter.end));
}
function auxHistorySelection() { return auxHistoryRows().find(row => row.recordIndex === S.historySelected); }
function renderHistory() {
  const A = auxiliary(), rows = auxHistoryRows(), selected = auxHistorySelection();
  if (S.historyError) S.errors.historyStart = S.historyError;
  const fields = `<div class="form-grid three">${field('包裹条码', 'historyQuery', '保留前导零；支持部分条码')}${field('投递位', 'historySide', '', { options: [['all', '全部位置'], ['left', '左投递位'], ['right', '右投递位']] })}${field('处理结果', 'historyResult', '', { options: [['all', '全部结果'], ['任务已取得', '任务已取得'], ['结果待核对', '结果待核对']] })}${field('开始日期（包含）', 'historyStart', 'Asia/Shanghai · UTC+08:00', { type: 'date' })}${field('结束日期（包含）', 'historyEnd', '按次日零点之前查询', { type: 'date' })}<div class="actions">${btn('查询', 'aux-history-query', 'primary')}${btn('重置筛选', 'aux-history-reset')}</div></div>`;
  const table = rows.length ? `<div class="table-wrap"><table class="table"><thead><tr><th style="width:14%">时间</th><th style="width:12%">投递位</th><th style="width:23%">扫描条码</th><th style="width:19%">处理结果</th><th style="width:22%">目标格口</th><th style="width:10%">详情</th></tr></thead><tbody>${rows.map(row => `<tr class="${row.recordIndex === S.historySelected ? 'selected' : ''}"><td class="mono">${esc(row.time)}</td><td>${row.side === 'left' ? '左投递位' : '右投递位'}</td><td class="mono">${esc(row.barcode)}</td><td>${status(row.result, row.result === '任务已取得' ? 'success' : 'warning')}</td><td class="mono">${auxValue(row.chuteCode)}</td><td>${btn('查看', 'aux-history-select-' + row.recordIndex, 'compact', `aria-label="查看 ${esc(row.barcode)} 的记录"`)}</td></tr>`).join('')}</tbody></table></div>` : `<div class="empty"><h2>没有符合条件的记录</h2><p>试着缩短条码或调整日期与投递位。其他筛选输入仍保留。</p>${btn('重置筛选', 'aux-history-reset')}</div>`;
  const detail = selected ? `<section class="panel selection-details"><div class="row between"><h2>所选记录 · ${esc(selected.barcode)}</h2><span class="small muted">历史任务 · 不作为当前投递依据</span></div><div class="detail-grid">${facts([['扫描来源', `${selected.side === 'left' ? '左位' : '右位'}读码相机`], ['扫描条码', `<span class="mono">${esc(selected.barcode)}</span>`], ['波次号', auxValue(selected.waveNo)], ['RCS 任务号', auxValue(selected.taskId)]])}${facts([['SKU', auxValue(selected.skuId)], ['任务数量', auxValue(selected.quantity)], ['目标格口', auxValue(selected.chuteCode)], ['处理结果', esc(selected.result)]])}</div><div class="actions" style="margin-top:16px">${btn('查看关联诊断', 'aux-history-diagnostic')}${btn('复制记录详情', 'aux-history-copy', 'ghost')}</div><details><summary>原记录标识与数据来源</summary><pre class="code">${esc(JSON.stringify({ simulation: true, operationId: selected.operationId, snapshot: A.historySnapshotId, scanSource: 'camera observation', taskSource: 'RCS response', ...selected }, null, 2))}</pre></details></section>` : rows.length ? notice('info', '请选择一条记录', '筛选后不自动替换已选原件；点击对应行的“查看”。') : '';
  return heading('作业记录', '扫描信息与 RCS 任务信息按原包裹关联，列表由明确查询更新。', btn('导出当前结果', 'aux-history-export', '', auxDisabled(!rows.length, '当前没有可导出的记录'))) + `<section class="panel">${fields}</section>` + (S.historyError ? notice('danger', S.historyError, '查询尚未执行，上一份结果与其他输入均已保留。', btn('定位日期', 'aux-history-focus-date', 'ghost')) : '') + `<div class="row between" style="margin:16px 0 10px"><span class="small muted">${rows.length} 条模拟记录 · 查询快照 ${A.historySnapshotId} · ${A.historySnapshotAt}</span><span class="small muted">新扫码不会打断当前查看${S.historyNewCount ? ` · 有 ${S.historyNewCount} 条新记录待查询` : ''}</span></div>` + table + detail;
}
function auxRunHistoryQuery() {
  if (!S.historyStart || !S.historyEnd || S.historyStart > S.historyEnd) {
    S.historyError = '开始日期不能晚于结束日期，请核对日期范围。';
    S.errors.historyStart = S.historyError; render(); auxFocusField('historyStart'); return;
  }
  S.historyError = ''; delete S.errors.historyStart;
  S.historyApplied = { query: S.historyQuery, side: S.historySide, result: S.historyResult, start: S.historyStart, end: S.historyEnd };
  auxHistorySnapshot(true); S.historyNewCount = 0; render();
  toast('已更新本次模拟查询快照；选择与详情仍对应原记录。');
}

/* Diagnostics read existing lifecycle / heartbeat evidence. They do not send a heartbeat. */
function auxHeartbeatSnapshot() {
  const target = S.runtime.rcs.target, evidence = S.rcsHeartbeat;
  if (!evidence) return { target, httpStatus: null, checkedAt: '', failure: '', recorded: false, currentTarget: true, snapshotFresh: S.snapshotFresh, applicationAcknowledged: null, synthetic: true };
  return { ...copy(evidence), target: evidence.target || target, recorded: true,
    currentTarget: !evidence.target || evidence.target === target,
    snapshotFresh: S.snapshotFresh, applicationAcknowledged: null, synthetic: true };
}
function auxHeartbeatCheckRow() {
  const evidence = auxHeartbeatSnapshot();
  const boundary = '只读取最近一次周期心跳记录；此页不新发心跳。HTTP 交付不证明业务确认，也不清偿原请求。';
  if (!evidence.recorded) return ['最近心跳 HTTP 证据', boundary, '尚无可读记录', ''];
  const at = evidence.checkedAt ? ` · ${evidence.checkedAt}` : '';
  if (!evidence.currentTarget) return ['最近心跳 HTTP 证据', `记录目标 ${evidence.target}${at}，与当前目标不同；保留为历史，不能确认当前连接。`, '旧目标记录', 'warning'];
  if (!evidence.snapshotFresh) return ['最近心跳 HTTP 证据', `最近记录${at}已随快照失效；等待新的组件快照。${boundary}`, '当前未确认', 'warning'];
  if (evidence.failure) return ['最近心跳 HTTP 证据', `${evidence.failure}${at}。未重试业务原请求；下一周期由原组件生命周期管理。`, '心跳交付失败 · 模拟', 'danger'];
  const code = Number(evidence.httpStatus);
  if (code >= 200 && code < 300) return ['最近心跳 HTTP 证据', `HTTP ${code}${at}（模拟）。${boundary}`, 'HTTP 已交付 · 模拟', 'info'];
  return ['最近心跳 HTTP 证据', `${evidence.httpStatus ? `HTTP ${evidence.httpStatus}${at}（模拟）。` : '尚无明确 HTTP 结果。'}${boundary}`, evidence.httpStatus ? 'HTTP 未成功' : '未确认', evidence.httpStatus ? 'danger' : ''];
}
function auxDiagnosticTarget(object = S.diagnosticObject) {
  return object === 'rcs' ? S.runtime.rcs.target : S.runtime[object].ip;
}
function auxDiagnosticName(object = S.diagnosticObject) { return object === 'rcs' ? 'RCS 任务组件' : object === 'left' ? '左位读码相机' : '右位读码相机'; }
function auxDiagnosticResult(object = S.diagnosticObject) {
  const A = auxiliary(), result = A.diagnosticResults[object];
  if (result && result.target === auxDiagnosticTarget(object)) return result;
  if (S.diagnosticChecked && !result) return { target: auxDiagnosticTarget(object), reachable: object === 'rcs' ? S.rcsReachable : S[object + 'Online'], checkedAt: '16:07:00', synthetic: true };
  return null;
}
function auxDiagnosticEvidence() {
  const object = S.diagnosticObject, result = auxDiagnosticResult(), target = auxDiagnosticTarget();
  if (object === 'rcs') {
    let addressValid = false;
    try { const u = new URL(target); addressValid = ['http:', 'https:'].includes(u.protocol) && u.pathname === '/rcs/sorter/api' && !u.username && !u.password && !u.search && !u.hash; } catch (_) { /* Invalid address is a field result. */ }
    const lastAccepted = ['left', 'right'].filter(side => S[side].status === 'success').map(side => `${side === 'left' ? '左' : '右'}位 ${S[side].time}`);
    return checks([
      ['接口地址格式', '仅核对 HTTP(S) 地址和 /rcs/sorter/api 路径，不建立业务请求。', addressValid ? '格式通过' : '格式错误', addressValid ? 'success' : 'danger'],
      ['本地生命周期', '本项仅表示适配器启动；组件 Health 还包含下方最近心跳证据，不能用本项概括全部健康。', S.rcsClientReady ? '本地已启动' : '本地未启动', S.rcsClientReady ? 'info' : 'warning'],
      auxHeartbeatCheckRow(),
      ['地址与端口可达', result ? `检查目标：${target} · ${result.checkedAt}（模拟）` : '尚未检查。可执行只读传输检查，不发送 POST 或新的心跳。', result ? result.reachable ? '传输可达 · 模拟' : '连接失败 · 模拟' : '未检查', result ? result.reachable ? 'success' : 'danger' : ''],
      ['业务响应有效性', lastAccepted.length && S.snapshotFresh ? `已有已匹配回执：${lastAccepted.join('、')}；历史回执不证明此刻上游健康。` : '当前没有可确认的新业务回应；连接可达不能代替本层。', lastAccepted.length && S.snapshotFresh ? '有历史证据' : '未确认', ''],
      ['原请求结果查询', '当前协议未提供权威查询接口；不以发送新业务请求代替查询。', '接口未提供', 'warning']
    ]);
  }
  const online = S[object + 'Online'], lane = S[object], name = object === 'left' ? '左位' : '右位';
  return checks([
    ['实例与位置', `${S.runtime[object].instanceId} · ${name} · 与另一位使用相同驱动类型、独立参数。`, '对象已定位', 'info'],
    ['相机 IP 格式', `已读取内容：${target}。此驱动没有配置“连接端口”字段。`, validIPv4(target) ? '格式通过' : '格式错误', validIPv4(target) ? 'success' : 'danger'],
    ['驱动报告的连接状态', '读取已有资源状态，不在运行旁启动第二个 SDK 探针抢占设备。', S.snapshotFresh ? online ? '驱动已连接 · 模拟' : '连接中断 · 模拟' : '状态已失效', S.snapshotFresh && online ? 'info' : 'warning'],
    ['收到有效条码', lane.barcode ? `最后条码：${lane.barcode} · ${lane.time}；不表示此刻持续收到数据。` : '尚未收到有效条码。', lane.barcode && S.snapshotFresh ? '有条码记录' : '未确认', ''],
    ['业务准入', '全局条件满足后按投递位判断；本位未知请求或设备问题，不自行扩展为另一位的故障。', S.paused ? '全站暂停' : !online || !S.snapshotFresh || lane.status === 'unknown' ? '本位受阻' : '以作业状态为准', S.paused || !online || !S.snapshotFresh || lane.status === 'unknown' ? 'warning' : '']
  ]);
}
function renderDiagnostics() {
  const A = auxiliary(), object = S.diagnosticObject, result = auxDiagnosticResult(), target = auxDiagnosticTarget();
  const checking = !!A.diagnosticBusy[object], unresolved = ['left', 'right'].filter(side => S[side].status === 'unknown');
  const origin = A.diagnosticOrigin;
  const issue = object === 'rcs' ? (!S.rcsReachable ? notice('danger', 'RCS 连接中断 · 新任务获取受影响', '相机仍可产生扫描观察；是否接收新包以供包台准入状态为准。当前未确认结果的包裹必须保留，连接恢复不会补成业务成功。') : unresolved.length ? notice('warning', '传输状态与原请求结果分别处理', `${unresolved.map(s => s === 'left' ? '左位' : '右位').join('、')}有结果待核对。请保留原包裹及原请求；当前接口没有权威结果查询。全局条件满足后可恢复具备准入条件的位置，待核对本位继续受阻。`) : notice('info', '本地与上游状态分层展示', '已有任务回应仅证明对应请求，不由一个“正常”标签推断全部链路。')) : !S[object + 'Online'] ? notice('warning', `${auxDiagnosticName()}连接中断`, '故障归属此实例；另一位是否继续工作以供包台实际阻断范围为准。重连操作需要全站软件暂停。') : notice('info', '查看已绑定实例的实际证据', '连接、读到条码及准入分开显示。此页不新建相机资源，也不发送真实业务。');
  const original = origin ? notice('info', `${origin.kind === 'current' ? '来自本次作业' : '来自历史记录'} · ${esc(origin.barcode)}`, `${origin.side === 'left' ? '左位' : '右位'} · ${esc(origin.time)} · ${esc(origin.result)}。${origin.kind === 'current' ? '按本次原件处理，不重发未知请求。' : '此上下文不替代当前作业状态。'}`, btn(origin.kind === 'current' ? '返回作业' : '返回这条记录', origin.kind === 'current' ? 'aux-return-work' : 'aux-diag-back-history', 'ghost')) : unresolved.length ? `<div class="panel">${section('待处理的原件', unresolved.map(side => facts([['位置与条码', `${side === 'left' ? '左投递位' : '右投递位'} · <span class="mono">${esc(S[side].barcode)}</span>`], ['原请求编号', `<span class="mono">${esc(S[side].requestId)}</span>`], ['当前处置', '结果未知；保留原件，不自动重发。']])).join('<hr style="border:0;border-top:1px solid var(--line);margin:14px 0">'))}<div class="actions">${btn('查看原请求技术详情', 'aux-diag-original')}${btn('生成支持摘要', 'aux-diag-support', 'ghost')}</div></div>` : '';
  return heading('诊断与恢复', '先识别对象和影响，再按已验证的层级处理。', btn('返回作业', 'aux-return-work')) + auxTabs([['rcs', 'RCS 任务组件'], ['left', '左位相机'], ['right', '右位相机']], object, 'aux-diag-object-') + issue + (A.diagnosticFeedback ? notice('info', esc(A.diagnosticFeedback)) : '') + `<div class="detail-grid"><section class="panel"><div class="row between"><h2>${esc(auxDiagnosticName())}</h2>${status(checking ? '正在检查' : result ? '检查已完成' : '尚未执行检查', checking ? 'info' : '')}</div>${facts([['配置来源', S.ledger.confirmed && S.snapshotFresh ? '当前已确认运行内容' : '最后保存的运行内容 · 当前状态需核实'], ['目标', `<span class="mono">${esc(target)}</span>`], ['实例', `<span class="mono">${esc(object === 'rcs' ? S.runtime.rcs.instanceId : S.runtime[object].instanceId)}</span>`], ['修改入口', object === 'rcs' ? '参数配置 → 组件配置 → RCS 单一连接' : '参数配置 → 驱动配置 → 对应读码实例']])}${checking ? notice('info', '正在读取本次检查结果', `目标已固定为 ${esc(target)}；此操作不保存、不应用配置、不发送业务请求。`) : ''}<div class="actions" style="margin-top:18px">${btn(checking ? '正在检查…' : object === 'rcs' ? '检查传输连接（模拟）' : '读取驱动状态（模拟）', 'aux-diag-check', 'primary', auxDisabled(checking, '本对象的检查正在进行'))}${btn('定位相关参数', 'aux-diag-config')}${btn('复制技术详情', 'aux-diag-copy', 'ghost')}</div><details><summary>维护操作</summary><p class="help">先执行现有维护身份、全站暂停和资源排空守卫。重新连接不应用已保存待启动的配置，操作结束保持暂停。</p><div class="actions" style="margin-top:12px">${btn(object === 'rcs' ? '重新初始化本地客户端' : '重新连接此相机', 'aux-diag-reconnect', '', auxDisabled(A.reconnectBusy, '本次重连尚未完成'))}</div></details><details><summary>原型故障注入 · 仅改变模拟状态</summary><p class="help">用于走查外部问题修复后的界面；正式客户端不含这些按钮。</p>${btn('模拟现场修复', 'aux-demo-repair', 'compact')}${btn('模拟连接中断', 'aux-demo-break', 'compact')}</details></section><section class="panel"><h2>检查结果与证明范围</h2>${auxDiagnosticEvidence()}</section></div>${original ? `<div style="margin-top:16px">${original}</div>` : ''}`;
}
function auxRunDiagnosticCheck() {
  const A = auxiliary(), object = S.diagnosticObject, target = auxDiagnosticTarget(object);
  if (A.diagnosticBusy[object]) return;
  const serial = ++A.diagnosticSerial;
  A.diagnosticBusy[object] = serial; A.diagnosticFeedback = ''; render();
  demoDelay(() => {
    if (A.diagnosticBusy[object] !== serial) return;
    delete A.diagnosticBusy[object];
    const reachable = object === 'rcs' ? S.rcsReachable : S[object + 'Online'];
    A.diagnosticResults[object] = { target, reachable, checkedAt: '16:07:00', synthetic: true, evidence: object === 'rcs' ? 'transport only; existing heartbeat is read-only; no POST or new heartbeat' : 'existing driver snapshot; no new SDK owner' };
    if (S.diagnosticObject === object && auxDiagnosticTarget(object) === target) S.diagnosticChecked = true;
    if (S.view === 'diagnostics') render();
    toast(`已完成${auxDiagnosticName(object)}的模拟检查；原业务结果未改变。`);
  }, 750);
}
function auxReconnectDialog() {
  const A = auxiliary(), object = A.reconnectObject;
  const ready = S.paused && S.maintenance;
  openDialog(`${object === 'rcs' ? '重新初始化' : '重新连接'} · ${auxDiagnosticName(object)}`,
    notice('warning', '此维护操作影响整个供包台', '必须先软件暂停新准入、确认维护身份并等待现有资源排空。软件暂停不是设备急停；未知请求保留，操作后仍需确认恢复条件。') + facts([['本次对象', esc(auxDiagnosticName(object))], ['使用配置', '当前运行内容；不应用 Pending 配置'], ['运行状态', S.paused ? '已暂停新准入' : '当前尚未暂停'], ['维护身份', S.maintenance ? '模拟认证有效' : '尚未验证（原型不收集真实凭据）']]) + field('维护原因', 'aux.reconnectReason', '说明为何重新连接此对象；跟随本次维护记录。') + (A.reconnectBusy ? notice('info', '正在等待排空并重新连接', '相关操作暂不可重复执行；页面继续响应。') : ''),
    btn('返回诊断', 'aux-close') + (ready ? btn(A.reconnectBusy ? '正在执行…' : '开始维护重连（模拟）', 'aux-diag-reconnect-run', 'primary', auxDisabled(A.reconnectBusy, '维护操作正在执行')) : btn('模拟维护认证并暂停', 'aux-diag-reconnect-prepare', 'primary')));
}
function auxRunReconnect() {
  const A = auxiliary(), object = A.reconnectObject;
  if (A.reconnectBusy) return;
  if (!S.paused || !S.maintenance) { auxReconnectDialog(); return; }
  if (!A.reconnectReason.trim()) { S.errors['aux.reconnectReason'] = '请填写本次维护原因。'; auxReconnectDialog(); auxFocusField('aux.reconnectReason'); return; }
  delete S.errors['aux.reconnectReason']; A.reconnectBusy = true; auxReconnectDialog();
  const reconnectDialog = document.querySelector('#dialogs [role="dialog"]');
  const reconnectReason = A.reconnectReason;
  demoDelay(() => {
    A.reconnectBusy = false; S.paused = true;
    if (object === 'rcs') {
      S.rcsClientReady = true;
      A.diagnosticFeedback = 'RCS 本地客户端已重新初始化（模拟），全站仍暂停。上游传输与原请求结果未因此确认。';
    } else {
      A.diagnosticFeedback = S[object + 'Online'] ? `${auxDiagnosticName(object)}已重新连接（模拟），全站仍暂停；原包裹责任保留。` : `${auxDiagnosticName(object)}仍不可达（模拟），重连未完成；全站保持暂停，原配置和请求保留。`;
    }
    delete A.diagnosticResults[object]; S.diagnosticChecked = false;
    S.audit.push({ simulation: true, action: 'reconnect', object, reason: reconnectReason, pausedAfter: true, revision: S.ledger.revision, status: '维护重连结束，仍暂停' });
    // A later dialog belongs to its own task and must survive this completion.
    if (reconnectDialog?.isConnected) closeDialog();
    if (S.view === 'diagnostics') render(); toast(A.diagnosticFeedback);
  }, 900);
}
function auxDiagnosticPayload() {
  const A = auxiliary();
  return { simulation: true, application: 'sportmaster', object: S.diagnosticObject, target: auxDiagnosticTarget(), checks: auxDiagnosticResult(), localClientReady: S.rcsClientReady, heartbeatHttpEvidence: auxHeartbeatSnapshot(), upstreamHealthContract: false, queryCapability: false, pendingConfiguration: S.ledger.pending, paused: S.paused, originalContext: A.diagnosticOrigin || ['left', 'right'].map(side => ({ side, barcode: S[side].barcode, requestId: S[side].requestId, status: S[side].status })), note: 'Existing heartbeat evidence only. This view sent no heartbeat / business request and did not replay or resolve an original.' };
}

/* Help preview is bounded, immutable and expires; download uses that same preview. */
function auxSupportPayload() {
  return { simulation: true, generatedAtUtc: new Date().toISOString(), scope: { start: S.supportStart, end: S.supportEnd, timezone: 'Asia/Shanghai', maximumDays: 7, maximumEvents: 256 }, previewValiditySeconds: 300, application: 'sportmaster', configuration: { revision: S.ledger.revision, pending: !!S.ledger.pending, confirmed: S.ledger.confirmed }, installed: ['idmvs-reader', 'sportmaster-rcs-task'], connectionSummary: { left: S.leftOnline, right: S.rightOnline, rcsLocalClient: S.rcsClientReady, rcsHeartbeat: auxHeartbeatSnapshot(), rcsHeartbeatApplicationAcknowledgement: 'unconfirmed' }, events: [{ type: 'diagnostic-preview', note: 'Synthetic status summary only; no credentials, raw parcel data or business payload.' }], truncated: false };
}
function auxEnsureSupportPreview() {
  const A = auxiliary();
  if (S.supportPreview && !A.supportData) { A.supportData = auxSupportPayload(); S.supportExpires = Date.now() + 300000; }
  return A.supportData;
}
function renderHelp() {
  const A = auxiliary(), data = auxEnsureSupportPreview(), expired = data && Date.now() >= S.supportExpires;
  if (S.supportError) S.errors.supportStart = S.supportError;
  const preview = data ? `<section class="panel"><div class="row between"><h2>诊断摘要预览</h2>${status(A.supportStale ? '筛选已修改' : expired ? '预览已过期' : '预览可导出', A.supportStale || expired ? 'warning' : 'info')}</div>${facts([['包含内容', '业务模块与配置状态、已安装驱动/组件、连接摘要、有限诊断事件'], ['排除内容', '凭据、完整业务报文、原始条码、包裹图片和完整数据库'], ['采集范围', `${esc(data.scope.start)} 至 ${esc(data.scope.end)} · UTC+08:00`], ['数量与有效期', `${data.events.length} / 256 条事件 · 生成后 5 分钟内可导出同一预览`], ['截断', data.truncated ? '已达到上限，结果存在截断' : '本次模拟预览未截断']])}<details><summary>查看将导出的具体内容</summary><pre class="code">${esc(JSON.stringify(data, null, 2))}</pre></details><div class="actions" style="margin-top:15px">${btn('导出此份模拟摘要', 'aux-support-export', 'primary', auxDisabled(expired || A.supportStale, expired ? '预览已超过 5 分钟，请重新生成' : '筛选已修改，请重新预览'))}${btn('重新生成预览', 'aux-support-preview')}</div></section>` : notice('info', '先预览，再导出', '采集范围最多 7 天、256 条事件。预览生成后 5 分钟内有效；不是“只采集最近 5 分钟”。');
  return heading('帮助与支持', '保留对象上下文，先定位问题，再导出必要摘要。', btn('返回作业', 'aux-return-work')) + `<div class="detail-grid"><section class="panel"><h2>常见任务</h2>${checks([['RCS 无返回或连接失败', '分别查看本地生命周期、最近心跳 HTTP、传输与业务回执。', '诊断入口', ''], ['修改相机 IP', '在驱动配置中选择左/右实例；不要更改另一路参数。', '对象定位', ''], ['保存后尚未生效', '查看统一配置账及 Pending 内容；受控重启与重连不同。', '配置摘要', '']])}<div class="actions" style="margin-top:16px">${btn('诊断 RCS', 'aux-help-diagnostic')}${btn('驱动配置', 'aux-help-driver')}${btn('配置生效说明', 'aux-help-effect', 'ghost')}</div><details><summary>安装、首次启动与 Windows 环境</summary><p class="help">单一安装窗口自动准备文件，再显示选项与本地摘要；点击安装时才由 Windows 请求一次授权，同窗完成后以普通权限启动客户端。</p><p class="help">新工位首次启动进入独立配置，已有部署按原运行或恢复流程打开。日常修改使用系统、业务模块、驱动、组件配置；首次配置不是侧栏标签。</p><p class="help">优先功能目标为 Windows 10 22H2 家庭版 / 专业版 x64、Windows 11 x64。OS、CPU、缩放与设备 SDK 分别验收；原型不读取真实环境。正式安装器已有可复制的只读环境信息。</p>${btn('查看安装流程演示', 'aux-help-install', 'compact')}</details></section><section class="panel"><h2>生成必要诊断信息</h2><div class="form-grid">${field('开始时间', 'supportStart', 'Asia/Shanghai · UTC+08:00', { type: 'datetime-local' })}${field('结束时间', 'supportEnd', '最大跨度 7 天', { type: 'datetime-local' })}</div>${S.supportError ? notice('danger', S.supportError, '其他输入已保留，尚未生成新预览。') : ''}<div class="actions" style="margin-top:16px">${btn(A.supportPhase === 'loading' ? '正在生成…' : '生成摘要预览', 'aux-support-preview', 'primary', auxDisabled(A.supportPhase === 'loading', '正在生成此范围的预览'))}</div><p class="help">原型只生成本页合成数据，不读取实际文件或上传信息。</p></section></div><div style="margin-top:16px">${preview}</div>`;
}
function auxRunSupportPreview() {
  const A = auxiliary(); if (A.supportPhase === 'loading') return;
  const start = Date.parse(S.supportStart + ':00+08:00'), end = Date.parse(S.supportEnd + ':00+08:00');
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start || end - start > 7 * 86400000) {
    S.supportError = '请填写有效时间；结束不早于开始，范围不能超过 7 天。'; S.errors.supportStart = S.supportError; render(); auxFocusField('supportStart'); return;
  }
  delete S.errors.supportStart; S.supportError = ''; A.supportPhase = 'loading';
  const payload = auxSupportPayload(); render();
  demoDelay(() => {
    A.supportPhase = ''; A.supportData = copy(payload); S.supportPreview = true; S.supportExpires = Date.now() + 300000;
    A.supportStale = payload.scope.start !== S.supportStart || payload.scope.end !== S.supportEnd;
    if (S.view === 'help') render(); toast('模拟摘要预览已生成；导出将使用同一份内容。');
  }, 650);
}

/* Maintenance uses bounded task flows rather than a second management platform. */
function auxMaintenanceNotice() {
  return notice('info', '系统维护 · 不改变当前业务归属', '本机身份在系统配置编辑，任务规则在业务模块配置编辑，相机业务编码在对应驱动实例编辑；RCS 组件只读引用这些值。模板、完整备份与隔离恢复各有独立范围。');
}
function auxTemplatePreview() {
  const A = auxiliary();
  if (!A.templateValues) A.templateValues = { business: { queueCapacity: '96', timeoutMs: '1800' } };
  if (!A.templateBase) A.templateBase = JSON.stringify(S.draft);
  if (!A.templateBefore) A.templateBefore = copy(S.draft);
  const changes = Object.entries(A.templateValues).flatMap(([object, fields]) => Object.entries(fields).map(([key, value]) => ({ object, key, value })));
  return `<div class="table-wrap"><table class="table compare"><thead><tr><th>适用对象 / 参数</th><th>当前编辑值</th><th>模板示例值</th></tr></thead><tbody>${changes.map(({ object, key, value }) => `<tr><td>${esc(OBJECTS[object])} · ${esc(LABELS[key])}</td><td class="mono">${esc(A.templateBefore[object][key])}</td><td class="mono">${esc(value)}</td></tr>`).join('')}</tbody></table></div>`;
}

function renderMaintenance() {
  const A = auxiliary();
  let tab = A.maintenanceTab;
  if (S.migrationConflict && !A.migrationVisited) tab = 'migration';
  if (S.importPreview && !A.templateVisited && !S.migrationConflict) tab = 'templates';
  const tabs = auxTabs([['backup', '备份与恢复'], ['templates', '配置模板'], ['changes', '修改记录'], ['migration', '旧配置核对'], ['advanced', '技术详情']], tab, 'aux-maint-tab-');
  let body = '';
  if (tab === 'backup') {
    const backupDirectory = S.saved.system.backupEnabled !== 'true' || !String(S.saved.system.backupDirectory || '').trim()
      ? '计划备份已关闭；手动备份需另选目录（此处为模拟）'
      : `<span class="mono">${esc(S.saved.system.backupDirectory)}</span>`;
    body = `<div class="detail-grid"><section class="panel"><h2>完整备份</h2>${facts([['对象范围', '本站完整恢复集：配置、数据库及恢复所需同伴内容'], ['当前备份目录', backupDirectory], ['执行条件', '维护认证、全站软件暂停、原任务与存储守卫通过']])}${S.backupCreated ? notice('success', '模拟备份流程已完成', '原型仅生成备份结构摘要，未读取或创建实际备份文件。完整备份的可恢复性需 Windows 与真实存储验证。') : A.backupPhase ? notice('info', '正在生成模拟备份摘要', '此页继续响应；不会访问真实目录。') : ''}<div class="actions" style="margin-top:16px">${btn(A.backupPhase ? '正在备份…' : '创建完整备份（模拟）', 'aux-backup-create', 'primary', auxDisabled(!!A.backupPhase, '本次模拟备份正在进行'))}${S.backupCreated ? btn('下载示例目录清单', 'aux-backup-download') : ''}${btn('修改备份策略', 'aux-backup-settings', 'ghost')}</div><p class="help">配置模板只含适用白名单参数，不能用于恢复未完成业务。</p></section><section class="panel"><h2>隔离恢复</h2>${field('恢复来源', 'aux.restoreSource', '选择本页演示备份，不读取真实文件。', { readonly: true })}<div class="actions" style="margin-top:8px">${btn('选择演示备份', 'aux-restore-source', 'compact')}</div><div style="margin-top:12px">${field('隔离恢复目录', 'aux.restoreDirectory', '必须与当前数据和备份位置分开；恢复不覆盖当前运行数据。')}</div>${S.restoreReady ? notice('info', '模拟恢复前检查通过', '演示清单与目标目录已检查；接管责任和恢复放行仍是独立任务。') : ''}${A.restored ? notice('warning', '隔离恢复演示已完成 · 全站仍暂停', '仅展示恢复后的核对状态，原配置、原任务和当前数据均未被改写。') : ''}<div class="actions" style="margin-top:15px">${btn('检查恢复条件', 'aux-restore-check')}${btn(A.restorePhase ? '正在恢复…' : '执行隔离恢复（模拟）', 'aux-restore-run', '', auxDisabled(!S.restoreReady || !!A.restorePhase, !S.restoreReady ? '先选择演示备份并检查隔离目录' : '正在执行本次模拟恢复'))}</div></section></div>`;
  } else if (tab === 'templates') {
    body = `<section class="panel"><h2>同版安全配置模板</h2><p>用于复用适用业务处理策略、本机保留策略和精确提供者选择。模板导入先进入当前候选编辑，不直接保存或生效。</p>${notice('info', '模板不包含现场身份和连接信息', '排除站点/设备/IP/目标地址/凭据/请求编号/备份路径及业务数据。本演示模板只修改业务模块中的两个适用处理策略。')}<div class="actions">${btn('预览示例模板差异', 'aux-template-preview', 'primary')}${btn('导出安全模板示例', 'aux-template-export')}</div>${S.importPreview ? `<div style="margin-top:17px">${auxTemplatePreview()}<p class="help">以上 96 / 1800 为演示值，不是现场默认值。适用业务模块：Sportmaster；生产导入需同版身份核验。</p><div class="actions" style="margin-top:13px">${btn('将上述差异加入编辑', 'aux-template-apply', 'primary')}${btn('取消此份预览', 'aux-template-cancel')}</div></div>` : ''}</section>`;
  } else if (tab === 'changes') {
    const rows = S.audit.slice();
    body = `<section class="panel"><h2>配置与维护记录</h2><p class="help">记录本次模拟交互；配置修订属于整份配置信封，各对象没有独立运行版本号。</p>${facts([['当前账修订', esc(S.ledger.revision)], ['待生效内容', S.ledger.pending ? '存在待受控启动候选' : '无待生效候选'], ['运行确认', S.ledger.confirmed ? '已确认内容，健康仍按实时证据判断' : '当前运行内容尚未确认']])}<div style="margin-top:16px">${rows.length ? `<div class="table-wrap"><table class="table"><thead><tr><th style="width:28%">动作</th><th style="width:28%">对象</th><th>记录说明</th></tr></thead><tbody>${rows.map(row => `<tr><td>${esc(row.action || row.type || '配置变更')}</td><td>${esc(OBJECTS[row.object] || row.object || '完整配置')}</td><td>${esc(row.reason || row.note || '本次离线模拟记录')}</td></tr>`).join('')}</tbody></table></div>` : notice('info', '本次演示尚无修改记录', '完成配置保存或维护操作后，记录会出现在这里。历史修订不能直接覆盖正在运行的内容。')}</div><div class="actions" style="margin-top:14px">${btn('查看当前差异', 'aux-changes-diff')}${btn('恢复最后确认内容为草稿', 'aux-changes-restore')}</div></section>`;
  } else if (tab === 'migration') {
    const leftStation = A.legacyLeftStation || 'IS-L-L1-04', rightStation = A.legacyRightStation || 'IS-R-L1-04';
    body = `<section class="panel"><h2>旧字段到新对象的核对</h2>${notice(S.migrationConflict ? 'warning' : 'info', S.migrationConflict ? '旧左右供包台编码不同，不能自动合并' : '按参数语义迁移，不复制旧表单布局', '新系统配置只维护一个供包台编码。相机业务编码各自归对应实例；组件只读引用。下面是用于说明冲突的合成旧配置。')}${facts([['旧左侧 stationCode', `<span class="mono">${esc(leftStation)}</span>`], ['旧右侧 stationCode', `<span class="mono">${esc(rightStation)}</span>`]])}<div class="form-grid" style="margin-top:16px">${field('现场关系核对', 'aux.migrationAcknowledged', '属于不同供包台时应保留旧配置并停止合并。', { options: [['false', '尚未核实'], ['same', '确认属于同一供包台（演示）'], ['different', '属于不同供包台，不合并']] })}${field('统一后的供包台编码', 'aux.migrationStation', '只形成系统配置草稿，不立即保存或生效。')}</div>${A.migrationAcknowledged === 'different' ? notice('warning', '保持旧配置，停止合并', '当前业务模块的单站模型不能把两个不同现场站点合并为一个。先由维护人员明确正确安装与业务实例。') : ''}<div class="actions" style="margin-top:16px">${btn('将核对结果加入编辑', 'aux-migration-apply', '', auxDisabled(A.migrationAcknowledged === 'different', '不同供包台不能合并为同一业务实例'))}${btn('查看原 58 项字段清单', 'aux-old-fields', 'ghost')}</div><details><summary>迁移范围</summary><p class="help">原字段与状态逐项保留迁移去向；旧 JSON 仅供核对。未知值不补默认，模块间能力不混用，未确认冲突阻止完整配置保存。</p></details></section>`;
  } else {
    body = `<section class="panel"><h2>高级详情 · 只读</h2>${notice('info', '结构化表单是当前唯一编辑来源', '技术键名、实例 ID 与 JSON 用于核对；本页不建立第二份配置，也不允许绕过字段校验编辑。')}<div class="actions">${btn('复制当前候选 JSON', 'aux-advanced-copy')}${btn('查看原 58 项字段清单', 'aux-old-fields')}${btn('查看已安装组件', 'aux-advanced-components', 'ghost')}</div><pre class="code" style="max-height:430px">${esc(JSON.stringify({ simulation: true, loadedRevision: S.baseRevision, draft: S.draft }, null, 2))}</pre><p class="help">真实敏感字段应遮蔽；本原型只包含合成值，不收集实际凭据。</p></section>`;
  }
  return heading('维护与迁移', '从系统配置进入，按明确范围备份、恢复或核对旧值。', btn('返回系统配置', 'aux-return-system')) + tabs + auxMaintenanceNotice() + body;
}
function auxMaintenanceGuard(nextAction) {
  const A = auxiliary();
  if (S.paused && S.maintenance) return true;
  A.pendingMaintenanceAction = nextAction;
  openDialog('准备本次维护操作', notice('warning', '需全站软件暂停与维护认证', '此操作遵循现有守卫；暂停新准入并等待资源排空。软件暂停不代表物理设备急停。原型只模拟认证，不收集真实口令。') + facts([['新准入状态', S.paused ? '已暂停' : '尚未暂停'], ['维护身份', S.maintenance ? '模拟有效' : '尚未认证']]), btn('返回', 'aux-close') + btn('模拟维护认证并暂停', 'aux-maint-prepare', 'primary'));
  return false;
}
function auxPathsOverlap(left, right) {
  const normalize = value => value.toLowerCase().replaceAll('/', '\\').replace(/[\\]+$/, '');
  const a = normalize(left), b = normalize(right);
  return a === b || a.startsWith(b + '\\') || b.startsWith(a + '\\');
}
function auxCheckRestore() {
  const A = auxiliary();
  delete S.errors['aux.restoreSource']; delete S.errors['aux.restoreDirectory']; S.restoreReady = false;
  if (!A.restoreSource) S.errors['aux.restoreSource'] = '请先选择一份演示备份。';
  const path = A.restoreDirectory;
  if (!/^[A-Za-z]:[\\/].+/.test(path) || [A.installDataDirectory, A.installDirectory, S.saved.system.backupDirectory].some(p => auxPathsOverlap(p, path))) S.errors['aux.restoreDirectory'] = '请选择独立的本机隔离目录，不能使用当前数据或备份目录。';
  if (S.errors['aux.restoreSource'] || S.errors['aux.restoreDirectory']) { render(); auxFocusField(S.errors['aux.restoreSource'] ? 'aux.restoreSource' : 'aux.restoreDirectory'); return; }
  S.restoreReady = true; A.restoreCheckedDirectory = path; render(); toast('模拟恢复前检查完成；尚未修改当前数据或开始恢复。');
}

/* Ordinary installer specimen; no daily sidebar and no real disk installation. */
/* Installation keeps one view and one immutable reviewed attempt. No actual UAC or disk work. */
function auxInstallCandidate() {
  const A = auxiliary();
  return { program: A.installDirectory, data: A.installDataDirectory,
    desktopShortcut: A.installDesktopShortcut, menuShortcut: A.installMenuShortcut };
}
function auxInstallBusy() { return ['preparing', 'awaiting-authorization', 'installing', 'checking-receipt'].includes(auxiliary().installPhase); }
function auxPrepareInstall(renderNow = true) {
  const A = auxiliary();
  if (auxInstallBusy() || A.installPhase === 'receipt-unknown') return;
  const serial = A.installSerial = (A.installSerial || 0) + 1;
  A.installPhase = 'preparing'; A.installPrepared = false; A.installReview = null;
  A.installError = ''; A.installReceipt = null; S.installStep = 1; S.installDone = false;
  demoDelay(() => {
    if (S.aux !== A || A.installSerial !== serial || A.installPhase !== 'preparing') return;
    A.installPrepared = A.installPreparationOutcome !== 'failed';
    A.installPhase = A.installPrepared ? '' : 'prepare-failed';
    if (S.view === 'install') render();
  }, 700);
  if (renderNow) render();
}
function auxReviewInstall() {
  const A = auxiliary();
  if (auxInstallBusy() || A.installPhase === 'receipt-unknown' || !A.installPrepared) return;
  const paths = [A.installDirectory, A.installDataDirectory];
  if (paths.some(value => !/^[A-Za-z]:[\\/].+/.test(value)) || auxPathsOverlap(paths[0], paths[1])) {
    A.installError = '请填写两个有效且独立的本机程序、数据目录。';
    S.errors['aux.installDirectory'] = A.installError; S.installStep = 1; render(); auxFocusField('aux.installDirectory'); return;
  }
  A.installError = ''; delete S.errors['aux.installDirectory'];
  A.installReview = copy(auxInstallCandidate()); A.installPhase = 'review'; A.installReceipt = null;
  S.installStep = 2; render();
}
function auxBeginInstallAuthorization() {
  const A = auxiliary();
  if (S.installStep !== 2 || auxInstallBusy() || A.installPhase === 'receipt-unknown' || S.installDone) return;
  if (['blocked', 'failed'].includes(A.installPhase)) { toast('请先查看原因并重新检查安装摘要；已有选项保留。'); return; }
  if (!A.installReview) {
    // A named screenshot scene may start directly at its already-reviewed summary.
    A.installPrepared = true; auxReviewInstall();
    if (!A.installReview) return;
  }
  if (JSON.stringify(A.installReview) !== JSON.stringify(auxInstallCandidate())) {
    A.installPhase = ''; A.installReview = null; S.installStep = 1; render();
    toast('安装选项已变化，请重新检查摘要；尚未请求授权。'); return;
  }
  A.installPhase = 'awaiting-authorization'; A.installReceipt = null;
  S.installStep = 2; S.installDone = false; render();
}
function auxCompleteInstall() {
  const A = auxiliary();
  A.installPhase = 'completed'; A.installReceipt = { simulation: true, status: 'Completed', reviewed: copy(A.installReview || auxInstallCandidate()) };
  S.installStep = 3; S.installDone = true;
}
function auxRunAuthorizedInstall() {
  const A = auxiliary();
  if (A.installPhase !== 'awaiting-authorization') return;
  const serial = A.installSerial = (A.installSerial || 0) + 1;
  const outcome = A.installOutcome || 'completed', reviewed = copy(A.installReview);
  A.installPhase = 'installing'; render();
  demoDelay(() => {
    if (S.aux !== A || A.installSerial !== serial || A.installPhase !== 'installing') return;
    if (outcome === 'completed') auxCompleteInstall();
    else {
      A.installPhase = ['blocked', 'receipt-unknown', 'failed'].includes(outcome) ? outcome : 'receipt-unknown';
      A.installReceipt = { simulation: true, status: A.installPhase, reviewed };
      S.installStep = 2; S.installDone = false;
    }
    if (S.view === 'install') render();
    toast(S.installDone ? '模拟安装完成；没有访问磁盘或请求真实 Windows 授权。' : '本次模拟安装尚未确认完成；选项与原部署证据保留。');
  }, 900);
}
function auxInspectInstallReceipt() {
  const A = auxiliary();
  if (A.installPhase !== 'receipt-unknown') return;
  const serial = A.installSerial = (A.installSerial || 0) + 1;
  const result = A.installReceiptOutcome || 'completed';
  A.installPhase = 'checking-receipt'; render();
  demoDelay(() => {
    if (S.aux !== A || A.installSerial !== serial || A.installPhase !== 'checking-receipt') return;
    if (result === 'completed') auxCompleteInstall();
    else {
      A.installPhase = result === 'not-installed' ? 'not-installed' : 'receipt-unknown';
      A.installReceipt = { simulation: true, status: A.installPhase, reviewed: copy(A.installReview) };
    }
    if (S.view === 'install') render();
    toast('已核对本次模拟回执；没有重新执行安装。');
  }, 600);
}
function auxInstallFeedback() {
  const phase = auxiliary().installPhase;
  if (phase === 'awaiting-authorization') return notice('info', '等待 Windows 授权 · 模拟', '真实软件只在点击安装后请求一次系统授权。下方按钮仅演示允许或取消，不是 Windows 授权窗口，也不收集凭据。');
  if (phase === 'authorization-cancelled') return notice('warning', '模拟授权已取消 · 未执行安装', '程序目录、数据目录与快捷方式选项均已保留。可返回修改，或重新发起本次安装。');
  if (phase === 'blocked') return notice('warning', '原责任阻止升级 · 模拟', '原部署仍有未核对请求；本次未继续安装，原配置和数据保留。先按既有恢复流程处理原责任，再重新检查安装摘要。');
  if (phase === 'failed') return notice('danger', '介质校验失败 · 模拟', '本场景的安装文件字节与摘要不一致，未继续受保护安装。请核对安装介质，再重新检查；选项保留。');
  if (phase === 'receipt-unknown') return notice('warning', '安装回执待核对 · 不能重复执行', '授权操作可能已完成持久提交，尚不能判定为未安装。保留当前摘要，核对本次受保护回执；不自动终止进程或再次安装。');
  if (phase === 'checking-receipt') return notice('info', '正在核对本次模拟安装回执', '只读取这份模拟结果，不重新请求授权或执行安装。');
  if (phase === 'not-installed') return notice('info', '模拟回执已确认：本次未安装', '已有选项保留；确认原因后可以重新发起安装。这一结果不是从超时直接推断的。');
  return '';
}
function renderInstall() {
  const A = auxiliary();
  if (S.installStep > 1) A.installPrepared = true;
  if (S.installStep === 1 && !A.installPrepared && !A.installPhase) auxPrepareInstall(false);
  const step = Math.min(3, S.installStep), titles = ['安装选项', '确认安装', '安装完成'];
  const steps = `<div class="steps">${titles.map((title, index) => `<div class="step ${step === index + 1 ? 'active' : step > index + 1 ? 'complete' : ''}"><span>${index + 1}</span>${title}</div>`).join('')}</div>`;
  let body = '', buttons = '';
  if (step === 1 && !A.installPrepared) {
    const preparing = A.installPhase === 'preparing';
    body = section(preparing ? '正在准备安装文件 · 模拟' : A.installPhase === 'prepare-failed' ? '安装文件检查未完成' : '准备已取消',
      preparing ? notice('info', '在此窗口自动检查版本与完整性', '准备完成后自动显示安装选项；没有额外欢迎页、不启动第二个安装窗口，也不请求管理员授权。') + '<div class="progress-track" role="progressbar" aria-label="正在模拟准备安装文件"><span></span></div>' : notice('warning', A.installPhase === 'prepare-failed' ? '模拟介质检查失败' : '模拟准备已取消', '未执行安装或修改登记。可在此窗口重新准备，已有目录选项保留。'));
    buttons = preparing ? btn('取消准备（模拟）', 'aux-install-cancel-prepare') : btn('返回帮助', 'aux-install-help') + btn('重新准备（模拟）', 'aux-install-prepare', 'primary');
  } else if (step === 1) {
    body = section('安装位置', `<div class="form-grid">${field('程序目录', 'aux.installDirectory', '使用英文 SM 业务目录；示例路径可编辑。')}${field('数据目录', 'aux.installDataDirectory', '独立保存本站数据；不与程序目录重叠。')}</div>`) +
      section('快捷方式', `<div class="form-grid">${field('桌面快捷方式', 'aux.installDesktopShortcut', '', { options: [['yes', '创建'], ['no', '不创建']] })}${field('开始菜单快捷方式', 'aux.installMenuShortcut', '', { options: [['yes', '创建'], ['no', '不创建']] })}</div>`) +
      notice('info', '准备已完成 · 下一步只做本地检查', '安装不配置真实相机 IP，也不启动设备。安装成功后由普通客户端进入必要的首次工位配置。') +
      `<details><summary>Windows 目标与高级维护边界</summary><p class="help">优先功能目标为 Windows 10 22H2 家庭版 / 专业版 x64、Windows 11 x64。实际 OS、CPU、运行时及 SDK 资格分别核对；此处不检测本机或宣称目标环境通过。</p><p class="help">正式安装器已有运行账户、原配置来源、升级备份与只读环境详情；日常人工安装不要求填写无人值守计划。</p></details>`;
    buttons = btn('下一步 · 本地检查', 'aux-install-next', 'primary');
  } else if (step === 2) {
    const reviewed = A.installReview || auxInstallCandidate();
    body = auxInstallFeedback() + section('本次安装摘要', facts([
      ['业务模块', 'Sportmaster · 独立 Application'], ['程序目录', `<span class="mono">${esc(reviewed.program)}</span>`],
      ['数据目录', `<span class="mono">${esc(reviewed.data)}</span>`], ['组件', '相机读码驱动 × 1 种；RCS 任务组件 × 1'],
      ['后续实例', '首次启动配置左 / 右读码实例，RCS 为一个逻辑连接'],
      ['快捷方式', `${reviewed.desktopShortcut === 'no' ? '不创建桌面' : '创建桌面'}；${reviewed.menuShortcut === 'no' ? '不创建开始菜单' : '创建开始菜单'}`],
      ['检查范围', '本地选项与介质摘要；原责任和完整备份在安装授权后检查']
    ]));
    if (A.installPhase === 'installing') body = notice('info', '正在模拟授权后的安装', '在同一窗口等待原责任预检、备份和受保护安装结果；此演示不访问磁盘，页面保持响应。') + section('本次过程', checks([
      ['Windows 授权', '本次模拟允许已记录，不再重复请求。', '已允许 · 模拟', 'info'],
      ['责任与安装', '使用已审阅选项；有阻断不继续升级。', '进行中 · 模拟', 'info'],
      ['完成回执', '收到并核对回执后才显示安装完成。', '尚未确认', '']
    ]));
    else body += notice('info', '点击安装后由 Windows 请求一次授权', '升级会检查原责任并生成新的完整备份；有阻断则保留原部署。本地摘要不代表这些检查已经通过。');
    if (A.installPhase === 'awaiting-authorization') buttons = btn('模拟取消授权', 'aux-install-cancel-authorization') + btn('模拟允许授权并继续', 'aux-install-authorize', 'primary');
    else if (['installing', 'checking-receipt'].includes(A.installPhase)) buttons = btn(A.installPhase === 'installing' ? '安装进行中…' : '回执核对中…', 'aux-noop', '', 'disabled');
    else if (A.installPhase === 'receipt-unknown') buttons = btn('查看本次摘要', 'aux-install-details') + btn('核对模拟安装回执', 'aux-install-inspect-receipt', 'primary');
    else if (['blocked', 'failed'].includes(A.installPhase)) buttons = btn('查看原因与摘要', 'aux-install-details') + btn('返回选项并重新检查', 'aux-install-back', 'primary');
    else {
      body += `<details><summary>原型演示 · 授权后的结果</summary>${field('模拟结果', 'aux.installOutcome', '仅控制本页离线场景；不是正式软件的选项。', { options: [['completed', '安装完成'], ['blocked', '原责任阻断'], ['failed', '介质校验失败'], ['receipt-unknown', '安装回执待核对']] })}</details>`;
      buttons = btn('上一步', 'aux-install-back') + btn('开始模拟安装', 'aux-install-run', 'primary');
    }
  } else {
    body = notice('success', '模拟安装已完成并核对回执', '安装窗口始终为同一个。下一步由普通权限客户端启动；首次配置、资源启动和允许作业仍分别判断。') +
      section('首次启动与日常运行', checks([
        ['普通客户端启动', '完成后启动已安装客户端，不以管理员身份运行日常作业。', '下一步 · 模拟', 'info'],
        ['首次需要配置', '进入独立五步：业务模块、系统、驱动、组件、检查与完成。已有部署按原运行或恢复流程打开。', '独立任务', ''],
        ['Windows 与设备', '目标系统、缩放、实际 SDK / 相机 / RCS 链路按各自证据验收。', '此原型未验证', '']
      ]));
    buttons = S.installDone ? btn('启动客户端 · 首次配置演示', 'aux-install-setup', 'primary') : btn('尚未取得完成回执', 'aux-noop', '', 'disabled');
  }
  return `<div class="setup-wrap"><div class="setup-heading"><div><h1>安装菜鸟供包台</h1><p>Sportmaster · 同一窗口完成安装 · 离线模拟</p></div>${badge('安装程序演示', 'info')}</div>${steps}<div class="setup-main">${body}</div><div class="setup-footer"><span class="help">模拟流程；不请求真实授权，不访问本机文件。</span><div class="actions">${buttons}</div></div></div>`;
}

function renderAux(view) {
  auxiliary();
  if (view === 'history') return renderHistory();
  if (view === 'diagnostics') return renderDiagnostics();
  if (view === 'help') return renderHelp();
  if (view === 'maintenance') return renderMaintenance();
  if (view === 'install') return renderInstall();
  return '';
}
function auxState() { return auxiliary(); }

async function auxCopy(value) {
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
  try {
    if (navigator.clipboard && window.isSecureContext) { await navigator.clipboard.writeText(text); toast('已复制模拟详情。'); return; }
  } catch (_) { /* File previews can deny clipboard access; show a selectable fallback. */ }
  const selection = document.createElement('textarea'), previous = document.activeElement;
  selection.value = text; selection.readOnly = true; selection.style.position = 'fixed'; selection.style.left = '-9999px'; document.body.appendChild(selection); selection.select();
  let copied = false;
  try { copied = document.execCommand('copy'); } catch (_) { /* Keep explicit fallback. */ }
  selection.remove(); previous?.focus();
  if (copied) toast('已复制模拟详情。');
  else openDialog('技术详情 · 可选择复制', `<p>当前预览环境未允许自动复制。可选择下方完整内容并使用 Ctrl+C。</p><pre class="code" tabindex="0">${esc(text)}</pre>`, btn('关闭', 'aux-close'), true);
}
function onAuxField(path) {
  const A = auxiliary();
  if (path.startsWith('history')) { S.historyError = ''; delete S.errors.historyStart; return true; }
  if (path === 'supportStart' || path === 'supportEnd') { S.supportError = ''; delete S.errors.supportStart; A.supportStale = !!A.supportData; return true; }
  if (path.startsWith('aux.')) {
    delete S.errors[path];
    if (path === 'aux.restoreDirectory' || path === 'aux.restoreSource') S.restoreReady = false;
    if (path.startsWith('aux.install')) {
      A.installError = ''; delete S.errors['aux.installDirectory'];
      if (['aux.installDirectory', 'aux.installDataDirectory', 'aux.installDesktopShortcut', 'aux.installMenuShortcut'].includes(path) && !auxInstallBusy() && A.installPhase !== 'receipt-unknown') A.installReview = null;
    }
    return true;
  }
  return false;
}
function onAuxAction(action) {
  if (!action.startsWith('aux-')) return false;
  const A = auxiliary();
  const changesCandidate = ['aux-template-apply', 'aux-changes-restore', 'aux-changes-restore-confirm', 'aux-migration-apply'].includes(action);
  if (changesCandidate && editingLocked()) {
    toast(S.phase === 'save-unknown' ? '保存结果待核实，请先核对保存结果；当前候选和导入预览均已保留。' : '当前配置操作尚未结束，请等待完成后再修改候选；已有输入保留。');
    return true;
  }
  if (action === 'aux-noop') return true;
  if (action === 'aux-close') { closeDialog(); return true; }
  if (action === 'aux-return-work') { go('work'); return true; }
  if (action === 'aux-return-system') { auxConfig('system', 'system', 'maintenance'); return true; }
  if (action.startsWith('aux-input-details-')) { showInputDetails(action.slice('aux-input-details-'.length)); return true; }
  if (action === 'aux-input-copy') { if (A.inputDetail) auxCopy(copy(A.inputDetail)); return true; }
  if (action === 'aux-input-rule') { auxConfig('business', 'business', 'rules'); return true; }
  if (action === 'aux-history-query') { auxRunHistoryQuery(); return true; }
  if (action === 'aux-history-reset') { S.historyQuery = ''; S.historySide = 'all'; S.historyResult = 'all'; S.historyStart = S.historyEnd = '2026-10-20'; auxRunHistoryQuery(); return true; }
  if (action === 'aux-history-focus-date') { auxFocusField('historyStart'); return true; }
  if (action.startsWith('aux-history-select-')) { S.historySelected = Number(action.slice('aux-history-select-'.length)); render(); return true; }
  if (action === 'aux-history-export') { const rows = auxHistoryRows(); if (rows.length) { download('Sportmaster-records-demo.json', { simulation: true, snapshotId: A.historySnapshotId, filters: copy(S.historyApplied), records: copy(rows) }); toast('已导出当前筛选的模拟记录 JSON。'); } return true; }
  if (action === 'aux-history-copy') { const row = auxHistorySelection(); if (row) auxCopy({ simulation: true, ...row }); return true; }
  if (action === 'aux-history-diagnostic') { const row = auxHistorySelection(); if (row) { A.diagnosticOrigin = copy(row); S.diagnosticObject = 'rcs'; go('diagnostics'); } return true; }
  if (action.startsWith('aux-diag-object-')) { S.diagnosticObject = action.slice('aux-diag-object-'.length); S.diagnosticChecked = false; A.diagnosticFeedback = ''; A.diagnosticOrigin = null; render(); return true; }
  if (action === 'aux-diag-check') { auxRunDiagnosticCheck(); return true; }
  if (action === 'aux-diag-config') { S.diagnosticObject === 'rcs' ? auxConfig('components', 'rcs', 'connection') : auxConfig('drivers', S.diagnosticObject, 'connection'); return true; }
  if (action === 'aux-diag-back-history') { go('history'); return true; }
  if (action === 'aux-diag-copy') { auxCopy(auxDiagnosticPayload()); return true; }
  if (action === 'aux-diag-support' || action === 'aux-help-effect') {
    if (action === 'aux-diag-support') go('help');
    else openDialog('保存与生效分别判断', notice('info', '保存的是完整候选，运行内容需单独确认', '系统、业务模块、左右驱动实例和单一组件连接沿同一配置账。已保存待启动不能当作已运行；当前参数通过受控重启生效。') + facts([['未保存', '仍是编辑值；可跨对象保留'], ['待生效', '已入账，当前运行内容尚未切换'], ['应用失败', '保留候选与原责任，并读取实际运行状态'], ['重新连接', '使用原运行内容，不应用 Pending']]), btn('关闭', 'aux-close'));
    return true;
  }
  if (action === 'aux-diag-original') { openDialog('原请求详情 · 只读', notice('warning', '当前协议没有权威结果查询', '以下内容仅用于核对原件及技术诊断。查看、复制不会发送新请求或将未知结果标为成功。') + `<pre class="code">${esc(JSON.stringify(auxDiagnosticPayload(), null, 2))}</pre>`, btn('关闭', 'aux-close') + btn('复制技术详情', 'aux-diag-copy'), true); return true; }
  if (action === 'aux-diag-reconnect') { A.reconnectObject = S.diagnosticObject; auxReconnectDialog(); return true; }
  if (action === 'aux-diag-reconnect-prepare') { S.paused = true; S.maintenance = true; auxReconnectDialog(); return true; }
  if (action === 'aux-diag-reconnect-run') { auxRunReconnect(); return true; }
  if (action === 'aux-demo-repair' || action === 'aux-demo-break') {
    const repaired = action === 'aux-demo-repair', object = S.diagnosticObject;
    if (object === 'rcs') S.rcsReachable = repaired; else S[object + 'Online'] = repaired;
    delete A.diagnosticResults[object]; S.diagnosticChecked = false;
    A.diagnosticFeedback = repaired ? '已模拟外部故障修复。请重新检查；原业务结果和全站暂停状态不变。' : '已模拟对象连接中断。检查结果与原业务结果分别显示。'; render(); return true;
  }
  if (action === 'aux-help-diagnostic') { S.diagnosticObject = 'rcs'; A.diagnosticOrigin = null; go('diagnostics'); return true; }
  if (action === 'aux-help-driver') { auxConfig('drivers', 'left', 'connection'); return true; }
  if (action === 'aux-support-preview') { auxRunSupportPreview(); return true; }
  if (action === 'aux-support-export') {
    if (!A.supportData || A.supportStale || Date.now() >= S.supportExpires) { toast('预览缺失、已变化或已过期，请重新生成。'); if (S.view === 'help') render(); return true; }
    download('Sportmaster-support-demo.json', copy(A.supportData)); toast('已导出当前预览的模拟摘要；内容未重新采集。'); return true;
  }
  if (action.startsWith('aux-maint-tab-')) { A.maintenanceTab = action.slice('aux-maint-tab-'.length); A.migrationVisited = true; A.templateVisited = true; render(); return true; }
  if (action === 'aux-maint-prepare') { S.paused = true; S.maintenance = true; const next = A.pendingMaintenanceAction; A.pendingMaintenanceAction = ''; closeDialog(); if (next) onAuxAction(next); return true; }
  if (action === 'aux-backup-settings') { auxConfig('system', 'system', 'runtime'); return true; }
  if (action === 'aux-backup-create') {
    if (A.backupPhase || !auxMaintenanceGuard(action)) return true;
    A.backupPhase = 'running'; render(); demoDelay(() => { A.backupPhase = ''; S.backupCreated = true; A.backupTime = new Date().toISOString(); S.audit.push({ action: '完整备份流程', object: 'system', note: '仅离线模拟结构摘要，非真实备份', reason: '演示完整备份', revision: S.ledger.revision, status: '模拟完成，非真实备份', simulation: true }); if (S.view === 'maintenance') render(); toast('模拟备份流程已完成；未创建真实备份。'); }, 700); return true;
  }
  if (action === 'aux-backup-download') { if (S.backupCreated) download('Sportmaster-backup-structure-demo.json', { simulation: true, isRestorableBackup: false, createdAt: A.backupTime, contents: ['完整配置账与版本标识', '数据库及恢复同伴内容', '原请求责任与必要审计'], note: 'Only a structure specimen. No files or database have been backed up.' }); return true; }
  if (action === 'aux-restore-source') { A.restoreSource = 'Sportmaster · 演示完整备份'; S.restoreReady = false; delete S.errors['aux.restoreSource']; render(); return true; }
  if (action === 'aux-restore-check') { auxCheckRestore(); return true; }
  if (action === 'aux-restore-run') {
    if (!S.restoreReady || A.restorePhase || A.restoreCheckedDirectory !== A.restoreDirectory) { auxCheckRestore(); return true; }
    if (!auxMaintenanceGuard(action)) return true;
    A.restorePhase = 'running'; render(); demoDelay(() => { A.restorePhase = ''; A.restored = true; S.paused = true; if (S.view === 'maintenance') render(); toast('隔离恢复演示完成，全站保持暂停；实际数据未被修改。'); }, 850); return true;
  }
  if (action === 'aux-template-preview') { S.importPreview = true; A.templateBase = JSON.stringify(S.draft); A.templateBefore = copy(S.draft); A.templateValues = { business: { queueCapacity: '96', timeoutMs: '1800' } }; A.templateVisited = true; A.maintenanceTab = 'templates'; render(); return true; }
  if (action === 'aux-template-cancel') { S.importPreview = false; A.templateBase = null; A.templateBefore = null; render(); return true; }
  if (action === 'aux-template-apply') {
    if (!S.importPreview) return true;
    if (A.templateBase !== JSON.stringify(S.draft)) { toast('当前编辑内容已变化，请重新预览模板差异；已有输入保留。'); return true; }
    for (const [object, fields] of Object.entries(A.templateValues)) Object.assign(S.draft[object], fields); S.phase = 'dirty'; S.importPreview = false; A.templateBase = null; A.templateBefore = null;
    S.audit.push({ action: '模板加入编辑', object: 'business', note: '只加入两个白名单策略，未保存或生效', reason: '导入安全模板示例', revision: S.ledger.revision, status: '仅编辑，未保存', simulation: true }); auxConfig('business', 'business', 'runtime'); toast('模板差异已加入当前编辑，尚未保存或生效。'); return true;
  }
  if (action === 'aux-template-export') { download('Sportmaster-safe-template-demo.json', { simulation: true, application: 'sportmaster', schemaVersion: 1, sourceIdentity: 'same-build-identity-required-in-production', values: { business: { queueCapacity: S.draft.business.queueCapacity, timeoutMs: S.draft.business.timeoutMs, maximumOperations: S.draft.business.maximumOperations }, system: { residentHistory: S.draft.system.residentHistory, minimumFreeBytes: S.draft.system.minimumFreeBytes } }, note: 'No site/device/IP/endpoint/secrets/request-range/backup-path/business-data.' }); toast('已导出白名单参数的模拟模板。'); return true; }
  if (action === 'aux-changes-diff') { openDialog('当前编辑差异', differences().length ? diffTable() : notice('info', '当前没有未保存的字段修改'), btn('关闭', 'aux-close'), true); return true; }
  if (action === 'aux-changes-restore') {
    if (!S.ledger.confirmed) { toast('没有可确认的运行内容，不能把未知内容作为恢复基线。'); return true; }
    openDialog('恢复最后确认内容为草稿', notice('warning', '将替换当前候选的本次输入', '只复制有确认依据的运行内容形成新候选；不会倒写配置账或直接启动。若有未保存编辑，请先决定是否保留。') + facts([['恢复范围', '系统、业务模块、左右驱动实例、单一组件连接'], ['后续步骤', '重新校验、查看差异、保存与受控启动']]), btn('继续当前编辑', 'aux-close') + btn('确认替换为恢复草稿', 'aux-changes-restore-confirm', 'primary')); return true;
  }
  if (action === 'aux-changes-restore-confirm') { if (S.ledger.confirmed) { S.draft = copy(S.runtime); S.phase = dirty() ? 'dirty' : 'clean'; S.errors = {}; closeDialog(); auxConfig('system', 'system', 'station'); toast('已形成恢复候选；配置账与运行状态未改写。'); } return true; }
  if (action === 'aux-migration-apply') {
    if (A.migrationAcknowledged !== 'same') { S.errors['aux.migrationAcknowledged'] = '先核实两个旧编码是否属于同一供包台。'; render(); auxFocusField('aux.migrationAcknowledged'); return true; }
    if (!A.migrationStation.trim() || A.migrationStation.length > 256) { S.errors['aux.migrationStation'] = '请填写核实后的供包台编码，最多 256 个字符。'; render(); auxFocusField('aux.migrationStation'); return true; }
    S.draft.system.stationCode = A.migrationStation; S.migrationConflict = false; delete S.errors.migration; S.phase = 'dirty'; S.audit.push({ action: '旧编码核对', object: 'system', note: '模拟确认同一供包台，结果仅加入候选', reason: '核对旧供包台编码', revision: S.ledger.revision, status: '仅编辑，未保存', simulation: true }); auxConfig('system', 'system', 'station'); toast('核对结果已加入系统配置草稿，尚未保存或应用。'); return true;
  }
  if (action === 'aux-old-fields') { auxShowLegacyFields(); return true; }
  if (action === 'aux-advanced-copy') { auxCopy({ simulation: true, loadedRevision: S.baseRevision, draft: copy(S.draft) }); return true; }
  if (action === 'aux-advanced-components') { auxConfig('components', 'rcs', 'connection'); return true; }
  if (action === 'aux-install-prepare') { auxPrepareInstall(); return true; }
  if (action === 'aux-install-cancel-prepare') {
    if (A.installPhase === 'preparing') { A.installSerial++; A.installPhase = 'prepare-cancelled'; A.installPrepared = false; render(); }
    return true;
  }
  if (action === 'aux-install-help' || action === 'aux-help-install') {
    if (!auxInstallBusy() && A.installPhase !== 'receipt-unknown') go(action === 'aux-help-install' ? 'install' : 'help');
    return true;
  }
  if (action === 'aux-install-next') { auxReviewInstall(); return true; }
  if (action === 'aux-install-back') {
    if (auxInstallBusy() || A.installPhase === 'receipt-unknown') { toast('本次操作或回执尚未确认；当前摘要和选项保留。'); return true; }
    A.installPhase = ''; A.installReview = null; A.installReceipt = null; S.installStep = 1; S.installDone = false; render(); return true;
  }
  if (action === 'aux-install-run') { auxBeginInstallAuthorization(); return true; }
  if (action === 'aux-install-cancel-authorization') {
    if (A.installPhase === 'awaiting-authorization') { A.installSerial++; A.installPhase = 'authorization-cancelled'; render(); }
    return true;
  }
  if (action === 'aux-install-authorize') { auxRunAuthorizedInstall(); return true; }
  if (action === 'aux-install-inspect-receipt') { auxInspectInstallReceipt(); return true; }
  if (action === 'aux-install-details') {
    openDialog('本次安装摘要 · 模拟', auxInstallFeedback() + notice('info', '同一份已审阅选项', '查看或复制不会重新授权、安装、备份或改变业务状态。实际操作以受保护回执为准。') + `<pre class="code">${esc(JSON.stringify({ simulation: true, phase: A.installPhase, reviewed: A.installReview, receipt: A.installReceipt }, null, 2))}</pre>`, btn('返回安装', 'aux-close'), true); return true;
  }
  if (action === 'aux-install-setup') {
    if (S.installStep !== 3 || !S.installDone || editingLocked()) { toast('先确认安装完成，并结束当前配置操作；已有输入保留。'); return true; }
    S.setupStep = 1; S.setupChecked = false; S.setupComplete = false; S.setupSaved = false; S.setupResumed = false; S.appStarted = false; go('setup'); return true;
  }
  return false;
}

/* Original field identifiers are retained as a read-only migration index. */
const AUX_LEGACY_FIELDS = [
  [
    "P01",
    "维护账号",
    "维护操作 → 身份验证",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P02",
    "维护口令",
    "维护操作 → 身份验证",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P03",
    "维护变更原因",
    "跨类配置变更 → 完整候选保存摘要",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P04",
    "编辑来源",
    "系统配置 → 高级 → 编辑来源",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P05",
    "配置修订 / 草稿CAS",
    "设置公共状态区 → 草稿基线/CAS",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P06",
    "Effective / Pending",
    "设置公共状态区 → 保存/生效状态",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P07",
    "稳定设备ID",
    "驱动配置 → 本位实例 → 稳定设备标识",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P08",
    "原角色",
    "驱动配置 → 本位实例 → 业务用途",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P09",
    "已安装提供者 / 精确组件来源",
    "驱动配置 → 已安装类型 → 实例",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P10",
    "实例身份",
    "驱动配置 → 本位实例 → 实例身份",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P11",
    "完整候选配置（高级）JSON",
    "系统配置 → 高级 → 完整候选JSON",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P12",
    "SchemaVersion",
    "系统配置 → 高级 → 格式信息",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P13",
    "维护核对说明",
    "参数配置 → 系统配置 → 维护与迁移 → 旧配置核对",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P14",
    "原字节SHA256 / 原账状态",
    "参数配置 → 系统配置 → 维护与迁移 → 旧配置证据",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P15",
    "Providers.Kind",
    "驱动配置/组件配置 → 已安装类型（按实际能力分类）",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P16",
    "Providers.Implementation",
    "驱动配置/组件配置 → 类型详情",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P17",
    "Providers.Version",
    "驱动配置/组件配置 → 类型详情",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P18",
    "Providers.Contract",
    "驱动配置/组件配置 → 类型技术详情",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P19",
    "Providers.Parameters",
    "驱动配置/组件配置 → 指定实例 → 适用参数",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P20",
    "Requirements.MeasuredWeight",
    "驱动配置/组件配置 → 类型兼容性",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P21",
    "Requirements.ImageAddress",
    "驱动配置/组件配置 → 类型兼容性",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P22",
    "Requirements.Query",
    "组件配置 → RCS → 能力说明",
    "当前RCS Query=false；不得以真实dest_request POST伪造查询或连接检查。"
  ],
  [
    "P23",
    "Requirements.PhysicalConfirmation",
    "驱动配置/组件配置 → 类型兼容性",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P24",
    "能力（新增类型下拉）",
    "驱动配置/组件配置 → Application允许的实例管理",
    "当前Sportmaster固定左右2 reader，RCS最多1实例；不能调用通用新增能力创建第三个未绑定reader。"
  ],
  [
    "P25",
    "启用",
    "驱动配置/组件配置 → 实例状态",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P26",
    "旧版绑定",
    "系统配置 → 高级 → 旧配置迁移",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P27",
    "已安装提供者",
    "驱动配置/组件配置 → 已安装类型选择",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P28",
    "实例身份",
    "驱动配置/组件配置 → 实例技术详情",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P29",
    "协议目标 / 读码身份",
    "驱动配置 → 设备身份；组件配置 → 连接目标",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P30",
    "合同版本",
    "驱动配置/组件配置 → 接口技术详情",
    "按实际硬件交换能力/外部服务职责分类，不能把所有非observation都称组件；安装不等于适用、绑定或激活。"
  ],
  [
    "P31",
    "称重来源",
    "业务模块配置 → 适用业务策略/旧配置迁移 → 重量来源",
    "只在Application与已安装能力实际适用时出现；Sportmaster不因旧截图新增称重/采图/上传。范围/单位由当前契约复核，旧示例0不作默认。"
  ],
  [
    "P32",
    "称重单位",
    "业务模块配置 → 适用业务策略/旧配置迁移 → 重量单位",
    "只在Application与已安装能力实际适用时出现；Sportmaster不因旧截图新增称重/采图/上传。范围/单位由当前契约复核，旧示例0不作默认。"
  ],
  [
    "P33",
    "精度",
    "业务模块配置 → 适用业务策略/旧配置迁移 → 重量小数位",
    "只在Application与已安装能力实际适用时出现；Sportmaster不因旧截图新增称重/采图/上传。范围/单位由当前契约复核，旧示例0不作默认。"
  ],
  [
    "P34",
    "配置值",
    "业务模块配置 → 适用业务策略/旧配置迁移 → 配置重量",
    "只在Application与已安装能力实际适用时出现；Sportmaster不因旧截图新增称重/采图/上传。范围/单位由当前契约复核，旧示例0不作默认。"
  ],
  [
    "P35",
    "本件等待期限(ms)",
    "业务模块配置 → 适用业务策略/旧配置迁移 → 等待称重期限",
    "只在Application与已安装能力实际适用时出现；Sportmaster不因旧截图新增称重/采图/上传。范围/单位由当前契约复核，旧示例0不作默认。"
  ],
  [
    "P36",
    "测量新鲜度(ms)",
    "业务模块配置 → 适用业务策略/旧配置迁移 → 测量新鲜度",
    "只在Application与已安装能力实际适用时出现；Sportmaster不因旧截图新增称重/采图/上传。范围/单位由当前契约复核，旧示例0不作默认。"
  ],
  [
    "P37",
    "重量有效期",
    "业务模块配置 → 适用业务策略/旧配置迁移 → 重量有效期",
    "只在Application与已安装能力实际适用时出现；Sportmaster不因旧截图新增称重/采图/上传。范围/单位由当前契约复核，旧示例0不作默认。"
  ],
  [
    "P38",
    "图片上传策略",
    "业务模块配置 → 适用业务策略/旧配置迁移 → 上传策略",
    "只在Application与已安装能力实际适用时出现；Sportmaster不因旧截图新增称重/采图/上传。范围/单位由当前契约复核，旧示例0不作默认。"
  ],
  [
    "P39",
    "离线签名包路径",
    "安装维护 → 驱动/组件包安装或更新",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P40",
    "原包 SHA256",
    "安装维护 → 包校验",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P41",
    "扩展库存状态",
    "驱动配置/组件配置 → 库存状态；首启 → 已安装能力检查",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P42",
    "原配置目录（包含business.json）",
    "首次启动配置 → 系统；后续系统配置 → 数据与文件",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P43",
    "原数据根（保留Simulation/Production、站和Application层级）",
    "首次启动配置 → 系统；后续系统配置 → 数据与文件/迁移",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P44",
    "工位身份摘要",
    "系统配置 → 本地身份与业务站点（各字段分开）",
    "本地StationId与RCS stationCode分开，后者属于本轮用户确认的新系统字段；旧left/right冲突显式处理。"
  ],
  [
    "P45",
    "读码来源 · sportmaster.camera.left / right",
    "驱动配置 → 固定两个实例与投递位对应；首次启动 → 驱动实例",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P46",
    "稳定设备身份（离线录入，待真实SDK核验）",
    "首次启动 → 驱动实例；后续驱动配置 → 标识/受控迁移",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P47",
    "输入队列容量",
    "业务模块配置 → Sportmaster → 运行与容量",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P48",
    "调用超时（ms）",
    "业务模块配置 → Sportmaster → 运行与容量",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P49",
    "作业容量",
    "业务模块配置 → Sportmaster → 运行与容量",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P50",
    "常驻历史条数（同库归档不释放磁盘）",
    "系统配置 → 数据与备份",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P51",
    "计划备份维护限制（文案）",
    "系统配置 → 数据与备份",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P52",
    "维护原因（必填；保留审计与原配置字节）",
    "首次启动配置 → 检查并保存",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P53",
    "浏览完整备份目录（最多256份；校验整个恢复集，保留无效项）",
    "参数配置 → 系统配置 → 维护与迁移 → 完整备份与隔离恢复",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P54",
    "备份列表：时间 / 完整性 / 字节 / 备份",
    "参数配置 → 系统配置 → 维护与迁移 → 备份列表与完整性",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P55",
    "恢复交接证据",
    "参数配置 → 系统配置 → 维护与迁移 → 换机恢复",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P56",
    "记录命令目标",
    "作业记录 → 固定原作业 → 处置详情",
    "查询/恢复按钮按实际协议；当前RCS无Query，不可把旧表中的“只查询”当成已支持查询。"
  ],
  [
    "P57",
    "确认已检查身份冲突旁输入框",
    "作业记录/诊断 → 身份冲突核对",
    "只变更交互归属；原始截图事实继续保留。"
  ],
  [
    "P58",
    "原接收发生 / 确切ID / RawDurable",
    "作业记录/诊断 → 原接收责任与证据",
    "只变更交互归属；原始截图事实继续保留。"
  ]
];

function auxShowLegacyFields() {
  openDialog('原 58 项字段与状态 · 迁移索引',
    '<p>保留原编号用于查缺补漏。新界面的字段归属遵循系统 / 业务模块 / 驱动 / 组件四类；此清单只读，真实值与能力按当前 Application 合同核对。</p>' +
    '<div class="table-wrap"><table class="table"><thead><tr><th style="width:8%">编号</th><th style="width:27%">原字段 / 状态</th><th style="width:35%">新位置</th><th>处理说明</th></tr></thead><tbody>' +
    AUX_LEGACY_FIELDS.map(([id, label, destination, resolution]) => `<tr><td>${esc(id)}</td><td>${esc(label)}</td><td>${esc(destination)}</td><td>${esc(resolution)}</td></tr>`).join('') +
    '</tbody></table></div>', btn('关闭', 'aux-close'), true);
}
