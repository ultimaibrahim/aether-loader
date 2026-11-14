"""Texto legal del sitio (términos de uso y aviso de privacidad), en español e inglés."""

UPDATED = {'es': 'Última actualización: 7 de noviembre de 2026.', 'en': 'Last updated: 7 November 2026.'}
CONTACT = 'hello@ultimaibrahim.dev'

TERMS = {
 'es': ('Términos de uso', '''
<p class="lede">%(updated)s</p>
<h2>Beta pública</h2>
<p>Aether está en beta. Puede fallar, cambiar o dejar de mantenerse, y no ofrecemos garantías de disponibilidad, compatibilidad ni resultado mientras dure la beta.</p>
<h2>Qué es Aether</h2>
<p>Aether es un cargador de mods gratuito y de código abierto (licencia Apache-2.0) para Block Story, con un instalador, una ventana de monitor y herramientas para quien crea mods. Es un proyecto independiente de Ultima Ibrahim; <b>no es oficial ni está afiliado a Big Cube Interactive</b>, titular de Block Story. «Block Story» es marca de su titular y se nombra aquí solo para indicar compatibilidad.</p>
<h2>Qué necesitas</h2>
<p>Una copia legítima de Block Story y el cumplimiento de la licencia del juego. Debes tener al menos 13 años, o la edad mínima que exija la ley de tu país; si eres menor, usa Aether con permiso de tu madre, padre o tutor.</p>
<h2>Cómo funciona y qué instala</h2>
<ul>
<li>El instalador copia los archivos de Aether en la carpeta del juego y, si falta, descarga <b>BepInEx</b> (software de terceros, licencia LGPL) desde su publicación oficial en GitHub y verifica su huella SHA-256 antes de instalarlo.</li>
<li>Haces copia de seguridad de tus partidas antes de usar mods. Aether guarda copias de los archivos que reemplaza en <code>Aether/backup</code>, pero no podemos garantizar que un mod no dañe un mundo guardado.</li>
<li>Puedes quitar Aether cuando quieras borrando las carpetas <code>BepInEx/plugins/Aether</code> y <code>Aether</code> del juego.</li>
</ul>
<h2>Mods de terceros</h2>
<p>Los mods los crean terceros y se ejecutan con tus permisos dentro del juego. Los mods oficiales de Aether (Lens, Wayfinder, Blueprint y Dusk) y los del catálogo <a href="https://lattice.ultimaibrahim.dev" rel="noopener">Lattice</a> son información o comodidad, no una garantía de que sean seguros o libres de errores. Instala solo lo que confíes. Lattice tiene sus propios términos.</p>
<h2>Juego limpio</h2>
<p>Aether no incluye trampas. No uses Aether ni sus mods para obtener ventajas injustas, automatizar el juego, modificar la memoria en multijugador, ni para infringir las reglas de un servidor o la licencia del juego. Eres responsable de lo que instales y de cómo lo uses, incluidas las sanciones que un servidor pueda aplicar.</p>
<h2>Licencia del código y uso del nombre</h2>
<p>El código de Aether se publica bajo la licencia Apache-2.0 (consulta el archivo LICENSE del repositorio); esa licencia manda sobre el código. Estos términos se aplican a tu uso del instalador, la web y los servicios que consultan. No se te concede ningún derecho sobre las marcas de Block Story ni sobre arte, texturas o código del juego, que no distribuimos.</p>
<h2>Disponibilidad y responsabilidad</h2>
<p>Aether se ofrece «tal cual», sin garantías de ningún tipo. En la medida que la ley lo permite, no respondemos por daños derivados del uso de Aether o de mods de terceros (pérdida de partidas, fallos del juego, sanciones en servidores, etc.). Nada de esto limita derechos que la ley de tu país no permita limitar.</p>
<h2>Retiradas y avisos</h2>
<p>Podemos retirar descargas o contenido si lo exige un titular de derechos, por seguridad o por incumplir estas reglas. Para avisarnos de un problema de seguridad, de derechos de autor o de cualquier otro, escribe a %(contact)s. Las vulnerabilidades del propio juego deben reportarse en privado a Big Cube Interactive (ver SECURITY.md del repositorio).</p>
<h2>Cambios</h2>
<p>Podemos actualizar estos términos; la fecha de arriba indica la versión vigente. Seguir usando Aether después de un cambio importante implica aceptarlo.</p>
<h2>Ley aplicable</h2>
<p>Estos términos se rigen por las leyes de México. Si usas Aether como consumidor, conservas las protecciones imperativas de la ley de tu país de residencia.</p>
<h2>Contacto</h2>
<p>%(contact)s</p>
'''),
 'en': ('Terms of use', '''
<p class="lede">%(updated)s</p>
<h2>Public beta</h2>
<p>Aether is in beta. It may fail, change or stop being maintained, and we offer no guarantee of availability, compatibility or outcome during the beta.</p>
<h2>What Aether is</h2>
<p>Aether is a free, open-source (Apache-2.0 license) mod loader for Block Story, with an installer, a monitor window and tools for mod makers. It is an independent project by Ultima Ibrahim; <b>it is not official and is not affiliated with Big Cube Interactive</b>, the rights holder of Block Story. “Block Story” is its owner’s trademark and is used here only to state compatibility.</p>
<h2>What you need</h2>
<p>A legitimate copy of Block Story, and compliance with the game’s license. You must be at least 13, or the minimum age your country’s law requires; if you are a minor, use Aether with a parent or guardian’s permission.</p>
<h2>How it works and what it installs</h2>
<ul>
<li>The installer copies Aether’s files into the game folder and, if missing, downloads <b>BepInEx</b> (third-party software, LGPL) from its official GitHub release and checks its SHA-256 before installing it.</li>
<li>Back up your saves before using mods. Aether keeps copies of the files it replaces in <code>Aether/backup</code>, but we cannot guarantee that a mod will not damage a saved world.</li>
<li>You can remove Aether at any time by deleting the <code>BepInEx/plugins/Aether</code> and <code>Aether</code> folders in the game.</li>
</ul>
<h2>Third-party mods</h2>
<p>Mods are made by third parties and run with your permissions inside the game. Aether’s official mods (Lens, Wayfinder, Blueprint and Dusk) and those in the <a href="https://lattice.ultimaibrahim.dev" rel="noopener">Lattice</a> catalog are information or convenience, not a guarantee that they are safe or error-free. Install only what you trust. Lattice has its own terms.</p>
<h2>Fair play</h2>
<p>Aether includes no cheats. Do not use Aether or its mods to gain unfair advantages, automate the game, modify memory in multiplayer, or break a server’s rules or the game’s license. You are responsible for what you install and how you use it, including any penalty a server may apply.</p>
<h2>Code license and use of the name</h2>
<p>Aether’s code is published under the Apache-2.0 license (see the LICENSE file in the repository); that license governs the code. These terms apply to your use of the installer, the website and the services they query. You are granted no rights over Block Story’s trademarks or over game art, textures or code, which we do not distribute.</p>
<h2>Availability and liability</h2>
<p>Aether is provided “as is”, without warranties of any kind. To the extent the law allows, we are not liable for damage arising from the use of Aether or third-party mods (lost saves, game crashes, server penalties, etc.). Nothing here limits rights your country’s law does not allow to be limited.</p>
<h2>Takedowns and notices</h2>
<p>We may remove downloads or content if a rights holder requires it, for security reasons or for breaking these rules. To report a security, copyright or any other problem, write to %(contact)s. Vulnerabilities in the game itself must be reported privately to Big Cube Interactive (see SECURITY.md in the repository).</p>
<h2>Changes</h2>
<p>We may update these terms; the date above shows the current version. Continuing to use Aether after a significant change means accepting it.</p>
<h2>Governing law</h2>
<p>These terms are governed by the laws of Mexico. If you use Aether as a consumer, you keep the mandatory protections of the law of your country of residence.</p>
<h2>Contact</h2>
<p>%(contact)s</p>
'''),
}

PRIVACY = {
 'es': ('Aviso de privacidad', '''
<p class="lede">%(updated)s</p>
<p>Aether es operado por <b>Saul Ibrahim García Ochoa</b>, persona física, como responsable del tratamiento de datos personales (contacto al final).</p>
<h2>Lo esencial</h2>
<p><b>Aether no recopila datos personales.</b> No hay cuentas, ni telemetría, ni analítica, ni publicidad, ni cookies, ni perfiles. El instalador, el monitor y el cargador no envían nada tuyo a nuestros servidores: ni tu nombre, ni tu correo, ni tus partidas, ni tus registros.</p>
<h2>Qué hace Aether con la red</h2>
<ul>
<li><b>Instalador:</b> descarga BepInEx desde <code>github.com</code>. GitHub ve tu dirección IP al servir el archivo.</li>
<li><b>Aether Monitor</b> (solo si lo abres, y puedes apagarlo creando el archivo <code>Aether/no-update-check</code>): consulta <code>api.github.com</code> para saber si hay una versión nueva de Aether, y el catálogo firmado y la lista de revocaciones de <code>lattice.ultimaibrahim.dev</code> (alojado en Cloudflare). Las descargas de mods y de actualizaciones salen de las publicaciones en GitHub.</li>
<li><b>El juego</b> con Aether instalado no usa la red por Aether.</li>
</ul>
<p>Estos proveedores (GitHub y Cloudflare) pueden registrar tu dirección IP y datos técnicos de la petición según sus propias políticas, y pueden tratarlos en otros países, incluidos Estados Unidos. Nosotros no recibimos ni guardamos esos datos.</p>
<h2>Archivos que quedan en tu equipo</h2>
<p>Aether guarda en la carpeta del juego registros de sesión y reportes de fallos (<code>Aether/Logs</code>, <code>BepInEx/crash-reports</code>) y una copia verificada del catálogo (<code>Aether/lattice</code>). Se quedan en tu equipo y no se envían solos. Si compartes un registro para pedir ayuda, tú decides a quién y qué compartes: puede incluir nombres de carpetas de tu equipo.</p>
<h2>Este sitio web</h2>
<p>No usa cookies ni analítica. Guarda en tu navegador solo tu elección de tema y de idioma. Cloudflare aloja el sitio y, como todo proveedor, genera registros técnicos de las visitas. Los enlaces de descarga llevan a GitHub.</p>
<h2>Tus derechos</h2>
<p>Como no tratamos datos personales tuyos, no hay nada que acceder o borrar de nuestra parte. Si crees que sí lo hacemos, o quieres ejercer tus derechos (acceso, rectificación, cancelación, oposición, y los equivalentes del RGPD o de otras leyes), escribe a %(contact)s y responderemos dentro del plazo que marque la ley aplicable.</p>
<h2>Menores</h2>
<p>Aether no está dirigido a menores de 13 años y no recopila sus datos.</p>
<h2>Cambios</h2>
<p>Si este aviso cambia, actualizaremos la fecha de arriba.</p>
<h2>Contacto</h2>
<p>%(contact)s</p>
'''),
 'en': ('Privacy notice', '''
<p class="lede">%(updated)s</p>
<p>Aether is operated by <b>Saul Ibrahim García Ochoa</b>, an individual, as the controller of personal data (contact at the end).</p>
<h2>The short version</h2>
<p><b>Aether collects no personal data.</b> There are no accounts, telemetry, analytics, ads, cookies or profiles. The installer, the monitor and the loader send nothing of yours to our servers: not your name, email, saves or logs.</p>
<h2>What Aether does on the network</h2>
<ul>
<li><b>Installer:</b> downloads BepInEx from <code>github.com</code>. GitHub sees your IP address when serving the file.</li>
<li><b>Aether Monitor</b> (only when you open it, and you can turn it off by creating the file <code>Aether/no-update-check</code>): asks <code>api.github.com</code> whether a newer Aether exists, and fetches the signed catalog and revocation list from <code>lattice.ultimaibrahim.dev</code> (hosted on Cloudflare). Mod and update downloads come from GitHub releases.</li>
<li><b>The game</b> with Aether installed does not use the network because of Aether.</li>
</ul>
<p>These providers (GitHub and Cloudflare) may log your IP address and technical request data under their own policies, and may process it in other countries, including the United States. We neither receive nor store that data.</p>
<h2>Files that stay on your computer</h2>
<p>Aether keeps session logs and crash reports in the game folder (<code>Aether/Logs</code>, <code>BepInEx/crash-reports</code>) and a verified copy of the catalog (<code>Aether/lattice</code>). They stay on your computer and are not sent automatically. If you share a log to ask for help, you decide with whom and what: it may include folder names from your computer.</p>
<h2>This website</h2>
<p>It uses no cookies and no analytics. It stores in your browser only your theme and language choice. Cloudflare hosts the site and, like any provider, produces technical visit logs. Download links go to GitHub.</p>
<h2>Your rights</h2>
<p>Since we do not process personal data about you, there is nothing for us to access or delete. If you believe we do, or want to exercise your rights (access, rectification, erasure, objection, and the GDPR or other law equivalents), write to %(contact)s and we will answer within the time the applicable law sets.</p>
<h2>Children</h2>
<p>Aether is not directed at children under 13 and does not collect their data.</p>
<h2>Changes</h2>
<p>If this notice changes, we will update the date above.</p>
<h2>Contact</h2>
<p>%(contact)s</p>
'''),
}

def render(table, lang):
    title, body = table[lang]
    return title, body % {'updated': UPDATED[lang], 'contact': '<a href="mailto:%s">%s</a>' % (CONTACT, CONTACT)}
