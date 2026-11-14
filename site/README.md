# Sitio del proyecto

Estático, sin dependencias en el navegador y con las fuentes alojadas aquí (licencias en `fonts/`). Se genera con Python estándar:

    python3 site/build.py                     # escribe site/dist
    python3 -m http.server -d site/dist 8765  # http://127.0.0.1:8765

Español en la raíz e inglés en `/en/`. Tema del sistema con interruptor claro/oscuro, `prefers-reduced-motion` respetado y política de seguridad de contenido estricta (nada de recursos de terceros).

## Dónde editar

| Qué | Dónde |
| :--- | :--- |
| Textos, preguntas, docs (ES y EN) | `content/data.json` |
| Aspecto | `css/style.css` |
| Comportamiento (tema, copiar, animaciones) | `js/main.js`, `js/theme.js` |
| Plantillas de página | `build.py` |
| Capturas del juego | `img/` (ver `CAPTURAS.md`) |
| Favicon, ícono de Apple, imagen para redes | `favicon.svg`, `favicon-32.png`, `apple-touch-icon.png`, `og.png` (fuentes en `assets-src/`) |

## Interruptores (`config.json`)

| Clave | Efecto |
| :--- | :--- |
| `releases_ready` + `release_url` | Activa los botones «Descargar» (hoy dicen «pronto») y muestra los comandos `./aether add`. |
| `mod_urls` | Enlace de descarga por mod: `{"lens": "https://…"}`. |
| `repo_url` | Enlace al repositorio en el pie. |
| `lattice_url` | Muestra «Lattice ↗» en la barra y el pie. |
| `base_url` | Activa enlaces canónicos, `hreflang`, Open Graph y `sitemap.xml`. |
| `domain` | Escribe un archivo `CNAME` (solo lo usa GitHub Pages; en Cloudflare Pages el dominio se añade en el panel). |

## Pendiente antes de publicar

- Sustituir los marcadores por las capturas reales.
- La pregunta «¿Cuándo salen las descargas?» dice que el repositorio aún es privado: actualizarla cuando sea público.
- Poner los valores reales en `config.json` (releases, repo, dominio).
- Los zips por mod se llaman `aether-<mod>-<versión>.zip` (los genera `scripts/package.sh`); la versión sale de `config.json`.

## Despliegue

Cloudflare (decidido el 5-nov-2026), con *Workers & Pages → Create → Connect to Git*. El archivo `wrangler.jsonc` de la raíz le dice a Cloudflare qué carpeta publicar (`site/dist`); sin él, `wrangler deploy` falla con «Could not detect a directory containing static files».

| Ajuste en Cloudflare | Valor |
| :--- | :--- |
| Build command | `python3 site/build.py` |
| Deploy command | `npx wrangler deploy` (el valor por defecto) |
| Root directory | (vacío: la raíz del repositorio) |
| Nombre del proyecto | `aether-loader` (debe coincidir con `name` en `wrangler.jsonc`) |

El generador escribe `_headers` (CSP, nosniff, referrer, caché de fuentes) y `404.html`; Cloudflare los aplica solos. Cada push a `main` publica. Para probar la configuración sin subir nada: `python3 site/build.py && npx wrangler deploy --dry-run`.
Dominio propio: *Settings → Domains & Routes → Add custom domain*.

## Indexación
`config.json` → `indexable` (por defecto `false`): con `false` cada página lleva `<meta name="robots" content="noindex,nofollow">`, `robots.txt` dice `Disallow: /` y no se genera `sitemap.xml`. Ponlo en `true` al lanzar (con capturas reales y descargas).
