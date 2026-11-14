(function(){
  var RM=window.matchMedia&&window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var root=document.documentElement;

  /* interruptor de tema: claro / oscuro (por defecto, el del sistema) */
  var tg=document.querySelector('[data-theme-toggle]');
  function isDark(){var t=root.getAttribute('data-theme');if(t)return t==='dark';return window.matchMedia&&window.matchMedia('(prefers-color-scheme: dark)').matches}
  if(tg)tg.addEventListener('click',function(){var n=isDark()?'light':'dark';root.setAttribute('data-theme',n);try{localStorage.setItem('aether-theme',n)}catch(e){}});

  /* menú: se cierra al elegir un enlace, con Escape o al pulsar fuera */
  var mn=document.querySelector('[data-menu]');
  if(mn){
    document.addEventListener('click',function(e){if(mn.open&&!mn.contains(e.target))mn.open=false});
    document.addEventListener('keydown',function(e){if(e.key==='Escape'&&mn.open){mn.open=false;var s=mn.querySelector('summary');if(s)s.focus()}});
    mn.addEventListener('click',function(e){var a=e.target.closest&&e.target.closest('a');if(a&&a.getAttribute('href')&&a.getAttribute('href').charAt(0)!=='#')setTimeout(function(){mn.open=false},0)});
  }

  /* botones de copiar en los bloques de código */
  [].forEach.call(document.querySelectorAll('pre.code'),function(pre){
    var b=document.createElement('button');b.type='button';b.className='copy';b.textContent=pre.getAttribute('data-copy')||'copy';
    b.addEventListener('click',function(){
      var txt=pre.innerText.replace(/\s*(copiar|copy)\s*$/i,'');
      function done(){var o=b.textContent;b.textContent='✓';setTimeout(function(){b.textContent=o},1200)}
      if(navigator.clipboard&&navigator.clipboard.writeText){navigator.clipboard.writeText(txt).then(done,function(){})}
    });
    pre.appendChild(b);
  });

  /* «Aether carga. Tú eliges.»: los signos se encienden en orden */
  var strip=document.querySelector('[data-load]');
  if(strip){
    var its=[].slice.call(strip.querySelectorAll('.it')),bar=document.querySelector('.trk i'),tx=document.querySelector('.trkt'),tpl=strip.getAttribute('data-load'),k=0,t0=null;
    function show(c){its.forEach(function(it,i){it.classList.toggle('on',i<c)});if(bar)bar.style.width=(c/its.length*100)+'%';if(tx)tx.textContent=tpl.replace('{n}',c).replace('{t}',its.length)}
    function tick(){var c=k%6;if(c>4)c=4;show(c);k++;t0=setTimeout(tick,850)}
    if(RM||!('IntersectionObserver' in window)){show(its.length)}
    else{var io=new IntersectionObserver(function(es){if(es[0].isIntersecting){if(!t0)tick()}else{clearTimeout(t0);t0=null}},{threshold:.3});io.observe(strip)}
  }

  /* hero: polvo de bloques que se desplaza con el cursor */
  var cv=document.querySelector('.hero canvas');
  if(cv&&cv.getContext){
    var cx=cv.getContext('2d'),W=0,H=0,dpr=Math.min(2,window.devicePixelRatio||1),P=[],ptr=null,vis=true,box=cv.parentNode;
    function size(){var r=cv.getBoundingClientRect();W=r.width;H=r.height;cv.width=W*dpr;cv.height=H*dpr;cx.setTransform(dpr,0,0,dpr,0,0)}
    function mk(init){return{x:Math.random()*W,y:init?Math.random()*H:H+30,r:Math.random()*14+8,v:Math.random()*.18+.06,s:Math.random()*6.28,a:Math.random()*.3+.15,z:Math.random()*.8+.2}}
    function css(n){return getComputedStyle(root).getPropertyValue(n).trim()||'#888'}
    size();for(var i=0;i<24;i++)P.push(mk(true));
    window.addEventListener('resize',size);
    box.addEventListener('pointermove',function(e){var r=box.getBoundingClientRect();ptr={x:(e.clientX-r.left)/r.width-.5,y:(e.clientY-r.top)/r.height-.5}});
    if('IntersectionObserver' in window)new IntersectionObserver(function(es){vis=es[0].isIntersecting;if(vis&&!RM)requestAnimationFrame(frame)}).observe(box);
    function frame(){
      if(!vis)return;
      cx.clearRect(0,0,W,H);var ox=ptr?ptr.x:0,oy=ptr?ptr.y:0,ink=css('--fg'),em=css('--ember');
      P.forEach(function(p,i){
        if(!RM){p.y-=p.v;p.s+=.004}if(p.y<-30)P[i]=mk(false);
        cx.save();cx.translate(p.x-ox*40*p.z,p.y-oy*30*p.z);cx.scale(1,.58);cx.rotate(Math.PI/4+Math.sin(p.s)*.25);
        cx.globalAlpha=p.a*.35;cx.fillStyle=em;cx.fillRect(-p.r/2,-p.r/2,p.r,p.r);
        cx.globalAlpha=p.a*.9;cx.strokeStyle=ink;cx.lineWidth=1.2;cx.strokeRect(-p.r/2,-p.r/2,p.r,p.r);cx.restore();
      });
      if(!RM)requestAnimationFrame(frame);
    }
    frame();
  }
})();
