# Prototype shared contract

Single offline HTML, plain JS and CSS. Target 1600 x 900 CSS px, exported PNG at 2x. Windows UI specimen, all data simulated. No external network calls. Root owns styles.css, app.js and build/render scripts. Auxiliary agent owns aux.js only. All files concatenated into one `<script>` with root helpers preceding aux; initialization after both.

## Shared state and helpers

- `S`: root state. Auxiliary own namespace `S.aux` initialized by auxiliary if absent. `S.view`: work | settings | history | diagnostics | setup | install | backup | support | advanced. `S.object`: summary | left | right | wcs | bindings | policies | modules. Do not replace root state.
- `render()`: rerender the main application.
- `go(view, object)`: dirty guarded navigation.
- `toast(text)`: small non-blocking feedback.
- `openDialog(title, bodyHTML, footerHTML)`: modal; actions remain delegated.
- `closeDialog()`.
- `btn(label, action, type='secondary', attrs='')`: button with `data-action` action. types primary, secondary, danger, ghost. attrs a raw optional string.
- `tag(label, tone='neutral')`: compact status, tones success, warning, danger, info, neutral.
- `note(tone, title, detail='', actionHTML='')`: horizontal notification panel.
- `section(title, bodyHTML, sub='')`: form section, h3 plus description and content.
- `field(label, key, value, help='', options={})`: input; data-field key. options include type='text', readonly=false, unit, placeholder, error. Root handles data-field automatically in S.drafts for core; for aux keys prefix `aux.` and auxiliary handles through auxField(key,value).
- `selectField(label,key,value,options,help='')`: options array of strings or [value,label].
- `esc(value)`: HTML escape.
- `icon(name,size=18)`: inline line icon (names box, work, history, settings, wrench, help, camera, server, check, alert, arrow, chevron, folder, search, close, download, upload, link, clock, copy, shield, pause, play).
- Global `renderAux(view)` returns HTML body or empty string for unknown view.
- Global `auxAction(action,element)` returns true if handled, false otherwise. Global `auxField(key,value)` handles keys beginning aux.

## Shared CSS classes

`.page-heading` flex title left, actions right; h1 25px, p 14px muted. `.panel` white border radius8 padding24; `.form-section` border-bottom, padding-bottom24, margin-bottom24; `.form-grid` 2 columns gap20; `.form-grid.three` 3 columns; `.field`, `.help`, `.muted`, `.mono`, `.row` flex align center gap12, `.between` justify between; `.stack` grid gap16; `.split` grid 1fr 1fr gap20; `.table` full width, border-collapse rows 48px; `.list-row` flex padded16 border-bottom; `.data-pair` flex label/value; `.actions` flex gap10; `.small` 13px; `.subnav` horizontal buttons selected `.active`; `.steps` horizontal stepper; `.step` and `.step.active`; `.notice` from note(); `.object-summary` light blue summary; `.checklist` vertical rows; `.footer-actions` sticky within page at bottom. `.code` monospaced pre block white-space pre-wrap. `.empty` centered icon/title/explanation.

## Product constraints

Sportmaster only, two cameras -> WCS -> displayed chute. No PLC action, no parcel image preview. All IP, ports, service addresses and device driver identity displayed in new screens are marked design examples: the source screenshots did not reveal actual connection fields. All screenshot sample values are NOT defaults.

Saved vs running different. Runtime changes default to controlled restart as source text implies; do NOT promise hot reload/device-only restart. Backup and configuration template different. Full restore isolated and must not auto release original requests. No mandatory operator login added. Maintenance authentication preserve existing route, propose verify only at sensitive apply boundary, no credentials entered in prototype.

## Auxiliary pages requested

1. setup: 4 steps workstation/locations; devices & binding; operating rules; checks & completion. Back/next, unfinished local draft, connection-check mock and final saved-pending outcome; initial entry NOT maintenance routine. Original sample queues64/timeout1500ms/capacity10000/history200 labeled examples. Scope existing identity read-only; new station entry separate installed identity source, not arbitrary editable primary key.
2. install: 4 steps directories/runtime account; media and components; review plan; install result. Simulation only. Old image only first screen: other steps explicitly design proposals. No real install.
3. backup: tabs config template/change history/full backup-recovery. Import preview object & differences, secret missing, save pending only. Show old schema weight/image preservation in compatibility view if useful; not Sportmaster runtime toggles.
4. support: offline guide search and diagnostic package preview; time range with timezone Asia/Shanghai +08:00 displayed; preserve screenshot max7 days; old text says fixed5min but its semantics are unknown, do not assume validity or range. no data sent. mock export local JSON can delegate `downloadText(name,text,type)` root helper.
5. advanced: version / raw config / old schema migration; technical keys in on-demand area. Only current source known visible keys may appear in legacy/read-only section, no unknown fields invented. Put installed metadata elsewhere if root modules covers.

Auxiliary controls must actually perform documented mock workflow or clearly be disabled with reason; no decorative dead buttons. Preserve inputs during errors/navigation. `renderAux` will be wrapped by root chrome/main scroll container, do not add html/head/body, page header/chrome. Use fonts/tokens from root. Provide 5-8 screenshot scenarios via own function `setAuxScenario(name)` only if needed, root can set S.aux state directly.
