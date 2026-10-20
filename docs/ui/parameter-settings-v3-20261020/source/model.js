'use strict';
// Every value below is synthetic. This prototype cannot contact devices or services.
const initial = {
  station:'IS-L-L1-04', heartbeatStation:'IS-L-L1-04', zone:'13421', project:'13421',
  history:'500', freeSpace:'2048', backupEnabled:false, backupDirectory:'D:\\SupplyStation\\Backups', backupInterval:'1440', backupMaximum:'30',
  pattern:'BC[0-9]{4}', quantity:'1', specified:false, chute:'', queue:'128', budget:'3000', operations:'1000',
  leftIp:'192.168.10.21', leftSerial:'', leftCode:'IS-L-L1-04-PICK-1', leftHeartbeat:'IS-L-L1-04-PICK-1', leftMode:'trigger', leftEncoding:'utf-8', leftQueue:'128',
  rightIp:'192.168.10.22', rightSerial:'', rightCode:'IS-L-L1-04-PICK-2', rightHeartbeat:'IS-L-L1-04-PICK-2', rightMode:'trigger', rightEncoding:'utf-8', rightQueue:'128',
  endpoint:'http://localhost:58023/rcs/sorter/api', source:'PDA', locale:'zh_CN', requestBase:'20260925000', requestCount:'1000000', heartbeat:'5'
};
const defs = {
 station:['供包台编码','system','stationCode','取格口请求使用的供包台身份。'], heartbeatStation:['心跳工位编码','system','StationCode','用于周期心跳；与取格口编码分别维护。'], zone:['区域 ID','system','zone-id','由现场 RCS 分配。'],project:['项目 ID','system','project-id','由现场 RCS 分配。'],
 history:['常驻历史条数','data','ResidentHistory','主界面和本机记录的常驻数量。'],freeSpace:['关键写入保留空间','data','MinimumFreeMiB','空间不足时阻止新的供包处理。'],backupEnabled:['启用自动备份','backup','BackupEnabled',''],backupDirectory:['备份目录','backup','BackupDirectory','填写本机绝对路径。'],backupInterval:['备份间隔','backup','BackupIntervalMinutes',''],backupMaximum:['备份份数上限','backup','BackupMaximumSets','达到上限时停止创建，不自动删除原备份。'],
 pattern:['条码匹配规则','filter','BarcodePattern','左右共用，按完整条码匹配。'], quantity:['每次请求数量','request','quantity','请求 RCS 时提交的数量，与响应数量分别保存。'],specified:['指定格口','request','specified-chute',''],chute:['指定格口编码','request','chute-code','开启指定格口后必填。'],queue:['输入队列容量','processing','QueueCapacity',''],budget:['单次调用预算','processing','TimeoutMs',''],operations:['未清偿作业容量','processing','MaximumOperations',''],
 endpoint:['服务地址','rcs','Target','RCS 取格口与心跳共用的服务地址。'],source:['请求来源','rcs','source','使用与 RCS 约定的来源。'],locale:['协议语言','rcs','locale','使用与 RCS 约定的语言标识。'],requestBase:['请求编号区间起点（不含）','rcs','request-id-base','本站独占的编号区间。'],requestCount:['请求编号区间容量','rcs','request-id-count','不可与其他工作站重叠。'],heartbeat:['心跳间隔','rcs','heartbeat-interval-seconds','']
};
for(const side of ['left','right']){
 defs[side+'Ip']=['相机 IP 地址',side,'device.ip','填写相机实际 IPv4 地址。'];
 defs[side+'Serial']=['预期序列号',side,'device.serial','可选；填写后同时核对 IP 和序列号。'];
 defs[side+'Code']=['取格口相机编码',side,side+'-bcr-code','用于 RCS 取格口请求。'];
 defs[side+'Heartbeat']=['心跳相机编码',side,'bcr-code','用于向 RCS 上报相机心跳。'];
 defs[side+'Mode']=['实体关联方式',side,'entity.mode','触发关联须满足相机和现场的实际部署资格。'];
 defs[side+'Encoding']=['条码字节编码',side,'code.encoding','按驱动支持的编码严格解码。'];
 defs[side+'Queue']=['回调队列容量',side,'queue.capacity','只缓冲驱动回调；溢出后需检查实体再重连。'];
}
const pages = {
 system:{title:'供包台标识',group:'系统',desc:'设置这台供包台在现场系统中的身份。'},
 data:{title:'数据与记录',group:'系统',desc:'查看本机数据位置，设置记录数量和磁盘保留空间。'},
 backup:{title:'自动备份',group:'系统',desc:'在满足作业条件时创建完整备份，便于原机恢复。'},
 display:{title:'显示偏好',group:'系统',desc:'只影响当前 Windows 用户的显示，不改变设备或业务参数。'},
 filter:{title:'条码筛选',group:'业务模块',desc:'Sportmaster · 为左右相机设置相同的条码筛选规则。'},
 request:{title:'取格口请求',group:'业务模块',desc:'Sportmaster · 定义本次包裹向 RCS 请求任务时的规则。'},
 processing:{title:'处理参数',group:'业务模块',desc:'Sportmaster · 设置输入和外部调用的处理限额。'},
 left:{title:'左相机',group:'驱动',desc:'左投递位 · 修改这一路相机的连接和编码。'},
 right:{title:'右相机',group:'驱动',desc:'右投递位 · 修改这一路相机的连接和编码。'},
 rcs:{title:'RCS 连接',group:'组件',desc:'左右投递位共用此连接，接收取格口结果并上报心跳。'},
 auth:{title:'维护认证',group:'组件',desc:'查看已安装的维护认证能力及其作用范围。'}
};
const groups=[['系统','system',['system','data','backup','display']],['业务模块','module',['filter','request','processing']],['驱动','camera',['left','right']],['组件','component',['rcs','auth']]];
const scenes=[
 ['left','01 · 左相机'],['system','02 · 系统配置'],['request','03 · 业务模块'],['rcs','04 · RCS 组件'],
 ['invalid','05 · 参数错误'],['review','06 · 保存审阅'],['pending','07 · 已保存待生效'],['connection','08 · 连接失败'],
 ['backup','09 · 自动备份'],['template','10 · 导入设置模板'],['search','11 · 搜索定位'],['leave','12 · 离开未保存'],
 ['unknown','13 · 保存结果未知'],['advanced','14 · 相机高级参数'],['filter','15 · 条码规则'],['work-fault','16 · 作业异常'],
 ['work','17 · 连续作业'],['data','18 · 数据与记录'],['processing','19 · 处理参数'],['display','20 · 显示偏好'],
 ['missing','21 · 驱动不可用'],['legacy','22 · 旧设置冲突'],['saving','23 · 正在保存'],['save-failed','24 · 保存失败']
];
const fresh = () => ({draft:structuredClone(initial),saved:structuredClone(initial),running:structuredClone(initial),page:'left',view:'settings',search:'',errors:{},paused:false,authorized:false,revision:12,savedRevision:12,pending:false,dirtyPreserved:false,unknown:false,saveFailure:false,missing:false,legacy:false,fault:false,connection:null,connectionSignature:null,reason:'',sample:'BC0003\nPALLET-02',sampleResult:null,display:'system',density:'comfortable',pageScroll:{},lastField:null,dialog:null,scene:'left'});
let state=fresh();
const changes=()=>Object.keys(defs).filter(k=>String(state.draft[k])!==String(state.saved[k])).map(k=>({key:k,label:defs[k][0],page:defs[k][1],before:state.saved[k],after:state.draft[k]}));
const valueLabel=v=>typeof v==='boolean'?(v?'开启':'关闭'):(v===''?'未填写':String(v));
const clone=x=>structuredClone(x);
function validate(){
 const d=state.draft,e={};
 const required=['station','heartbeatStation','source','locale','leftCode','rightCode','leftHeartbeat','rightHeartbeat'];
 for(const k of required)if(!String(d[k]).trim())e[k]='请填写'+defs[k][0]+'。';
 const nums={zone:[1n,9223372036854775807n],project:[1n,9223372036854775807n],quantity:[1n,2147483647n],history:[1n,10000n],freeSpace:[1n,1048576n],queue:[1n,4096n],budget:[100n,30000n],operations:[1n,100000n],leftQueue:[1n,1024n],rightQueue:[1n,1024n],requestBase:[0n,9223372036854775806n],requestCount:[1n,9223372036854775807n],heartbeat:[1n,3600n]};
 if(d.backupEnabled){nums.backupInterval=[15n,10080n];nums.backupMaximum=[1n,1000n];if(!/^[A-Za-z]:\\/.test(d.backupDirectory)&&!/^\\\\[^\\]+\\[^\\]+/.test(d.backupDirectory))e.backupDirectory='请填写本机或共享位置的绝对路径，例如 D:\\SupplyStation\\Backups（示例）。';}
 for(const [k,[lo,hi]]of Object.entries(nums)){if(!/^\d+$/.test(d[k])||BigInt(d[k])<lo||BigInt(d[k])>hi)e[k]=`请输入 ${lo}～${hi} 范围内的整数。`;}
 if(!e.requestBase&&!e.requestCount&&BigInt(d.requestBase)+BigInt(d.requestCount)>9223372036854775807n)e.requestCount='编号起点与容量之和超出支持范围，请减小容量。';
 for(const side of ['left','right']){const ip=d[side+'Ip'];if(!/^(0|[1-9]\d{0,2})(\.(0|[1-9]\d{0,2})){3}$/.test(ip)||ip.split('.').some(n=>+n>255))e[side+'Ip']='请输入有效的 IPv4 地址，每段为 0～255，例如 192.168.10.21（示例）。';if(d[side+'Serial'].length>32)e[side+'Serial']='序列号最多 32 个字符。';}
 if(!e.leftIp&&!e.rightIp&&d.leftIp===d.rightIp){e.leftIp='与右相机 IP 重复。请确认设备地址后分别填写。';e.rightIp='与左相机 IP 重复。请确认设备地址后分别填写。';}
 if(d.leftHeartbeat===d.rightHeartbeat)e.rightHeartbeat='两路相机的心跳编码不能相同，请核对右相机编码。';
 if(d.specified&&!d.chute.trim())e.chute='已开启指定格口，请填写格口编码。';
 try{new RegExp('^(?:'+d.pattern+')$');if(!d.pattern.trim())e.pattern='已有条码规则不能清空，请填写完整匹配规则。';}catch{e.pattern='正则表达式无法解析，请检查括号、转义符或字符范围。';}
 try{const u=new URL(d.endpoint);if(!['http:','https:'].includes(u.protocol)||u.username||u.password||u.search||u.hash||u.pathname!=='/rcs/sorter/api')throw Error();}catch{e.endpoint='请输入不含凭据、查询参数和片段的 HTTP(S) 地址，路径为 /rcs/sorter/api。';}
 state.errors=e;return Object.keys(e);
}
// All icons are authored geometric UI glyphs, in one consistent stroke system.
const glyphs={
 back:'<path d="m14 6-6 6 6 6M8 12h13"/>',chevron:'<path d="m9 5 7 7-7 7"/>',down:'<path d="m6 9 6 6 6-6"/>',search:'<circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/>',
 camera:'<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="12" cy="12" r="4"/><path d="M5.5 7.5h1"/>',system:'<rect x="3" y="4" width="18" height="13" rx="1.5"/><path d="M8 21h8m-4-4v4"/>',module:'<path d="m12 3 9 5-9 5-9-5 9-5Zm-9 9 9 5 9-5M3 16l9 5 9-5"/>',component:'<rect x="5" y="5" width="14" height="14" rx="2"/><path d="M9 1v4m6-4v4M9 19v4m6-4v4M1 9h4m-4 6h4m14-6h4m-4 6h4M9 9h6v6H9z"/>',
 check:'<path d="m5 12 4 4 10-10"/>',checkCircle:'<circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/>',info:'<circle cx="12" cy="12" r="9"/><path d="M12 11v6m0-10v.5"/>',warning:'<path d="m12 3 10 18H2L12 3Zm0 6v5m0 3v.5"/>',error:'<circle cx="12" cy="12" r="9"/><path d="m9 9 6 6m0-6-6 6"/>',clock:'<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',lock:'<rect x="5" y="10" width="14" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3m-4 4v3"/>',more:'<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',close:'<path d="m6 6 12 12M6 18 18 6"/>',minus:'<path d="M5 12h14"/>',window:'<rect x="5" y="5" width="14" height="14" rx=".5"/>',settings:'<path d="M4 6h16M4 12h16M4 18h16"/><circle cx="9" cy="6" r="2" fill="currentColor"/><circle cx="15" cy="12" r="2" fill="currentColor"/><circle cx="9" cy="18" r="2" fill="currentColor"/>',
 folder:'<path d="M3 7V4h6l3 3h9v13H3V7Z"/>',download:'<path d="M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4"/>',upload:'<path d="M12 16V4m-5 5 5-5 5 5M4 17v4h16v-4"/>',restore:'<path d="M3 11a9 9 0 1 1 2.5 7M3 4v7h7"/>',file:'<path d="M5 3h9l5 5v13H5V3Zm9 0v6h5M8 13h8m-8 4h6"/>',link:'<path d="m10 8 3-3a4 4 0 0 1 6 6l-3 3m-2 2-3 3a4 4 0 0 1-6-6l3-3m1 7 6-10"/>',refresh:'<path d="M20 8A9 9 0 0 0 4 7M4 3v4h4m-4 9a9 9 0 0 0 16 1m0 4v-4h-4"/>',pause:'<path d="M8 5v14M16 5v14"/>',play:'<path d="m7 4 13 8-13 8V4Z"/>',shield:'<path d="m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6l8-3Z"/><path d="m8 12 3 3 5-6"/>',history:'<path d="M3 11a9 9 0 1 1 2 7M3 4v7h7m2-4v5l3 2"/>',copy:'<rect x="8" y="7" width="12" height="14" rx="1"/><path d="M15 7V3H4v14h4"/>',edit:'<path d="m4 16 11-11 4 4L8 20H4v-4Zm9-9 4 4"/>',help:'<circle cx="12" cy="12" r="9"/><path d="M9 9a3 3 0 1 1 5 2.3c-1 .7-2 1-2 2.7m0 3v.5"/>',database:'<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 4 16 4 16 0V5M4 12c0 4 16 4 16 0"/>'
};
const icon=(name,cls='')=>`<svg class="icon ${cls}" viewBox="0 0 24 24" aria-hidden="true">${glyphs[name]||glyphs.info}</svg>`;
const esc=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
