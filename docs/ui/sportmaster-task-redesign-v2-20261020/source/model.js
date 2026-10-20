/* Editable, offline design model. Values, identifiers, states and outcomes are synthetic.
 * No production transport, SDK, filesystem writer, login or configuration authority exists here.
 * Production must use the existing Application contracts and configuration ledger. */
'use strict';
const copy=v=>JSON.parse(JSON.stringify(v));
const INITIAL={
  system:{zoneId:'13421',projectId:'13421',stationCode:'IS-L-L1-04',heartbeatStationCode:'HB-ST-04',residentHistory:'200',minimumFreeBytes:'67108864',backupEnabled:'true',backupDirectory:'D:\\ImporterPC\\Backups',backupInterval:'1440',backupMaximum:'7'},
  business:{quantity:'1',specifiedChute:'false',chuteCode:'',queueCapacity:'64',timeoutMs:'1500',maximumOperations:'10000',barcodePattern:''},
  left:{driver:'idmvs-reader',instanceId:'reader-left',deviceId:'reader-left',position:'left',ip:'192.0.2.21',serial:'',entityMode:'trigger',encoding:'utf-8',queueCapacity:'128',bcrCode:'IS-L-L1-04-PICK-1',heartbeatBcrCode:'HB-CAM-L-04'},
  right:{driver:'idmvs-reader',instanceId:'reader-right',deviceId:'reader-right',position:'right',ip:'192.0.2.22',serial:'',entityMode:'trigger',encoding:'utf-8',queueCapacity:'128',bcrCode:'IS-L-L1-04-PICK-2',heartbeatBcrCode:'HB-CAM-R-04'},
  rcs:{instanceId:'rcs-main',target:'http://rcs.example.test:58023/rcs/sorter/api',source:'PDA',locale:'zh_CN',requestIdBase:'20260925000',requestIdCount:'100000',heartbeatInterval:'5'}
};
const OBJECTS={system:'系统配置',business:'Sportmaster 业务模块',left:'左位读码相机',right:'右位读码相机',rcs:'RCS 任务组件'};
const LABELS={zoneId:'区域 ID',projectId:'项目 ID',stationCode:'供包台编码（取格口）',heartbeatStationCode:'工位编码（心跳）',quantity:'请求数量',specifiedChute:'指定格口',chuteCode:'指定格口编码',queueCapacity:'输入队列容量',timeoutMs:'调用超时',maximumOperations:'未清偿作业容量',barcodePattern:'左右共用条码筛选规则',residentHistory:'常驻历史条数',minimumFreeBytes:'最低可用磁盘空间',backupEnabled:'计划备份',backupDirectory:'备份目录',backupInterval:'备份间隔',backupMaximum:'最多保留备份集',ip:'相机 IP 地址',serial:'预期设备序列号',entityMode:'实体关联方式',encoding:'条码字符编码',bcrCode:'取格口相机编码',heartbeatBcrCode:'心跳相机编码',target:'RCS 接口地址',source:'协议来源',locale:'协议语言',requestIdBase:'请求编号起点（不含）',requestIdCount:'请求编号容量',heartbeatInterval:'心跳请求间隔',driver:'驱动类型',instanceId:'配置实例',deviceId:'稳定设备 ID',position:'作业位置'};
function seedLane(side){const left=side==='left';return {status:'success',barcode:left?'BC0003':'BC0004',seq:left?128:127,time:left?'16:06:14':'16:06:12',requestId:left?'20260925003':'20260925004',task:{chuteCode:left?'IS-L-L1-1-01':'IS-L-L1-1-02',waveNo:'W00001',taskId:left?'task-1790488002079':'task-1790488002080',skuId:left?'1BC000':'1BC001',quantity:'1'},epoch:1,error:''};}
function makeState(){const state={view:'work',category:'system',object:'system',section:'station',phase:'clean',errors:{},draft:copy(INITIAL),saved:copy(INITIAL),runtime:copy(INITIAL),baseRevision:12,ledger:{revision:12,pending:null,effectiveOriginRevision:12,confirmed:true},maintenance:false,paused:false,rcsReachable:true,rcsClientReady:true,leftOnline:true,rightOnline:true,snapshotFresh:true,left:seedLane('left'),right:seedLane('right'),reason:'',failure:'none',probe:null,diagnosticObject:'rcs',diagnosticChecked:false,retained:false,setupStep:1,setupChecked:false,setupComplete:false,setupResumed:false,setupApplication:'sportmaster',setupSaved:false,setupError:'',appStarted:true,showTray:false,historyQuery:'',historySide:'all',historyResult:'all',historyStart:'2026-10-20',historyEnd:'2026-10-20',historyError:'',historySelected:0,historyApplied:{query:'',side:'all',result:'all'},historyNewCount:0,filterExample:'BC0003\nLABEL-A',filterTest:null,supportStart:'2026-10-20T15:00',supportEnd:'2026-10-20T16:00',supportError:'',supportPreview:false,supportExpires:0,restoreReady:false,backupCreated:false,importPreview:false,migrationConflict:false,preferences:{density:'comfortable',text:'standard'},installStep:1,installDone:false,scene:'work-normal',generation:0,audit:[],waitingNav:null,nextRequestId:'20260925004'};for(const side of ['left','right'])state[side].frozenRequest=freezeRequest(side,state.runtime,state[side]);return state;}
let S=makeState();window.S=S;
const HISTORY=[
 {time:'16:06:14',side:'left',barcode:'BC0003',result:'任务已取得',...seedLane('left').task},
 {time:'16:06:12',side:'right',barcode:'BC0004',result:'任务已取得',...seedLane('right').task},
 {time:'16:05:58',side:'left',barcode:'BC0002',result:'任务已取得',chuteCode:'IS-L-L1-1-03',waveNo:'W00000',taskId:'task-1790488002030',skuId:'1BC003',quantity:'1'},
 {time:'16:04:10',side:'right',barcode:'BC0001',result:'结果待核对',chuteCode:null,waveNo:null,taskId:null,skuId:null,quantity:null}
];
const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const PATHS={work:'M3 4h7v16H3zM14 4h7v16h-7M6 8h1M6 12h1M17 8h1M17 12h1',history:'M3 11a9 9 0 119 10M3 3v6h6M12 7v5l4 2',settings:'M12 8a4 4 0 100 8 4 4 0 000-8M9 3h6l1 3 3 1 2 4-2 2v4l-4 3-3-1-3 1-4-3v-4l-2-2 2-4 3-1 1-3',wrench:'M14 4a6 6 0 00-7 8l-4 5a2 2 0 003 3l5-5a6 6 0 008-7l-4 4-4-4 3-4',camera:'M3 5h14v14H3zM17 10l4-3v10l-4-3M7 8h6M8 12a2 2 0 104 0 2 2 0 00-4 0',server:'M4 3h16v7H4zM4 14h16v7H4zM7 6h.1M7 17h.1M11 6h6M11 17h6',check:'M4 12l5 5L20 6',alert:'M12 3L2 21h20L12 3M12 9v5M12 17v.1',help:'M9 8a3 3 0 016 0c0 2-3 2-3 5M12 17v.1M21 12a9 9 0 11-18 0 9 9 0 0118 0',close:'M5 5l14 14M19 5L5 19',arrow:'M4 12h16M14 6l6 6-6 6',pause:'M7 4v16M17 4v16',play:'M6 3l15 9-15 9V3',folder:'M3 5h6l2 3h10v12H3V5',download:'M12 3v12M7 10l5 5 5-5M3 16v5h18v-5',copy:'M8 8h13v13H8zM16 8V3H3v13h5',clock:'M12 6v6l4 2M21 12a9 9 0 11-18 0 9 9 0 0118 0',link:'M10 14l4-4M8 16l-2 2a4 4 0 01-6-6l5-5a4 4 0 016 0M16 8l2-2a4 4 0 016 6l-5 5a4 4 0 01-6 0'};
function icon(name,size=18){return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${PATHS[name]||PATHS.help}"/></svg>`;}
function btn(text,action,type='',attrs=''){return `<button type="button" class="button ${type}" data-action="${esc(action)}" ${attrs}>${text}</button>`;}
function badge(text,tone=''){return `<span class="badge ${tone}">${esc(text)}</span>`;}
function status(text,tone=''){return `<span class="status ${tone}">${icon(tone==='success'?'check':tone==='warning'||tone==='danger'?'alert':tone==='info'?'clock':'camera',14)}${esc(text)}</span>`;}
function notice(tone,title,detail='',actions=''){return `<div class="notice ${tone}" role="${tone==='danger'?'alert':'status'}">${icon(tone==='success'?'check':tone==='danger'||tone==='warning'?'alert':'help',18)}<div class="notice-main"><strong>${title}</strong>${detail?`<p>${detail}</p>`:''}</div>${actions?`<div class="actions">${actions}</div>`:''}</div>`;}
function heading(title,sub,actions=''){return `<div class="page-heading"><div><h1>${esc(title)}</h1>${sub?`<p>${esc(sub)}</p>`:''}</div><div class="actions">${actions}</div></div>`;}
function section(title,body,help=''){return `<section class="form-section"><h3>${title}</h3>${body}${help?`<p class="help">${help}</p>`:''}</section>`;}
function getPath(path,object=S){return path.split('.').reduce((v,k)=>v?.[k],object);}
function setPath(path,value,object=S){const keys=path.split('.');let at=object;for(const k of keys.slice(0,-1))at=at[k];at[keys.at(-1)]=value;}
function field(label,path,help='',options={}){
 const id='f-'+path.replaceAll('.','-'),error=S.errors[path],value=getPath(path)??'',saved=path.startsWith('draft.')?getPath(path.replace('draft.','saved.')):value;
 const readonly=options.readonly?'readonly':'',disabled=options.disabled||editingLocked()?'disabled':'',attrs=`id="${id}" data-field="${esc(path)}" ${disabled} ${error?`aria-invalid="true" aria-describedby="${id}-error"`:help?`aria-describedby="${id}-help"`:''}`;
 const input=options.options?`<select ${attrs}>${options.options.map(o=>`<option value="${esc(o[0])}" ${String(value)===o[0]?'selected':''}>${esc(o[1])}</option>`).join('')}</select>`:options.textarea?`<textarea ${attrs}>${esc(value)}</textarea>`:`<input ${attrs} class="${value!==saved?'changed-input':''}" type="${options.type||'text'}" value="${esc(value)}" ${readonly} ${options.numeric?'inputmode="numeric"':''} ${options.maxlength?`maxlength="${options.maxlength}"`:''}>`;
 return `<div class="field ${options.wide?'wide':''} ${error?'error':''}"><label for="${id}">${esc(label)}${options.optional?'<span class="optional">选填</span>':''}</label>${options.unit?`<div class="input-unit">${input}<span class="unit">${esc(options.unit)}</span></div>`:input}${error?`<div class="error-text" id="${id}-error">${icon('alert',13)}${esc(error)}</div>`:''}${help?`<div class="help" id="${id}-help">${help}</div>`:''}</div>`;
}
function facts(rows){return `<dl class="facts">${rows.map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join('')}</dl>`;}
function checks(rows){return rows.map(([title,description,result,tone])=>`<div class="check-row"><div class="grow"><strong>${esc(title)}</strong><p>${esc(description)}</p></div>${status(result,tone)}</div>`).join('');}
function candidate(){const value=copy(S.draft);if(value.business.specifiedChute==='false')value.business.chuteCode='';if(value.system.backupEnabled==='false')for(const k of ['backupDirectory','backupInterval','backupMaximum'])value.system[k]='';return value;}
function differences(){const value=candidate();return Object.keys(value).flatMap(o=>Object.keys(value[o]).filter(k=>value[o][k]!==S.saved[o][k]).map(k=>({object:o,key:k,before:S.saved[o][k],after:value[o][k]})));}
function dirty(){return differences().length>0;}
function busy(){return ['validating','saving','applying'].includes(S.phase);}
function editingLocked(){return busy()||S.phase==='save-unknown';}
function sharedReady(){return S.appStarted&&S.snapshotFresh&&S.ledger.confirmed&&S.rcsClientReady&&S.rcsReachable&&!['saving','applying','save-unknown'].includes(S.phase);}
function positionReady(side){return S[side+'Online']&&S.runtime[side].entityMode==='trigger'&&S[side].status!=='unknown';}
function canResume(){return sharedReady()&&!busy()&&['left','right'].some(positionReady);}
function diffTable(){const d=differences();return `<div class="table-wrap"><table class="table compare"><thead><tr><th>对象 / 参数</th><th>已保存值</th><th>本次编辑值</th></tr></thead><tbody>${d.map(x=>`<tr><td>${OBJECTS[x.object]}<br><span class="muted">${esc(x.key==='queueCapacity'&&['left','right'].includes(x.object)?'驱动事件队列容量':LABELS[x.key]||x.key)}</span></td><td class="mono">${esc(x.before||'空')}</td><td class="mono">${esc(x.after||'空')}</td></tr>`).join('')}</tbody></table></div>`;}
function intValid(value,min,max){try{return /^(0|[1-9]\d*)$/.test(value)&&BigInt(value)>=BigInt(min)&&BigInt(value)<=BigInt(max);}catch{return false;}}
function validIPv4(value){const p=value.split('.');return p.length===4&&p.every(x=>/^(0|[1-9]\d{0,2})$/.test(x)&&Number(x)<=255)&&value!=='0.0.0.0'&&value!=='255.255.255.255';}
function validWindowsPath(value){return /^[A-Za-z]:[\\/]/.test(value)||/^\\\\[^\\/]+[\\/][^\\/]+(?:[\\/]|$)/.test(value);}
function validExternalCode(value){return typeof value==='string'&&value.trim().length>0&&value.length<=256&&!/[\x00-\x1f\x7f-\x9f]/.test(value);}
function validate(){
 const errors={},put=(o,k,t)=>errors[`draft.${o}.${k}`]=t,d=S.draft;
 for(const side of ['left','right']){const x=d[side];if(!validIPv4(x.ip))put(side,'ip','请输入有效 IPv4 地址，例如 192.0.2.31；每段为 0–255，不能是全零或全广播地址。');if(x.serial.length>32)put(side,'serial','序列号最多 32 个字符。');if(!intValid(x.queueCapacity,1,1024))put(side,'queueCapacity','请输入 1–1024 之间的整数。');if(!x.bcrCode.trim()||x.bcrCode.length>256)put(side,'bcrCode','请填写该相机的业务编码，最多 256 个字符。');}
 if(d.left.ip===d.right.ip)put('right','ip','左右相机使用了同一 IP。请核对右位对应的物理设备。');
 if(d.left.bcrCode===d.right.bcrCode)put('right','bcrCode','左右相机编码相同，无法区分条码来源。请核对位置映射。');
 for(const side of ['left','right'])if(!validExternalCode(d[side].heartbeatBcrCode))put(side,'heartbeatBcrCode','请填写此实例的心跳相机编码：1–256 个字符，不能全为空白或含控制字符。');
 if(d.left.heartbeatBcrCode===d.right.heartbeatBcrCode)put('right','heartbeatBcrCode','左右心跳相机编码必须不同；按大小写精确比较，请核对右位实例。');
 for(const k of ['zoneId','projectId'])if(!intValid(d.system[k],1,'9223372036854775807'))put('system',k,'请输入有效的正整数标识（Int64），不要填写名称。');
 if(!d.system.stationCode.trim()||d.system.stationCode.length>256)put('system','stationCode','请填写供包台业务编码，最多 256 个字符。');
 if(!validExternalCode(d.system.heartbeatStationCode))put('system','heartbeatStationCode','请填写心跳工位编码：1–256 个字符，不能全为空白或含控制字符。');
 if(!intValid(d.business.quantity,1,2147483647))put('business','quantity','请求数量须为 1–2147483647 之间的整数。');
 if(d.business.specifiedChute==='true'&&!d.business.chuteCode.trim())put('business','chuteCode','已启用“指定格口”，请填写指定格口编码。');
 if(d.business.chuteCode.length>64)put('business','chuteCode','指定格口编码最多 64 个字符。');
 const pattern=d.business.barcodePattern;
 if(pattern===''&&S.saved.business.barcodePattern!=='')put('business','barcodePattern','已配置筛选规则，不能清空为关闭状态；请填写规则或撤销本次编辑。');
 else if(pattern!==''&&(!pattern.trim()||pattern.length>2048))put('business','barcodePattern','筛选规则须为 1–2048 个字符，不能全为空白；没有生产默认规则。');
 else if(pattern==='[')put('business','barcodePattern','此预置错误示例的方括号未闭合；请修正规则。这里只演示错误定位，真实语法由 Application 校验。');
 else if(pattern!==''&&pattern!=='^BC[0-9]+$')put('business','barcodePattern','此规则未在离线原型预置；需由真实 Application 校验，原型不能判定其 .NET 语法是否正确。');
 for(const [o,k,min,max,unit] of [['business','queueCapacity',1,4096,''],['business','timeoutMs',100,30000,' ms'],['business','maximumOperations',1,100000,''],['system','residentHistory',1,10000,' 条'],['system','minimumFreeBytes',1048576,1099511627776,' bytes'],['system','backupInterval',15,10080,' min'],['system','backupMaximum',1,1000,' 份']]){if(o==='system'&&k.startsWith('backup')&&d.system.backupEnabled!=='true')continue;if(!intValid(d[o][k],min,max))put(o,k,`请输入 ${min}–${max}${unit} 范围内的整数。`);}
 if(d.system.backupEnabled==='true'&&!validWindowsPath(d.system.backupDirectory))put('system','backupDirectory','请选择完整本机或 UNC 目录；实际权限、容量和目录隔离由 Application 校验。');
 try{const u=new URL(d.rcs.target);if(!['http:','https:'].includes(u.protocol)||u.pathname!=='/rcs/sorter/api'||u.username||u.password||u.search||u.hash)throw new Error('invalid');}catch{put('rcs','target','请填写完整 HTTP(S) 地址，路径须为 /rcs/sorter/api，且不含账号、查询参数或片段。');}
 for(const k of ['source','locale'])if(!d.rcs[k].trim()||d.rcs[k].length>64)put('rcs',k,'此项必填，最多 64 个字符。');
 if(!intValid(d.rcs.heartbeatInterval,1,3600))put('rcs','heartbeatInterval','心跳间隔须为 1–3600 秒之间的整数；软件默认 5 秒，受控重启后生效。');
 if(!intValid(d.rcs.requestIdBase,0,'9223372036854775806'))put('rcs','requestIdBase','请输入有效的非负 Int64 起点（不含该值）。');
 if(!intValid(d.rcs.requestIdCount,1,'9223372036854775807'))put('rcs','requestIdCount','请输入正整数容量。');
 try{if(BigInt(d.rcs.requestIdBase)+BigInt(d.rcs.requestIdCount)>9223372036854775807n)put('rcs','requestIdCount','区间终点超出 Int64；请缩小容量或修改起点。');}catch{}
 if(S.migrationConflict)errors['migration']='旧左右供包台编码不同，请先在维护与迁移中明确处理，不能自动合并。';
 return errors;
}
function runtimeEvidenceFresh(){return S.snapshotFresh&&S.ledger.confirmed&&S.appStarted&&S.phase!=='applying';}
function destinationTask(lane){return runtimeEvidenceFresh()&&lane.status==='success'?lane.task:null;}
function summaryLane(side){const l=S[side];if(!runtimeEvidenceFresh())return '运行待确认';if(!S[side+'Online'])return '相机中断';return {success:destinationTask(l)?.chuteCode||'无格口',waiting:'等待读码',processing:'正在获取任务',unknown:'结果待核对',rejected:'任务未取得',filtered:'本次未请求 RCS'}[l.status]||'等待确认';}
function freezeRequest(side,config,lane){return {target:config.rcs.target,body:{source:config.rcs.source,version:1,requestId:lane.requestId,locale:config.rcs.locale,data:[{index:1,command:'sorter.task.dest_request',params:{zoneId:config.system.zoneId,projectId:config.system.projectId,loadCode:lane.barcode,stationCode:config.system.stationCode,bcrCode:config[side].bcrCode,specifiedChute:config.business.specifiedChute==='true',chuteCode:config.business.specifiedChute==='true'?config.business.chuteCode:null,quantity:config.business.quantity}}]}};}
function createRequest(side){return copy(S[side].frozenRequest?.body||freezeRequest(side,S.runtime,S[side]).body);}
function requestJson(side){return JSON.stringify(createRequest(side),null,2).replace(/"(requestId|zoneId|projectId|quantity)": "(0|[1-9]\d*)"/g,'"$1": $2');}
function allocateRequestId(){const base=BigInt(S.runtime.rcs.requestIdBase),end=base+BigInt(S.runtime.rcs.requestIdCount);let next=BigInt(S.nextRequestId);if(next<base)next=base;if(next>=end)return null;S.nextRequestId=String(next+1n);return S.nextRequestId;}
function demoDelay(action,ms=550){const generation=S.generation;setTimeout(()=>{if(S.generation===generation)action();},ms);}
