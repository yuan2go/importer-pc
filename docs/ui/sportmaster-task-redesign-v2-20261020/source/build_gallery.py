"""Generate the static offline image gallery and the README image index."""
from pathlib import Path
import html
import json
import re

ROOT = Path(__file__).resolve().parent.parent
SCREENS = json.loads((ROOT / 'screens.json').read_text())
e = html.escape
labels = {'named-scene': '界面状态', 'viewport': '窗口变体', 'scroll-detail': '滚动详情'}
table = ['| 编号 | 界面 / 状态（点击大图） | 覆盖的任务与决定 | PNG 像素 |',
         '|---|---|---|---|']
cards = []
for i, s in enumerate(SCREENS, 1):
    dimensions = f"{s['pixelWidth']}×{s['pixelHeight']}"
    table.append(f"| {i:02d} | [{s['title']}]({s['file']}) | {s['description']} | {dimensions} |")
    cards.append(f'''<article data-text="{e(s['title']+' '+s['description'],quote=True)}"><a href="{e(s['file'])}"><img loading="lazy" src="{e(s['file'])}" alt="{e(s['title'])}"></a><div class="caption"><span class="kind">{i:02d} · {labels[s['kind']]} · {dimensions}</span><h2><a href="{e(s['file'])}">{e(s['title'])}</a></h2><p>{e(s['description'])}</p><a class="demo" href="Prototype.html?scene={e(s['scene'])}">打开对应原型场景</a></div></article>''')

gallery = '''<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'none'"><title>Sportmaster V2 · 高清界面图集</title><style>
*{box-sizing:border-box}body{margin:0;background:#f2f5f8;color:#183047;font:15px/1.6 'Microsoft YaHei UI','Microsoft YaHei','Segoe UI',sans-serif}header{background:#fff;border-bottom:1px solid #ccd6e1;padding:24px 30px}h1{font-size:25px;margin:0 0 8px}header p{margin:6px 0;color:#586b80}nav{display:flex;gap:22px;flex-wrap:wrap;margin-top:16px}a{color:#185fbc;text-decoration:none}a:hover{text-decoration:underline}a:focus-visible,input:focus-visible{outline:3px solid #2972c9;outline-offset:3px}.tools{padding:20px 30px;display:flex;gap:18px;align-items:center;flex-wrap:wrap}.tools input{height:40px;padding:8px 12px;min-width:300px;border:1px solid #b3c1d0;border-radius:4px;font:inherit}main{padding:0 30px 30px;display:grid;grid-template-columns:repeat(auto-fill,minmax(420px,1fr));gap:20px}article{background:#fff;border:1px solid #ccd6e1;border-radius:5px;overflow:hidden}article[hidden]{display:none}article img{display:block;width:100%;height:auto;border-bottom:1px solid #dce3eb}.caption{padding:16px 18px}.kind{color:#586b80;font-size:12px}h2{font-size:17px;margin:6px 0}p{margin:7px 0}.caption p{color:#586b80;font-size:13px}.demo{font-size:13px}footer{padding:24px 30px;background:#fff;border-top:1px solid #ccd6e1;color:#586b80}#empty{display:none;padding:30px}@media(max-width:500px){main{grid-template-columns:1fr;padding:0 14px 20px}header,.tools,footer{padding-left:14px;padding-right:14px}.tools input{width:100%;min-width:0}}
</style></head><body><header><h1>Sportmaster UI 重设计 V2</h1><p>系统、业务模块、驱动、组件四类配置；首次启动独立流程；每位相机、扫描、RCS任务三组信息。</p><p>51个命名状态 · 58张PNG（含5个窗口变体和2个滚动详情）。全部为离线模拟，不代表Windows、设备链路或性能验收。</p><nav><a href="Prototype.html">打开可点击原型</a><a href="README.md">完整交付索引</a><a href="HANDOFF.md">开发交接</a><a href="qa/README.md">必要走查与限制</a></nav></header><div class="tools"><label for="filter">查找界面</label><input id="filter" placeholder="例如：驱动、保存、首次启动" autocomplete="off"><span id="count" aria-live="polite">58 / 58 张</span></div><main>''' + ''.join(cards) + '''</main><p id="empty">没有匹配项；清空筛选可查看全部界面。</p><footer>点击图片打开完整PNG。图集随screens目录一起下载可离线查看；单文件Prototype.html可独立打开。浏览器缩略图不替代逐张原图检查。</footer><script>const f=document.getElementById('filter'),items=[...document.querySelectorAll('article')];f.addEventListener('input',()=>{const q=f.value.trim().toLowerCase();let n=0;for(const item of items){item.hidden=!item.dataset.text.toLowerCase().includes(q);if(!item.hidden)n++;}document.getElementById('count').textContent=n+' / '+items.length+' 张';document.getElementById('empty').style.display=n?'none':'block';});</script></body></html>'''
named = sum(s['kind'] == 'named-scene' for s in SCREENS)
gallery = gallery.replace('51个命名状态 · 58张PNG', f'{named}个命名状态 · {len(SCREENS)}张PNG').replace('58 / 58 张', f'{len(SCREENS)} / {len(SCREENS)} 张')
(ROOT / 'Gallery.html').write_text(gallery)
readme = ROOT / 'README.md'
text = readme.read_text()
start, end = '<!-- SCREENS_TABLE -->', '<!-- END_SCREENS_TABLE -->'
content = start + '\n\n' + '\n'.join(table) + '\n\n' + end
text = re.sub(re.escape(start) + r'.*?' + re.escape(end), lambda _: content, text, flags=re.S) if end in text else text.replace(start, content)
readme.write_text(text)
print(json.dumps({'png_links': len(SCREENS), 'gallery': str(ROOT / 'Gallery.html')}, ensure_ascii=False))
