let sceneGeneration=0;
function setScene(name){
 closeDialog();clearTimeout(toastTimer);document.getElementById('toasts').innerHTML='';
 S=makeState();S.generation=++sceneGeneration;S.scene=name;window.S=S;
 const config=(category='system',object='system',section='station')=>{S.view='settings';S.category=category;S.object=object;S.section=section;};
 const changed=()=>{config('drivers','left');S.draft.left.ip='192.0.2.31';S.draft.left.bcrCode='IS-L-L1-04-PICK-01';S.reason='核对左相机更换后的 IP 与业务编码';S.phase='dirty';};
 const pending=()=>{changed();acceptSaved({revision:13,candidate:candidate(),reason:S.reason});S.paused=true;S.maintenance=true;};
 const unknown=()=>{S.left.status='unknown';S.left.task=null;S.left.error='请求已发送，返回超时；原请求结果尚不能确认。';};
 if(name==='work-processing'){S.left.status='processing';S.left.barcode='BC0005';S.left.task=null;S.left.seq=129;S.left.requestId='20260925005';}
 if(name==='work-waiting')for(const side of ['left','right'])Object.assign(S[side],{status:'waiting',barcode:'',task:null});
 if(name==='work-rcs-outage'){unknown();S.rcsReachable=false;S.paused=true;}
 if(name==='work-camera-outage'){S.leftOnline=false;S.left.status='waiting';S.left.barcode='';S.left.task=null;}
 if(name==='work-unknown'){unknown();S.right.status='rejected';S.right.task=null;S.right.error='RCS 明确拒绝，具体原因与技术原文保留在本次记录。';}
 if(name==='work-stale'){S.snapshotFresh=false;S.paused=true;}
 if(name==='work-long'){S.left.barcode='BC00000012345678901234567890123456';S.left.task={chuteCode:'IS-L-L1-1-01-EXT',waveNo:'WAVE-20261020-000001',taskId:'task-1790488002079-20261020',skuId:'SPORTMASTER-1BC000-EXTENDED',quantity:'100'};}
 if(name==='work-fields-missing'){S.left.task.waveNo=null;S.left.task.quantity=null;}
 if(name.startsWith('work-filter-')){
  const kind=name.slice('work-filter-'.length),selected=kind==='selected';
  const candidates=kind==='none'?['LABEL-A','BOX-X']:['multiple','details'].includes(kind)?['BC0003','BC0004']:['BC0003','LABEL-A'];
  Object.assign(S.left,{status:selected?'success':'filtered',barcode:selected?'BC0003':'',seq:150,time:'16:08:12',requestId:selected?'20260925005':null,task:selected?{chuteCode:'IS-L-L1-1-01',waveNo:'W00001',taskId:'task-1790488002079',skuId:'1BC000',quantity:'1'}:null,error:selected?'':kind==='none'?'候选没有匹配当前筛选规则；本次未请求 RCS。':'有两个匹配条目；本次未请求 RCS，不由操作员手选。',scanEvidence:{candidates,complete:true,selectedCode:selected?'BC0003':null,outcome:selected?'唯一匹配':kind==='none'?'无匹配':'多码歧义',ruleHash:'demo-BC-rule-v1',simulated:true}});
  S.runtime.business.barcodePattern='^BC[0-9]+$';S.saved.business.barcodePattern='^BC[0-9]+$';S.draft.business.barcodePattern='^BC[0-9]+$';
 }
 if(['business-barcode','business-barcode-error'].includes(name)){config('business','business','barcode');S.draft.business.barcodePattern=name==='business-barcode-error'?'[':'^BC[0-9]+$';S.filterExample='BC0003\nLABEL-A';S.phase='dirty';if(name==='business-barcode-error'){S.errors=validate();S.phase='invalid';}else S.filterTest={outcome:'唯一匹配',selectedCode:'BC0003',matches:['BC0003'],candidates:['BC0003','LABEL-A'],complete:true,stale:false,pattern:S.draft.business.barcodePattern,input:S.filterExample,simulated:true};}
 if(name==='system-station')config();
 if(name==='business-rules')config('business','business','rules');
 if(name==='business-runtime')config('business','business','runtime');
 if(name==='business-chute-error'){config('business','business','rules');S.draft.business.specifiedChute='true';S.draft.business.chuteCode='';S.errors=validate();S.phase='invalid';}
 if(name==='system-records')config('system','system','runtime');
 if(name==='system-backup-disabled'){config('system','system','runtime');S.draft.system.backupEnabled='false';S.phase='dirty';}
 if(name==='system-preferences')config('system','system','preferences');
 if(name==='driver-left')config('drivers','left');
 if(name==='driver-right')config('drivers','right');
 if(name==='driver-dirty')changed();
 if(name==='driver-invalid'){changed();S.draft.left.ip='192.0.2.999';S.errors=validate();S.phase='invalid';}
 if(name==='driver-conflict'){config('drivers','right');S.draft.right.ip=S.draft.left.ip;S.draft.right.bcrCode=S.draft.left.bcrCode;S.errors=validate();S.phase='invalid';}
 if(name==='business-barcode'){const result=document.getElementById('barcode-test-result');result?.scrollIntoView({block:'nearest'});}
 if(name==='driver-checking'){changed();S.probe={object:'left',busy:true};}
 if(name==='components')config('components','rcs');
 if(name==='components-failed'){config('components','rcs');S.rcsReachable=false;S.probe={object:'rcs',busy:false,target:S.draft.rcs.target,reachable:false};}
 if(name==='config-save-review')changed();
 if(name==='config-validating'){changed();S.phase='validating';}
 if(name==='config-saving'){changed();S.phase='saving';S.paused=true;S.maintenance=true;}
 if(name==='config-save-failed'){changed();S.phase='save-failed';S.paused=true;S.maintenance=true;}
 if(name==='config-save-unknown'){changed();S.phase='save-unknown';S.paused=true;S.maintenance=true;S.uncertainSaved={revision:13,candidate:candidate(),reason:S.reason};}
 if(['config-pending','config-applying','config-apply-failed','config-runtime-unknown','config-effective'].includes(name)){pending();if(name==='config-applying')S.phase='applying';if(name==='config-apply-failed')S.phase='apply-failed';if(name==='config-runtime-unknown'){S.phase='apply-failed';S.ledger.confirmed=false;}if(name==='config-effective'){S.runtime=copy(S.saved);S.ledger.pending=null;S.ledger.effectiveOriginRevision=13;S.phase='clean';}}
 if(name==='unsaved'){changed();S.waitingNav={view:'work'};}
 if(name.startsWith('setup-')){S.view='setup';S.appStarted=false;S.paused=true;S.ledger={revision:0,pending:null,effectiveOriginRevision:0,confirmed:false};S.baseRevision=0;S.reason='首次配置当前供包台、左右相机与 RCS 连接';S.setupStep={'setup-module':1,'setup-system':2,'setup-devices':3,'setup-component':4,'setup-review':5,'setup-pending':5,'setup-complete':5}[name]||1;if(['setup-review','setup-pending','setup-complete'].includes(name))S.setupChecked=true;if(['setup-pending','setup-complete'].includes(name)){acceptSaved({revision:1,candidate:candidate(),reason:S.reason});S.setupSaved=true;}if(name==='setup-complete'){S.setupComplete=true;S.appStarted=true;S.ledger.pending=null;S.ledger.confirmed=true;S.ledger.effectiveOriginRevision=1;S.phase='clean';}}
 if(name.startsWith('history'))S.view='history';
 if(name==='history-empty'){S.historyQuery='不存在的示例条码';S.historyApplied.query=S.historyQuery;}
 if(name==='history-invalid'){S.historyStart='2026-10-21';S.historyEnd='2026-10-20';S.historyError='开始日期不能晚于结束日期。';}
 if(name.startsWith('diagnostics-')){S.view='diagnostics';S.diagnosticObject='rcs';unknown();S.rcsReachable=name==='diagnostics-recovered';S.paused=true;S.diagnosticChecked=name==='diagnostics-recovered';}
 if(name==='maintenance'||name==='migration-conflict'){S.view='maintenance';S.migrationConflict=name==='migration-conflict';}
 if(name==='support-preview'){S.view='help';S.supportPreview=true;S.supportExpires=Date.now()+300000;}
 if(name==='install-review'){S.view='install';S.installStep=2;S.appStarted=false;S.paused=true;}
 if(['install-preparing','install-cancelled','install-receipt-unknown'].includes(name)){S.view='install';S.appStarted=false;S.paused=true;S.installStep=name==='install-preparing'?1:2;const a=auxiliary();a.installPrepared=name!=='install-preparing';a.installPhase=name==='install-preparing'?'preparing':name==='install-cancelled'?'authorization-cancelled':'receipt-unknown';a.installReview=name==='install-preparing'?null:auxInstallCandidate();}
 S.rcsHeartbeat={target:S.runtime.rcs.target,httpStatus:S.rcsReachable?200:null,checkedAt:'16:06:15',failure:S.rcsReachable?'':'模拟连接被拒绝',synthetic:true};
 for(const side of ['left','right'])S[side].frozenRequest=S[side].status==='filtered'?null:freezeRequest(side,S.runtime,S[side]);
 if(name==='work-processing')S.nextRequestId='20260925005';
 if(typeof seedAuxScene==='function')seedAuxScene(name);
 render(false);for(const selector of ['.workspace','.config-content','.object-tree','.setup-main'])document.querySelector(selector)?.scrollTo({top:0});
 if(name==='business-barcode'){const result=document.getElementById('barcode-test-result');result?.scrollIntoView({block:'nearest'});}
 if(name==='driver-checking'){const content=document.querySelector('.config-content');if(content)content.scrollTop=content.scrollHeight;}
 if(name==='config-save-review')showSaveReview();
 if(name==='unsaved')showUnsaved();
 if(name==='work-filter-details')showInputDetails('left');
}
window.setScene=setScene;window.simulateScan=simulateScan;window.getPrototypeState=()=>copy(S);
const requestedScene=new URLSearchParams(location.search).get('scene');
setScene(SCENES.some(x=>x[0]===requestedScene)?requestedScene:'work-normal');
