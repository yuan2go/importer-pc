import assert from 'assert';
import vm from 'vm';
import fs from 'fs';

let failures = 0;
function test(name, action) {
  try { action(); process.stdout.write('PASS ' + name + '\n'); }
  catch (error) { failures++; process.stderr.write('FAIL ' + name + ': ' + error.stack + '\n'); process.exitCode = 1; }
}

function load(file, extra = {}) {
  const context = vm.createContext(extra);
  vm.runInContext(fs.readFileSync(new URL('../' + file, import.meta.url), 'utf8'), context);
  return context;
}

test('double-star slash accepts zero directories, never a partial directory name', () => {
  const api = load('live-browser-ignores.js').__IMPECCABLE_LIVE_IGNORES__;
  const ignores = { ignoreFiles: ['src/**/page.html'] };
  assert.equal(api.resolveDetectIgnores({ ignores, pathname: '/src/page.html' }).skipScan, true);
  assert.equal(api.resolveDetectIgnores({ ignores, pathname: '/src/other/page.html' }).skipScan, true);
  assert.equal(api.resolveDetectIgnores({ ignores, pathname: '/src/otherpage.html' }).skipScan, false);
});

test('ambiguous served pages do not borrow a source-only waiver', () => {
  const api = load('live-browser-ignores.js').__IMPECCABLE_LIVE_IGNORES__;
  const result = api.resolveDetectIgnores({ pathname: '/page.html', ignores: {
    roots: ['src'], pageFiles: ['src/page.html', 'public/page.html'], ignoreFiles: ['src/page.html'],
    ignoreValues: [{ rule: {}, value: '*' }, { rule: 'contrast', value: null }]
  } });
  assert.equal(result.skipScan, false);
  assert.equal(result.disabledRules.length, 0);
  assert.equal(result.disabledValues.length, 0);
});

test('unavailable localStorage does not prevent session initialization', () => {
  const context = {};
  Object.defineProperty(context, 'localStorage', { get() { throw new Error('Storage denied'); } });
  const api = load('live-browser-session.js', context).__IMPECCABLE_LIVE_SESSION__;
  const session = api.createLiveBrowserSessionState({ prefix: 'local', idFactory: () => 'test-id' });
  session.saveSession({ id: 'session' });
  assert.equal(session.loadSession(), null);
  session.clearSession();
});

test('DOM ownership uses literal prefixes and late shadow hosts join the active top layer', () => {
  function element(id = '') {
    return { id, style: { setProperty() {} }, parentNode: null, parentElement: null,
      appendChild(node) { node.parentNode = this; node.parentElement = this; return node; } };
  }
  const document = { createElement: () => element(), body: element('body') };
  const context = load('live-browser-dom.js', { document });
  const prefix = '9 prefix"[';
  const dom = context.__IMPECCABLE_LIVE_DOM__.createLiveBrowserDomHelpers({ prefix, document });
  const parent = element(prefix + '-toolbar');
  assert.equal(dom.own({ id: '', parentElement: parent }), parent);
  assert.equal(dom.own(element('page')), false);
  assert.equal(dom.cssId('9 x'), '\\39 \\20 \\78 ');
  const modal = element('modal'); modal.appendChild(dom.topLayerHost);
  const host = element('adapter');
  context.__IMPECCABLE_LIVE_UI_ROOT__ = { host, appendChild(node) { node.shadowHost = host; } };
  const node = element(); dom.uiAppend(node);
  assert.equal(host.parentNode, dom.topLayerHost);
  assert.equal(node.shadowHost, host);
});
