"""Re-export visual states from the original v1.1 HTML, without changing its source."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib, json, platform
from playwright.sync_api import sync_playwright
from PIL import Image
BASE = Path(__file__).resolve().parent
if BASE.name == '.ui-import':
    BASE = Path('docs/ui/ss-ui-vis-001-v1.1').resolve()
else:
    BASE = BASE.parent
html_path = BASE/'供包工作站_高保真交互原型.html'
html = html_path.read_text(encoding='utf-8')
expected = 'd46b0ed9b057cda6779012ecdc6981d0799ba8c3de73e9be22d92b61d4860b6c'
assert hashlib.sha256(html_path.read_bytes()).hexdigest() == expected
pairs = [
 ('01-normal-1920.png','normal'),('02-camera-offline-1920.png','offline'),
 ('03-result-unknown-1920.png','unknown'),('04-business-rejected-1920.png','rejected'),
 ('05-no-read-1920.png','noread'),('06-multiple-barcodes-1920.png','multi'),
 ('07-storage-failed-1920.png','storage'),('08-split-unapproved-1920.png','splitBlocked'),
 ('09-split-candidate-1920.png','split'),('10-shortage-1920.png','shortage'),
 ('11-records-1920.png','records'),('12-record-detail-1920.png','recordDetail'),
 ('12b-record-detail-attempts-1920.png','recordDetail'),('13-devices-1920.png','settings'),
 ('14-login-1920.png','login'),('15-recovery-1920.png','recovering'),
 ('16-components-1920.png','components'),('17-waiting-1920.png','waiting'),
 ('18-recovered-1920.png','recovered'),('19-alarm-1920.png','alarms'),
 ('20-wcs-offline-1920.png','wcsOffline'),('21-long-content-1920.png','long'),
 ('22-no-records-1920.png','empty'),('23-pending-1920.png','pending'),
 ('24-maintenance-1920.png','maintenance')]
items = [(name, scene, 1920, 1080) for name, scene in pairs]
items += [('25-long-content-1366.png','long',1366,768),('26-split-1366.png','split',1366,768)]
items += [(f'layout-{w}x{h}.png','normal',w,h) for w,h in [(1920,1080),(1536,864),(1366,768),(1280,720),(1280,680),(1093,614),(911,512)]]
assert len(items) == 34
screens = BASE/'screens'; screens.mkdir(exist_ok=True)
errors=[]; network=[]; output=[]
with sync_playwright() as p:
    browser = p.chromium.launch()
    engine = browser.version
    for name, scene, width, height in items:
        context = browser.new_context(viewport={'width':width,'height':height}, device_scale_factor=1, locale='zh-CN')
        context.route('**/*', lambda route: route.abort())
        page = context.new_page()
        page.on('pageerror', lambda e, n=name: errors.append({'file':n,'error':str(e)}))
        page.on('request', lambda r, n=name: network.append({'file':n,'url':r.url}))
        page.set_content(html, wait_until='domcontentloaded')
        page.wait_for_function('window.SS_DEMO && document.fonts.status === "loaded"')
        page.evaluate('(scene) => SS_DEMO.setScene(scene)', scene)
        page.wait_for_timeout(180)
        page.evaluate('() => {const t=document.getElementById("toast");if(t){t.textContent="";t.className="";}}')
        if name.startswith('12b-'):
            page.evaluate('() => {const d=document.querySelector(".drawer-body"); if(d)d.scrollTop=d.scrollHeight;}')
        page.screenshot(path=str(screens/name), animations='disabled', full_page=False)
        raw=(screens/name).read_bytes()
        with Image.open(screens/name) as image:
            image.load(); assert image.size == (width,height)
        output.append({'file':'screens/'+name,'scene':scene,'viewport':[width,height],
                       'sha256':hashlib.sha256(raw).hexdigest(),'size_bytes':len(raw)})
        context.close()
    browser.close()
report={'design':'SS-UI-VIS-001 v1.1','method':'new Chromium exports of byte-identical original HTML through set_content',
        'exported_at_utc':datetime.now(timezone.utc).isoformat(),'source_html_sha256':expected,'engine':engine,
        'platform':platform.platform(),'font_dependency':'system Noto CJK, not distributed in this repository',
        'screenshots':output,'page_errors':errors,'network_requests_blocked':network,
        'checks':'source identity, requested scene activation, PNG decode/dimensions; no business regression or native desktop acceptance',
        'windows_client_run':'NOT_RUN','sdk_wcs_site':'NOT_RUN','original_screenshot_bytes':'NOT_UPLOADED'}
(BASE/'render-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
assert not errors, errors
assert not network, network
print('Exported 34 PNGs; no page errors or network requests. Not a Windows/production acceptance.')
