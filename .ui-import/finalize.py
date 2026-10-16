"""Publish design navigation and provenance; do not alter original UI or application code."""
from pathlib import Path
import hashlib, json, re, shutil, zipfile
from xml.etree import ElementTree as ET
BASE=Path('docs/ui/ss-ui-vis-001-v1.1')
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,text): p.write_text(text.rstrip()+'\n',encoding='utf-8')
source=json.loads((BASE/'source-manifest.json').read_text())
render=json.loads((BASE/'render-verification.json').read_text())
for item in source['files']:
    assert digest(BASE/item['path'])==item['sha256']
assert len(render['screenshots'])==34 and not render['page_errors'] and not render['network_requests_blocked']
boards=json.loads((BASE/'design/editable-boards.json').read_text())
assert len(boards)==18
pptx=BASE/'design/供包工作站_可编辑UI画板_v1.1.pptx'
external=[]; disguised=0
with zipfile.ZipFile(pptx) as z:
    assert z.testzip() is None
    assert len([n for n in z.namelist() if re.fullmatch(r'ppt/slides/slide\d+\.xml',n)])==18
    for name in z.namelist():
        if name.endswith('.rels'):
            external += [x.attrib for x in ET.fromstring(z.read(name)) if x.attrib.get('TargetMode')=='External']
        if name.startswith('ppt/media/') and name.endswith('.png') and b'<svg' in z.read(name)[:1000]: disguised+=1
        assert not name.lower().endswith(('.ttf','.otf','.woff','.woff2','.eot'))
assert not external
(BASE/'tools').mkdir(exist_ok=True)
shutil.copyfile('.ui-import/render.py',BASE/'tools/reexport_screenshots.py')
write(BASE/'README.md','''# 供包工作站最终交付 UI 设计基线 · v1.1

本目录实际保存 SS-UI-VIS-001 v1.1 的设计原文件、可编辑画板和全部34个页面/状态/窗口图位。版本依据是本设计会话最后一次完整交付，不以另一会话的版本号或未导出的Canva更新时间判断更晚定稿。

## Codex 必须先读

先查看下方主工作台和 [全部状态图](GALLERY.md)，再阅读 [开发交接全文](docs/供包工作站_UIUX设计与开发交接说明.md)、[原交互HTML](供包工作站_高保真交互原型.html)、[分离源码](prototype/index.html) 和 [tokens](design/tokens.json)。不能再仅凭说明文字、接口字段或当前WPF调试界面猜布局。

![主工作台](screens/01-normal-1920.png)

## 直接使用的文件

|内容|位置|
|---|---|
|34张独立PNG与预览|[图册](GALLERY.md)、screens/|
|自包含可交互HTML|[供包工作站_高保真交互原型.html](供包工作站_高保真交互原型.html)|
|分离HTML/CSS/JavaScript|[prototype/index.html](prototype/index.html)、[workstation.css](prototype/workstation.css)、[workstation.js](prototype/workstation.js)|
|18页可编辑PPTX|[供包工作站_可编辑UI画板_v1.1.pptx](design/供包工作站_可编辑UI画板_v1.1.pptx)|
|18页元素坐标/图形源|[editable-boards.json](design/editable-boards.json)|
|颜色、字号和布局变量|[tokens.json](design/tokens.json)、[tokens.css](design/tokens.css)|
|完整设计规范、PAGE/REQ映射和开发交接|[UIUX设计与开发交接说明](docs/供包工作站_UIUX设计与开发交接说明.md)|
|离线全图册|[界面图册.html](界面图册.html)|
|来源及逐文件校验|[source-manifest.json](source-manifest.json)、[render-verification.json](render-verification.json)、[publication-checks.json](publication-checks.json)|

GitHub文件页面可查看PNG、Markdown和源码；交互HTML需要克隆仓库后用浏览器本地打开。HTML无CDN依赖、禁止真实相机/WCS/实体调用；底部评审控制台切换原有44种场景。它是设计载体，不改变Windows/WPF生产技术路线。

## 精确的来源与验收边界

11份源文件与原v1.1交付ZIP内对应文件逐字节一致；原README另存为README.source.md，其“本次/evidence/tools”等描述属于2026-10-15原交付，不代表本次上传重跑了历史验证。

**本目录34张PNG是此次由原样HTML在隔离Chromium重新导出的图片，不是原交付ZIP里的历史PNG字节，也不是Windows客户端运行截图。** 重新导出未修改HTML/CSS/JS；字体和渲染器差异可能改变细微排版。逐张尺寸、场景、哈希与渲染环境已登记。截图只证明该设计状态的视觉导出，不能证明按钮、SDK、实际WCS或现场验收通过。

可编辑PPTX保持原字节。已知部分.png命名媒体实际是SVG；不将保存源文件宣称为PowerPoint/Canva跨渲染器排版通过，也不冒充新的Canva导出。没有包含字体文件或厂商SDK。

## 设计与实现的取舍

本组资料作为已交付视觉与交互意图基线，**不是业务规则和旧HTML逻辑的自动批准**。必须同时读取 [框架设计](../../design/framework.md)、[当前运行入口](../../INDEX.md)、[未关闭评审](../../reviews/STATUS.md)。D/RV仍由原登记和实际修复证据决定。继承专业信息层级，不复制已知gate/恢复/计数/焦点缺陷，不降低当前应用守卫来模仿原型成功。

布局保持共享箱/波次、双通道当前任务/条码/数量/结果、持续异常、固定主操作、最近记录。工程控制不能取代操作工作台；模拟标识必须保留，真实未知字段不得补造。新的WPF实现需另交实际Windows运行图和交互证据。

原客户PRD、原产品HTML及私密来源、其他受限设计/评审附件没有随本目录上传，完整客户资料归档仍未闭环。文件库另一会话v2.1包未取得可核验原字节，未将其冒称当前稿。原交付历史证据引用不在本目录时，按未随本次上传处理，不造空文件。

## 重新导出这些图

可选导出工具：Python、Playwright 1.56.0、其Chromium、Pillow和系统简体中文字体；打开单文件HTML本身不需要Python。安装上述依赖后执行 `python docs/ui/ss-ui-vis-001-v1.1/tools/reexport_screenshots.py`。工具只切换设计场景和截图，阻断外联，不执行现场业务测试。
''')
lines=['# 供包工作站 v1.1 · 全部34张独立设计图','',
       '这些是原样HTML的本次重新导出，不是历史PNG字节或WPF运行证据。每张可点开查看完整尺寸。','',
       '[设计入口](README.md) · [HTML原型](供包工作站_高保真交互原型.html) · [完整交接](docs/供包工作站_UIUX设计与开发交接说明.md)','']
for item in render['screenshots']:
    width,height=item['viewport']; path=item['file']; label=f"{Path(path).stem} · {item['scene']} · {width}×{height}"
    lines += ['## '+label,'',f'![{label}]({path})','']
write(BASE/'GALLERY.md','\n'.join(lines))
checks={'scope':'UI source publication and screenshot export only','source_files_byte_identical':11,
        'png_exports':34,'pptx_pages':18,'editable_board_pages':18,'pptx_external_relations':external,
        'known_png_named_svg_parts':disguised,'source_business_logic_changed':False,
        'business_tests':'NOT_RUN','windows_sdk_wcs_site':'NOT_RUN',
        'source_and_image_identity':'PASS','historical_evidence_reexecution':False,
        'not_included':['historical screenshot bytes','raw customer PRD/product HTML and private source links','new Canva export','other conversation unmaterialized v2.1 package'],
        'publication_basis':'explicit repeated user request to upload this final generated UI set to the known public repository; not a blanket customer-material release'}
write(BASE/'publication-checks.json',json.dumps(checks,ensure_ascii=False,indent=2))
index=Path('docs/INDEX.md'); text=index.read_text()
section='''## 已入库的最终交付UI：Codex页面开发必读

[SS-UI-VIS-001 v1.1完整设计入口](ui/ss-ui-vis-001-v1.1/README.md) · [34张独立状态图](ui/ss-ui-vis-001-v1.1/GALLERY.md) · [原始交互HTML](ui/ss-ui-vis-001-v1.1/供包工作站_高保真交互原型.html) · [完整UI规范与交接](ui/ss-ui-vis-001-v1.1/docs/供包工作站_UIUX设计与开发交接说明.md)。PPTX、画板JSON、tokens及分离HTML/CSS/JS均在该目录，不仅是链接或摘要。11份源文件原字节保留，PNG为同源重新导出、非Windows运行截图；版本与边界见目录README。

该组生成成果依据用户本次明确上传要求发布。下文历史“所有衍生图均未入库”仅对本组以外材料有效；完整客户资料归档仍为部分完成。设计保存不自动关闭D/RV，也不能覆盖实际应用安全约束。

'''
if '## 已入库的最终交付UI' not in text:
    text=text.replace('## 当前结论',section+'## 当前结论',1)
write(index,text)
agents=Path('AGENTS.md'); text=agents.read_text()
if 'ss-ui-vis-001-v1.1' not in text:
    text+='\n## UI实现必须读取的实物基线\n\n修改页面前先读 `docs/ui/ss-ui-vis-001-v1.1/README.md`、`GALLERY.md`、原HTML、分离CSS/JS和完整UI交接，不得仅凭文字或现有调试页面猜设计。保留专业作业信息层级；原HTML是模拟设计，不照抄已知业务缺陷；PNG为同源重新导出，Windows交互和截图须另行验收。原文件和当前业务实现的来源、版本与验证边界分别报告。\n'
write(agents,text)
status=Path('docs/ui/STATUS.md'); old=status.read_text()
write(status,'# UI资料当前状态\n\n2026-10-16：依据用户明确要求，新增 [最终完整交付v1.1实物](ss-ui-vis-001-v1.1/README.md)。原HTML、分离源码、18页PPTX/JSON、tokens和完整UI交接已保存；34张PNG由同源重新导出，不是历史PNG或WPF截图。\n\n旧状态中的“没有对应完整原型/未入库”对本组已被本次实物入库取代。历史获取、未决事项及原资产问题仍保留，不倒写成通过。\n\n## 入库前历史状态（原文保留）\n\n'+old)
for name in ['PUBLICATION.md','MISSING.md']:
    p=Path('docs/baseline')/name; old=p.read_text()
    note='## 2026-10-16 最终生成UI的限定更新\n\n用户在已知公开仓库范围后再次明确要求上传最终UI图及HTML设计资料。本次仅发布 [SS-UI-VIS-001 v1.1生成设计集](../ui/ss-ui-vis-001-v1.1/README.md)：11份原字节源文件和34张同源重导出PNG；不是原PRD、私密原型来源、SDK/字体或其他客户资料的整体公开许可。以下历史未上传表对该组已被更新，其他限制继续有效；全量归档仍为PUBLIC_SUBSET_ONLY。历史PNG字节、新Canva完整导出和另一会话v2.1未作为此次成果上传。\n\n'
    title,body=old.split('\n',1); write(p,title+'\n\n'+note+body.lstrip())
manifest_path=Path('docs/manifest.json'); manifest=json.loads(manifest_path.read_text())
records={item['path']:item for item in manifest['files']}
touched=[agents,index,status,Path('docs/baseline/PUBLICATION.md'),Path('docs/baseline/MISSING.md')]
touched += sorted(p for p in BASE.rglob('*') if p.is_file())
for p in touched:
    key=p.as_posix(); rec=records.get(key,{}).copy()
    rec.update({'path':key,'source':'user-requested final UI publication / SS-UI-VIS-001 v1.1',
                'version':'ui-publication-20261016','sha256':digest(p),'size_bytes':p.stat().st_size,
                'purpose':'complete visual/source design baseline for Codex; not production acceptance',
                'status':'design_reference_not_approved' if p.is_relative_to(BASE) else 'current_baseline_control',
                'storage_scope':'public_repository','collected':True,'hash_checked':True,
                'authorization_status':'user_explicit_final_generated_ui_upload_request',
                'remote_inclusion':'verify_final_main_after_merge'})
    records[key]=rec
by_hash={x['sha256']:str(BASE/x['path']) for x in source['files']}
for item in manifest.get('withheld_primary_artifacts',[]):
    if item.get('sha256') in by_hash:
        item.update({'actual_repository_path':by_hash[item['sha256']], 'status':'included_by_final_ui_request',
                     'reason':'user_explicit_final_generated_ui_upload_request'})
manifest['files']=sorted(records.values(),key=lambda x:x['path'])
manifest['full_archive_in_main']=False
manifest['ui_design_publication']={'version':'SS-UI-VIS-001 v1.1','entry':str(BASE/'README.md'),
    'source_files':11,'screens':34,'screen_provenance':'new exports of exact original HTML, not old PNG bytes',
    'application_code_changed':False,'remote_verification':'read exact main commit and file identities after merge'}
write(manifest_path,json.dumps(manifest,ensure_ascii=False,indent=2))
write(Path('docs/manifest.sha256'),digest(manifest_path)+'  manifest.json')
for p in [BASE/'界面图册.html',BASE/'prototype/index.html']:
    for target in re.findall(r'(?:src|href)="([^"]+)"',p.read_text()):
        if target.startswith(('http:','https:','data:','#')):continue
        assert (p.parent/target).is_file(),(p,target)
for item in source['files']: assert digest(BASE/item['path'])==item['sha256']
assert digest(manifest_path)==Path('docs/manifest.sha256').read_text().split()[0]
print(json.dumps({'source_files':11,'screenshots':34,'design_files':len(list(BASE.rglob('*'))),
                  'scope':'design assets/documents only','pptx_pages':18,'known_svg_parts':disguised},ensure_ascii=False))
