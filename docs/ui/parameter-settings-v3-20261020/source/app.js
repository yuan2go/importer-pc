const app=document.querySelector('#app'),dialog=document.querySelector('#dialog');
let noticeTimer;
function notify(message){const el=document.querySelector('#notice');el.textContent=message;el.style.display='block';clearTimeout(noticeTimer);noticeTimer=setTimeout(()=>el.style.display='none',3800);}
function render(){app.innerHTML=frame();bindSearch();const close=document.querySelector('.window-controls span:last-child');if(close){close.parentElement.removeAttribute('aria-hidden');close.dataset.action='close-editor';close.tabIndex=0;close.setAttribute('role','button');close.setAttribute('aria-label','关闭前处理未保存更改');}const scroll=document.querySelector('#main-scroll');if(scroll)scroll.scrollTop=state.pageScroll[state.page]||0;}
function renderPage(){const scroll=document.querySelector('#main-scroll');if(!scroll)return;const top=scroll.scrollTop;scroll.innerHTML=pageBody();scroll.scrollTop=top;updateChrome();}
function updateChrome(){const nav=document.querySelector('.directory');if(nav)nav.innerHTML=directory();const save=document.querySelector('.save-bar');if(save)save.innerHTML=savebar();document.querySelector('.runtime-strip').innerHTML=runtimeBar();document.querySelector('.toolbar').innerHTML=toolbar();}
function navigate(page,focusKey=null){if(!pages[page])return;const scroll=document.querySelector('#main-scroll');if(scroll)state.pageScroll[state.page]=scroll.scrollTop;state.page=page;state.view='settings';state.search='';render();if(focusKey)requestAnimationFrame(()=>focusField(focusKey));}
function focusField(key){const page=defs[key]?.[1];if(!page)return;if(state.page!==page||state.view!=='settings'){navigate(page,key);return;}state.search='';const search=document.querySelector('#settings-search');if(search)search.value='';document.querySelector('.directory').innerHTML=directory();const el=document.getElementById('f-'+key);if(el){let parent=el.parentElement;while(parent){if(parent.tagName==='DETAILS')parent.open=true;parent=parent.parentElement;}el.scrollIntoView({block:'center'});el.focus();state.lastField=key;}}
function bindSearch(){const el=document.querySelector('#settings-search');if(!el)return;el.addEventListener('input',()=>{state.search=el.value;document.querySelector('.directory').innerHTML=directory();});el.addEventListener('keydown',e=>{if(e.key==='Escape'){e.preventDefault();el.value='';state.search='';document.querySelector('.directory').innerHTML=directory();}if(e.key==='Enter'){e.preventDefault();const first=document.querySelector('.search-row');if(first)act(first.dataset.action);}});}
const currentCandidate=()=>JSON.stringify(state.draft);
const connectionSignature=()=>state.page==='rcs'?state.draft.endpoint:state.draft[state.page+'Ip'];
function clearFieldError(key){const node=document.querySelector(`[data-key="${key}"]`);if(node){node.classList.remove('is-error');const err=node.querySelector('.error');if(err)err.remove();}}
app.addEventListener('input',e=>{
 const el=e.target;if(el.dataset.field){const key=el.dataset.field;state.draft[key]=el.type==='checkbox'?el.checked:el.value;state.lastField=key;delete state.errors[key];clearFieldError(key);const node=el.closest('.field');if(node)node.classList.toggle('is-dirty',String(state.draft[key])!==String(state.saved[key]));if(key==='pattern'&&state.sampleResult){state.sampleResult='规则已改变，原检查结果已过期。';document.querySelector('#sample-result').innerHTML=note(state.sampleResult,'warning');}updateChrome();}
 if(el.hasAttribute('data-sample')){state.sample=el.value;if(state.sampleResult){state.sampleResult='样例已改变，原检查结果已过期。';document.querySelector('#sample-result').innerHTML=note(state.sampleResult,'warning');}}
});
app.addEventListener('change',e=>{
 const el=e.target;if(el.dataset.field&&(el.type==='checkbox'||el.tagName==='SELECT')){state.draft[el.dataset.field]=el.type==='checkbox'?el.checked:el.value;delete state.errors[el.dataset.field];renderPage();document.getElementById(el.id)?.focus();}
 if(el.dataset.pref){state[el.dataset.pref]=el.value;notify('显示偏好已在本机保存（模拟），不改变业务配置。');}
 if(el.id==='scene-picker')setScene(el.value);
});
function modal(title,body,footer,sub=''){
 state.dialog=title;dialog.innerHTML=`<div class="dialog-layout"><header class="dialog-header"><div><h2 id="dialog-title">${title}</h2>${sub?`<p>${sub}</p>`:''}</div>${btn(icon('close'),'dismiss','ghost icon-only','aria-label="关闭对话框"')}</header><div class="dialog-body">${body}</div><footer class="dialog-footer">${footer}</footer></div>`;
 if(!dialog.open)dialog.showModal();
}
function closeModal(){dialog.close();state.dialog=null;}
dialog.addEventListener('cancel',e=>{if(state.dialog==='正在保存更改'){e.preventDefault();return;}state.dialog=null;});
function diffTable(list=changes()){return `<table class="diff-table"><thead><tr><th>修改内容</th><th>已保存值</th><th>本次更改</th></tr></thead><tbody>${list.map(x=>`<tr><td>${esc(x.label)}<span class="obj">${pages[x.page].group} › ${pages[x.page].title}</span></td><td>${esc(valueLabel(x.before))}</td><td class="new">${esc(valueLabel(x.after))}</td></tr>`).join('')}</tbody></table>`;}
function review(save=true){
 const c=changes();if(!c.length){notify('当前没有未保存的更改。');return;}
 const guards=`<div class="prerequisite"><div class="step">${icon(state.paused?'checkCircle':'pause',state.paused?'green':'amber')}<div><strong>${state.paused?'新入场已暂停':'保存前需要暂停新入场'}</strong><p>${state.paused?'没有在途外呼，可继续保存（模拟状态）。':'先暂停新入场，再等待在途外呼结束。'}</p></div></div>${state.paused?'':btn('暂停新入场','review-pause','small')}</div><div class="prerequisite"><div class="step">${icon(state.authorized?'checkCircle':'shield',state.authorized?'green':'')}<div><strong>${state.authorized?'维护授权有效':'维护授权'}</strong><p>${state.authorized?'本次沿用有效授权。':'仅在受控修改时授权；请使用演示内容，不输入真实口令。'}</p></div></div></div>${state.authorized?'':`<div class="authorize-fields"><div class="field"><label for="auth-name">演示账号</label><input id="auth-name" value="maintenance-demo" autocomplete="off"></div><div class="field"><label for="auth-proof">演示口令</label><input id="auth-proof" type="password" placeholder="任意演示内容" autocomplete="off"></div>${btn('授权','authorize')}</div><div id="auth-error"></div>`}<div class="field"><label for="change-reason">修改原因</label><textarea id="change-reason" maxlength="1024" placeholder="说明修改目的，便于后续追溯">${esc(state.reason)}</textarea><p class="hint">1～1024 个字符；不会记录维护口令。</p></div>`;
 modal(save?'保存更改':'查看更改',`<p style="margin:0;font-size:13px">本次保存 <strong>${c.length} 项更改</strong>，涉及 ${new Set(c.map(x=>x.page)).size} 个设置对象。</p>${diffTable(c)}${save?note('保存后形成新的配置修订，<strong>受控重启后才投入运行</strong>。当前包裹继续保留原请求和原配置。')+guards:''}`,save?`${btn('继续编辑','dismiss')}${btn('确认保存','submit','primary',(!state.paused||!state.authorized||!state.reason.trim())?'disabled':'')}`:btn('返回设置','dismiss','primary'),'作用范围：当前供包台的全部未保存更改');
}
function startSave(){
 if(state.unknown){act('inspect');return;}if(state.missing){notify('请先恢复所需驱动，当前输入已保留。');return;}
 const keys=validate();if(keys.length){navigate(defs[keys[0]][1],keys[0]);return;}review(true);
}
dialog.addEventListener('input',e=>{
 if(e.target.id==='change-reason'){state.reason=e.target.value;const submit=dialog.querySelector('[data-action="submit"]');if(submit)submit.disabled=!state.paused||!state.authorized||!state.reason.trim();}
});
function savedDialog(){
 modal('更改已保存',`<div class="dialog-success"><span class="success-icon">${icon('check','lg')}</span><div><h3>已保存，尚未投入运行</h3><p>运行值和已保存值分别显示，可随时再次核对。</p></div></div><div class="revision-row"><div><span>当前运行</span><strong>版本 ${state.revision}</strong><small>继续使用原配置</small></div><div><span>已保存</span><strong>版本 ${state.savedRevision}</strong><small>等待受控重启</small></div></div>${note('更改会在满足启动条件后的受控重启中应用。重连设备不会应用这份配置，也不会自动恢复新入场。')}`,`${btn('查看配置状态','configuration-status')}${btn('返回设置','dismiss','primary')}`);
}
function configurationStatus(){
 modal('配置状态',`<div class="revision-row"><div><span>实际运行配置</span><strong>版本 ${state.revision}</strong><small>本次模拟运行已确认</small></div><div><span>已保存配置</span><strong>版本 ${state.savedRevision}</strong><small>${state.pending?'待受控重启':'与运行版本一致'}</small></div></div>${note(state.pending?'版本 '+state.savedRevision+' 尚未应用。需要按原生客户端的受控启动条件重启，核对启动结果后再恢复新入场。':'当前没有待应用更改。')}<div class="stage-list"><div>${icon('checkCircle','green')}已保存值与运行值分别读取</div><div>${icon(state.pending?'clock':'checkCircle',state.pending?'amber':'green')}${state.pending?'等待受控启动核对':'当前运行已确认（模拟）'}</div></div><p class="muted" style="font-size:12px">如果应用失败，应说明现在运行的版本；不能把上次有效记录当作本次启动成功。</p>`,`${btn('返回设置','dismiss')}${btn('查看启动与恢复说明','restart-info','primary')}`);
}
function showTools(){
 const row=(ico,title,desc,action)=>`<button class="tool-row" data-action="${action}">${icon(ico)}<span><strong>${title}</strong><p>${desc}</p></span>${icon('chevron','chevron')}</button>`;
 modal('设置工具',`<div class="tool-list"><div class="tool-section">配置文件与草稿</div>${row('download','导出设置模板','复制同版通用策略，排除身份、设备地址和敏感数据。','export-template')}${row('upload','导入设置模板','先查看适用对象与差异，确认后只进入草稿。','template')}${row('edit','未完成的设置','保留、继续或明确重新读取草稿。','draft-tools')}<div class="tool-section" style="margin-top:17px">按需要使用</div>${row('folder','自动备份与完整备份','备份数据库及同名恢复目录。','page:backup')}${row('component','设备与模块管理','查看安装详情、能力实例和高级绑定。','installation')}${row('file','高级配置与运行状态','配置文本、原账核对和旧版兼容设置。','advanced-tools')}${row('restore','恢复后核对','原机恢复、原业务责任及恢复交接。','recovery')}</div>`,btn('关闭','dismiss'));
}
function showTemplate(){
 const list=[{key:'history',label:defs.history[0],page:'data',before:state.draft.history,after:'800'},{key:'queue',label:defs.queue[0],page:'processing',before:state.draft.queue,after:'256'}];
 modal('导入设置模板',`<div class="readonly-line" style="margin-top:0"><span class="label">模板文件</span><span class="value mono">sportmaster-settings-demo.json</span></div><div class="readonly-line"><span class="label">适用对象</span><span class="value">同一 Sportmaster 版本 · 通过白名单检查（模拟）</span></div><p style="font-size:13px;margin:17px 0 0">导入后将修改以下草稿内容：</p>${diffTable(list)}${note('模板不包含供包台编码、相机 IP、RCS 目标、凭据、请求编号、备份路径或业务数据。确认导入只改草稿，之后仍需显式保存。')}<p style="font-size:12px;color:#64748b;margin-bottom:0">若版本或 Application 不兼容，应阻止导入并保留当前输入。此演示使用内置合成模板。</p>`,`${btn('取消','dismiss')}${btn('应用到草稿','apply-template','primary')}`);
}
function requestClose(){
 if(!changes().length){notify('原型保留在当前窗口，实际客户端按既有关闭流程退出。');return;}
 modal('关闭前处理未保存更改',`<p style="margin-top:0">有 <strong>${changes().length} 项更改</strong>尚未保存。请选择如何处理。</p>${diffTable()}${note('保留未完成设置会写入本机加密草稿；如果保留失败，原生客户端必须留在窗口内。原型只演示这个分支。')}`,`${btn('继续编辑','dismiss')}${btn('放弃更改','discard-and-leave','danger')}${btn('保留草稿并返回','keep-and-leave','primary')}`);
}
function checkConnection(){
 const isRcs=state.page==='rcs';const key=isRcs?'endpoint':state.page+'Ip';validate();if(state.errors[key]){focusField(key);renderPage();focusField(key);return;}
 state.errors={};state.connection={object:state.page,state:'testing'};state.connectionSignature=connectionSignature();renderPage();
 setTimeout(()=>{if(!state.connection)return;state.connection.state='failed';renderPage();},650);
}
async function sampleCheck(){
 // A terminable worker protects this browser UI; it does not verify .NET regex semantics.
 const signature=state.draft.pattern+'\n'+state.sample;
 if(state.sample.length>4096||state.draft.pattern.length>256){state.sampleResult='演示输入过长，请缩短本地样例。';renderPage();return;}
 state.sampleResult='正在检查本地样例…';renderPage();
 const workerCode="onmessage=e=>{try{const re=new RegExp('^(?:'+e.data.pattern+')$');postMessage({matches:e.data.sample.split(/\\r?\\n/).filter(x=>re.test(x))});}catch{postMessage({error:true});}}";
 const url=URL.createObjectURL(new Blob([workerCode],{type:'text/javascript'}));
 const worker=new Worker(url);
 const result=await new Promise(resolve=>{let done=false;const finish=value=>{if(done)return;done=true;clearTimeout(timer);worker.terminate();URL.revokeObjectURL(url);resolve(value);};const timer=setTimeout(()=>finish({timeout:true}),150);worker.onmessage=e=>finish(e.data);worker.onerror=()=>finish({error:true});worker.postMessage({pattern:state.draft.pattern,sample:state.sample});});
 if(signature!==state.draft.pattern+'\n'+state.sample)state.sampleResult='输入已改变，检查结果已过期。';
 else if(result.timeout)state.sampleResult='演示检查已超时并停止，请简化规则。';
 else if(result.error)state.sampleResult='规则无法解析，请先修正条码规则。';
 else state.sampleResult=result.matches.length===1?'选中 '+result.matches[0]+' · 1 个有效候选（本地模拟）。':result.matches.length===0?'没有匹配的条码，请检查规则和样例。':'匹配到多个条码，请检查规则，不能擅自选择第一个。';
 if(state.page==='filter')renderPage();
}
async function submit(){
 if(!state.paused||!state.authorized||!state.reason.trim()||state.unknown)return;
 const frozen=clone(state.draft);state.frozen=frozen;
 modal('正在保存更改',`<h3 style="font-size:17px;margin-top:0">正在保存并核对结果…</h3><div class="progress-line"><span></span></div><div class="stage-list"><div>${icon('checkCircle','green')}字段和关联参数校验通过</div><div>${icon('checkCircle','green')}已提交完整配置修订</div><div>${icon('clock','blue')}正在核对原保存记录</div></div><p class="muted" style="font-size:12px">当前输入与提交对象已固定。核对结束前，不重复提交。</p>`,btn('正在保存…','noop','primary','disabled'));
 const x=dialog.querySelector('[data-action="dismiss"]');if(x)x.disabled=true;
 await new Promise(r=>setTimeout(r,750));
 state.saved=clone(frozen);state.savedRevision=state.revision+1;state.pending=true;state.errors={};updateChrome();renderPage();savedDialog();
}
function returnWork(keep=true){
 closeModal();const s=document.querySelector('#main-scroll');if(s)state.pageScroll[state.page]=s.scrollTop;state.view='work';render();if(keep&&changes().length)notify('未保存更改已留在本窗，返回参数设置可继续编辑。');
}
function act(action){
 if(action?.startsWith('page:')){closeModal();navigate(action.slice(5));return;}if(action?.startsWith('focus:')){focusField(action.slice(6));return;}
 switch(action){
 case'dismiss':closeModal();break;
 case'reset':setScene('left');break;
 case'save':startSave();break;
 case'view-changes':review(false);break;
 case'authorize':{const p=dialog.querySelector('#auth-proof');if(!p?.value){dialog.querySelector('#auth-error').innerHTML=note('请输入任意演示口令，不要使用真实凭据。','warning');break;}p.value='';state.authorized=true;review(true);break;}
 case'review-pause':state.paused=true;updateChrome();review(true);break;
 case'submit':submit();break;
 case'undo':modal('撤销本次更改',`<p>将撤销当前全部 <strong>${changes().length} 项</strong>未保存更改，恢复为已保存值。已经保存或正在运行的配置不受影响。</p>`,`${btn('继续编辑','dismiss')}${btn('撤销全部更改','confirm-undo','primary')}`);break;
 case'confirm-undo':state.draft=clone(state.saved);state.errors={};state.legacy=false;closeModal();render();notify('本次未保存更改已撤销。');break;
 case'leave':returnWork();break;
 case'close-editor':requestClose();break;
 case'discard-and-leave':state.draft=clone(state.saved);state.errors={};returnWork(false);notify('已放弃本次未保存更改（模拟）。');break;
 case'keep-and-leave':state.dirtyPreserved=true;returnWork(false);notify('未完成设置已保留为演示草稿。实际落盘需原生加密保存成功。');break;
 case'work':returnWork();break;
 case'settings':state.view='settings';render();if(state.lastField)focusField(state.lastField);break;
 case'pause':state.paused=true;render();notify('已暂停新入场（模拟）。软件暂停不是安全急停。');break;
 case'resume':if(state.fault){notify('RCS 故障尚未排除，不能恢复新入场。');break;}state.paused=false;closeModal();render();notify('已恢复新入场（模拟）。');break;
 case'work-status':modal('当前作业状态',`<div class="prerequisite" style="border-top:0;padding-top:0"><div class="step">${icon(state.fault?'error':state.paused?'pause':'checkCircle',state.fault?'red':state.paused?'amber':'green')}<div><strong>${state.fault?'RCS 异常，左右两路受影响':state.paused?'已暂停新入场':'正在接收新包裹'}</strong><p>左、右当前结果持续显示在窗口底部。</p></div></div></div>${note('进入设置和返回作业都不会自动暂停或恢复。软件暂停不代替现场安全急停。')}`,`${btn('关闭','dismiss')}${btn(state.paused?'恢复新入场':'暂停新入场',state.paused?'resume':'pause','primary',state.fault?'disabled':'')}`);break;
 case'tools':showTools();break;
 case'template':showTemplate();break;
 case'apply-template':state.draft.history='800';state.draft.queue='256';closeModal();navigate('data');notify('2 项模板设置已进入草稿，尚未保存。');break;
 case'export-template':{const data={Synthetic:true,Application:'sportmaster',Purpose:'same-version-safe-template-demonstration',Settings:{ResidentHistory:state.saved.history,QueueCapacity:state.saved.queue}};const link=document.createElement('a');link.href='data:application/json;charset=utf-8,'+encodeURIComponent(JSON.stringify(data,null,2));link.download='sportmaster-settings-demo.json';link.click();notify('已导出演示模板，排除设备地址、身份和业务数据。');break;}
 case'draft-tools':modal('未完成的设置',`<p>当前 ${changes().length} 项更改保留在本窗中，切换设置对象不会丢失。</p>${note('原生客户端支持本机加密保留与继续草稿。重新读取会覆盖本窗输入，需要明确决定。')}`,`${btn('返回设置','dismiss')}${btn('处理未保存更改…','close-editor','primary')}`);break;
 case'backup-now':modal('创建完整备份',`<div class="field"><label for="backup-path">新备份的完整路径</label><input id="backup-path" value="D:\\SupplyStation\\Backups\\station-demo-20261020.db" spellcheck="false"><p class="hint">示例路径。正式客户端不得覆盖已有恢复集。</p></div><div style="margin-top:17px">${note('包含数据库及同名 .recovery 目录。执行前需已暂停、维护授权有效、没有在途外呼。')}</div><p class="muted" style="font-size:12px">原型不会写入生产文件；本按钮只演示反馈。</p>`,`${btn('取消','dismiss')}${btn('演示备份反馈','backup-demo','primary')}`);break;
 case'backup-demo':modal('完整备份结果',`${note('演示备份已完成。正式客户端必须核对实际恢复集后才能显示成功。','success')}<div class="readonly-line"><span class="label">恢复集范围</span><span class="value">数据库 + 同名恢复目录</span></div>`,btn('返回自动备份','page:backup','primary'));break;
 case'check-connection':checkConnection();break;
 case'connection-details':modal('连接技术详情',`<p style="font-size:13px">对象：${esc(state.connection?.object==='rcs'?'RCS 连接':pages[state.connection?.object||'left'].title)}</p><pre class="mono" style="background:#f3f6fa;border-radius:5px;padding:14px;white-space:pre-wrap;font-size:12px">SYNTHETIC DIAGNOSTIC\nTarget: ${esc(state.connectionSignature||state.draft.endpoint)}\nStage: TransportConnect\nResult: ConnectionRefused\nBusinessRequestSent: false\nSavedOrApplied: false</pre>${note('只证明本次模拟连接未建立；没有验证业务响应，不代表已经检查真实服务。')}`,`${btn('关闭','dismiss')}${btn('复制详情','copy-details','primary')}`);break;
 case'copy-details':{const value=dialog.querySelector('pre')?.textContent||'';if(navigator.clipboard?.writeText)navigator.clipboard.writeText(value).then(()=>notify('已复制模拟技术详情。')).catch(()=>notify('浏览器限制复制，请选中详情复制。'));else notify('请选中技术详情复制；本地浏览器未提供剪贴板接口。');break;}
 case'sample-check':sampleCheck();break;
 case'configuration-status':configurationStatus();break;
 case'inspect':state.unknown=false;state.saved=clone(state.frozen||state.draft);state.savedRevision=state.revision+1;state.pending=true;renderPage();savedDialog();break;
 case'restart-info':modal('应用已保存配置',`<p style="margin-top:0">本次更改需要受控重启。重启前核对原作业责任和启动条件，启动后确认实际运行版本。</p><div class="stage-list"><div>${icon('checkCircle','green')}保留已保存的完整配置修订</div><div>${icon('clock','amber')}按实际后台资格执行受控启动</div><div>${icon('clock','amber')}核对本次启动结果和资源连接</div><div>${icon('clock','amber')}明确恢复新入场</div></div>${note('原型不执行真实重启。失败时必须保留运行事实，不能把旧记录当成本次启动成功。')}`,btn('返回配置状态','configuration-status','primary'));break;
 case'installation':modal('设备与模块管理',`<table class="key-table"><thead><tr><th>对象</th><th>安装与绑定</th></tr></thead><tbody><tr><td>左相机 / 右相机</td><td>同一个 Hikrobot 驱动；实例数量和位置由 Sportmaster 决定。</td></tr><tr><td>RCS 连接</td><td>一个 Sportmaster RCS 组件，左右共用。</td></tr><tr><td>维护认证</td><td>按实际安装描述及原绑定保留。</td></tr></tbody></table><p class="muted" style="font-size:12px">精确版本、签名包安装/卸载、未绑定能力管理和高级绑定沿用已有受控入口。本页演示可理解的入口，不生成虚构版本或增加第三路相机。</p>${state.missing?note('缺少当前配置所需驱动，请核对原包和精确版本。输入不会被清空。','warning'):''}`,`${btn('返回设置','dismiss')}${btn('安装与绑定说明','installation-info','primary')}`);break;
 case'installation-info':modal('安装与绑定说明',`<p>原生客户端保留：验证签名包、安装/卸载、核对原安装操作、查看精确提供者与依赖、管理未绑定适用能力、受控编辑高级绑定。</p>${note('安装驱动类型、编辑具体实例、绑定业务位置是不同任务。普通参数页只编辑当前 Application 已确定的对象。')}`,btn('返回','installation','primary'));break;
 case'advanced-tools':modal('高级配置',`<div class="tool-list"><button class="tool-row" data-action="configuration-status">${icon('file')}<span><strong>已保存与运行中的配置</strong><p>核对两份配置的版本和应用状态。</p></span>${icon('chevron','chevron')}</button><button class="tool-row" data-action="json-info">${icon('edit')}<span><strong>配置文本与兼容设置</strong><p>显式选择表单或 JSON 为唯一编辑来源。</p></span>${icon('chevron','chevron')}</button></div>${note('旧配置核对只在实际发现旧格式或冲突时出现；不再有常驻“迁移”分类。')}`,btn('关闭','dismiss'));break;
 case'json-info':modal('配置文本与兼容设置',`<p>正式客户端保留高级 JSON 编辑、加载回表单、完整校验及原账核对。它们沿用同一配置账和同一保存守卫。</p>${note('不要同时启用表单与 JSON 两个可写来源。此原型不提供另一套配置写入服务。')}`,btn('返回高级配置','advanced-tools','primary'));break;
 case'recovery':modal('恢复后核对',`<p style="margin-top:0">用于原机恢复、原账确认和原业务责任处理。先选择实际故障或恢复记录，再核对具体事实。</p><div class="stage-list"><div>${icon('database')}核对完整恢复集及原机身份</div><div>${icon('file')}读取原恢复交接和原业务责任</div><div>${icon('shield')}按受控资格确认，保持暂停</div></div>${note('确认报警、恢复连接和重试业务含义不同。未知原请求不自动重发，普通改参数不需要走这个入口。')}`,btn('返回设置','dismiss','primary'));break;
 case'diagnostic':modal('RCS 连接诊断',`<div class="banner" style="margin:0 0 18px">${icon('error')}<div><strong>左右两路共用 RCS，新格口请求受影响</strong><p>相机连接仍可独立可用。当前失败包裹没有有效格口。</p></div></div><div class="checks"><div class="check-row">${icon('checkCircle','green')}左右相机连接：已连接（模拟）</div><div class="check-row">${icon('error','red')}RCS 目标连接：未建立（模拟）</div><div class="check-row">${icon('minus')}本次业务响应：未确认</div></div><p class="muted" style="font-size:12px">先核对服务地址和实际服务状态。连接恢复不会重新发送未知原业务请求。</p>`,`${btn('返回作业','dismiss')}${btn('查看 RCS 设置','page:rcs','primary')}`);break;
 case'history':modal('作业记录',`<table class="key-table"><thead><tr><th>时间 / 位置</th><th>条码</th><th>本次结果</th></tr></thead><tbody><tr><td>14:32:08 · 左</td><td>BC0003</td><td>${state.fault?'RCS 响应未确认':'1-01'}</td></tr><tr><td>14:32:07 · 右</td><td>BC0002</td><td>1-05 · 上一件</td></tr><tr><td>14:32:06 · 左</td><td>BC0001</td><td>1-03 · 上一件</td></tr></tbody></table><p class="muted" style="font-size:12px">全部为模拟记录。历史结果始终标注位置和时间，不覆盖当前包裹的格口。</p>`,btn('关闭记录','dismiss','primary'));break;
 case'noop':break;
 default:throw new Error('Unhandled prototype action: '+action);
 }
}
document.addEventListener('click',e=>{const target=e.target.closest('[data-action]');if(target&&!target.disabled)act(target.dataset.action);});
document.addEventListener('keydown',e=>{if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='f'&&state.view==='settings'&&!dialog.open){e.preventDefault();document.querySelector('#settings-search').focus();}if((e.key==='Enter'||e.key===' ')&&e.target.getAttribute('role')==='button'&&e.target.dataset.action){e.preventDefault();act(e.target.dataset.action);}});
function setScene(scene){
 closeModal();state=fresh();state.scene=scene;
 if(pages[scene])state.page=scene;
 const edit=()=>{state.page='left';state.draft.leftIp='192.168.10.31';};
 if(['invalid','review','pending','leave','unknown','saving','save-failed'].includes(scene))edit();
 if(scene==='invalid'){state.draft.leftIp='192.168.10.999';state.draft.leftSerial='HK-DEMO-00021';validate();}
 if(scene==='review'){state.paused=true;state.authorized=true;state.reason='更换左相机后更新连接地址。';}
 if(scene==='pending'){state.paused=true;state.saved=clone(state.draft);state.savedRevision=13;state.pending=true;}
 if(scene==='connection'){state.page='rcs';state.connection={object:'rcs',state:'failed'};state.connectionSignature=state.draft.endpoint;}
 if(scene==='backup')state.draft.backupEnabled=state.saved.backupEnabled=true;
 if(scene==='search'){state.search='相机编码';state.page='left';}
 if(scene==='unknown'){state.unknown=true;state.paused=true;state.frozen=clone(state.draft);}
 if(scene==='advanced')state.page='left';
 if(scene==='missing'){state.page='left';state.missing=true;}
 if(scene==='legacy'){state.page='system';state.legacy=true;state.draft.station='';state.errors.station='请核对原左右值后填写共同供包台编码。';}
 if(scene==='work'||scene==='work-fault'){state.view='work';state.fault=scene==='work-fault';state.paused=state.fault;}
 if(scene==='saving'||scene==='save-failed'){state.paused=true;state.authorized=true;}
 render();
 if(scene==='review')review(true);
 if(scene==='template')showTemplate();
 if(scene==='leave')requestClose();
 if(scene==='saving'){state.paused=true;state.authorized=true;modal('正在保存更改',`<h3 style="font-size:17px;margin-top:0">正在保存并核对结果…</h3><div class="progress-line"><span></span></div><div class="stage-list"><div>${icon('checkCircle','green')}字段和关联参数校验通过</div><div>${icon('checkCircle','green')}已提交完整配置修订</div><div>${icon('clock','blue')}正在核对原保存记录</div></div><p class="muted" style="font-size:12px">这是固定的演示状态。真实流程需等待原账核对回执，不允许重复提交。</p>`,btn('正在保存…','noop','primary','disabled'));}
 if(scene==='save-failed'){state.paused=true;modal('更改未保存',`${note('核对原配置记录后，已确认本次没有保存。当前仍为版本 12，所有输入已保留。','error-note')}<p style="font-size:13px">原因：演示存储写入失败。检查磁盘和访问条件后，可以再次保存。</p><div class="readonly-line"><span class="label">受影响对象</span><span class="value">左相机 · IP 地址</span></div>`,`${btn('返回检查设置','dismiss')}${btn('查看更改','view-changes','primary')}`);}
 const picker=document.querySelector('#scene-picker');if(picker)picker.value=scene;
}
window.prototype={setScene,getState:()=>clone({page:state.page,view:state.view,draft:state.draft,saved:state.saved,running:state.running,errors:state.errors,changes:changes(),revision:state.revision,savedRevision:state.savedRevision,pending:state.pending,unknown:state.unknown,paused:state.paused,authorized:state.authorized,dialog:state.dialog}),navigate,act};
setScene(new URLSearchParams(location.search).get('scene')||'left');
