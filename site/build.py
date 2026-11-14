#!/usr/bin/env python3
"""Genera el sitio estático de Aether en site/dist. Solo biblioteca estándar.

    python3 site/build.py            # ES en la raíz, EN bajo /en/
    python3 -m http.server -d site/dist 8765

Contenido: site/content/data.json (textos ES/EN). Opciones: site/config.json.
Capturas: soltar site/img/<nombre>.jpg (ver site/CAPTURAS.md); si no existe, se muestra un marcador.
"""
import json, os, re, shutil
import legal

ROOT = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(ROOT, 'dist')
cfg = json.load(open(os.path.join(ROOT, 'config.json'), encoding='utf-8'))
D = json.load(open(os.path.join(ROOT, 'content', 'data.json'), encoding='utf-8'))
LANGS = ['es', 'en']
BASE = (cfg.get('download_base') or '').rstrip('/')
READY = bool(cfg.get('releases_ready') and (BASE or cfg.get('release_url')))
VER = cfg.get('version', '')
def dl_url(name): return (BASE + '/' + name) if BASE else cfg.get('release_url')
def loader_url(rid, full=False): return dl_url('aether-%s-%s%s.zip' % (VER, rid, '-full' if full else ''))
def size_of(rid, full):
    v = cfg.get('sizes', {}).get('%s-%s' % (rid, 'full' if full else 'light'))
    return ' (%s)' % v if v else ''
def mod_url(key_name):
    # With "mod_dll" the official mods are offered as the bare .dll (drag and drop into Aether/Mods); the zip stays in the release too.
    if cfg.get('mod_dll'): return dl_url('Aether.Mods.%s.dll' % key_name)
    return cfg.get('mod_urls', {}).get(key_name.lower()) or dl_url('aether-%s-%s.zip' % (key_name.lower(), cfg.get('mod_version', VER)))

DL = {
 'es': dict(win='Windows', lin='Linux', light='Descargar', full='Versión completa', net_h='¿Cuál descargo?', net_p='La versión normal es pequeña y necesita el runtime gratuito de <b>.NET 9</b> (<a href="https://dotnet.microsoft.com/download/dotnet/9.0" rel="noopener">descárgalo desde la página oficial de Microsoft</a>, elige «Runtime»). Si prefieres no instalar nada, la <b>versión completa</b> lo trae todo, pero pesa más. En Windows, si falta .NET, el programa te lleva a descargarlo.', sums_note='Opcional, para usuarios avanzados: sumas de verificación (SHA-256) de los archivos:', note_w='Windows 10/11: descomprime y abre <code>aether-installer.exe</code>.',
   light_l='Versión ligera (si ya tienes .NET 9)', note_l='Linux: descomprime y abre <code>aether-installer</code>. La versión ligera solo abre si ya tienes .NET 9; sin él no muestra ningún aviso, por eso aquí recomendamos la completa.', mods_h='2 · Mods oficiales',
   mods_p='Cada mod es un zip pequeño que trae <b>Aether Nano</b>, su única dependencia. Funciona con Aether (añádelo desde Aether Monitor, botón «Añadir mod…») o solo con BepInEx: copia las dos DLL del zip a <code>BepInEx/plugins</code>.',
   needs='Trae Aether Nano', needs_long='El zip trae Aether Nano: funciona con Aether o solo con BepInEx.', get='Descargar', open_h='3 · Abre el juego'),
 'en': dict(win='Windows', lin='Linux', light='Download', full='Full version', net_h='Which one do I download?', net_p='The regular version is small and needs the free <b>.NET 9</b> runtime (<a href="https://dotnet.microsoft.com/download/dotnet/9.0" rel="noopener">get it from Microsoft’s official page</a>, pick “Runtime”). If you would rather install nothing, the <b>full version</b> carries everything but is larger. On Windows, if .NET is missing, the program points you to it.', sums_note='Optional, for advanced users: checksums (SHA-256) of the files:', note_w='Windows 10/11: unzip and open <code>aether-installer.exe</code>.',
   light_l='Light version (if you have .NET 9)', note_l='Linux: unzip and open <code>aether-installer</code>. The light version only opens if .NET 9 is installed; without it nothing shows up, so here we recommend the full one.', mods_h='2 · Official mods',
   mods_p='Each mod is a small zip that carries <b>Aether Nano</b>, its only dependency. It runs with Aether (add it from Aether Monitor, “Add mod…” button) or with BepInEx alone: copy both DLLs from the zip into <code>BepInEx/plugins</code>.',
   needs='Carries Aether Nano', needs_long='The zip carries Aether Nano: it runs with Aether or with BepInEx alone.', get='Download', open_h='3 · Open the game'),
}

X = {
 'es': dict(skip='Saltar al contenido', theme='Cambiar tema claro u oscuro', other='EN', other_name='English', copy='copiar',
   genkeys='Teclas de Aether en general: <span class="kc">F10</span> menú de mods · <span class="kc">F7</span> gestor de teclas · <span class="kc">F9</span> editor de HUD. Todas se cambian en F7.',
   load='cargador listo · mods elegidos: {n} de {t} (ejemplo)', shot='captura del juego', repo='Repositorio', docs_h='Docs', ldesc='Cargador de mods para Block Story',
   nf_h='Página no encontrada', nf_p='Esa dirección no existe. Prueba desde el inicio.', nf_b='Ir al inicio', more='Ver novedades',
   desc_home='Aether es un cargador de mods para Block Story. Instala el cargador y elige solo los mods que quieras.',
   desc_show='Los cuatro mods oficiales de Aether: Lens, Wayfinder, Blueprint y Dusk, con sus teclas.',
   desc_docs='Cómo crear un mod para Block Story con Aether, desde cero y sin experiencia previa.',
   desc_dl='Descarga Aether, el cargador de mods para Block Story.', desc_faq='Preguntas frecuentes sobre Aether.',
   desc_nov='Novedades y versiones de Aether.', prev='Anterior', next='Siguiente'),
 'en': dict(skip='Skip to content', theme='Toggle light or dark theme', other='ES', other_name='Español', copy='copy',
   genkeys='Aether keys in general: <span class="kc">F10</span> mod menu · <span class="kc">F7</span> keybind manager · <span class="kc">F9</span> HUD editor. All can be changed in F7.',
   load='loader ready · chosen mods: {n} of {t} (example)', shot='in-game screenshot', repo='Repository', docs_h='Docs', ldesc='A mod loader for Block Story',
   nf_h='Page not found', nf_p='That address does not exist. Try from the start.', nf_b='Go home', more='See news',
   desc_home='Aether is a mod loader for Block Story. Install the loader and pick only the mods you want.',
   desc_show='The four official Aether mods: Lens, Wayfinder, Blueprint and Dusk, with their keys.',
   desc_docs='How to create a Block Story mod with Aether, from scratch and with no prior experience.',
   desc_dl='Download Aether, the mod loader for Block Story.', desc_faq='Frequently asked questions about Aether.',
   desc_nov='News and releases of Aether.', prev='Previous', next='Next'),
}
ROUTES = {'inicio': '', 'showcase': 'showcase', 'docs': 'docs', 'dl': 'descargar', 'faq': 'preguntas', 'nov': 'novedades', 'terms': 'terminos', 'privacy': 'privacidad'}
LEGAL_NAMES = {'es': {'terms': 'Términos de uso', 'privacy': 'Privacidad'}, 'en': {'terms': 'Terms of use', 'privacy': 'Privacy'}}

# ---------- sprite (logo y signos) ----------
def pts(a): return ' '.join('%s,%s' % (x, y) for x, y in a)
TOP = [(50, 24), (86, 44), (50, 62), (14, 44)]; LEFT = [(14, 44), (50, 62), (50, 102), (14, 82)]; RIGHT = [(50, 62), (86, 44), (86, 82), (50, 102)]
LID = [(x, y - 16) for x, y in TOP]
def poly(p, cls, extra=''): return '<polygon points="%s" class="%s"%s/>' % (pts(p), cls, extra)
SPRITE = ('<svg xmlns="http://www.w3.org/2000/svg" class="sprite" width="0" height="0" aria-hidden="true" focusable="false"><defs>'
  '<radialGradient id="g-top" cx="50%" cy="60%" r="70%"><stop offset="0" stop-color="#FFD27A"/><stop offset=".55" stop-color="#FF7A1A"/><stop offset="1" stop-color="#B53A06"/></radialGradient></defs>'
  '<symbol id="i-cube" viewBox="0 0 100 112">' + poly(TOP, 'i-top') + poly(LEFT, 'i-fl i-ink') + poly(RIGHT, 'i-fr i-ink') + poly(LID, 'i-lid i-ink')
  + '<circle cx="30" cy="14" r="1.6" class="i-sp"/><circle cx="70" cy="10" r="1.3" class="i-sp"/><circle cx="52" cy="2" r="1.1" class="i-sp"/></symbol>'
  '<symbol id="s-lens" viewBox="0 0 100 112">' + poly(TOP, 's-top i-ink') + poly(LEFT, 'i-fl i-ink') + poly(RIGHT, 'i-fr i-ink') + '<circle cx="68" cy="72" r="11" fill="none" stroke="#38B6E0" stroke-width="3.4"/><circle cx="68" cy="72" r="3.6" fill="#38B6E0"/></symbol>'
  '<symbol id="s-way" viewBox="0 0 100 112"><polygon points="%s" fill="#6BBF55" class="i-ink"/>' % pts(TOP) + poly(LEFT, 'i-fl i-ink') + poly(RIGHT, 'i-fr i-ink') + '<path d="M50 34 L56 46 L50 52 L44 46 Z" class="i-lid"/></symbol>'
  '<symbol id="s-blue" viewBox="0 0 100 112"><polygon points="%s" fill="none" stroke="#E8A93C" stroke-width="2.6" stroke-dasharray="5 4" stroke-linejoin="round"/>' % pts([(50, 24), (86, 44), (86, 82), (50, 102), (14, 82), (14, 44)])
  + ''.join('<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="#E8A93C" stroke-width="2.6" stroke-dasharray="5 4"/>' % (a[0], a[1], b[0], b[1]) for a, b in [((14, 44), (50, 62)), ((50, 62), (86, 44)), ((50, 62), (50, 102))]) + '</symbol>'
  '<symbol id="s-dusk" viewBox="0 0 100 112">' + poly(TOP, 's-top i-ink') + poly(LEFT, 's-dk i-ink') + '<polygon points="%s" fill="#E8643C" class="i-ink"/></symbol></svg>' % pts(RIGHT))
def ico(i, cls='', w=None): return '<svg class="%s" aria-hidden="true" focusable="false"%s><use href="#%s"/></svg>' % (cls, '' if not w else ' width="%s"' % w, i)
MODS = [('lens', 's-lens'), ('way', 's-way'), ('blue', 's-blue'), ('dusk', 's-dusk')]  # orden del contenido: MN
def mod_names(): return {k: name for k, name in D['MN']}

def has_img(name): return os.path.exists(os.path.join(ROOT, 'img', name + '.jpg'))

class Page:
    def __init__(self, lang, route):
        self.lang, self.route = lang, route
        parts = ([lang] if lang != 'es' else []) + ([route] if route else [])
        self.parts = '/'.join(parts).split('/') if parts else []
        self.depth = len(self.parts); self.prefix = '../' * self.depth if self.depth else './'
        self.t, self.n, self.x = D['T'][lang], D['N'][lang], X[lang]
    def href(self, route, lang=None):
        lang = lang or self.lang
        parts = ([lang] if lang != 'es' else []) + ([route] if route else [])
        p = '/'.join(parts)
        return self.prefix + (p + '/' if p else '')
    def asset(self, p): return self.prefix + p
    def shot(self, name, alt):
        if has_img(name): return '<div class="shot"><img src="%s" alt="%s" loading="lazy" decoding="async"></div>' % (self.asset('img/%s.jpg' % name), alt)
        return '<div class="shot" role="img" aria-label="%s"><span>%s</span></div>' % (alt, self.x['shot'])
    def dlbtn(self, soon, ready, cls='', url=None):
        u = url or (self.href(ROUTES['dl']) if READY else None)
        if READY and u: return '<a class="btn %s" href="%s" rel="noopener">%s</a>' % (cls, u, ready)
        return '<span class="btn off %s" aria-disabled="true">%s</span>' % (cls, soon)

def head(p, title, desc, route_key):
    t = []
    t.append('<!doctype html>\n<html lang="%s">\n<head>\n<meta charset="utf-8">\n<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">' % p.lang)
    t.append('<meta http-equiv="Content-Security-Policy" content="default-src \'self\'; img-src \'self\' data:; font-src \'self\'; style-src \'self\'; script-src \'self\'; connect-src \'none\'; object-src \'none\'; base-uri \'none\'; form-action \'none\'">')
    if not cfg.get('indexable'): t.append('<meta name="robots" content="noindex,nofollow">')
    t.append('<title>%s</title>\n<meta name="description" content="%s">' % (title, desc))
    t.append('<meta name="theme-color" content="#FBFAF8" media="(prefers-color-scheme: light)"><meta name="theme-color" content="#0C0A08" media="(prefers-color-scheme: dark)">')
    t.append('<link rel="icon" href="%s" type="image/svg+xml"><link rel="icon" href="%s" sizes="32x32" type="image/png"><link rel="apple-touch-icon" href="%s">' % (p.asset('favicon.svg'), p.asset('favicon-32.png'), p.asset('apple-touch-icon.png')))
    base = cfg.get('base_url')
    if base:
        base = base.rstrip('/')
        for l in LANGS:
            u = base + '/' + '/'.join(([l] if l != 'es' else []) + ([route_key] if route_key else []))
            u = u.rstrip('/') + '/'
            t.append('<link rel="alternate" hreflang="%s" href="%s">' % (l, u))
            if l == p.lang: t.append('<link rel="canonical" href="%s"><meta property="og:url" content="%s">' % (u, u))
        t.append('<meta property="og:type" content="website"><meta property="og:title" content="%s"><meta property="og:description" content="%s"><meta property="og:image" content="%s/og.png"><meta name="twitter:card" content="summary_large_image">' % (title, desc, base))
    t.append('<link rel="stylesheet" href="%s">\n<script src="%s"></script>\n</head>' % (p.asset('css/style.css'), p.asset('js/theme.js')))
    return '\n'.join(t)

MENU_T = {
 'es': {'menu': 'Menú', 'explore': 'Explorar', 'project': 'Proyecto', 'legal': 'Legal', 'prefs': 'Preferencias', 'repo': 'Repositorio', 'releases': 'Versiones', 'changelog': 'Registro de cambios', 'issues': 'Reportar un problema',
        'license': 'Licencia Apache-2.0', 'third': 'Avisos de terceros', 'security': 'Seguridad', 'contact': 'Contacto', 'main': 'Principal', 'close': 'Cerrar menú'},
 'en': {'menu': 'Menu', 'explore': 'Explore', 'project': 'Project', 'legal': 'Legal', 'prefs': 'Preferences', 'repo': 'Repository', 'releases': 'Releases', 'changelog': 'Changelog', 'issues': 'Report an issue',
        'license': 'Apache-2.0 license', 'third': 'Third-party notices', 'security': 'Security', 'contact': 'Contact', 'main': 'Main', 'close': 'Close menu'},
}
CONTACT = 'hello@ultimaibrahim.dev'

def link_groups(p):
    """The same links feed the dropdown menu and the footer, so they never drift apart."""
    n, T = p.n, MENU_T[p.lang]
    names = ['showcase', 'faq', 'nov', 'docs']
    labels = [n['nav'][0], n['nav'][2], n['nav'][3], n['nav'][4]]
    explore = [(l, p.href(ROUTES[k]), k, False) for l, k in zip(labels, names)]
    explore.insert(0, (n['dl'], p.href(ROUTES['dl']), 'dl', False))
    if cfg.get('lattice_url'): explore.append(('Lattice', cfg['lattice_url'], None, True))
    repo = cfg.get('repo_url', '').rstrip('/')
    project = []
    if repo:
        project = [(T['repo'], repo, None, True), (T['releases'], repo + '/releases', None, True), (T['changelog'], repo + '/blob/main/CHANGELOG.md', None, True), (T['issues'], repo + '/issues', None, True)]
    legal = [(LEGAL_NAMES[p.lang]['terms'], p.href(ROUTES['terms']), 'terms', False), (LEGAL_NAMES[p.lang]['privacy'], p.href(ROUTES['privacy']), 'privacy', False)]
    if repo:
        legal += [(T['license'], repo + '/blob/main/LICENSE', None, True), (T['third'], repo + '/blob/main/THIRD_PARTY_NOTICES.md', None, True), (T['security'], repo + '/blob/main/SECURITY.md', None, True)]
    return [(T['explore'], explore), (T['project'], project), (T['legal'], legal)]

def top(p, active):
    n, x, T = p.n, p.x, MENU_T[p.lang]
    h = ['<a class="skip" href="#main">%s</a>' % x['skip'], '<header class="top"><a class="brand" href="%s">%s<span>aether</span></a>' % (p.href(''), ico('i-cube'))]
    h.append(p.dlbtn(n['dl'], n['dl'], url=None) if READY else '<a class="btn" href="%s">%s</a>' % (p.href(ROUTES['dl']), n['dl']))
    h.append('<details class="menu" data-menu><summary class="tool" aria-label="%s">%s <span aria-hidden="true">▾</span></summary><div class="mpanel"><nav aria-label="%s">' % (T['menu'], T['menu'], T['main']))
    for title, items in link_groups(p):
        if not items: continue
        h.append('<h4>%s</h4><ul>' % title)
        for label, href, key, ext in items:
            cur = ' aria-current="page"' if key and active == key else ''
            h.append('<li><a href="%s"%s%s>%s%s</a></li>' % (href, ' rel="noopener"' if ext else '', cur, label, ' ↗' if ext else ''))
        h.append('</ul>')
    ol = 'en' if p.lang == 'es' else 'es'
    h.append('<h4>%s</h4><div class="mprefs"><a class="tool" href="%s" hreflang="%s" lang="%s" title="%s">%s</a><button type="button" class="tool" data-theme-toggle aria-label="%s">◐</button></div>' % (T['prefs'], p.href(p.route, ol), ol, ol, x['other_name'], x['other'], x['theme']))
    h.append('</nav></div></details></header>')
    return ''.join(h)

def foot(p):
    n, x, T = p.n, p.x, MENU_T[p.lang]
    cols = []
    for title, items in link_groups(p):
        if not items: continue
        lis = ''.join('<li><a href="%s"%s>%s%s</a></li>' % (href, ' rel="noopener"' if ext else '', label, ' ↗' if ext else '') for label, href, key, ext in items)
        cols.append('<div><h4>%s</h4><ul>%s</ul></div>' % (title, lis))
    return ('<footer><div class="fcols"><div><div class="fbrand">%s<span>aether</span></div><p>%s</p><p><a href="mailto:%s">%s</a></p></div>%s</div><div class="flow"><span>%s</span><span>%s</span></div></footer>'
            % (ico('i-cube'), n['fdesc'], CONTACT, CONTACT, ''.join(cols), n['fnote'], n['fsign']))

def shell(p, title, desc, active, body, route_key=None):
    rk = p.route if route_key is None else route_key
    return head(p, title, desc, rk) + '\n<body>\n' + SPRITE + '\n<div class="wrap">' + top(p, active) + '</div>\n' + body.replace('@@FOOT@@', '<div class="wrap">' + foot(p) + '</div>') + '\n<script src="%s" defer></script>\n</body>\n</html>\n' % p.asset('js/main.js')

def write(p, html):
    d = os.path.join(DIST, *p.parts) if p.parts else DIST
    os.makedirs(d, exist_ok=True)
    open(os.path.join(d, 'index.html'), 'w', encoding='utf-8').write(html)

def short(p, k): return p.t[k].split(':')[0].split('.')[0] + '.'

def page_home(p):
    t, n, x = p.t, p.n, p.x
    signs = ''.join('<div class="it">%s<h3>%s</h3><p>%s</p><span class="tag">%s</span></div>' % (ico(sym), mod_names()[k], short(p, k), n['opc']) for k, sym in MODS)
    b = ['<main id="main"><div class="hero"><canvas aria-hidden="true"></canvas><div class="in">%s<h1>aether</h1><p>%s</p>%s<div class="meta">%s</div></div></div>' % (ico('i-cube', 'logo'), t['tag'], p.dlbtn(t['dlsoon'], t['dl'], 'cta'), t['meta'])]
    b.append('<div class="wrap"><div class="col"><section class="first"><h2>%s</h2><p class="lede">%s</p><div class="signs" data-load="%s">%s</div><div class="trk"><i></i></div><div class="trkt" aria-live="off"></div></section>' % (n['carga'], n['cargap'], x['load'], signs))
    b.append('<section><h2>%s</h2><p class="lede">%s</p><div class="shots">%s%s%s</div><p class="more"><a href="%s">%s →</a></p></section>' % (t['showc'], t['showp'], p.shot('showcase-lens', 'Lens'), p.shot('showcase-wayfinder', 'Wayfinder'), p.shot('showcase-blueprint', 'Blueprint'), p.href(ROUTES['showcase']), n['nav'][0]))
    b.append('<section><h2>%s</h2><ol class="steps"><li><span>%s</span></li><li><span>%s</span></li><li><span>%s</span></li></ol></section>' % (n['instp'], n['i1'], n['i2'], n['i3']))
    b.append('<section id="estado"><h2>%s</h2><p class="lede">%s</p></section>' % (t['est'], t['estp']))
    b.append('<section><h2>%s</h2><p class="lede">%s</p><p class="more"><a href="%s">%s →</a></p></section></div></div></main>@@FOOT@@' % (t['nov'], t['novp'], p.href(ROUTES['nov']), x['more']))
    return shell(p, 'aether · ' + t['tag'], x['desc_home'], '', ''.join(b))

def page_showcase(p):
    t, n, x = p.t, p.n, p.x
    names = mod_names(); keys = D['KM']
    b = ['<main id="main"><div class="wrap"><div class="col"><section class="first"><h1>%s</h1><p class="lede">%s</p>' % (n['showh'], n['showp'])]
    for k, sym in MODS:
        kl = ''.join('<div><span class="kc">%s</span><span>%s</span></div>' % (a, b_) for a, b_ in keys[k])
        cmd = ('<p class="need">%s</p>' % DL[p.lang]['needs_long']) if READY else ''
        b.append('<div class="mod"><div class="hd">%s<div><h2>%s</h2><p>%s</p>%s</div>%s</div><div class="sk">%s<details class="kd" open><summary>%s (%d)</summary><div class="kl">%s</div></details></div></div>'
                 % (ico(sym), names[k], t[k], cmd, p.dlbtn(n['mdlsoon'], n['mdl'], 'sm', mod_url(names[k]) if READY else None), p.shot('showcase-' + names[k].lower(), names[k]), n['keys'], len(keys[k]), kl))
    b.append('<p class="genkeys">%s</p></section></div></div></main>@@FOOT@@' % x['genkeys'])
    return shell(p, n['showh'] + ' · aether', x['desc_show'], 'showcase', ''.join(b))

def page_dl(p):
    n, x = p.n, p.x
    b = '<main id="main"><div class="wrap"><div class="col"><section class="first"><h1>%s</h1>' % n['dlh']
    if READY:
        d = DL[p.lang]
        osrows = ''
        # Linux leads with the full package: without .NET 9 the light one does nothing visible when opened (no dialog, unlike Windows).
        for rid, label, note, full_first in (('win-x64', d['win'], d['note_w'], False), ('linux-x64', d['lin'], d['note_l'], True)):
            first, second = ((True, d['light']), (False, d['light_l'])) if full_first else ((False, d['light']), (True, d['full']))
            osrows += '<div class="dlos"><strong>%s</strong><div class="dlrow"><a class="btn" href="%s" download>%s%s</a><a class="btn o" href="%s" download>%s%s</a></div><p>%s</p></div>' % (
                label, loader_url(rid, first[0]), first[1], size_of(rid, first[0]), loader_url(rid, second[0]), second[1], size_of(rid, second[0]), note)
        b += '<div class="card"><h3>%s</h3><p>%s</p>%s<p>%s</p></div>' % (n['dl1'], n['dl1p'], osrows, d['net_p'])
        rows = ''.join('<div class="dlmod">%s<div><strong>%s</strong> <span class="tag">%s</span><br><span>%s</span></div><a class="btn o sm" href="%s" download>%s</a></div>'
                       % (ico(sym), mod_names()[k], d['needs'], short(p, k), mod_url(mod_names()[k]), d['get']) for k, sym in MODS)
        b += '<div class="card"><h3>%s</h3><p>%s</p>%s</div>' % (d['mods_h'], d['mods_p'], rows)
    else:
        b += '<div class="card"><h3>%s</h3><p>%s</p>%s</div>' % (n['dl1'], n['dl1p'], p.dlbtn(n['dl1b'], n['dl'], 'sm'))
        b += '<div class="card"><h3>%s</h3><p>%s</p><a class="btn o sm" href="%s">%s</a></div>' % (n['dl2'], n['dl2p'], p.href(ROUTES['showcase']), n['dl2b'])
    b += '<div class="card"><h3>%s</h3><p>%s</p></div>' % (n['dl3'], n['dl3p'])
    if READY: b += '<p class="fine">%s <a href="%s">SHA256SUMS.txt</a></p>' % (DL[p.lang]['sums_note'], dl_url('SHA256SUMS.txt'))
    b += '</section></div></div></main>@@FOOT@@'
    return shell(p, n['dlh'] + ' · aether', x['desc_dl'], '', b)

def page_faq(p):
    t, n, x = p.t, p.n, p.x
    f = list(t['faq']); f.insert(4, n['faqx'])
    qs = ''.join('<details class="q"%s><summary>%s</summary><p>%s</p></details>' % (' open' if i == 0 else '', q[0], q[1]) for i, q in enumerate(f))
    b = '<main id="main"><div class="wrap"><div class="col"><section class="first"><h1>%s</h1>%s</section>' % (t['faqh'], qs)
    b += '<section><div class="how"><h3>%s</h3><ol><li><span>%s</span></li><li><span>%s</span></li><li><span>%s</span></li></ol><div class="warn">%s</div></div></section></div></div></main>@@FOOT@@' % (n['how'], n['h1'], n['h2'], n['h3'], n['warn'])
    return shell(p, t['faqh'] + ' · aether', x['desc_faq'], 'faq', b)

def page_nov(p):
    t, x = p.t, p.x
    items = ''.join('<div class="news"><h3>%s</h3><p>%s</p></div>' % (a, b_) for a, b_ in t['novlist'])
    b = '<main id="main"><div class="wrap"><div class="col"><section class="first"><h1>%s</h1>%s</section></div></div></main>@@FOOT@@' % (t['novh'], items)
    return shell(p, t['novh'] + ' · aether', x['desc_nov'], 'nov', b)

def doc_slugs(lang):
    g = D['DOCS'][lang]['g']
    return [(grp[0], [(k, label) for k, label in grp[1]]) for grp in g]

def page_doc(p, key):
    lang = p.lang; DOC = D['DOCS'][lang]; groups = doc_slugs(lang); flat = [k for _, items in groups for k, _ in items]
    route = lambda k: 'docs' if k == 'intro' else 'docs/' + k
    nav = ['<nav class="dn" aria-label="Docs">']
    for gname, items in groups:
        nav.append('<h4>%s</h4>' % gname)
        for k, label in items: nav.append('<a href="%s"%s>%s</a>' % (p.href(route(k)), ' aria-current="page"' if k == key else '', label))
    nav.append('</nav>')
    title, html, _src = DOC[key]
    html = re.sub(r'<table>', '<div class="tablewrap"><table>', html); html = re.sub(r'</table>', '</table></div>', html)
    html = html.replace('<pre class="code">', '<pre class="code" data-copy="%s">' % p.x['copy'])
    i = flat.index(key); label = dict(kv for _, items in groups for kv in items)
    pn = '<div class="pn"><span>%s</span><span>%s</span></div>' % (
        ('<a href="%s">← %s</a>' % (p.href(route(flat[i - 1])), label[flat[i - 1]])) if i else '',
        ('<a href="%s">%s →</a>' % (p.href(route(flat[i + 1])), label[flat[i + 1]])) if i < len(flat) - 1 else '')
    b = '<main id="main"><div class="wrap"><div class="docs">%s<article class="db"><h1>%s</h1>%s%s</article></div></div></main>@@FOOT@@' % (''.join(nav), title, html, pn)
    return shell(p, '%s · Docs · aether' % title, p.x['desc_docs'], 'docs', b, route(key))

def page_legal(p, key):
    table = legal.TERMS if key == 'terms' else legal.PRIVACY
    title, body = legal.render(table, p.lang)
    b = '<main id="main"><div class="wrap"><div class="col"><section class="first legal"><h1>%s</h1>%s</section></div></div></main>@@FOOT@@' % (title, body)
    return shell(p, title + ' · aether', title, '', b, ROUTES[key])

def page_404(lang):
    p = Page(lang, ''); x = X[lang]
    # 404: rutas absolutas (válido con dominio propio en la raíz)
    p.prefix = '/'; p.href = lambda route, l=None: '/' + (('en/' if (l or lang) == 'en' else '') + (route + '/' if route else '')) if (route or (l or lang) == 'en') else '/'
    body = '<main id="main"><div class="wrap"><div class="col"><section class="first"><h1>%s</h1><p class="lede">%s</p><p><a class="btn" href="/">%s</a></p></section></div></div></main>@@FOOT@@' % (x['nf_h'], x['nf_p'], x['nf_b'])
    return shell(p, x['nf_h'] + ' · aether', x['nf_h'], '', body, '')

def build():
    if os.path.exists(DIST): shutil.rmtree(DIST)
    os.makedirs(DIST)
    urls = []
    for lang in LANGS:
        for key, builder in [('', page_home), ('showcase', page_showcase), ('descargar', page_dl), ('preguntas', page_faq), ('novedades', page_nov)]:
            p = Page(lang, key); write(p, builder(p)); urls.append((lang, key))
        for key in ('terms', 'privacy'):
            p = Page(lang, ROUTES[key]); write(p, page_legal(p, key)); urls.append((lang, ROUTES[key]))
        for k in [k for _, items in doc_slugs(lang) for k, _ in items]:
            r = 'docs' if k == 'intro' else 'docs/' + k
            p = Page(lang, r); write(p, page_doc(p, k)); urls.append((lang, r))
    open(os.path.join(DIST, '404.html'), 'w', encoding='utf-8').write(page_404('es'))
    for d in ['css', 'js', 'fonts', 'img']:
        s = os.path.join(ROOT, d)
        if os.path.isdir(s): shutil.copytree(s, os.path.join(DIST, d), ignore=shutil.ignore_patterns('*.md', '.gitkeep'))
    for f in ['favicon.svg', 'favicon-32.png', 'apple-touch-icon.png', 'og.png']:
        if os.path.exists(os.path.join(ROOT, f)): shutil.copy(os.path.join(ROOT, f), DIST)
    base = (cfg.get('base_url') or '').rstrip('/')
    indexable = bool(cfg.get('indexable'))
    open(os.path.join(DIST, 'robots.txt'), 'w').write('User-agent: *\n' + ('Allow: /\n' + ('Sitemap: %s/sitemap.xml\n' % base if base else '') if indexable else 'Disallow: /\n'))
    if base and indexable:
        loc = ''.join('<url><loc>%s/%s</loc></url>' % (base, '/'.join(([l] if l != 'es' else []) + ([r] if r else [])) + ('/' if (l != 'es' or r) else '')) for l, r in urls)
        open(os.path.join(DIST, 'sitemap.xml'), 'w').write('<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">%s</urlset>' % loc)
    if cfg.get('domain'): open(os.path.join(DIST, 'CNAME'), 'w').write(cfg['domain'] + '\n')
    # Cabeceras HTTP para Cloudflare Pages (otros servidores estáticos las ignoran; la política del <meta> sigue vigente).
    open(os.path.join(DIST, '_headers'), 'w').write(
        "/*\n  Content-Security-Policy: default-src 'self'; img-src 'self' data:; font-src 'self'; style-src 'self'; script-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\n"
        "  X-Content-Type-Options: nosniff\n  Referrer-Policy: strict-origin-when-cross-origin\n  Permissions-Policy: camera=(), microphone=(), geolocation=()\n  Strict-Transport-Security: max-age=31536000\n"
        "/fonts/*\n  Cache-Control: public, max-age=31536000, immutable\n/css/*\n  Cache-Control: public, max-age=3600\n/js/*\n  Cache-Control: public, max-age=3600\n")
    print('páginas: %d (+404) → %s' % (len(urls), DIST))

if __name__ == '__main__':
    build()
