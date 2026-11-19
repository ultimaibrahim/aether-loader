(function(){
  var RM=window.matchMedia&&window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var root=document.documentElement;

  /* interruptor de tema: claro / oscuro (cubre botón desktop y cajón móvil) */
  function isDark(){var t=root.getAttribute('data-theme');if(t)return t==='dark';return window.matchMedia&&window.matchMedia('(prefers-color-scheme: dark)').matches}
  [].forEach.call(document.querySelectorAll('[data-theme-toggle]'),function(tg){
    tg.addEventListener('click',function(){var n=isDark()?'light':'dark';root.setAttribute('data-theme',n);try{localStorage.setItem('aether-theme',n)}catch(e){}});
  });

  /* índice de guías colapsable en móvil */
  var dnNav=document.querySelector('[data-dn]');
  var dnBtn=document.querySelector('[data-dn-toggle]');
  if(dnNav&&dnBtn){
    dnBtn.addEventListener('click',function(){
      var open=dnNav.classList.toggle('open');
      dnBtn.setAttribute('aria-expanded',open?'true':'false');
    });
  }

  /* submenús y menú móvil: solo uno abierto a la vez; cierra fuera, con Escape o al navegar */
  var dds=[].slice.call(document.querySelectorAll('[data-dd], [data-menu]'));
  if(dds.length){
    function closeOthers(except){dds.forEach(function(d){if(d!==except)d.open=false})}
    dds.forEach(function(d){
      d.addEventListener('toggle',function(){if(d.open)closeOthers(d)});
      d.addEventListener('click',function(e){
        var a=e.target.closest&&e.target.closest('a');
        if(a&&a.getAttribute('href')&&a.getAttribute('href').charAt(0)!=='#')setTimeout(function(){d.open=false},0);
      });
    });
    document.addEventListener('click',function(e){
      dds.forEach(function(d){if(d.open&&!d.contains(e.target))d.open=false});
    });
    document.addEventListener('keydown',function(e){
      if(e.key==='Escape'){
        dds.forEach(function(d){
          if(d.open){d.open=false;var s=d.querySelector('summary');if(s)s.focus()}
        });
      }
    });
  }

  /* botones de copiar en los bloques de código */
  function attachCopyButtons(scope){
    [].forEach.call((scope||document).querySelectorAll('pre.code'),function(pre){
      if(pre.querySelector('.copy'))return;
      var b=document.createElement('button');b.type='button';b.className='copy';b.textContent=pre.getAttribute('data-copy')||'copy';
      b.addEventListener('click',function(){
        var txt=pre.innerText.replace(/\s*(copiar|copy|✓)\s*$/i,'');
        function done(){var o=b.textContent;b.textContent='✓';setTimeout(function(){b.textContent=o},1200)}
        if(navigator.clipboard&&navigator.clipboard.writeText){navigator.clipboard.writeText(txt).then(done,function(){})}
      });
      pre.appendChild(b);
    });
  }
  attachCopyButtons(document);

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

  /* ───────────────────────── GUÍA DE MODDING (ESTÁTICA) ───────────────────────── */
  var board=document.getElementById('forum-board');
  var searchInput=document.getElementById('forum-search');
  if(board){
    var state={cat:'',kind:'all',tag:'',q:''};
    var catCards=[].slice.call(document.querySelectorAll('[data-filter-cat]'));
    var kindTabs=[].slice.call(document.querySelectorAll('[data-filter-kind]'));
    var rows=[].slice.call(board.querySelectorAll('[data-thread-row]'));
    var activeFilterBar=document.getElementById('forum-active-filter');
    var activeFilterLbl=document.getElementById('forum-filter-label');
    var clearFilterBtn=document.getElementById('forum-clear-filter');
    var emptyEl=document.getElementById('forum-empty');

    function applyFilters(){
      var visible=0;
      var q=state.q.trim().toLowerCase();
      rows.forEach(function(r){
        var rCat=r.getAttribute('data-cat')||'';
        var rKind=r.getAttribute('data-kind')||'';
        var rSolved=r.getAttribute('data-solved')==='1';
        var rTags=(r.getAttribute('data-tags')||'').split(',');
        var rSearch=r.getAttribute('data-search')||'';

        var okCat=!state.cat||rCat===state.cat;
        var okKind=state.kind==='all'||(state.kind==='pin'&&rKind==='pin')||(state.kind==='comm'&&rKind==='comm')||(state.kind==='solved'&&rSolved);
        var okTag=!state.tag||rTags.indexOf(state.tag)>=0;
        var okQ=!q||rSearch.indexOf(q)>=0||(q.charAt(0)==='#'&&rTags.indexOf(q.slice(1))>=0);

        var show=okCat&&okKind&&okTag&&okQ;
        r.hidden=!show;
        if(show)visible++;
      });
      catCards.forEach(function(c){
        c.classList.toggle('on',c.getAttribute('data-filter-cat')===state.cat);
      });
      kindTabs.forEach(function(t){
        t.classList.toggle('on',t.getAttribute('data-filter-kind')===state.kind);
      });
      if(activeFilterBar&&activeFilterLbl){
        var parts=[];
        if(state.cat)parts.push('cat:'+state.cat);
        if(state.tag)parts.push('#'+state.tag);
        if(parts.length){
          activeFilterBar.hidden=false;
          activeFilterLbl.textContent=parts.join(' · ');
        }else{
          activeFilterBar.hidden=true;
        }
      }
      if(emptyEl)emptyEl.hidden=(visible>0);
    }

    /* Leer query params (?tag=..., ?cat=..., ?q=...) */
    try{
      var sp=new URLSearchParams(window.location.search);
      if(sp.get('tag'))state.tag=sp.get('tag');
      if(sp.get('cat'))state.cat=sp.get('cat');
      if(sp.get('q')&&searchInput){searchInput.value=sp.get('q');state.q=sp.get('q')}
    }catch(e){}

    catCards.forEach(function(c){
      c.addEventListener('click',function(){
        var cid=c.getAttribute('data-filter-cat');
        state.cat=(state.cat===cid)?'':cid;
        applyFilters();
      });
    });
    kindTabs.forEach(function(t){
      t.addEventListener('click',function(){
        state.kind=t.getAttribute('data-filter-kind')||'all';
        applyFilters();
      });
    });
    board.addEventListener('click',function(e){
      var tg=e.target.closest&&e.target.closest('[data-tag]');
      if(tg&&!tg.getAttribute('href')){
        var val=tg.getAttribute('data-tag');
        state.tag=(state.tag===val)?'':val;
        applyFilters();
      }
    });
    if(clearFilterBtn){
      clearFilterBtn.addEventListener('click',function(){
        state.cat='';state.tag='';state.kind='all';
        if(searchInput){searchInput.value='';state.q=''}
        applyFilters();
      });
    }
    if(searchInput){
      searchInput.addEventListener('input',function(){
        state.q=searchInput.value||'';
        applyFilters();
      });
    }
    document.addEventListener('keydown',function(e){
      if(e.key==='/'&&document.activeElement!==searchInput&&!/^(INPUT|TEXTAREA)$/i.test((document.activeElement||{}).tagName||'')){
        if(searchInput){e.preventDefault();searchInput.focus()}
      }
    });

    applyFilters();
  }
})();
