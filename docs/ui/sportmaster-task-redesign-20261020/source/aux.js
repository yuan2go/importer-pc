/* Supporting task flows. Offline simulation only; no device, service or filesystem operations. */
function auxState() {
  if (!S.aux || !S.aux.setup) {
    S.aux = {
      dirtyView: null, errors: {}, savedSnapshots: {},
      setup: {step:1, configDir:'D:\\ImporterPC\\Config', dataDir:'D:\\ImporterPC\\Data', loaded:true, baseRevision:S.savedRev||12, saveError:'', applyError:'',
        left:'camera-left', right:'camera-right', service:'wcs-main', driver:'demo-camera', queue:'64', timeout:'1500', capacity:'10000', history:'200',
        reason:'首次配置左右投递位与 WCS 服务', checked:false, checking:false, saved:false, applied:false, applying:false, draft:false},
      install: {step:1, programDir:'D:\\Apps\\ImporterPC', dataDir:'D:\\ImporterPC\\Data', account:'WORKSTATION\\Operator',
        driver:'include', integration:'include', checked:false, checking:false, installing:false, done:false, percent:0},
      backup: {tab:'template', imported:false, staged:false, pending:false, credentials:'keep', reason:'从已审阅的模板更新连接参数',baseRevision:S.savedRev||12,saveError:'',targetAddresses:{left:'192.0.2.31',right:'192.0.2.32'},templateSource:'sportmaster-example.template.json（模拟模板）',
        directory:'D:\\ImporterPC\\Backups', scanned:true, selected:'b1', restored:false, creating:false, restoring:false,
        restoreDir:'D:\\ImporterPC\\Recovery\\isolated-20261020', evidence:false, handover:false, extra:false, restoreError:''},
      support: {tab:'help', query:'', searched:'', topic:'wcs', start:'2026-10-20T10:00', end:'2026-10-20T11:00',
        preview:false, checking:false, error:'', exported:false},
      advanced: {tab:'text', text:'{\n  "SchemaVersion": 3\n}', edit:false, validation:null, migration:false,
        loaded:false, note:'核对原配置与运行记录，保留原配置字节及原始错误。', confirmed:false, repaired:false}
    };
    for (const v of ['setup','install','backup','advanced']) S.aux.savedSnapshots[v] = JSON.parse(JSON.stringify(S.aux[v]));
  }
  return S.aux;
}

function auxTitle(title, sub, actions='') {
  return `<div class="page-heading"><div><h1>${esc(title)}</h1><p>${esc(sub)}</p></div><div class="actions">${actions}</div></div>`;
}
function auxSteps(labels, current) {
  return `<div class="steps">${labels.map((s,i)=>`<div class="step ${i+1===current?'active':''}" style="flex:1"><span class="step-num">${i+1<current?'✓':i+1}</span> ${esc(s)}</div>`).join('')}</div>`;
}
function auxTabs(items,current,prefix) {
  return `<div class="subnav">${items.map(([id,label])=>`<button type="button" data-action="${prefix+id}" class="${id===current?'active':''}">${esc(label)}</button>`).join('')}</div>`;
}
function auxInput(label,key,value,help='',options={}) {
  const A=auxState();
  return field(label,key,value,help,{...options,error:A.errors[key]||options.error});
}
function auxRow(label,value) {
  return `<div class="data-pair"><span class="muted">${esc(label)}</span><strong>${value}</strong></div>`;
}
function auxChecklist(rows) {
  return `<div class="checklist">${rows.map(([title,detail,status,tone])=>`<div class="list-row between"><div><strong>${esc(title)}</strong><div class="help">${esc(detail)}</div></div>${tag(status,tone||'neutral')}</div>`).join('')}</div>`;
}
function auxDownload(name,data) {
  const txt=typeof data==='string'?data:JSON.stringify(data,null,2);
  if (typeof downloadText==='function') downloadText(name,txt,'application/json;charset=utf-8');
  else { const blob=new Blob([txt],{type:'application/json;charset=utf-8'});const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000); }
}
function auxSaveSnapshot(view) {
  const A=auxState(); A.savedSnapshots[view]=JSON.parse(JSON.stringify(A[view])); if(A.dirtyView===view) A.dirtyView=null;
}
function auxHasUnsaved() {const A=auxState();return !!A.dirtyView&&A.dirtyView===S.view;}
function auxKeepDraft() {const A=auxState(); if(A.dirtyView==='setup') A.setup.draft=true;A.dirtyView=null;}
function auxDiscardChanges() {
  const A=auxState(),v=A.dirtyView;
  if(v&&A.savedSnapshots[v]) {const oldStep=A[v].step,oldTab=A[v].tab;A[v]=JSON.parse(JSON.stringify(A.savedSnapshots[v]));if(oldStep)A[v].step=oldStep;if(oldTab)A[v].tab=oldTab;}
  A.dirtyView=null;A.errors={};
}
function auxField(key,value) {
  if(!key.startsWith('aux.'))return false;
  const A=auxState(),parts=key.split('.').slice(1);let at=A;
  for(let i=0;i<parts.length-1;i++){if(!at[parts[i]])at[parts[i]]={};at=at[parts[i]];}
  at[parts[parts.length-1]]=value;
  delete A.errors[key];
  const v=parts[0];
  if(['setup','install','advanced'].includes(v)||(v==='backup'&&['credentials','reason'].includes(parts[1])))A.dirtyView=v;
  if(v==='setup') {A.setup.checked=false;A.setup.saved=false;A.setup.applied=false;}
  if(v==='support') {A.support.preview=false;A.support.exported=false;A.support.error='';A.support.errorField='';const exportButton=document.querySelector('[data-action="aux-support-export"]');if(exportButton){exportButton.disabled=true;exportButton.title='范围已修改，请重新生成本次预览';}}
  if(v==='advanced'&&parts[1]==='text')A.advanced.validation=null;
  return true;
}

function renderSetup() {
  const A=auxState(),x=A.setup;
  if(x.saved&&x.savedRevision===S.runtimeRev&&S.runtimeKnown){x.saved=false;x.applied=true;}
  let body='';
  if(x.step===1) body=section('核对当前工作站',
    `<div class="object-summary stack" style="display:grid;gap:8px">${auxRow('工作站','reference-17 <span class="muted small">（示例）</span>')}${auxRow('业务模块','Sportmaster · 左右两路扫码 → WCS → 显示格口')}${auxRow('运行环境','离线模拟 · 本次首配尚未应用')}</div>`+
    `<p class="help">身份从读取的配置确定。更换工作站或业务模块须重新核对装配，不能在这里改一个编号切换现场。</p>`)+
    section('配置与数据位置',`<div class="form-grid">${auxInput('配置文件夹','aux.setup.configDir',x.configDir,'设计示例路径；更换后点击“读取配置”核对身份。')}${auxInput('作业数据文件夹','aux.setup.dataDir',x.dataDir,'保留环境、工作站和 Application 的数据隔离。')}</div><div class="actions">${btn('读取配置（模拟）','aux-setup-read')}${btn('载入示例文件夹','aux-setup-folder','ghost')}</div>`)+
    note(x.loaded?'info':'warning',x.loaded?'已读取示例配置':'文件夹已选择，尚未读取',x.loaded?'确认工作站身份与目录后，再配置设备。所有路径均为设计示例。':'读取只确认身份与已有配置，不启动设备。');
  if(x.step===2) body=section('先有设备实例，再绑定到投递位',
    `<table class="table"><thead><tr><th>设备实例</th><th>已选择驱动</th><th>物理设备身份</th><th>核验</th></tr></thead><tbody><tr><td><strong>左侧读码相机</strong></td><td>读码驱动 · 演示适配器</td><td class="mono">CAM-L-021</td><td>${tag('待真实设备核验','warning')}</td></tr><tr><td><strong>右侧读码相机</strong></td><td>读码驱动 · 演示适配器</td><td class="mono">CAM-R-022</td><td>${tag('待真实设备核验','warning')}</td></tr></tbody></table><p class="help">设备身份是设计示例；离线录入不等于发现、连接或收到条码。</p>`)+
    section('将设备对应到现场位置',`<div class="form-grid">${selectField('左投递位使用','aux.setup.left',x.left,[['camera-left','左侧读码相机 · CAM-L-021'],['camera-right','右侧读码相机 · CAM-R-022']],'核对实际安装方向。')}${selectField('右投递位使用','aux.setup.right',x.right,[['camera-right','右侧读码相机 · CAM-R-022'],['camera-left','左侧读码相机 · CAM-L-021']],'同一示例设备不能同时绑定左右位置。')}</div>${A.errors['aux.setup.right']?note('danger','位置绑定冲突',A.errors['aux.setup.right']):''}`)+
    section('两路共享的外部服务',`<div class="form-grid">${selectField('WCS 服务实例','aux.setup.service',x.service,[['wcs-main','WCS 主服务（示例）']],'提供包裹号查询和格口结果，不下发 PLC。')}<div class="field"><label>其他能力</label><div style="padding:10px 0">未为此演示配置登录、称重或传图</div><div class="help">仅按已安装组件及本 Application 的真实需求呈现。</div></div></div>`);
  if(x.step===3) body=section('处理与容量',`<div class="form-grid">${auxInput('输入队列容量','aux.setup.queue',x.queue,'沿用原截图字段；容量范围与满载行为需由实现给出。')}${auxInput('调用超时','aux.setup.timeout',x.timeout,'作用范围需核实，不能自行认定只作用于 WCS。',{unit:'ms'})}${auxInput('作业容量','aux.setup.capacity',x.capacity,'容量的业务含义与有效范围待实现核实。')}${auxInput('常驻历史条数','aux.setup.history',x.history,'同库归档不会释放磁盘。',{unit:'条'})}</div>`)+
    note('info','原图示例值，均不是生产默认','64 / 1500 ms / 10000 / 200 来自合成截图。此原型只校验整数格式；真实允许范围和 0 的含义须由现有实现确认。')+
    section('备份规则',`<p>完整备份进入受控维护窗口执行。份数达到限制时提示人工处理，不自动删除旧备份。</p><p class="help">后续维护可从“备份与恢复”进入，不必重走首次配置。</p>`);
  if(x.step===4) body=`<div class="split" style="align-items:start"><div>`+section('本次配置',`<div class="object-summary stack" style="display:grid;gap:6px;padding:14px 0;background:transparent">${auxRow('工作站','reference-17（示例）')}${auxRow('业务模块','Sportmaster')}${auxRow('设备与服务','左右相机 · 共用 WCS')}${auxRow('保存范围','整站关联修订')}</div>`)+
    section('保存说明',auxInput('设置说明','aux.setup.reason',x.reason,'必填；随修订保存。'))+
    (x.saved?note('warning',`已保存 v${S.savedRev}，尚未投入运行`,`${S.runtimeKnown?`当前仍运行 v${S.runtimeRev}`:`最后确认 v${S.runtimeRev}，当前运行版本待核实`}。本次修订尚未确认生效。`):x.applied?note('success',`运行端已确认 v${S.runtimeRev}（模拟）`,'新包接收仍保持暂停。确认现场准备完成后，再恢复接收。'):note('info','先保存，再判断实际生效','应用前核对整站影响；应用后读取运行配置。'))+
    (x.saveError?note('danger','未保存',x.saveError):'')+(x.applyError?note('danger','未完成应用',x.applyError):'')+`</div><div>`+
    section('分层检查',auxChecklist([
      ['字段格式与对象引用',x.checked?'已检查本次示例输入及左右绑定。':'尚未执行本次输入检查。',x.checking?'正在检查':x.checked?'通过':'未检查',x.checked?'success':'neutral'],
      ['设备身份与驱动匹配','示例数据可演示绑定，真实 SDK 身份仍需核实。',x.checked?'演示检查通过':'未检查',x.checked?'info':'neutral'],
      ['相机与 WCS 连接','不连接真实设备或生产服务。',x.checked?'模拟可达':'未测试',x.checked?'info':'neutral'],
      ['真实扫码与业务响应','需在受控实机验收中完成。','未验证','warning']
    ])+`<div class="actions" style="margin-top:16px">${btn(x.checking?'正在检查…':'检查配置（模拟）','aux-setup-check','secondary',x.checking?'disabled':'')}</div>`)+`</div></div>`;
  const left=`${btn('暂存本次设置','aux-setup-draft','ghost')}${x.draft?tag('演示草稿已暂存','info'):''}`;
  const right=x.step<4?`${x.step>1?btn('上一步','aux-setup-prev'):''}${btn('下一步','aux-setup-next','primary')}`:
    `${btn('上一步','aux-setup-prev')}${x.applied?btn('返回作业','aux-return-work','primary'):x.saved?btn(x.applying?'应用检查中…':'应用并检查运行（模拟）','aux-setup-apply','primary',x.applying?'disabled':''):btn('保存为待生效修订','aux-setup-save','primary',x.checking?'disabled':'')}`;
  return auxTitle('首次工作站配置','用于首次准备现场；后续维护直接编辑相应对象。',btn('查看本步帮助','aux-setup-help','ghost'))+
    auxSteps(['核对工作站','设备与位置','运行策略','检查并保存'],x.step)+
    `<div class="panel">${body}</div><div class="footer-actions between" style="justify-content:space-between"><div class="actions">${left}</div><div class="actions">${right}</div></div>`;
}

function renderInstall() {
  const x=auxState().install;
  let body='';
  if(x.step===1)body=section('选择安装位置',`<div class="form-grid">${auxInput('程序安装文件夹','aux.install.programDir',x.programDir,'设计示例；此原型不访问或更改磁盘。')}${auxInput('作业数据文件夹','aux.install.dataDir',x.dataDir,'与程序位置分开；升级时保留已有配置和作业数据。')}</div>`)+
    section('日常运行使用的 Windows 账户',`<div class="form-grid">${auxInput('运行账户','aux.install.account',x.account,'示例格式：计算机名\\账户名。沿用已准备的现场账户，不在这里创建新身份。')}<div class="field"><label>安装和维护授权</label><div style="padding:10px 0">在需要写入受保护位置时，由 Windows 确认授权</div><div class="help">日常运行保持现有非提权方式。</div></div></div>`)+
    note('info','本次安装范围','Sportmaster 客户端及已选择的组件；安装完成后单独配置相机与 WCS。');
  if(x.step===2)body=section('检查当前安装介质',`<div class="row between"><div><strong>Importer PC · Sportmaster 安装介质</strong><div class="help">来源：D:\\Install\\ImporterPC（设计示例）</div></div>${btn(x.checking?'正在检查…':'检查介质（模拟）','aux-install-check','secondary',x.checking?'disabled':'')}</div>`+
    auxChecklist([['文件完整性','校验清单与所选安装介质对应。',x.checked?'模拟通过':'未检查',x.checked?'info':'neutral'],['发布者与兼容性','显示可核对的发布者和组件信息；完整性不等于发布者可信。',x.checked?'模拟通过':'未检查',x.checked?'info':'neutral']]))+
    section('选择要安装的组件',`<table class="table"><thead><tr><th>组件</th><th>用途</th><th>本次选择</th></tr></thead><tbody><tr><td><strong>Sportmaster 客户端</strong></td><td>左右两路扫码及格口显示</td><td>${tag('必需','neutral')}</td></tr></tbody></table><div class="form-grid" style="margin-top:18px">${selectField('相机驱动包（示例）','aux.install.driver',x.driver,[['include','安装'],['skip','本次不安装']],'仅改变安装库存，不自动创建或绑定设备实例。')}${selectField('WCS 连接组件（示例）','aux.install.integration',x.integration,[['include','安装'],['skip','本次不安装']],'缺少适用组件时，首次配置将明确提示依赖缺失。')}</div>`);
  if(x.step===3)body=section('确认本次安装',`<div class="stack">${auxRow('程序位置',`<span class="mono">${esc(x.programDir)}</span>`)}${auxRow('数据位置',`<span class="mono">${esc(x.dataDir)}</span>`)}${auxRow('运行账户',esc(x.account))}${auxRow('客户端','Sportmaster')}${auxRow('相机驱动包',x.driver==='include'?'安装（示例）':'本次不安装')}${auxRow('WCS 连接组件',x.integration==='include'?'安装（示例）':'本次不安装')}</div>`)+
    note('info','安装后进入首次配置','此步骤准备程序与组件。尚不连接相机、发送 WCS 请求或接收包裹。')+
    `<div class="actions">${btn('导出本次部署计划','aux-install-plan','ghost')}${btn('查看安装检查摘要','aux-install-summary','ghost')}</div>`;
  if(x.step===4)body=x.done?`<div class="empty" style="padding:36px 20px">${icon('check',44)}<h2>安装完成</h2><p>模拟安装已完成，下一步配置工作站、相机和 WCS。</p>${tag('交互演示 · 未执行真实安装','info')}</div>`+
    auxChecklist([['程序与目录','本次所选程序、数据位置与运行账户已记录。','模拟完成','info'],['已选组件','安装库存与业务启用分开。','模拟完成','info'],['现场运行','尚未配置和验证真实设备。','待首次配置','warning']]):
    `<div class="empty" style="padding:60px 20px">${icon('folder',42)}<h2>正在模拟安装</h2><p>检查计划 → 准备目录 → 部署所选组件 → 生成安装摘要</p><progress max="100" value="${x.percent}" style="width:70%;height:16px"></progress><p>${x.percent}%</p></div>`;
  return auxTitle('安装与部署','安装流程设计演示 · 不写入系统或连接设备。',btn('返回设置','aux-return-settings','ghost'))+
    auxSteps(['位置与账户','介质与组件','确认安装','安装结果'],x.step)+`<div class="panel">${body}</div>`+
    `<div class="footer-actions between" style="justify-content:space-between"><span class="help">除附件中的安装入口外，后续步骤均为本次重设计建议。</span><div class="actions">${x.step>1&&x.step<4?btn('上一步','aux-install-prev'):''}${x.step<3?btn('下一步','aux-install-next','primary'):x.step===3?btn('开始模拟安装','aux-install-run','primary'):x.done?btn('进入首次配置','aux-install-setup','primary'):''}</div></div>`;
}

function renderBackup() {
  const A=auxState(),x=A.backup;
  const tabs=auxTabs([['template','配置模板'],['history','修改记录'],['full','完整备份与恢复']],x.tab,'aux-backup-tab-');
  let body='';
  if(x.tab==='template')body=section('导入到当前工作站',`<div class="row between"><div><strong>先核对对象与差异，再载入编辑区</strong><div class="help">模板不包含工作站身份、秘密和历史作业；导入不直接改变运行配置。</div></div><div class="actions">${btn('导出示例模板','aux-template-export','ghost')}${btn('载入模板示例','aux-template-import','secondary')}</div></div>`+
    (x.imported?`<div class="object-summary" style="display:grid;grid-template-columns:1.2fr 1fr 1fr;gap:18px;margin-top:18px"><div><div class="muted small">导入来源</div><strong>${esc(x.templateSource)}</strong></div><div><div class="muted small">目标工作站</div><strong>reference-17 · 身份保留</strong></div><div><div class="muted small">匹配范围</div><strong>Sportmaster · 两路相机与 WCS</strong></div></div><table class="table" style="margin-top:12px"><thead><tr><th>对象 / 字段</th><th>当前示例值</th><th>导入示例值</th></tr></thead><tbody><tr><td>左侧相机 · 连接地址</td><td class="mono">${esc(S.saved.left.address)}</td><td class="mono">${esc(x.targetAddresses.left)}</td></tr><tr><td>右侧相机 · 连接地址</td><td class="mono">${esc(S.saved.right.address)}</td><td class="mono">${esc(x.targetAddresses.right)}</td></tr><tr><td>WCS 服务 · 连接目标</td><td class="mono">${esc(S.saved.wcs.url)}</td><td>保持当前值</td></tr><tr><td>工作站与设备稳定身份</td><td>当前受保护身份</td><td>不覆盖，须人工核对设备对应</td></tr></tbody></table><div class="form-grid" style="margin-top:18px">${selectField('组件需要的敏感参数','aux.backup.credentials',x.credentials,[['keep','沿用当前受保护参数（模拟）'],['missing','有必需项缺失（演示错误）']],'示例模板不含秘密；真实必填条件由所选组件提供。')}${auxInput('变更原因','aux.backup.reason',x.reason,'随完整待生效修订保存。')}</div>${A.errors['aux.backup.credentials']?note('danger','模板尚不能保存',A.errors['aux.backup.credentials']):''}<div class="actions" style="margin-top:18px">${btn(x.staged?'已载入编辑草稿':'仅载入编辑草稿','aux-template-stage','secondary',x.staged?'disabled':'')}${btn('保存为待生效修订','aux-template-save','primary',!x.staged?'disabled title="先将已审阅差异载入编辑草稿"':'')}</div>`:
    `<div class="empty" style="padding:28px"><p>导入预览会列出每个目标对象、修改字段与缺失项。</p></div>`))+
    (x.pending?(x.savedRevision===S.runtimeRev&&S.runtimeKnown?note('success',`模板修订 v${x.savedRevision} 已确认运行`,'本次模板修改已经过模拟应用；新包接收状态仍以工作站运行状态为准。',btn('查看运行配置','aux-return-settings')):note('warning',`已保存 v${x.savedRevision||S.savedRev}，${S.runtimeKnown?`当前运行 v${S.runtimeRev}`:"运行版本待确认"}`,'完整待生效修订已生成。核对整站影响后再应用。',btn('查看应用状态','aux-return-settings'))):x.staged?note('info','模板内容已载入编辑草稿','当前尚未保存；继续检查或保存为待生效修订。'):'')+(x.saveError?note('danger','保存未完成',x.saveError):'');
  if(x.tab==='history')body=section('修改记录',`<p class="help">以下均为演示记录。配置修订与安装版本分开管理。</p><table class="table"><thead><tr><th>时间（UTC+08:00）</th><th>修订</th><th>修改范围</th><th>状态</th><th>操作</th></tr></thead><tbody><tr><td>2026-10-20 10:42</td><td class="mono">示例修订 13</td><td>右侧相机连接参数</td><td>${tag('已保存待生效','warning')}</td><td>${btn('查看差异','aux-history-diff-13','ghost')}</td></tr><tr><td>2026-10-20 09:10</td><td class="mono">示例修订 12</td><td>左右设备绑定与 WCS 服务</td><td>${tag('最后确认有效','success')}</td><td>${btn('查看差异','aux-history-diff-12','ghost')}</td></tr><tr><td>2026-10-19 16:20</td><td class="mono">示例修订 11</td><td>首次配置</td><td>${tag('历史修订','neutral')}</td><td>${btn('恢复为编辑草稿','aux-history-restore','ghost')}</td></tr></tbody></table>`)+note('info','恢复配置只产生新的编辑草稿','先比较与当前运行配置的差异，再保存和应用；不会把历史修订直接变成正在运行的版本。');
  if(x.tab==='full')body=section('读取完整备份',`<div class="form-grid">${auxInput('备份文件夹','aux.backup.directory',x.directory,'设计示例路径；扫描不恢复、不改动现有文件。')}<div class="field"><label>操作</label><div class="actions" style="padding-top:4px">${btn('扫描演示备份','aux-backup-scan')}${btn(x.creating?'创建中…':'创建完整备份（模拟）','aux-backup-create','secondary',x.creating?'disabled':'')}</div><div class="help">保留无效备份并说明原因；读取上限沿用现有实现核实。</div></div></div>`+
    `<table class="table"><thead><tr><th>备份时间</th><th>备份名称</th><th>完整性</th><th>大小</th><th>操作</th></tr></thead><tbody>${x.extra?`<tr><td>2026-10-20 11:00</td><td>完整备份 · 本次演示</td><td>${tag('模拟校验通过','info')}</td><td>24.6 MB（示例）</td><td>${btn('隔离恢复','aux-backup-restore-b4','ghost')}</td></tr>`:''}<tr><td>2026-10-20 09:40</td><td>reference-17 · 完整恢复集</td><td>${tag('模拟校验通过','info')}</td><td>24.2 MB（示例）</td><td>${btn('隔离恢复','aux-backup-restore-b1','ghost')}</td></tr><tr><td>2026-10-20 09:05</td><td>reference-17 · 不完整备份</td><td>${tag('恢复同伴目录缺失','danger')}</td><td>23.8 MB（示例）</td><td>${btn('查看原因','aux-backup-invalid','ghost')}</td></tr></tbody></table>`)+
    (x.restoring?note('info','正在恢复到隔离目录（模拟）','正在核对数据库与恢复同伴数据。此操作完成前不会开始第二次恢复。'):x.restored?note('warning','演示备份已恢复到隔离目录','未接管此目录，未开放新包，也未重新发送原请求。下一步核对原机交接与当前机器身份。',btn('核对原机交接','aux-backup-handover')):note('info','完整备份与配置模板用途不同','完整恢复集包含数据库及恢复所需同伴数据。恢复必须先进入隔离目录，再核对交接；配置模板不会带入原作业。'))+
    (x.handover?note('warning','恢复核对已记录，作业仍保持暂停','接管与恢复放行是独立动作；原有业务责任和未知结果仍需逐件核对。'):'');
  return auxTitle('备份、导入与恢复','为明确的对象保留配置和恢复证据。',btn('返回设置','aux-return-settings','ghost'))+tabs+`<div class="panel">${body}</div>`;
}

const AUX_HELP_CONTENT=[
  {id:'wcs',title:'WCS 连接异常',desc:'判断影响范围，再检查对应服务。',steps:['查看异常关联的 WCS 服务、左右影响范围及最后成功响应时间。','检查地址与端口、协议连接、响应内容；“地址可达”不能替代业务响应验证。','按检查结果修改对应参数或联系服务维护。保存后单独处理应用状态。','连接恢复后，仍需核对结果未知的原包裹；不要盲目重新扫码或重新提交。']},
  {id:'camera',title:'相机没有读到条码',desc:'从当前投递位定位到绑定相机。',steps:['在作业页确定左或右投递位，进入该相机的诊断。','核对物理设备标识、所选驱动与当前绑定；连接成功后再检查是否收到数据。','保留当前包裹和现场位置，按驱动说明处理焦距、触发或网络问题。','恢复连接后按真实验收要求确认条码来源，避免左右接反。']},
  {id:'pending',title:'保存后为什么还没生效',desc:'分清编辑、已保存与实际运行。',steps:['查看设置顶部的运行修订与已保存修订；二者不同表示尚未投入运行。','打开修改摘要，核对涉及的设备、服务及应用影响。','在安全维护时机执行既有的应用或受控重启流程。','读取实际运行结果。失败时保留待生效修订，并查清正在运行、已停止还是状态未知。']},
  {id:'unknown',title:'包裹结果待核对',desc:'核对原请求，不把未知结果当失败重发。',steps:['保留包裹及对应投递位，打开本包详情确认原请求身份。','只有服务支持可靠结果查询时，执行“核对 WCS 原请求结果”。','仍无法确定时保留原件并导出诊断摘要；网络恢复并不代表此包成功。','继续未执行步骤须以后端明确证明该步骤从未尝试为前提。']},
  {id:'install',title:'首次配置与安装维护',desc:'安装完成后准备工作站、设备和服务。',steps:['检查安装介质与组件来源，确认程序位置、数据位置和既有运行账户。','首次配置先读取身份，再配置设备实例并绑定左右投递位。','分层检查后保存待生效修订；真实设备尚未验证时不要宣称可生产。','后续修改直接从设置选择对象，不用重走首次向导。']},
  {id:'restore',title:'备份和换机恢复',desc:'先隔离恢复，再核对交接。',steps:['选择完整恢复集，核对数据库和恢复同伴目录；无效项保留并说明原因。','恢复到新的隔离目录，保留原失败记录与原请求。','读取原机退役、受保护登记、当前机器与目标目录等交接证据。','记录核对后仍保持暂停，未明确外部责任前不自动开放业务。']}
];
function renderSupport() {
  const x=auxState().support;
  const tabs=auxTabs([['help','离线帮助'],['export','诊断摘要'],['about','版本与安装']],x.tab,'aux-support-tab-');
  let body='';
  if(x.tab==='help') {
    const q=x.searched.trim().toLowerCase(),topics=AUX_HELP_CONTENT.filter(t=>!q||(t.title+t.desc+t.steps.join('')).toLowerCase().includes(q));
    const t=AUX_HELP_CONTENT.find(t=>t.id===x.topic)||AUX_HELP_CONTENT[0];
    body=`<div class="row" style="align-items:flex-end;margin-bottom:22px"><div style="flex:1">${field('查找操作或故障','aux.support.query',x.query,'例如：WCS、相机、待生效、备份。')}</div>${btn('搜索','aux-help-search','primary')}${btn('清除','aux-help-clear','ghost')}</div>`+
      `<div class="split" style="grid-template-columns:300px 1fr"><div>${topics.length?topics.map(o=>`<button class="list-row" data-action="aux-help-topic-${o.id}" style="width:100%;text-align:left;background:${o.id===x.topic?'#edf4ff':'#fff'};border:0;border-bottom:1px solid #dce2e9;cursor:pointer"><span><strong>${esc(o.title)}</strong><span class="help" style="display:block">${esc(o.desc)}</span></span></button>`).join(''):`<div class="empty"><p>没有匹配的任务。</p>${btn('清除搜索','aux-help-clear','ghost')}</div>`}</div><article><h2 style="font-size:22px;margin:0 0 10px">${esc(t.title)}</h2><p class="muted">${esc(t.desc)}</p><ol style="padding-left:24px;line-height:1.8">${t.steps.map(s=>`<li style="padding:7px 0 10px 8px">${esc(s)}</li>`).join('')}</ol><div class="actions" style="margin-top:24px">${btn('打开诊断','aux-open-diagnostics')}${btn('准备诊断摘要','aux-support-tab-export','ghost')}</div></article></div>`;
  }
  if(x.tab==='export')body=`<div class="split" style="grid-template-columns:1.15fr 1fr;align-items:start"><div>`+
    section('选择本次诊断范围',`<p><strong>reference-17（示例） · Sportmaster</strong></p><p class="help">关联左右投递位与 WCS；仅使用原型内的模拟记录。</p><div class="form-grid" style="margin-top:16px">${field('开始时间（含）','aux.support.start',x.start,'现场时间 UTC+08:00。',{type:'datetime-local',error:x.error&&x.errorField==='start'?x.error:''})}${field('结束时间（不含）','aux.support.end',x.end,'最多 7 天，不隐式转换时区。',{type:'datetime-local',error:x.error&&x.errorField==='end'?x.error:''})}</div>`)+
    section('包含内容',auxChecklist([['对象与配置摘要','工作站、设备、服务与配置修订。','脱敏','neutral'],['业务状态与异常','保留指定范围内的定位关系。','范围明确','neutral'],['已有连接检查','未检查层级明确为未验证。','保留边界','neutral']])+`<p class="help">不包含口令、令牌、包裹图片或任意磁盘日志。</p>`)+
    `<details><summary class="small muted">现有实现待核实项</summary><p class="help">旧页写有“预览固定 5 分钟”，具体含义需核实；不认定为采集范围或有效期。</p></details></div><aside style="border-left:1px solid #dce2e9;padding-left:24px"><h3>预览与导出</h3><p class="help" style="margin:8px 0 18px">先看清这次包含什么。只导出已审阅的预览，不发送到外部服务。</p>`+
    (x.error?note('danger','范围未通过检查',x.error):x.preview?`<div class="object-summary stack" style="display:grid;gap:14px;margin-bottom:20px"><strong>本次预览 · 模拟数据</strong><div><div class="muted small">时间范围 · UTC+08:00</div><strong>${esc(x.start.replace('T',' '))}<br>至 ${esc(x.end.replace('T',' '))}（不含）</strong></div><div><div class="muted small">对象与记录</div><strong>3 个连接对象 · 2 条示例异常<br>1 份配置修订摘要</strong></div><div><div class="muted small">导出内容</div><strong>与这份预览一致，秘密已排除</strong></div></div>`:note('neutral','尚未生成预览','修改范围后，需要重新预览才能导出。'))+
    `<div class="actions" style="flex-wrap:wrap">${btn(x.checking?'正在生成预览…':'预览本次摘要','aux-support-preview','secondary',x.checking?'disabled':'')}${btn('导出这份预览','aux-support-export','primary',!x.preview?'disabled title="先完成本次范围的预览"':'')}</div></aside></div>`;
  if(x.tab==='about')body=section('当前产品',`<div class="stack">${auxRow('产品','Importer PC · Sportmaster')}${auxRow('本次内容','产品交互与视觉重设计原型')}${auxRow('输入依据','2026-10-20 提供的 36 张截图')}${auxRow('业务范围','左右相机读取包裹号 → WCS 返回格口 → 页面展示')}</div>`)+
    section('安装与支持',`<div class="row between"><div><strong>安装 / 修复程序</strong><div class="help">在独立维护流程中核对目录、账户、介质和组件。</div></div>${btn('打开安装演示','aux-open-install')}</div>`)+
    note('info','此处展示的是重设计成果','真实发布版本、签名、安装状态和运行环境，应在客户端中从实际安装信息读取。');
  return auxTitle('诊断与支持','按现场任务找帮助；导出前先看清对象和内容。',btn('返回作业','aux-return-work','ghost'))+tabs+`<div class="panel">${body}</div>`;
}

function renderAdvanced() {
  const x=auxState().advanced;
  const tabs=auxTabs([['text','配置文本'],['legacy','旧版迁移'],['reconcile','原配置核对']],x.tab,'aux-advanced-tab-');
  let body='';
  if(x.tab==='text')body=section('高级配置文本',`<div class="row between" style="margin-bottom:14px"><div><strong>reference-17（示例） · Sportmaster · 配置格式 3</strong><div class="help">默认只读；表单与文本编辑使用同一份草稿，不形成两个同时可写的来源。</div></div>${btn(x.edit?'结束文本编辑':'进入文本编辑','aux-advanced-edit','secondary')}</div><textarea class="code" data-field="aux.advanced.text" style="width:100%;min-height:170px;padding:18px;box-sizing:border-box;font-size:15px" ${x.edit?'':'readonly'}>${esc(x.text)}</textarea><p class="help">这里只展示原图可辨认的 SchemaVersion 字段，不是一份完整运行配置；未知字段不能根据截图补造。</p><div class="actions">${btn('检查文本语法','aux-advanced-validate')}${btn('复制本段文本','aux-advanced-copy','ghost')}${btn('保存为完整待生效修订','aux-advanced-save','primary','disabled title="本原型只展示截图可辨认片段，缺少完整对象与参数，不可保存为运行配置"')}</div>`)+
    (x.validation?note(x.validation.ok?'info':'danger',x.validation.ok?'文本语法通过':'文本语法有误',x.validation.message):'')+
    section('表单编辑入口',`<div class="row between"><div><strong>通过对象表单修改设备、WCS 和运行策略</strong><div class="help">回到对象表单会保留本段高级文本草稿；完整转换需要实际字段描述和关联校验。</div></div>${btn('返回对象表单','aux-return-settings')}</div>`);
  if(x.tab==='legacy')body=note('warning','旧版本字段单独保留，不混入当前作业','以下来自旧 schema 1/2 策略页。出现称重或上传字段，不代表 Sportmaster 需要这些能力。')+`<div class="split" style="grid-template-columns:1.8fr 1fr;align-items:start"><div>`+
    section('旧称重策略',`<table class="table"><thead><tr><th>原字段</th><th>截图可见值</th><th>迁移处理</th></tr></thead><tbody><tr><td>称重来源</td><td class="mono">Disabled</td><td>保留“未启用”的原始含义</td></tr><tr><td>称重单位</td><td class="mono">Kg</td><td>由适用驱动或集成解释单位</td></tr><tr><td>精度</td><td class="mono">0</td><td>具体语义待核实，不擅自改成小数位</td></tr><tr><td>配置值</td><td>原图空白</td><td>不可把配置补值标成实测重量</td></tr><tr><td>本件等待期限 / 测量新鲜度</td><td class="mono">0 ms / 0 ms</td><td>0 的含义和允许范围待核实</td></tr><tr><td>重量有效期</td><td class="mono">UntilDispatch</td><td>原文说明持续到提交；AtAssociation 仅在关联时检查</td></tr></tbody></table>`)+
    `</div><aside style="border-left:1px solid #dce2e9;padding-left:24px">`+section('旧图片上传策略',`<p><span class="mono">BeforeMain</span><br>前置成功后才进入主链。</p><p style="margin-top:12px"><span class="mono">Attached</span><br>独立恢复。</p><p class="help" style="margin-top:14px">当前选择与其他字段未在截图中显示；只迁移到确认适用的 Application，不为 Sportmaster 增加图片预览。</p>`)+
    `<div class="actions" style="flex-wrap:wrap;margin-bottom:16px">${btn('检查迁移影响','aux-legacy-check')}${btn('导出核对清单','aux-legacy-export','ghost')}</div>`+
    (x.migration?note('warning','仍有待核实项','精度、配置值、0 值、上传选项及适用 Application 必须逐项核实。'):'')+`</aside></div>`;
  if(x.tab==='reconcile')body=section('读取原记录与运行证据',`<div class="row between"><div><strong>未确认配置 · 原记录核对</strong><div class="help">保留原配置字节、原错误及原请求；核对不代表恢复业务。</div></div>${btn('读取演示原记录','aux-reconcile-read')}</div>`+
    (x.loaded?auxChecklist([['原配置记录','本演示未读取真实文件；仅展示证据对象和范围。','示例已读','info'],['原字节校验值','真实值必须从原配置字节计算，不能人工伪造。','需实装生成','warning'],['运行输入','最后确认版本与已保存待生效修订分别核对。','模拟可比较','info']]):note('neutral','尚未读取核对证据','先明确原记录身份，再记录核对结论。')))+
    section('记录核对结论',auxInput('核对说明','aux.advanced.note',x.note,'跟随原记录保存；真实执行复用现有维护认证。'))+
    `<div class="actions">${btn('记录核对，保持暂停','aux-reconcile-confirm','primary',!x.loaded?'disabled title="先读取并核对原记录"':'')}${btn('准备受控修复草稿','aux-reconcile-repair','secondary',!x.loaded?'disabled title="先读取原记录"':'')}</div>`+
    (x.confirmed?note('warning','核对结论已记录，作业仍保持暂停','原请求没有被重新发送，历史错误没有被改写。'):'')+
    (x.repaired?note('info','受控修复内容已进入演示草稿','尚未保存和应用。需由实际实现完成原记录、对象关联和修复范围校验。'):'');
  return auxTitle('高级维护','仅在排障、兼容迁移和受控修复时使用。',btn('返回设置','aux-return-settings','ghost'))+tabs+`<div class="panel">${body}</div>`;
}

function renderAux(view) {
  auxState();
  return ({setup:renderSetup,install:renderInstall,backup:renderBackup,support:renderSupport,advanced:renderAdvanced}[view]||(()=>''))();
}

function auxValidateSetup(step) {
  const A=auxState(),x=A.setup;A.errors={};
  if(step===1){
    for(const k of ['configDir','dataDir'])if(!/^[a-zA-Z]:[\\/].+/.test(x[k]))A.errors['aux.setup.'+k]='请输入带盘符的示例文件夹路径，如 D:\\ImporterPC\\Data。';
    if(!x.loaded) A.errors['aux.setup.configDir']='请先读取所选配置，核对工作站身份。';
  }
  if(step===2&&x.left===x.right)A.errors['aux.setup.right']='左右投递位绑定了同一设备。请为右投递位选择另一个设备实例。';
  if(step===3)for(const k of ['queue','timeout','capacity','history'])if(!/^-?\d+$/.test(String(x[k])))A.errors['aux.setup.'+k]='请填写整数。真实有效范围须由 Application 或组件提供。';
  if(step===4&&!x.reason.trim())A.errors['aux.setup.reason']='请填写本次设置说明。';
  return Object.keys(A.errors).length===0;
}
function auxConfirmRestore() {
  const x=auxState().backup;
  openDialog('恢复到隔离目录',`<p>所选：<strong>reference-17 · 完整恢复集（模拟）</strong></p><p>保留数据库和恢复同伴数据；原运行目录保持原状。恢复后不自动接管目录、不开放新包、不重发原请求。</p>${field('新的隔离目录','aux.backup.restoreDir',x.restoreDir,'必须与当前数据目录不同；设计示例，不访问磁盘。')}${x.restoreError?note('danger','隔离目录不正确',x.restoreError):''}`,
    `${btn('取消','aux-dialog-close')}${btn('执行隔离恢复（模拟）','aux-backup-restore-run','primary')}`);
}
function auxHandoverDialog() {
  const x=auxState().backup;
  openDialog('核对原机交接',auxChecklist([
    ['原根退役与接管记录','明确原机目录不会继续承担同一份业务责任。',x.evidence?'模拟证据匹配':'未读取',x.evidence?'info':'neutral'],
    ['受保护登记与当前机器','当前机器与新的隔离目录登记一致。',x.evidence?'模拟证据匹配':'未读取',x.evidence?'info':'neutral'],
    ['原包裹与外部责任','恢复确认不抹除未知结果，仍需后续逐项处理。','保持隔离','warning']
  ])+note('info','不在此处开放作业','确认只记录恢复核对结论；实际接管和放行由独立条件控制。'),
  `${btn('关闭','aux-dialog-close')}${x.evidence?btn('记录核对，保持暂停','aux-backup-handover-confirm','primary'):btn('读取演示交接证据','aux-backup-handover-read','primary')}`);
}

function auxRootCandidate(view) {
  const A=auxState(),candidate=clone(S.saved);
  if(view==='backup') {
    candidate.left.address=A.backup.targetAddresses.left;
    candidate.right.address=A.backup.targetAddresses.right;
  }
  if(view==='setup') {
    candidate.bindings.left=A.setup.left==='camera-left'?'cam-left':'cam-right';
    candidate.bindings.right=A.setup.right==='camera-right'?'cam-right':'cam-left';
    candidate.bindings.service='wcs-01';
    for(const k of ['queue','timeout','capacity','history'])candidate.policies[k]=A.setup[k];
  }
  return candidate;
}
function auxStageRootTemplate() {
  const A=auxState(),b=A.backup;
  S.drafts.left.address=b.targetAddresses.left;S.drafts.right.address=b.targetAddresses.right;
  S.phase='dirty';S.draftRetained=false;b.staged=true;A.dirtyView='backup';render();
}
function auxCommitRoot(view,reason,label) {
  if(['validating','saving','applying'].includes(S.phase)){toast('当前操作正在完成，请稍候。');return;}
  const A=auxState(),v=A[view],candidate=auxRootCandidate(view);v.saveError='';
  if(v.baseRevision!==S.savedRev||S.failure==='conflict'){
    v.saveError=`编辑基于 v${v.baseRevision}，当前保存状态已有变化。输入与草稿已保留，请重新读取并审阅差异后保存。`;
    S.phase='conflict';render();return;
  }
  const errors=validateCandidate(candidate);
  if(Object.keys(errors).length){
    S.errors=errors;S.phase='invalid';v.saveError=Object.entries(errors).map(([k,text])=>(fieldNames[k.split('.')[1]]||k)+'：'+text).join(' ');render();return;
  }
  S.phase='saving';render();
  setTimeout(()=>{
    if(S.failure==='save') {v.saveError='保存失败。已输入内容和原运行配置保留；可以再次保存。';S.phase='save-failed';render();return;}
    if(v.baseRevision!==S.savedRev){v.saveError='保存时发现已有新修订，未覆盖。请重新读取并比较，编辑已保留。';S.phase='conflict';render();return;}
    const changes=allChanges(candidate,S.saved);
    S.saved=clone(candidate);S.savedRev++;S.editBase=S.savedRev;
    for(const change of changes)S.drafts[change.object][change.key]=change.after;
    S.changeLog.unshift({rev:S.savedRev,time:'11:48:00（模拟）',object:label,changes,reason});
    S.phase='pending';S.errors={};S.draftRetained=false;v.baseRevision=S.savedRev;v.savedRevision=S.savedRev;
    if(view==='backup'){v.pending=true;v.staged=true;}else{v.saved=true;v.applied=false;}
    auxSaveSnapshot(view);render();toast(`已保存 v${S.savedRev}，${S.runtimeKnown?`当前仍运行 v${S.runtimeRev}`:"运行版本待确认"}。`);
  },650);
}

function auxAction(action,element) {
  if(!action.startsWith('aux-'))return false;
  const A=auxState(),x=A.setup,i=A.install,b=A.backup,u=A.support,a=A.advanced;
  if(action==='aux-dialog-close'){closeDialog();return true;}
  if(action==='aux-return-work'){go('work');return true;}
  if(action==='aux-return-settings'){go('settings','summary');return true;}
  if(action==='aux-open-install'){go('install');return true;}
  if(action==='aux-open-diagnostics'){go('diagnostics');return true;}
  if(action==='aux-setup-next'){if(auxValidateSetup(x.step))x.step++;render();return true;}
  if(action==='aux-setup-prev'){x.step=Math.max(1,x.step-1);render();return true;}
  if(action==='aux-setup-folder'){x.configDir='D:\\ImporterPC\\Config';x.dataDir='D:\\ImporterPC\\Data';x.loaded=false;A.dirtyView='setup';render();toast('已填入示例路径；请读取配置核对身份。');return true;}
  if(action==='aux-setup-read'){x.loaded=true;if(!auxValidateSetup(1)){x.loaded=false;render();return true;}x.baseRevision=S.savedRev;x.saveError='';A.errors={};render();toast('示例配置已读取；工作站身份为 reference-17 / Sportmaster。');return true;}
  if(action==='aux-setup-draft'){x.draft=true;auxSaveSnapshot('setup');render();toast('未完成输入已暂存到本次演示会话；不是可运行配置。');return true;}
  if(action==='aux-setup-help'){openDialog('首次配置：'+['核对工作站','设备与位置','运行策略','检查并保存'][x.step-1],`<p>${['先读取身份，再核对两个文件夹。切换目录不是修改工作站主键，也不启动任何设备。','驱动是设备的实现；设备实例对应真实设备；绑定决定设备服务于左还是右投递位。三个对象不要混为一个选择框。','沿用原图参数，但不把示例值当生产默认。确认参数作用范围、0 的含义和允许范围后才能配置真实现场。','先检查完整配置，再保存待生效修订。实际运行状态必须在应用后读回；仅保存不能证明已经投入运行。'][x.step-1]}</p>`,btn('返回当前步骤','aux-dialog-close','primary'));return true;}
  if(action==='aux-setup-check'){
    for(let s=1;s<=4;s++)if(!auxValidateSetup(s)){x.step=s;render();return true;}
    x.checking=true;render();setTimeout(()=>{x.checking=false;x.checked=true;render();toast('模拟分层检查完成；真实设备与业务响应仍未验证。');},650);return true;
  }
  if(action==='aux-setup-save'){
    if(!auxValidateSetup(4)){render();return true;}
    if(!x.checked){toast('先完成本次配置的分层检查。');return true;}
    auxCommitRoot('setup',x.reason,'首次配置 · 绑定与运行策略');return true;
  }
  if(action==='aux-setup-apply'){
    if(!x.saved){toast('本次首配已修改，请重新检查并保存。');render();return true;}
    if(S.hasUnknown||!S.runtimeKnown){x.applyError='仍有原请求结果或运行归属待核对，当前不能应用。请先进入诊断处理。';render();return true;}
    openDialog('应用首配并检查运行（模拟）',`<p>作用范围：<strong>reference-17 整个工作站</strong>，包括左右相机、WCS 服务和运行策略。</p><p>应用 v${S.savedRev} 前暂停新包裹接收，复用既有维护授权与运行条件核对。应用完成仍保持暂停，等待现场确认。</p>${note('info','本次仅演示','不连接相机、不发送 WCS 业务，也不修改真实配置。')}`,`${btn('稍后应用','aux-dialog-close')}${btn('开始模拟应用','aux-setup-apply-run','primary')}`);return true;
  }
  if(action==='aux-setup-apply-run'){
    closeDialog();x.applying=true;x.applyError='';S.phase='applying';S.paused=true;S.maintenance=true;render();
    setTimeout(()=>{
      x.applying=false;S.maintenance=false;
      if(S.failure==='apply'||S.failure==='unknown-runtime'){
        S.runtimeKnown=S.failure!=='unknown-runtime';S.phase='apply-failed';x.applyError=S.runtimeKnown?`应用失败，已确认仍运行 v${S.runtimeRev}；新包接收保持暂停。`:'应用失败，当前运行版本无法确认；保持新包接收暂停并进入诊断。';render();return;
      }
      S.runtime=clone(S.saved);S.runtimeRev=S.savedRev;S.runtimeKnown=true;S.phase='clean';S.loginRequired=S.runtime.modules.login==='on';
      S.changeLog.unshift({rev:S.runtimeRev,time:'11:49:00（模拟）',object:'整个工作站',changes:[],reason:'首配运行端已确认生效；接收保持暂停'});
      x.saved=false;x.applied=true;auxSaveSnapshot('setup');render();
    },900);return true;
  }
  if(action==='aux-install-next'){
    A.errors={};
    if(i.step===1){for(const k of ['programDir','dataDir'])if(!/^[a-zA-Z]:[\\/].+/.test(i[k]))A.errors['aux.install.'+k]='请输入带盘符的示例文件夹路径。';if(i.programDir.toLowerCase()===i.dataDir.toLowerCase())A.errors['aux.install.dataDir']='请将数据与程序放在不同的示例文件夹。';if(!i.account.trim())A.errors['aux.install.account']='请填写既有 Windows 运行账户。';}
    if(Object.keys(A.errors).length){render();return true;}
    if(i.step===2&&!i.checked){toast('先检查当前安装介质。');return true;}
    i.step=Math.min(4,i.step+1);render();return true;
  }
  if(action==='aux-install-prev'){i.step=Math.max(1,i.step-1);render();return true;}
  if(action==='aux-install-check'){i.checking=true;render();setTimeout(()=>{i.checked=true;i.checking=false;render();toast('模拟介质检查完成；尚未安装。');},650);return true;}
  if(action==='aux-install-plan'){auxDownload('importer-install-plan-demo.json',{kind:'UI prototype installation plan',simulated:true,programDirectory:i.programDir,dataDirectory:i.dataDir,runtimeAccount:i.account,application:'Sportmaster',cameraPackage:i.driver,integrationPackage:i.integration});toast('已导出本次模拟部署计划。');return true;}
  if(action==='aux-install-summary'){openDialog('安装检查摘要（模拟）',auxChecklist([['介质完整性','当前演示介质可用于此安装计划。','模拟通过','info'],['组件选择','主客户端和所选驱动/连接组件。','已记录','neutral'],['设备与业务','安装不会启用新包接收。','未启动','neutral']]),btn('返回计划','aux-dialog-close','primary'));return true;}
  if(action==='aux-install-run'){i.step=4;i.installing=true;i.percent=10;A.dirtyView=null;render();let n=0;const timer=setInterval(()=>{n++;i.percent=Math.min(100,10+n*30);if(i.percent===100){clearInterval(timer);i.done=true;i.installing=false;auxSaveSnapshot('install');}render();},420);return true;}
  if(action==='aux-install-setup'){x.step=1;go('setup');return true;}
  if(action.startsWith('aux-backup-tab-')){b.tab=action.slice('aux-backup-tab-'.length);render();return true;}
  if(action==='aux-template-export'){auxDownload('sportmaster-example.template.json',{kind:'UI configuration template example',simulated:true,application:'Sportmaster',scope:['camera-left','camera-right','wcs-main'],excluded:['workstation-identity','secrets','historical-jobs'],exampleAddresses:{left:'192.0.2.31',right:'192.0.2.32'}});toast('已导出不含秘密与作业历史的示例模板。');return true;}
  if(action==='aux-template-import'){b.imported=true;b.staged=false;b.pending=false;b.baseRevision=S.savedRev;b.saveError='';b.targetAddresses={left:'192.0.2.31',right:'192.0.2.32'};b.templateSource='sportmaster-example.template.json（模拟模板）';render();return true;}
  if(action==='aux-template-stage'){
    const overlaps=allChanges(S.drafts,S.saved).filter(d=>d.key==='address'&&['left','right'].includes(d.object));
    if(overlaps.length){openDialog('模板与现有编辑重叠',`<p>相机地址已有未保存修改。请选择是否用已审阅的模板地址替换这些编辑；其他字段草稿保留。</p>${compareTable(overlaps)}`,`${btn('继续原编辑','aux-dialog-close')}${btn('用模板替换这些地址','aux-template-stage-confirm','primary')}`);return true;}
    auxStageRootTemplate();return true;
  }
  if(action==='aux-template-stage-confirm'){closeDialog();auxStageRootTemplate();return true;}
  if(action==='aux-template-save'){
    A.errors={};if(b.credentials==='missing')A.errors['aux.backup.credentials']='所选示例组件有必需敏感项缺失。请沿用已核实的受保护参数；不能从模板猜测或补造。';if(!b.reason.trim())A.errors['aux.backup.reason']='请填写此次模板导入的变更原因。';
    if(Object.keys(A.errors).length){render();return true;}
    auxCommitRoot('backup',b.reason,'配置模板 · 左右相机地址');return true;
  }
  if(action.startsWith('aux-history-diff-')){const v=action.slice(-2);openDialog('示例修订 '+v+' · 修改差异',`<p>作用范围：reference-17 / Sportmaster 整体配置修订。</p><table class="table"><thead><tr><th>对象</th><th>字段</th><th>修改</th></tr></thead><tbody><tr><td>右侧读码相机</td><td>连接地址（示例）</td><td class="mono">192.0.2.22 → 192.0.2.32</td></tr></tbody></table>${note('info','只查看记录','此窗口不恢复或应用配置。')}`,btn('关闭','aux-dialog-close','primary'));return true;}
  if(action==='aux-history-restore'){openDialog('从历史修订准备草稿',`<p>将示例修订 11 的可复用设置载入一份新的编辑草稿。</p><p>当前运行配置保持原状。历史工作站身份及敏感参数不自动覆盖；保存和应用需要另行完成。</p>`,`${btn('取消','aux-dialog-close')}${btn('创建演示草稿','aux-history-restore-confirm','primary')}`);return true;}
  if(action==='aux-history-restore-confirm'){closeDialog();b.tab='template';b.imported=true;b.staged=false;b.pending=false;b.baseRevision=S.savedRev;b.targetAddresses={left:'192.0.2.21',right:'192.0.2.22'};b.templateSource='示例历史修订 11 · 恢复预览';A.dirtyView='backup';render();toast('历史配置差异已准备好，审阅后可载入草稿。');return true;}
  if(action==='aux-backup-scan'){b.scanned=true;render();toast('示例备份已扫描；无效恢复集仍保留在列表中。');return true;}
  if(action==='aux-backup-invalid'){openDialog('恢复集不完整',note('danger','缺少恢复同伴目录','只有数据库文件不足以恢复原请求责任与恢复信息。请找到完整恢复集，或保留现场并联系维护人员。')+`<p class="help">此条是模拟故障样例。没有删除或修复任何真实文件。</p>`,btn('返回备份列表','aux-dialog-close','primary'));return true;}
  if(action==='aux-backup-create'){openDialog('创建完整备份（模拟）',`<p>范围：当前工作站的数据库及恢复所需同伴数据。</p><p>真实执行须进入受控维护窗口并核对在途任务。示例条件为新包已暂停、没有在途包裹。</p>${note('info','不删除旧备份','达到份数限制时提示处理，不为本次备份静默删掉旧文件。')}`,`${btn('取消','aux-dialog-close')}${btn('模拟核对并创建','aux-backup-create-run','primary')}`);return true;}
  if(action==='aux-backup-create-run'){closeDialog();b.creating=true;S.paused=true;render();setTimeout(()=>{b.creating=false;b.extra=true;render();toast('完整备份模拟已完成；新包接收仍暂停。');},700);return true;}
  if(action.startsWith('aux-backup-restore-b')){if(b.restoring){toast('当前隔离恢复仍在进行，请等待本次结果。');return true;}b.selected=action.slice('aux-backup-restore-'.length);b.restoreError='';auxConfirmRestore();return true;}
  if(action==='aux-backup-restore-run'){
    if(!/^[a-zA-Z]:[\\/].+/.test(b.restoreDir)||b.restoreDir.toLowerCase()==='d:\\importerpc\\data'){b.restoreError='请使用新的隔离文件夹，不能覆盖当前示例数据目录 D:\\ImporterPC\\Data。';auxConfirmRestore();return true;}
    closeDialog();b.restoring=true;render();toast('正在模拟恢复完整恢复集…');setTimeout(()=>{b.restoring=false;b.restored=true;b.evidence=false;b.handover=false;auxSaveSnapshot('backup');render();toast('已模拟恢复到隔离目录；没有开放业务。');},800);return true;
  }
  if(action==='aux-backup-handover'){auxHandoverDialog();return true;}
  if(action==='aux-backup-handover-read'){b.evidence=true;auxHandoverDialog();return true;}
  if(action==='aux-backup-handover-confirm'){closeDialog();b.handover=true;S.paused=true;auxSaveSnapshot('backup');render();return true;}
  if(action.startsWith('aux-support-tab-')){u.tab=action.slice('aux-support-tab-'.length);render();return true;}
  if(action==='aux-help-search'){u.searched=u.query;const match=AUX_HELP_CONTENT.find(t=>(t.title+t.desc+t.steps.join('')).toLowerCase().includes(u.searched.toLowerCase()));if(match)u.topic=match.id;render();return true;}
  if(action==='aux-help-clear'){u.query='';u.searched='';render();return true;}
  if(action.startsWith('aux-help-topic-')){u.topic=action.slice('aux-help-topic-'.length);render();return true;}
  if(action==='aux-support-preview'){
    u.error='';u.errorField='';u.preview=false;
    const start=Date.parse(u.start+':00+08:00'),end=Date.parse(u.end+':00+08:00');
    if(!Number.isFinite(start)){u.error='开始时间不是有效日期及时间，请检查这一项。';u.errorField='start';}
    else if(!Number.isFinite(end)){u.error='结束时间不是有效日期及时间，请检查这一项。';u.errorField='end';}
    else if(end<=start){u.error='结束时间必须晚于开始时间。其他输入已经保留。';u.errorField='end';}
    else if(end-start>7*86400000){u.error='本次支持范围最多为 7 天，请缩小时间范围。';u.errorField='end';}
    if(u.error){render();return true;}
    const captured={start:u.start,end:u.end};u.checking=true;render();setTimeout(()=>{u.checking=false;if(u.start!==captured.start||u.end!==captured.end){u.preview=false;render();toast('时间范围已改变，请重新生成预览。');return;}u.preview=true;u.previewRange=captured;render();},600);return true;
  }
  if(action==='aux-support-export'){
    if(!u.preview){toast('请先生成并审阅本次预览。');return true;}
    auxDownload('importer-diagnostics-demo.json',{kind:'UI diagnostics preview',simulated:true,workstation:'reference-17',application:'Sportmaster',start:u.start+':00+08:00',endExclusive:u.end+':00+08:00',objects:['left-camera','right-camera','wcs-main'],records:[{type:'connection-unavailable',object:'wcs-main',data:'simulated'},{type:'result-unknown',object:'left-camera',data:'simulated'}],excluded:['passwords','tokens','parcel-images','arbitrary-disk-logs']});u.exported=true;toast('已导出这份模拟预览，没有发送到外部服务。');return true;
  }
  if(action.startsWith('aux-advanced-tab-')){a.tab=action.slice('aux-advanced-tab-'.length);render();return true;}
  if(action==='aux-advanced-edit'){a.edit=!a.edit;render();return true;}
  if(action==='aux-advanced-validate'){
    try{const obj=JSON.parse(a.text);if(!obj||Array.isArray(obj)||typeof obj!=='object')throw new Error('顶层必须是 JSON 对象。');a.validation={ok:true,message:'仅验证了 JSON 语法。完整对象引用、未知字段和运行参数仍需实际后端校验；本片段不能保存为完整配置。'};}
    catch(e){a.validation={ok:false,message:'请检查引号、逗号和括号。输入内容已保留。技术详情：'+e.message};}render();return true;
  }
  if(action==='aux-advanced-copy'){if(navigator.clipboard&&navigator.clipboard.writeText)navigator.clipboard.writeText(a.text).then(()=>toast('已复制此段配置文本。')).catch(()=>{openDialog('复制配置片段',`<pre class="code">${esc(a.text)}</pre>`,btn('关闭','aux-dialog-close','primary'));});else openDialog('复制配置片段',`<pre class="code">${esc(a.text)}</pre>`,btn('关闭','aux-dialog-close','primary'));return true;}
  if(action==='aux-legacy-check'){a.migration=true;render();return true;}
  if(action==='aux-legacy-export'){auxDownload('legacy-migration-review-demo.json',{kind:'UI legacy migration review',simulated:true,sourcePage:'15-maintenance-6.png',weight:{source:'Disabled',unit:'Kg',precisionVisible:'0 (meaning unverified)',configuredValue:'not visible',waitVisible:'0 ms (meaning unverified)',freshnessVisible:'0 ms (meaning unverified)',validityVisible:'UntilDispatch'},imageModesMentioned:['BeforeMain','Attached'],unverified:['Current image mode','Application applicability','Precision semantics','Zero semantics'],sportmasterPreviewAdded:false});toast('已导出旧版字段核对清单。');return true;}
  if(action==='aux-reconcile-read'){a.loaded=true;render();return true;}
  if(action==='aux-reconcile-confirm'){if(!a.note.trim()){A.errors['aux.advanced.note']='请填写针对原记录的核对说明。';render();return true;}a.confirmed=true;S.paused=true;auxSaveSnapshot('advanced');render();return true;}
  if(action==='aux-reconcile-repair'){a.repaired=true;A.dirtyView='advanced';render();return true;}
  return false;
}

function setAuxScenario(name) {
  const A=auxState();A.dirtyView=null;A.errors={};
  if(name==='setup-devices'){S.view='setup';A.setup.step=2;}
  if(name==='setup-check'){S.view='setup';A.setup.step=4;A.setup.checked=true;A.setup.saved=false;A.setup.applied=false;}
  if(name==='setup-pending'){S.view='setup';A.setup.step=4;A.setup.checked=true;A.setup.saved=true;A.setup.applied=false;S.savedRev=S.runtimeRev+1;S.editBase=S.savedRev;S.phase='pending';A.setup.savedRevision=S.savedRev;A.setup.baseRevision=S.savedRev;}
  if(name==='install-review'){S.view='install';A.install.step=3;A.install.checked=true;}
  if(name==='install-complete'){S.view='install';A.install.step=4;A.install.done=true;A.install.percent=100;}
  if(name==='template-preview'){S.view='backup';A.backup.tab='template';A.backup.imported=true;A.backup.staged=false;A.backup.pending=false;}
  if(name==='backup-recovery'){S.view='backup';A.backup.tab='full';A.backup.restored=true;}
  if(name==='support-preview'){S.view='support';A.support.tab='export';A.support.start='2026-10-20T10:00';A.support.end='2026-10-20T11:00';A.support.previewRange={start:A.support.start,end:A.support.end};A.support.preview=true;A.support.error='';A.support.errorField='';}
  if(name==='support-invalid'){S.view='support';A.support.tab='export';A.support.preview=false;A.support.end='2026-10-19T10:00';A.support.error='结束时间必须晚于开始时间。其他输入已经保留。';A.support.errorField='end';}
  if(name==='legacy-review'){S.view='advanced';A.advanced.tab='legacy';A.advanced.migration=true;}
  if(name==='reconcile'){S.paused=true;S.view='advanced';A.advanced.tab='reconcile';A.advanced.loaded=true;A.advanced.confirmed=true;}
  render();
}
