Fuentes del favicon, el ícono de Apple y la imagen para redes. `icon.html` usa `var(--s)` como tamaño: sustitúyelo por el tamaño en px en una copia temporal y apunta `src` a la ruta absoluta de `favicon.svg`. Con Chromium:

    chromium --headless=new --no-sandbox --default-background-color=00000000 --window-size=32,32  --screenshot=site/favicon-32.png        <icon-de-32.html>
    chromium --headless=new --no-sandbox --window-size=180,180 --screenshot=site/apple-touch-icon.png <icon-de-180.html>
    chromium --headless=new --no-sandbox --window-size=1200,630 --screenshot=site/og.png site/assets-src/og.html

(Para el de 180 usa fondo `#FBFAF8` en lugar de transparente.)
