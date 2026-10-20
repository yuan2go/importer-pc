#!/usr/bin/env python3
"""Regenerate source-attributed field migration from the installed provider schemas."""
from pathlib import Path
import csv,json

REPO=Path(__file__).resolve().parents[4]
OUT=Path(__file__).resolve().parent.parent/'FIELD-MIGRATION.csv'
rows=[]
def add(category,obj,group,key,label,kind,unit='',required='',constraint='',effect='保存后受控重启',default='',source='',note=''):
    rows.append(dict(category=category,object=obj,page_group=group,original_key=key,chinese_label=label,control=kind,unit=unit,required_when=required,validation=constraint,effect=effect,documented_default=default,source=source,notes=note))

source='src/Ipc.Application.ParcelFlow.Desktop/ParameterEditorModel.cs; ParameterSettingsControl.Policy.cs'
add('系统','当前供包台','本地身份 / 技术信息','StationId','本地工作站标识','只读','','始终','不能被外部编码替代','运行内只读',source=source)
add('系统','当前供包台','供包台标识 / 心跳身份','StationCode','心跳工位编码','文本','','必填','1～256 非控制字符',source=source,note='与取格口 stationCode 分别维护，不静默互换')
for key,label,unit,low,high in [('ResidentHistory','常驻历史条数','条',1,10000),('MinimumFreeMiB','关键写入保留空间','MiB',1,1048576)]:
    add('系统','当前供包台','数据与记录',key,label,'数值',unit,'必填',f'{low}～{high}',source=source)
for key,label in [('ConfigurationDirectory','配置目录'),('DataDirectory','数据目录')]:
    add('系统','当前供包台','数据与记录',key,label,'只读路径','','宿主提供位置投影时','未提供就明确说明，不猜当前工作目录','运行内只读',source='IParcelConfigurationLocationApplication.ConfigurationLocations')
add('系统','操作员登录角色','数据与记录','SessionLifetimeMs','操作员会话有效时长','数值','ms','绑定操作员登录或该字段已有错误','100～86400000，无条件校验',source=source,note='日常读取 ConfigurationRoles 登录选择；首配读取 Bound；解绑保留值，已有错误仍可定位；维护认证不用此时长。不新增强制登录。')
add('系统','当前供包台','自动备份','BackupEnabled','启用自动备份','开关','','可选','关闭时提交 BackupSchedule=null',default='无计划时关闭；保留已有计划',source=source,note='关闭不删除已有恢复集')
add('系统','当前供包台','自动备份','BackupDirectory','备份目录','绝对路径','','开启计划时','本机或共享绝对路径；目标事先存在，普通目录且无链接祖先',default='LocalAppData/ImporterPc/backups/<ApplicationId>（仅空计划编辑初值）',source=source,note='不自动创建或改道；计划仅在暂停、维护授权有效、无在途外呼时执行')
add('系统','当前供包台','自动备份','BackupIntervalMinutes','备份间隔','数值','分钟','开启计划时','15～10080',default='60（仅空计划编辑初值）',source=source)
add('系统','当前供包台','自动备份','BackupMaximumSets','备份份数上限','数值','份','开启计划时','1～1000',default='10（仅空计划编辑初值）',source=source,note='达到上限停止创建，不自动删除原备份')
add('系统','当前 Windows 用户','显示偏好','PresentationPreferences','作业页布局偏好','现有布局切换','','本机用户','标准 / 优先紧凑，空间不足自动适配','本机单独保存',source='src/Ipc.Platform/PresentationPreferences.cs',note='不产生业务配置修订；不新增主题或语言后台能力')
add('业务模块','Sportmaster','条码筛选','BarcodePattern','条码匹配规则','正则文本','','现有规则不可清空','1～2048字符；完整匹配；单次25ms、总计100ms',source=source,note='旧 null 保持原严格单码兼容；原型JS不是.NET正则验收')
add('业务模块','Sportmaster','条码筛选','LocalCandidateSample','本地候选条码','多行文本','','按需检查','每行一个，一次完整观察，最多300项完整候选；不完整/零匹配/多匹配拒绝选择；结果随输入改变过期','本地检查，不保存不外呼',source='ParameterSettingsControl.Preview')
for key,label,unit,low,high in [('QueueCapacity','输入队列容量','条',1,4096),('TimeoutMs','单次调用预算','ms',100,30000),('MaximumOperations','未清偿作业容量','件',1,100000)]:
    add('业务模块','当前 Application','处理参数',key,label,'数值',unit,'必填',f'{low}～{high}',source=source)

driver_path='src/Ipc.Drivers.Hikrobot/parameters.json'
for side in ['左相机','右相机']:
    for p in json.loads((REPO/driver_path).read_text()):
        k=p['Key']; group='连接' if k in ['device.ip','device.serial'] else '相机编码' if k=='bcr-code' else '识别与缓冲'
        label={'bcr-code':'心跳相机编码','entity.mode':'实体关联方式'}.get(k,p['Label'])
        constraint=[]
        if k=='device.ip':constraint+=['规范 IPv4；跨实例不能重复；不新增端口字段']
        if p.get('MaximumLength'):constraint += [f"最多{p['MaximumLength']}字符"]
        if 'Minimum' in p:constraint += [f"{p['Minimum']}～{p['Maximum']}"]
        if 'Choices' in p:constraint += [' / '.join(p['Choices'])]
        if k=='bcr-code':constraint += ['非空、无控制字符、最多256字符；左右区分大小写不得重复']
        add('驱动',side,group,k,label,{'choice':'枚举','integer':'数值','string':'文本'}[p['Type']],p.get('Unit',''),'绑定 RCS 运行时' if k=='bcr-code' else '必填' if p.get('Required') else '按驱动描述', '; '.join(constraint),default=p.get('Default','未提供，不推断'),source=driver_path,note=(p.get('Description','')+'；无独立心跳启用开关，手工模拟驱动例外使用显式 DeviceId') if k=='bcr-code' else p.get('Description',''))

rcs_path='src/Ipc.Integrations.SportmasterRcs/parameters.json'
for p in json.loads((REPO/rcs_path).read_text()):
    k=p['Key'];category,obj,group='组件','共享 RCS 连接','协议与心跳'
    label=p['Label'];notes=''
    if k in ['zone-id','project-id','left-station-code','right-station-code']:
        category,obj,group='系统','当前供包台','供包台标识 / 取格口身份'
        label={'zone-id':'区域 ID','project-id':'项目 ID','left-station-code':'共同供包台编码','right-station-code':'共同供包台编码'}[k]
        if 'station-code' in k:notes='同一个可写投影映射原左右参数；原值冲突先显示两值，由用户明确统一；不是两套可写输入'
    elif k in ['quantity','specified-chute','chute-code']:
        category,obj,group='业务模块','Sportmaster','取格口请求'
        label={'quantity':'每次请求数量','specified-chute':'指定格口','chute-code':'指定格口编码'}[k]
        notes='请求数量与响应quantity分离；指定格口关闭时从候选移除chute-code，实际请求为null'
    elif k in ['left-bcr-code','right-bcr-code']:
        category,obj,group='驱动','左相机' if k.startswith('left') else '右相机','相机编码'
        label='取格口相机编码';notes='写入原 RCS schema，对应驱动实例唯一可写；与驱动心跳bcr-code不同用途'
    elif k.startswith('request-id'):group='请求编号区间'
    constraint=[]
    if 'Minimum' in p:constraint += [f"{p['Minimum']}～{p['Maximum']}"]
    if p.get('MaximumLength'):constraint += [f"最多{p['MaximumLength']}字符"]
    if 'Choices' in p:constraint += [' / '.join(p['Choices'])]
    if k=='request-id-count':constraint += ['与起点之和不能溢出；原持久编号责任保持']
    required='指定格口为 true' if k=='chute-code' else '必填' if p.get('Required') else '按组件描述'
    add(category,obj,group,k,label,{'choice':'开关' if k=='specified-chute' else '枚举','integer':'数值','string':'文本'}[p['Type']],p.get('Unit',''),required,'; '.join(constraint),default=p.get('Default','未提供，不推断'),source=rcs_path,note=notes or p.get('Description',''))
add('组件','共享 RCS 连接','服务连接','Target','服务地址','地址','','必填','HTTP(S) /rcs/sorter/api；禁止凭据、query、fragment',source='ParameterEditorModel.cs; RcsTaskOptions.cs; ParameterSettingsControl.Capabilities.cs',note='地址只有一个可写来源，不再新增重复endpoint')
for category,obj in [('驱动','左右驱动实例'),('组件','各实际适用组件')]:
    keys='Provider / InstanceId / DeviceId / Role' if category=='驱动' else 'Provider / InstanceId / Target / Contract / Role'
    control='精确提供者选择 + 只读身份' if category=='驱动' else '精确提供者选择 + 可编辑连接目标 + 只读身份/合同'
    add(category,obj,'安装与技术信息',keys,'安装类型、精确版本、原实例与绑定',control,'','按实际安装描述','缺失精确依赖时说明原因；不猜替代版本',source='CapabilityDraft; ParameterSettingsControl.Capabilities.cs',note='schema3固定left/right两个读码位置，绑定共享Wcs（选用RCS时一连接）；组件依真实绑定/库存呈现，含维护认证，非组件总数为1；旧schema1/2原Reader兼容保留')
add('业务模块','其他独立 Application','原适用业务策略','WeightSource/Unit/Precision/ConfiguredWeight/MaximumAgeMs/WaitTimeoutMs/Validity','原重量策略','沿原枚举与数值','','仅实际业务适用','沿原完整校验，不更改语义',source='ParameterSettingsControl.Policy.cs',note='不因重设计给 Sportmaster 新增称重')
add('业务模块','其他独立 Application','原适用图像策略','UploadStrategy/ImageBytes/TotalBytes/MaximumPending','原图像策略与容量','沿原枚举与数值','','仅实际业务适用','沿原完整校验，不更改语义',source='ParameterSettingsControl.Policy.cs',note='不因重设计给 Sportmaster 新增预览或传图')
add('组件','其他实际安装并适用的组件','所属组件对象 / 高级参数','Parameters[key]','按安装描述的组件参数','按真实类型生成控件','','按角色、安装schema和依赖','沿 Required/Choices/Range/Secret 等真实描述，不推断未来字段',source='CapabilityDraft; ParameterSettingsControl.Capabilities.cs',note='兜底保留如 reference integration 的 slot、维护登录 subjects 等实际参数；敏感字段隐藏，技术键可定位；不因RCS示例删其他组件')
defaults={'ResidentHistory':'200','MinimumFreeMiB':'64','QueueCapacity':'64','TimeoutMs':'1500','MaximumOperations':'10000','SessionLifetimeMs':'28800000'}
for row in rows:
    if row['original_key'] in defaults:
        row['documented_default']=defaults[row['original_key']]+'（源码默认；已有配置不覆盖）'
        row['source']+='; src/Ipc.Application.Contracts/SystemParameterDefaults.cs; ParcelContracts.cs'
    if row['original_key']=='StationCode':
        row['notes']+='；原值null时才从本地StationId显式补入未保存候选'
with OUT.open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
print(json.dumps({'rows':len(rows),'file':str(OUT)},ensure_ascii=False))
