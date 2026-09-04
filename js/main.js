/* eGlobe Solutions, shared front-end behaviour (no backend, no dependencies) */
(function(){
  'use strict';

  var prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ---------- Mark current nav link active ---------- */
  function markActiveNav(){
    var path = location.pathname.split('/').pop() || 'index.html';
    document.querySelectorAll('.nav-dock__links a, .topbar__links a').forEach(function(a){
      var href = a.getAttribute('href');
      if(href === path || (path === '' && href === 'index.html')){
        a.classList.add('active');
      }
    });
  }

  /* ---------- Platform + Solutions mega-menus ----------
     Injected into every page's topbar (and mobile burger list, which reuses the
     same #topbar-links markup) so this lives in one place instead of 40 files.
     "Platform" (what we offer) lists all 16 products grouped into the same
     categories used on the homepage ecosystem section. "Solutions" (who we
     serve) lists the 6 property-type pages. Both are built by the same
     buildTopbarMega() helper below, keyed off a literal link-text match on
     the server-rendered MenuItems seed ("Products" / "Solutions"). */
  var PLATFORM_CATEGORIES = [
    {
      label: 'Core Operations',
      items: [
        { name: 'Property Management System', href: 'products/pms.html' },
        { name: 'Channel Manager', href: 'products/channel-manager.html' },
        { name: 'Housekeeping', href: 'products/housekeeping.html' },
        { name: 'Point of Sale (POS)', href: 'products/pos.html' },
        { name: 'Kitchen Order Ticket (KOT)', href: 'products/kot.html' },
        { name: 'PMS APIs', href: 'products/pms-apis.html' }
      ]
    },
    {
      label: 'Revenue & Distribution',
      items: [
        { name: 'Finance & Revenue Management', href: 'products/finance-revenue.html' },
        { name: 'OTA Listing & Management', href: 'products/ota-management.html' },
        { name: 'Google Hotel Ads', href: 'products/google-hotel-ads.html' },
        { name: 'Meta Search Engines', href: 'products/meta-search.html' },
        { name: 'B2B Stay', href: 'products/b2b-stay.html' }
      ]
    },
    {
      label: 'Guest Experience & Growth',
      items: [
        { name: 'Booking Engine', href: 'products/booking-engine.html' },
        { name: 'Website Builder', href: 'products/website-builder.html' },
        { name: 'Reviews Manager', href: 'products/reviews-manager.html' },
        { name: 'eGlobe AI Tools', href: 'products/ai-tools.html' },
        { name: 'Payment Gateway', href: 'products/payment-gateway.html' }
      ]
    }
  ];

  var SOLUTIONS_CATEGORIES = [
    {
      label: 'Property Types',
      items: [
        { name: 'Hotels & Resorts', href: 'solutions/hotels-resorts.html' },
        { name: 'Boutique Properties', href: 'solutions/boutique-properties.html' },
        { name: 'Vacation Rentals', href: 'solutions/vacation-rentals.html' },
        { name: 'Hostels', href: 'solutions/hostels.html' },
        { name: 'Guest Houses', href: 'solutions/guest-houses.html' },
        { name: 'Travel Agencies', href: 'solutions/travel-agencies.html' }
      ]
    }
  ];

  function buildTopbarMega(matchText, opts){
    var links = document.getElementById('topbar-links');
    if(!links) return;

    var trigger = null;
    links.querySelectorAll('a').forEach(function(a){
      if(a.textContent.trim() === matchText) trigger = a;
    });
    if(!trigger) return;

    var depth = /\/(products|solutions|blog-articles)\//.test(location.pathname) ? '../' : '';
    if(opts.renameTo) trigger.textContent = opts.renameTo;
    trigger.setAttribute('aria-haspopup', 'true');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.classList.add('topbar__mega-trigger');

    var wrap = document.createElement('div');
    wrap.className = 'topbar__mega-wrap';
    trigger.parentNode.insertBefore(wrap, trigger);
    wrap.appendChild(trigger);

    var panel = document.createElement('div');
    panel.className = 'topbar__mega' + (opts.compact ? ' topbar__mega--compact' : '');

    var columnsHtml = opts.categories.map(function(cat){
      var itemsHtml = cat.items.map(function(it){
        return '<a href="' + depth + it.href + '">' + it.name + '</a>';
      }).join('');
      return '<div class="topbar__mega-col"><div class="topbar__mega-label">' + cat.label + '</div>' + itemsHtml + '</div>';
    }).join('');

    panel.innerHTML = columnsHtml + opts.ctaHtml(depth);
    wrap.appendChild(panel);

    function openMenu(){
      panel.classList.add('open');
      trigger.setAttribute('aria-expanded', 'true');
    }
    function closeMenu(){
      panel.classList.remove('open');
      trigger.setAttribute('aria-expanded', 'false');
    }

    // #topbar-links (and this trigger with it) is hidden entirely below 760px
    // in favour of the separate nav-dock mobile pill, so this only ever needs
    // to handle desktop: hover to open, click as a keyboard/accessibility
    // toggle (the trigger keeps its original href as a no-JS fallback).
    wrap.addEventListener('mouseenter', openMenu);
    wrap.addEventListener('mouseleave', closeMenu);
    trigger.addEventListener('click', function(e){
      e.preventDefault();
      panel.classList.contains('open') ? closeMenu() : openMenu();
    });
    document.addEventListener('keydown', function(e){
      if(e.key === 'Escape') closeMenu();
    });
  }

  function initPlatformMenu(){
    buildTopbarMega('Products', {
      renameTo: 'Platform',
      categories: PLATFORM_CATEGORIES,
      ctaHtml: function(depth){
        return '<div class="topbar__mega-cta">' +
          '<div class="topbar__mega-cta-label">Not sure where to start?</div>' +
          '<p>See how every module connects into one platform, sourced and managed for you.</p>' +
          '<a href="' + depth + 'index.html#ecosystem" class="btn btn-ghost btn-sm">View Full Ecosystem</a>' +
          '<a href="' + depth + 'contact.html" class="btn btn-primary btn-sm">Book a Demo</a>' +
        '</div>';
      }
    });
  }

  function initSolutionsMenu(){
    buildTopbarMega('Solutions', {
      categories: SOLUTIONS_CATEGORIES,
      compact: true,
      ctaHtml: function(depth){
        return '<div class="topbar__mega-cta">' +
          '<div class="topbar__mega-cta-label">Not sure which fits?</div>' +
          '<p>Every eGlobe solution runs on the same platform, just configured for how your property operates.</p>' +
          '<a href="' + depth + 'solutions/hotels-resorts.html" class="btn btn-ghost btn-sm">Browse All Solutions</a>' +
          '<a href="' + depth + 'contact.html" class="btn btn-primary btn-sm">Book a Demo</a>' +
        '</div>';
      }
    });
  }

  /* ---------- Top header mobile menu ---------- */
  function initTopbarBurger(){
    var burger = document.getElementById('topbar-burger');
    var links = document.getElementById('topbar-links');
    if(!burger || !links) return;

    function closeMenu(){
      links.classList.remove('open');
      burger.setAttribute('aria-expanded', 'false');
    }
    function toggleMenu(){
      var open = links.classList.toggle('open');
      burger.setAttribute('aria-expanded', open ? 'true' : 'false');
    }

    burger.addEventListener('click', function(e){
      e.stopPropagation();
      toggleMenu();
    });
    document.addEventListener('click', function(e){
      if(!links.contains(e.target) && e.target !== burger) closeMenu();
    });
    document.addEventListener('keydown', function(e){
      if(e.key === 'Escape' && links.classList.contains('open')){
        closeMenu();
        burger.focus();
      }
    });
    links.querySelectorAll('a').forEach(function(a){
      a.addEventListener('click', closeMenu);
    });
    window.addEventListener('resize', function(){
      if(window.innerWidth > 760) closeMenu();
    }, {passive:true});
  }

  /* ---------- Platform + Solutions in the nav-dock pill ----------
     The nav-dock pill (Home/Solutions/Platform/Pricing/Resellers) is the
     ONLY navigation surface on mobile, #topbar-links is display:none below
     760px entirely. A stacked accordion listing every product/solution here
     turned out to be the wrong shape for a phone (too much to scan inside
     an already-small popover), so on mobile these links just navigate
     straight to their href (Platform -> the ecosystem section, Solutions ->
     the first solution page), exactly like Home/Pricing/Resellers. Desktop
     keeps the small dropdown (real hover/click target, room for
     categories) since the pill isn't the cramped surface there. This
     converts the existing server-rendered <a> in place (rather than
     inserting a new node) so there's exactly one nav-dock entry per
     concept, no duplicate link left behind. Must run before initDock() so
     these links and their dropdown items are included in initDock()'s "any
     link click closes the mobile menu" wiring below. */
  function buildNavDockMega(matchText, opts){
    var links = document.querySelector('.nav-dock__links');
    if(!links) return;

    var toggle = null;
    links.querySelectorAll('a').forEach(function(a){
      if(a.textContent.trim() === matchText) toggle = a;
    });
    if(!toggle) return;

    var depth = /\/(products|solutions|blog-articles)\//.test(location.pathname) ? '../' : '';
    var isMobileNav = function(){ return window.innerWidth <= 860; };

    toggle.classList.add('nav-dock__link', 'nav-dock__mega-toggle');
    toggle.setAttribute('aria-expanded', 'false');
    if(opts.renameTo) toggle.textContent = opts.renameTo;

    var panel = document.createElement('div');
    panel.className = 'nav-dock__mega-panel';
    panel.innerHTML = opts.categories.map(function(cat){
      var itemsHtml = cat.items.map(function(it){
        return '<a href="' + depth + it.href + '">' + it.name + '</a>';
      }).join('');
      return '<div class="nav-dock__mega-label">' + cat.label + '</div>' + itemsHtml;
    }).join('');

    function closePanel(){
      panel.classList.remove('open');
      toggle.setAttribute('aria-expanded', 'false');
    }

    toggle.addEventListener('click', function(e){
      if(isMobileNav()) return; // let the href navigate normally
      e.preventDefault();
      e.stopPropagation(); // don't let this bubble into the mobile "click outside closes menu" handler
      var open = panel.classList.toggle('open');
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    // Desktop pill has no burger/backdrop, so this dropdown needs its own
    // click-outside-to-close (mobile doesn't reach this code path at all,
    // its tap navigates away immediately).
    document.addEventListener('click', function(e){
      if(!panel.contains(e.target) && e.target !== toggle) closePanel();
    });
    document.addEventListener('keydown', function(e){
      if(e.key === 'Escape') closePanel();
    });

    toggle.insertAdjacentElement('afterend', panel);
  }

  function initNavDockPlatform(){
    buildNavDockMega('Products', { renameTo: 'Platform', categories: PLATFORM_CATEGORIES });
  }

  function initNavDockSolutions(){
    buildNavDockMega('Solutions', { categories: SOLUTIONS_CATEGORIES });
  }

  /* ---------- Floating dock: hidden only in the hero and footer zones ---------- */
  function initDock(){
    var dockWrap = document.querySelector('.nav-dock-wrap');
    if(!dockWrap) return;
    var ticking = false;

    var heroEl = document.querySelector('.hero, .page-hero');
    var footerEl = document.querySelector('footer');

    function applyVisibility(){
      var vh = window.innerHeight;
      var isMobile = window.innerWidth <= 760;
      /* On mobile the topbar has no page links, so the dock is the only way
         to navigate through the main page content, it stays visible through
         the hero there. The footer is different: it has its own full link
         list (Home/Products/Pricing/etc.), so the dock would just be
         floating on top of, and hiding, footer content with nothing gained,
         hide it there on mobile same as desktop. */
      var inHero = (!isMobile && heroEl) ? heroEl.getBoundingClientRect().bottom > vh * 0.35 : false;
      var inFooter = footerEl ? footerEl.getBoundingClientRect().top < vh * 0.65 : false;
      dockWrap.classList.toggle('hide', inHero || inFooter);
    }

    window.addEventListener('resize', applyVisibility, {passive:true});
    window.addEventListener('scroll', function(){
      if(ticking) return;
      ticking = true;
      requestAnimationFrame(function(){
        applyVisibility();
        ticking = false;
      });
    }, {passive:true});

    applyVisibility();

    // mobile burger
    var burger = document.querySelector('.nav-dock__burger');
    var links = document.querySelector('.nav-dock__links');
    if(burger && links){
      function closeMenu(){
        links.classList.remove('open');
        burger.setAttribute('aria-expanded', 'false');
      }
      burger.addEventListener('click', function(){
        var open = links.classList.toggle('open');
        burger.setAttribute('aria-expanded', open ? 'true' : 'false');
      });
      document.addEventListener('click', function(e){
        if(!dockWrap.contains(e.target)) closeMenu();
      });
      document.addEventListener('keydown', function(e){
        if(e.key === 'Escape' && links.classList.contains('open')){
          closeMenu();
          burger.focus();
        }
      });
      links.querySelectorAll('a').forEach(function(a){
        a.addEventListener('click', closeMenu);
      });
    }
  }

  /* ---------- Scroll progress bar ---------- */
  function initProgress(){
    var bar = document.querySelector('.scroll-progress');
    if(!bar) return;
    // Batched through requestAnimationFrame like every other scroll listener
    // in this file, this one used to write to style.width on every raw
    // scroll event (dozens per second on a trackpad/momentum scroll),
    // fighting the browser's own scroll-compositing and reading as jank.
    var ticking = false;
    function update(){
      var h = document.documentElement;
      var pct = (h.scrollTop) / (h.scrollHeight - h.clientHeight) * 100;
      bar.style.width = pct + '%';
      ticking = false;
    }
    window.addEventListener('scroll', function(){
      if(ticking) return;
      ticking = true;
      requestAnimationFrame(update);
    }, {passive:true});
  }

  /* ---------- Reveal on scroll (IntersectionObserver) ---------- */
  function initReveal(){
    var targets = document.querySelectorAll('[data-reveal], [data-reveal-group]');
    if(!targets.length) return;
    if(prefersReduced){
      targets.forEach(function(t){ t.classList.add('in-view'); });
      return;
    }
    // rootMargin's bottom edge is pulled up 15% of the viewport instead of a
    // flat 60px, so a section starts revealing while it's still approaching
    // from below and finishes arriving in step with the scroll, rather than
    // waiting until it's already deep in the viewport and then popping in
    // all at once, the same fixed 60px lagged badly on a tall/short viewport.
    var io = new IntersectionObserver(function(entries){
      entries.forEach(function(entry){
        if(entry.isIntersecting){
          entry.target.classList.add('in-view');
          io.unobserve(entry.target);
        }
      });
    }, {threshold:0.1, rootMargin:'0px 0px -15% 0px'});
    targets.forEach(function(t){ io.observe(t); });
  }

  /* ---------- Animated counters ---------- */
  function initCounters(){
    var counters = document.querySelectorAll('[data-counter]');
    if(!counters.length) return;
    var io = new IntersectionObserver(function(entries){
      entries.forEach(function(entry){
        if(!entry.isIntersecting) return;
        io.unobserve(entry.target);
        var el = entry.target;
        var end = parseFloat(el.getAttribute('data-counter'));
        var decimals = (el.getAttribute('data-counter').split('.')[1] || '').length;
        var suffix = el.getAttribute('data-suffix') || '';
        if(prefersReduced){
          el.textContent = end.toFixed(decimals) + suffix;
          return;
        }
        var start = 0;
        var dur = 1600;
        var startTime = null;
        function step(ts){
          if(!startTime) startTime = ts;
          var progress = Math.min((ts - startTime) / dur, 1);
          var eased = 1 - Math.pow(1 - progress, 3);
          var val = start + (end - start) * eased;
          el.textContent = val.toFixed(decimals) + suffix;
          if(progress < 1) requestAnimationFrame(step);
        }
        requestAnimationFrame(step);
      });
    }, {threshold:0.4});
    counters.forEach(function(c){ io.observe(c); });
  }

  /* ---------- Kinetic typography: scroll-tied phrase highlighting ----------
     Editorial-style effect for [data-kinetic] containers: each already-
     accented phrase (.accent/.dim spans, the same markup content editors
     already write) dims until it scrolls into a band near the vertical
     centre of the viewport, then lights up, rather than being statically
     coloured on load. Driven by IntersectionObserver with many thresholds
     (a cheap way to get a continuous-feeling progress value without a raw
     `scroll` listener recalculating layout every frame), not a scroll-jack:
     the page still scrolls completely normally, only the text color/opacity
     responds. */
  function initKineticTypography(){
    var containers = document.querySelectorAll('[data-kinetic]');
    if(!containers.length) return;
    if(prefersReduced){
      containers.forEach(function(el){ el.classList.add('kinetic-static'); });
      return;
    }
    var thresholds = [];
    for(var t = 0; t <= 1; t += 0.05) thresholds.push(t);

    containers.forEach(function(container){
      var phrases = container.querySelectorAll('.accent, .dim');
      if(!phrases.length) return;
      phrases.forEach(function(p){ p.classList.add('kinetic-phrase'); });

      var io = new IntersectionObserver(function(entries){
        entries.forEach(function(entry){
          // Ratio of the element's own height currently visible, used as a
          // stand-in "progress" value, cheap since IO computes it for us.
          var progress = entry.intersectionRatio;
          entry.target.style.setProperty('--kinetic-progress', progress.toFixed(3));
        });
      }, {threshold:thresholds});

      io.observe(container);
    });
  }

  /* ---------- Department workspace tabs ---------- */
  function initDeptTabs(){
    var tabs = document.querySelectorAll('.dept-tab');
    var panelsList = document.querySelectorAll('.dept-panel');
    if(!tabs.length || !panelsList.length) return;
    var tabList = Array.prototype.slice.call(tabs);
    var panels = Array.prototype.slice.call(panelsList);
    var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // Wrap the panels in a single positioned "stage" so switching tabs reads
    // as one page sliding out while the next slides in from the same edge
    // (like flipping through a deck), instead of the old panel vanishing
    // and a new one popping into its place.
    var stage = document.createElement('div');
    stage.className = 'dept-stage';
    panels[0].parentNode.insertBefore(stage, panels[0]);
    panels.forEach(function(p){ stage.appendChild(p); });
    stage.classList.add('js-ready');

    var current = stage.querySelector('.dept-panel.active') || panels[0];
    stage.style.height = current.offsetHeight + 'px';
    requestAnimationFrame(function(){ stage.classList.add('ready'); });

    var animating = false;
    var DURATION = reduceMotion ? 1 : 440;
    var OFFSET = reduceMotion ? 0 : 34;

    function panelIndex(p){ return panels.indexOf(p); }

    function activate(tab, moveFocus){
      if(tab.classList.contains('active') || animating) return;
      var target = tab.getAttribute('data-dept');
      var next = stage.querySelector('.dept-panel[data-dept-panel="' + target + '"]');
      var outgoing = stage.querySelector('.dept-panel.active');
      if(!next || next === outgoing) return;

      tabList.forEach(function(t){
        t.classList.remove('active');
        t.setAttribute('tabindex','-1');
        t.setAttribute('aria-selected','false');
      });
      tab.classList.add('active');
      tab.setAttribute('tabindex','0');
      tab.setAttribute('aria-selected','true');
      if(moveFocus) tab.focus();

      var forward = panelIndex(next) > panelIndex(outgoing);
      animating = true;
      stage.style.height = next.offsetHeight + 'px';

      next.style.transition = 'none';
      next.style.transform = 'translateX(' + (forward ? OFFSET : -OFFSET) + 'px)';
      next.style.opacity = '0';
      next.classList.add('active');
      void next.offsetWidth; // flush the starting position before animating

      var easing = 'transform ' + DURATION + 'ms cubic-bezier(.4,0,.2,1), opacity ' + Math.round(DURATION * .85) + 'ms ease';
      outgoing.style.transition = easing;
      outgoing.style.transform = 'translateX(' + (forward ? -OFFSET : OFFSET) + 'px)';
      outgoing.style.opacity = '0';
      outgoing.classList.remove('active');

      requestAnimationFrame(function(){
        next.style.transition = easing;
        next.style.transform = 'translateX(0)';
        next.style.opacity = '1';
      });

      window.setTimeout(function(){
        outgoing.removeAttribute('style');
        next.removeAttribute('style');
        animating = false;
      }, DURATION + 40);
    }

    tabList.forEach(function(tab, i){
      tab.setAttribute('tabindex', tab.classList.contains('active') ? '0' : '-1');
      tab.setAttribute('aria-selected', tab.classList.contains('active') ? 'true' : 'false');
      tab.addEventListener('click', function(){
        // On mobile the stage starts hidden (CSS: display:none), so its
        // height was measured as 0 back at init time. activate() itself
        // no-ops when clicking the already-active tab (frontdesk, by
        // default) and so never re-measures it, meaning a first click on
        // Front Desk would reveal a 0-height, clipped stage without this.
        var wasHidden = !stage.classList.contains('mobile-revealed');
        stage.classList.add('mobile-revealed');
        activate(tab, false);
        if(wasHidden){
          var active = stage.querySelector('.dept-panel.active');
          if(active) stage.style.height = active.offsetHeight + 'px';
        }
      });
      tab.addEventListener('keydown', function(e){
        var idx = tabList.indexOf(tab);
        if(e.key === 'ArrowRight' || e.key === 'ArrowDown'){
          e.preventDefault();
          activate(tabList[(idx + 1) % tabList.length], true);
        } else if(e.key === 'ArrowLeft' || e.key === 'ArrowUp'){
          e.preventDefault();
          activate(tabList[(idx - 1 + tabList.length) % tabList.length], true);
        } else if(e.key === 'Home'){
          e.preventDefault();
          activate(tabList[0], true);
        } else if(e.key === 'End'){
          e.preventDefault();
          activate(tabList[tabList.length - 1], true);
        }
      });
    });

    // Keep the stage height honest if text reflows at a new viewport width.
    var resizeTimer;
    window.addEventListener('resize', function(){
      clearTimeout(resizeTimer);
      resizeTimer = setTimeout(function(){
        if(animating) return;
        var active = stage.querySelector('.dept-panel.active');
        if(active) stage.style.height = active.offsetHeight + 'px';
      }, 150);
    });

    /* Tasks: click to toggle done, with a small satisfying pop */
    document.querySelectorAll('.dept-task').forEach(function(task){
      task.addEventListener('click', function(){
        task.classList.toggle('done');
      });
    });
  }

  /* ---------- Mobile app section: "Read more" list toggle ---------- */
  function initAppFeatureReadMore(){
    var list = document.getElementById('app-feature-list');
    var btn = document.getElementById('app-feature-readmore');
    if(!list || !btn) return;
    btn.addEventListener('click', function(){
      var expanded = list.classList.toggle('expanded');
      btn.setAttribute('aria-expanded', expanded ? 'true' : 'false');
      btn.querySelector('span').textContent = expanded ? 'Read less' : 'Read more';
    });
  }

  /* ---------- Capability tabs (pipeline-style) ---------- */
  function initCapTabs(){
    var tabs = document.querySelectorAll('.cap-tab');
    if(!tabs.length) return;
    tabs.forEach(function(tab){
      tab.addEventListener('click', function(){
        var target = tab.getAttribute('data-cap');
        tabs.forEach(function(t){ t.classList.remove('active'); });
        tab.classList.add('active');
        document.querySelectorAll('.cap-panel').forEach(function(p){
          p.classList.toggle('active', p.getAttribute('data-cap-panel') === target);
        });
      });
    });
  }

  /* ---------- Core engine list -> stage swap ---------- */
  function initEngineList(){
    var items = document.querySelectorAll('.engine__item');
    if(!items.length) return;
    var stages = document.querySelectorAll('[data-engine-stage]');
    items.forEach(function(item){
      item.addEventListener('click', function(){
        var target = item.getAttribute('data-engine');
        items.forEach(function(i){ i.classList.remove('active'); });
        item.classList.add('active');
        stages.forEach(function(s){
          s.style.display = (s.getAttribute('data-engine-stage') === target) ? 'flex' : 'none';
        });
      });
    });
  }

  /* ---------- Ambient "live activity" badges over the hero dash mock ---------- */
  function initFloatyCycle(){
    var floaties = Array.prototype.slice.call(document.querySelectorAll('.floaty'));
    if(!floaties.length) return;

    var MESSAGES = [
      'Rate sync complete',
      'New booking, Room 204',
      'Housekeeping updated',
      'Payment received',
      'Guest checked in',
      'Availability pushed live'
    ];

    floaties.forEach(function(floaty, i){
      var textEl = floaty.querySelector('.floaty__text');
      if(!textEl) return;
      var msgIndex = i % MESSAGES.length;

      setInterval(function(){
        textEl.classList.add('swap');
        setTimeout(function(){
          msgIndex = (msgIndex + floaties.length) % MESSAGES.length;
          textEl.textContent = MESSAGES[msgIndex];
          textEl.classList.remove('swap');
        }, 300);
      }, 3600 + i * 400);
    });
  }

  /* ---------- Cursor-reactive pixel grid ---------- */
  /* ---------- Hero interactive demo: tabs, rate cells, inventory, promotions ---------- */

  function initHeroDemo(){
    var dash = document.querySelector('.dash');
    if(!dash) return;

    var tabs = Array.prototype.slice.call(dash.querySelectorAll('.dash__tab'));
    var panels = dash.querySelectorAll('.dash__panel');

    function activateTab(tab){
      tabs.forEach(function(t){ t.classList.remove('active'); t.setAttribute('aria-selected','false'); });
      tab.classList.add('active'); tab.setAttribute('aria-selected','true');
      var target = tab.getAttribute('data-dash-tab');
      panels.forEach(function(p){ p.classList.toggle('active', p.getAttribute('data-dash-panel') === target); });
    }

    tabs.forEach(function(tab){
      tab.addEventListener('click', function(){ activateTab(tab); pauseAutoCycle(); });
    });

    /* Auto-cycle through tabs so the demo feels alive on first view; pauses
       as soon as the visitor interacts with it (click/hover/touch) so it
       never fights a user who's exploring a panel. */
    var autoCycleTimer = null;
    var autoCyclePaused = false;
    function startAutoCycle(){
      if(autoCycleTimer || autoCyclePaused) return;
      autoCycleTimer = setInterval(function(){
        var activeIdx = tabs.findIndex(function(t){ return t.classList.contains('active'); });
        var next = tabs[(activeIdx + 1) % tabs.length];
        activateTab(next);
      }, 4200);
    }
    function pauseAutoCycle(){
      autoCyclePaused = true;
      if(autoCycleTimer){ clearInterval(autoCycleTimer); autoCycleTimer = null; }
    }
    dash.addEventListener('pointerenter', pauseAutoCycle);
    dash.addEventListener('touchstart', pauseAutoCycle, { passive: true });
    startAutoCycle();

    /* Rate cells: click to edit */
    dash.querySelectorAll('.demo-cell').forEach(function(cell){
      cell.addEventListener('click', function(){
        if(cell.classList.contains('editing')) return;
        var current = cell.getAttribute('data-val');
        cell.classList.add('editing');
        cell.innerHTML = '<input type="text" inputmode="numeric" value="' + current + '">';
        var input = cell.querySelector('input');
        input.focus();
        input.select();
        function commit(){
          var val = parseInt(input.value, 10);
          if(isNaN(val) || val < 0) val = parseInt(current, 10);
          cell.setAttribute('data-val', val);
          cell.classList.remove('editing');
          cell.textContent = val;
          cell.classList.add('saved');
          setTimeout(function(){ cell.classList.remove('saved'); }, 650);
        }
        input.addEventListener('blur', commit);
        input.addEventListener('keydown', function(e){
          if(e.key === 'Enter'){ e.preventDefault(); input.blur(); }
        });
      });
    });

    /* Sync button: animated per-OTA checklist */
    var syncBtn = dash.querySelector('#demo-sync-btn');
    if(syncBtn){
      syncBtn.addEventListener('click', function(){
        if(syncBtn.disabled) return;
        syncBtn.disabled = true;
        var label = syncBtn.textContent;
        syncBtn.textContent = 'Syncing…';
        var items = dash.querySelectorAll('.demo-sync-item__state');
        items.forEach(function(el){ el.className = 'demo-sync-item__state'; el.textContent = ''; });
        items.forEach(function(el, i){
          setTimeout(function(){
            el.classList.add('checking');
          }, i * 250);
          setTimeout(function(){
            el.className = 'demo-sync-item__state done';
            el.textContent = '✓ Synced';
          }, i * 250 + 500);
        });
        setTimeout(function(){
          syncBtn.disabled = false;
          syncBtn.textContent = label;
        }, items.length * 250 + 600);
      });
    }

    /* Inventory steppers + stop sell */
    dash.querySelectorAll('.demo-inv__row').forEach(function(row){
      var valEl = row.querySelector('.demo-inv__qty-val');
      row.querySelectorAll('.demo-inv__stepper button').forEach(function(btn){
        btn.addEventListener('click', function(){
          var qty = parseInt(row.getAttribute('data-qty'), 10) + parseInt(btn.getAttribute('data-d'), 10);
          if(qty < 0) qty = 0;
          row.setAttribute('data-qty', qty);
          valEl.textContent = qty;
        });
      });
      var stop = row.querySelector('.demo-inv__stop input');
      if(stop){
        stop.addEventListener('change', function(){
          row.classList.toggle('closed', stop.checked);
        });
      }
    });

    /* Promotions form */
    var promoRange = dash.querySelector('#demo-promo-range');
    var promoRangeVal = dash.querySelector('#demo-promo-range-val');
    if(promoRange && promoRangeVal){
      promoRange.addEventListener('input', function(){
        promoRangeVal.textContent = promoRange.value + '%';
      });
    }
    var promoForm = dash.querySelector('#demo-promo-form');
    var promoList = dash.querySelector('#demo-promo-list');
    if(promoForm && promoList){
      promoForm.addEventListener('submit', function(e){
        e.preventDefault();
        var name = dash.querySelector('#demo-promo-name').value.trim();
        if(!name) return;
        var discount = promoRange.value;
        var channels = Array.prototype.slice.call(promoForm.querySelectorAll('.demo-promo__channels input:checked'))
          .map(function(cb){ return cb.parentNode.textContent.trim(); });
        var item = document.createElement('div');
        item.className = 'demo-promo__list-item';
        item.innerHTML = '<span>' + name + ', <b>' + discount + '% off</b></span><span style="color:var(--ink-soft);font-size:11px;">' + (channels.join(', ') || 'No channel') + '</span>';
        promoList.insertBefore(item, promoList.firstChild);
        while(promoList.children.length > 3){
          promoList.removeChild(promoList.lastChild);
        }
        dash.querySelector('#demo-promo-name').value = '';
      });
    }
  }

  function initGridSpotlight(){
    if(prefersReduced) return;
    var root = document.documentElement;
    var gridBgEls = document.querySelectorAll('.grid-bg');
    if(!gridBgEls.length && !root) return;
    var raf = null, mx = -9999, my = -9999;

    function update(){
      root.style.setProperty('--spot-x', mx + 'px');
      root.style.setProperty('--spot-y', my + 'px');
      gridBgEls.forEach(function(el){
        var r = el.getBoundingClientRect();
        el.style.setProperty('--gx', (mx - r.left) + 'px');
        el.style.setProperty('--gy', (my - r.top) + 'px');
      });
      raf = null;
    }

    document.addEventListener('mousemove', function(e){
      mx = e.clientX; my = e.clientY;
      if(!raf) raf = requestAnimationFrame(update);
    });
    document.addEventListener('mouseleave', function(){
      mx = -9999; my = -9999;
      if(!raf) raf = requestAnimationFrame(update);
    });
  }

  /* ---------- Magnetic buttons ---------- */
  function initMagnetic(){
    if(prefersReduced) return;
    var els = document.querySelectorAll('.magnetic');
    els.forEach(function(el){
      var btn = el.querySelector('.btn') || el;
      el.addEventListener('mousemove', function(e){
        var r = el.getBoundingClientRect();
        var x = e.clientX - r.left - r.width/2;
        var y = e.clientY - r.top - r.height/2;
        btn.style.transform = 'translate(' + (x*0.18) + 'px,' + (y*0.28) + 'px)';
      });
      el.addEventListener('mouseleave', function(){
        btn.style.transform = '';
      });
    });
  }

  /* ---------- Product detail modal ---------- */
  var CHECK_SVG = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>';
  var CHEVRON_SVG = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>';
  var productModalData = {
    'pms': {
      page:'pms.html',
      icon:'<rect x="3" y="4" width="18" height="16" rx="2"/><line x1="3" y1="10" x2="21" y2="10"/>',
      title:'Property Management System',
      desc:'A smart, secure and scalable cloud PMS, fully integrated with Channel Manager for real-time OTA sync. Trusted by 7,000+ properties worldwide, it streamlines front desk tasks, auto-assigns rooms on booking, and gives you remote access from any device. No on-site servers, no IT maintenance.',
      points:['Auto room allotment on every booking', 'Access anytime, from any device', 'Multi-currency & multi-language support', 'Connected to Channel Manager, POS & Payment Gateway'],
      faq:[{q:'What is a Property Management System (PMS)?', a:'Hotel management software hosted on the cloud that lets you manage bookings, check-ins, housekeeping and billing from any device with an internet connection.'}]
    },
    'channel-manager': {
      page:'channel-manager.html',
      icon:'<circle cx="12" cy="12" r="9"/><line x1="3" y1="12" x2="21" y2="12"/><path d="M12 3a15 15 0 010 18a15 15 0 010-18"/>',
      title:'Channel Manager',
      desc:'Keeps rates, availability and inventory in sync across 100+ Indian and global OTAs (Booking.com, Expedia, MakeMyTrip, Goibibo and more) from one dashboard, in real time. A pooled inventory model means no room ever sits blocked on one channel while available on another.',
      points:['Two-way real-time sync across 100+ OTAs', 'Dynamic pricing based on demand', 'Automatic rate-parity alerts', 'Reservations flow straight into your PMS'],
      faq:[{q:'Will it prevent overbookings?', a:'Yes, real-time two-way sync updates every connected OTA within seconds of a booking, virtually eliminating double bookings.'}]
    },
    'finance': {
      page:'finance-revenue.html',
      icon:'<path d="M3 3v18h18M7 15l4-4 3 3 5-6"/>',
      title:'Finance & Revenue',
      desc:'Dynamic pricing and demand forecasting that adjusts your rates automatically as occupancy and demand shift, backed by financial reporting that stays accurate without manual spreadsheet work. See RevPAR, ADR and forecasted demand on one screen, and let the system suggest rate changes before you lose a booking to a competitor.',
      points:['Demand-based pricing suggestions', 'Live RevPAR & ADR tracking', 'Automated financial reports', 'Forecasts ahead of peak periods'],
      faq:[{q:'Does it set rates automatically?', a:'It suggests rate changes based on demand, your revenue team approves before anything goes live.'}]
    },
    'pos': {
      page:'pos.html',
      icon:'<rect x="2" y="7" width="20" height="14" rx="2"/><path d="M16 3v4M8 3v4M2 11h20"/>',
      title:'Point of Sale',
      desc:'Manage restaurant orders, table assignments and billing from any device, fully integrated with your hotel PMS. Post charges from the restaurant, bar, spa or room service straight to the guest room with one click, and run every outlet, including delivery and takeaway, from a single dashboard.',
      points:['Bill to room, posts directly to guest folio', 'Table management with real-time overview', 'Multi-outlet support: restaurant, bar, spa, room service', 'GST-compliant billing, generated in seconds'],
      faq:[{q:'Does it connect to my hotel PMS?', a:'Yes, bills post directly to the guest folio and sync in real time with eGlobe Cloud PMS.'}]
    },
    'kot': {
      page:'kot.html',
      icon:'<path d="M9 11l3 3L22 4M21 12v7a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h11"/>',
      title:'Kitchen Order Ticket (KOT)',
      desc:'Orders taken at the POS route instantly to a live kitchen display, so the kitchen starts cooking the moment a guest orders instead of waiting on a paper ticket to be walked over. It cuts down mix-ups between what a guest ordered and what the kitchen prepares, especially during a busy service.',
      points:['Instant order routing from POS', 'Live kitchen display screen', 'Fewer order mix-ups', 'Faster table and room-service turnaround'],
      faq:[{q:'Does it need a separate kitchen device?', a:'Just a screen or printer at the kitchen pass, no special hardware or software installation required.'}]
    },
    'housekeeping': {
      page:'housekeeping.html',
      icon:'<path d="M3 6l9-4 9 4M4 10h16v10H4z"/>',
      title:'Housekeeping',
      desc:'Room status updates flow instantly between front desk and housekeeping staff on mobile, replacing radios and phone calls with a live board everyone can see. The moment a room is marked clean, front desk can sell it, cutting the gap between checkout and a new guest checking into a ready room.',
      points:['Live room-status board', 'Task assignment on mobile', 'Faster room turnover', 'Fewer front-desk / housekeeping phone calls'],
      faq:[{q:'Do staff need a special device?', a:'No, it works from any smartphone or tablet housekeeping already carries.'}]
    },
    'booking-engine': {
      page:'booking-engine.html',
      icon:'<rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/>',
      title:'Booking Engine',
      desc:'A single-page, 4-step direct booking flow fully integrated with your PMS, Channel Manager and Payment Gateway, so guests book straight from your website instead of an OTA. Sell packages, apply discounts (early bird, last-minute, coupons) and accept payment through multiple gateways, with GST-compliant invoices generated automatically.',
      points:['Instant confirmation, no OTA commission', 'Auto-optimised for mobile devices', 'Multiple payment gateways supported', 'Packages, up-sells & discount codes built in'],
      faq:[{q:'Is it fully integrated with my other systems?', a:'Yes, the Booking Engine is fully integrated with your PMS, Channel Manager, and Payment Gateway.'}]
    },
    'ota-listing': {
      page:'ota-management.html',
      icon:'<circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15 15 0 014 10 15 15 0 01-4 10 15 15 0 01-4-10 15 15 0 014-10z"/>',
      title:'OTA Listing & Management',
      desc:'A fully managed OTA listing service. We set up and optimise your profiles on Booking.com, Expedia, MakeMyTrip and 100+ more, then connect them to your Channel Manager for real-time inventory and rate sync. Includes professional descriptions, photo optimisation, and rate-parity & promotions management.',
      points:['End-to-end account setup on 100+ OTAs', 'Real-time inventory & rate sync', 'Professional listing & photo optimisation', 'Rate parity & promotions management'],
      faq:[{q:'How long does OTA listing take?', a:'Usually 2 to 5 working days, depending on each platform\'s approval process and how complete your property details are.'}]
    },
    'google-ads': {
      page:'google-hotel-ads.html',
      icon:'<circle cx="12" cy="12" r="10"/><path d="M8 12l3 3 5-6"/>',
      title:'Google Hotel Ads',
      desc:'Display your live rates and availability on Google Search, Google Maps and your Google Business listing, and pay only for confirmed bookings. No setup fees, no rental costs. Available as fixed-rental or Pay Per Conversion plans, it\'s how 7,000+ properties reach travellers already searching on Google.',
      points:['Real-time rate updates on Search & Maps', 'Pay Per Conversion, no cost for clicks alone', 'No setup fees or rental costs', 'Detailed reporting on impressions, clicks & bookings'],
      faq:[{q:'Is there a cost to participate?', a:'No setup fees or rental costs, you pay a low commission only on confirmed bookings, under the Pay Per Conversion model.'}]
    },
    'meta-search': {
      page:'meta-search.html',
      icon:'<circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>',
      title:'Meta Search Engines',
      desc:'eGlobe is an official Google Hotel Ads partner in India, connecting your booking engine to the world\'s leading hotel meta search platforms, including direct Google Maps integration, so your live rates surface right where travellers are searching, alongside OTA rates, at the moment of decision.',
      points:['Official Google Hotel Ads partner in India', 'Google Maps integration for real-time visibility', 'Rates shown alongside OTA pricing', 'Reach travellers across desktop, tablet & mobile'],
      faq:[{q:'Which meta search platforms are supported?', a:'Google Hotel Ads and Google Maps Integration, with your rates shown alongside major OTAs at the point of search.'}]
    },
    'stay-b2b': {
      page:'b2b-stay.html',
      icon:'<path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2M9 11a4 4 0 100-8 4 4 0 000 8zM23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75"/>',
      title:'Stay B2B',
      desc:'A dedicated network for your corporate clients and travel agents to book your inventory at special rates, through their own branded mobile app or secure, role-based corporate logins, either online or via city ledger. Set custom commission structures per partner, with real-time inventory and automated invoicing.',
      points:['Dedicated branded mobile app for partners', 'Secure, role-based corporate logins', 'Custom rates & commission per partner', 'Automated invoicing & payment reconciliation'],
      faq:[{q:'Can partners see my public rates too?', a:'No, role-based access means each partner only sees the rates and inventory you assign to them.'}]
    },
    'website-builder': {
      page:'website-builder.html',
      icon:'<circle cx="12" cy="12" r="10"/><path d="M2 12h20M12 2a15 15 0 014 10 15 15 0 01-4 10 15 15 0 01-4-10 15 15 0 014-10z"/>',
      title:'Website Builder',
      desc:'We design and build your hotel website for you, as a single page or a full multi-page site, whichever fits your property. Use a domain you already own, or have us register one for you for an additional charge.',
      points:['Built for you, one-page or multi-page', 'SEO-optimised, mobile-friendly design', 'Your domain, or ours for an added charge', 'Shows live offers from your Booking Engine'],
      faq:[{q:'Who buys the domain?', a:'Either works, use a domain you already own, or we can register one for you for an additional charge.'}]
    },
    'reviews': {
      page:'reviews-manager.html',
      icon:'<path d="M12 17.75l-6.16 3.24 1.18-6.88L2 9.24l6.92-1.01L12 2l3.08 6.23L22 9.24l-5.02 4.87 1.18 6.88z"/>',
      title:'Reviews Manager',
      desc:'Brings guest reviews from every platform (Google, Booking.com, TripAdvisor and more) into a single inbox, so nothing gets missed and nothing waits days for a reply. Respond to a review once from the same screen it arrived in, and track how your rating is trending over time.',
      points:['All platforms, one inbox', 'Faster response times', 'Track rating trends over time', 'Reply once, without switching tabs'],
      faq:[{q:'Which review platforms are covered?', a:'Google, Booking.com, TripAdvisor and the other major platforms your guests already review you on.'}]
    },
    'ai-tools': {
      page:'ai-tools.html',
      icon:'<path d="M12 2a4 4 0 014 4c0 1.5-.8 2.5-1.5 3.3-.7.8-1.3 1.4-1.3 2.7v1h-2.4v-1c0-1.3-.6-1.9-1.3-2.7C8.8 8.5 8 7.5 8 6a4 4 0 014-4zM9 18h6M10 21h4"/>',
      title:'eGlobe AI Tools',
      desc:'Three AI agents built into your daily workflow: a Sales Agent that answers WhatsApp and website enquiries and pushes bookings to your PMS 24/7, a Smartdesk that assists staff at check-in/out and answers guest queries, and an Admin Agent that answers questions like "What was my occupancy last week?" instantly.',
      points:['AI Sales Agent, converts enquiries into bookings', 'AI Smartdesk, assists front desk & guest queries', 'AI Admin Agent, instant business insights', 'Works 24/7 across WhatsApp, website & PMS'],
      faq:[{q:'Does this replace my front-desk staff?', a:'No, it handles routine enquiries and check-in support so your team can focus on guests, reducing workload rather than replacing staff.'}]
    },
    'payment-gateway': {
      page:'payment-gateway.html',
      icon:'<path d="M1 10h22M5 15h1M10 15h1"/><rect x="1" y="4" width="22" height="16" rx="2"/>',
      title:'Payment Gateway',
      desc:'Secure card and digital payment processing that posts straight to the guest folio the moment a transaction clears, with no manual entry required by front desk. It supports the major payment methods guests expect, and every charge reconciles automatically against the correct reservation.',
      points:['Secure card & digital processing', 'Direct-to-folio posting', 'Multiple payment methods supported', 'Automatic reconciliation, no manual entry'],
      faq:[{q:'Is it PCI compliant?', a:'Yes, all card processing runs through PCI-certified payment partners.'}]
    },
    'apis': {
      page:'pms-apis.html',
      icon:'<path d="M8 3H5a2 2 0 00-2 2v3M16 3h3a2 2 0 012 2v3M8 21H5a2 2 0 01-2-2v-3M16 21h3a2 2 0 002-2v-3"/>',
      title:'APIs for PMS',
      desc:'Bi-directional, OAuth 2.0-secured endpoints for revenue-management tools, analytics platforms and PMS providers who need direct programmatic access. Extract booking data, push or pull inventory and rates in real time, backed by a 99.9% uptime guarantee, full documentation and dedicated developer support.',
      points:['Real-time, bi-directional data sync', 'OAuth 2.0 secured endpoints', '99.9% uptime guarantee', 'Full docs + dedicated developer support'],
      faq:[{q:'How secure is the API?', a:'All endpoints are protected with OAuth 2.0 authentication, so your data transfers are always secure.'}]
    }
  };

  function initProductModal(){
    var cards = document.querySelectorAll('.product-card[data-modal]');
    var overlay = document.getElementById('product-modal-overlay');
    if(!cards.length || !overlay) return;
    var iconEl = document.getElementById('product-modal-icon');
    var titleEl = document.getElementById('product-modal-title');
    var descEl = document.getElementById('product-modal-desc');
    var pointsEl = document.getElementById('product-modal-points');
    var faqEl = document.getElementById('product-modal-faq');
    var demoEl = document.getElementById('product-modal-demo');
    var closeBtn = document.getElementById('product-modal-close');
    var readMoreEl = document.getElementById('product-modal-readmore');
    var lastFocused = null;

    function buildModalDemo(key){
      if(!demoEl) return;

      demoEl.innerHTML = '';
    }

    function open(key){
      var data = productModalData[key];
      if(!data) return;
      iconEl.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">' + data.icon + '</svg>';
      titleEl.textContent = data.title;
      descEl.textContent = data.desc;
      pointsEl.innerHTML = data.points.map(function(pt){
        return '<div>' + CHECK_SVG + '<span>' + pt + '</span></div>';
      }).join('');
      if(data.faq && data.faq.length){
        faqEl.innerHTML = '<span class="product-modal__faq-label">Frequently Asked</span>' + data.faq.map(function(f){
          return '<div class="product-modal__faq-item">' +
            '<button type="button" class="product-modal__faq-q">' + f.q + CHEVRON_SVG + '</button>' +
            '<div class="product-modal__faq-a">' + f.a + '</div>' +
          '</div>';
        }).join('');
        faqEl.classList.add('active');
        faqEl.querySelectorAll('.product-modal__faq-q').forEach(function(btn){
          btn.addEventListener('click', function(){
            btn.closest('.product-modal__faq-item').classList.toggle('open');
          });
        });
      } else {
        faqEl.innerHTML = '';
        faqEl.classList.remove('active');
      }
      buildModalDemo(key);
      if(readMoreEl){
        if(data.page){
          readMoreEl.href = 'products/' + data.page;
          readMoreEl.style.display = 'inline-flex';
        } else {
          readMoreEl.style.display = 'none';
        }
      }
      lastFocused = document.activeElement;
      overlay.classList.add('active');
      closeBtn.focus();
      document.body.style.overflow = 'hidden';
    }

    function close(){
      overlay.classList.remove('active');
      document.body.style.overflow = '';
      if(lastFocused) lastFocused.focus();
    }

    cards.forEach(function(card){
      card.addEventListener('click', function(){ open(card.getAttribute('data-modal')); });
      card.addEventListener('keydown', function(e){
        if(e.key === 'Enter' || e.key === ' '){
          e.preventDefault();
          open(card.getAttribute('data-modal'));
        }
      });
    });
    closeBtn.addEventListener('click', close);
    overlay.addEventListener('click', function(e){
      if(e.target === overlay) close();
    });
    document.addEventListener('keydown', function(e){
      if(e.key === 'Escape' && overlay.classList.contains('active')) close();
    });
  }

  /* ---------- Contact form (frontend-only validation + fake submit state) ---------- */
  function initContactForm(){
    var form = document.querySelector('#sales-form');
    if(!form) return;

    var REQUIRED = ['name', 'company', 'email', 'phone', 'rooms'];
    var EMAIL_TYPOS = {
      'gmial.com':'gmail.com', 'gmai.com':'gmail.com', 'gmail.co':'gmail.com', 'gmial.co':'gmail.com',
      'yahooo.com':'yahoo.com', 'yaho.com':'yahoo.com',
      'hotmial.com':'hotmail.com', 'hotmil.com':'hotmail.com',
      'outlok.com':'outlook.com', 'outllok.com':'outlook.com'
    };
    var PLAN_BY_ROOMS = {
      '1-10': 'Great fit for our Per-Room plan.',
      '11-50': 'Per-Room or Per-Property, we\'ll help you compare.',
      '51-150': 'Per-Property plan, one flat rate covers you.',
      '150+': 'Enterprise, portfolio dashboards & SLA support.'
    };

    var selectedRooms = '';
    var selectedProducts = [];
    var progressEl = form.querySelector('#sales-form-progress');
    var barFillEl = form.querySelector('#sales-form-bar-fill');
    var planEl = document.querySelector('#sales-plan-suggestion');
    var roomsHint = form.querySelector('#rooms-hint');
    var statusEl = form.querySelector('.form-status');
    var submitBtn = form.querySelector('button[type="submit"]');
    var submitLabel = submitBtn ? submitBtn.textContent : 'Send';

    function fieldGroup(key){ return form.querySelector('.field[data-field="' + key + '"]'); }
    function setMsg(group, msg){
      var el = group.querySelector('.field-msg');
      if(el) el.innerHTML = msg || '';
    }

    function validateField(key){
      var group = fieldGroup(key);
      if(!group) return true;
      var input = group.querySelector('input');
      var val = (input && input.value || '').trim();

      if(key === 'name' || key === 'company'){
        var ok = val.length >= 2;
        group.classList.toggle('invalid', val.length > 0 && !ok);
        group.classList.toggle('valid', ok);
        setMsg(group, val.length > 0 && !ok ? 'Enter at least 2 characters.' : (ok ? '✓ Looks good' : ''));
        return ok;
      }
      if(key === 'email'){
        var emailOk = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(val);
        group.classList.toggle('invalid', val.length > 0 && !emailOk);
        group.classList.toggle('valid', emailOk);
        if(emailOk){
          var domain = val.split('@')[1].toLowerCase();
          if(EMAIL_TYPOS[domain]){
            var fixed = val.split('@')[0] + '@' + EMAIL_TYPOS[domain];
            setMsg(group, 'Did you mean <button type="button" data-fix="' + fixed + '">' + fixed + '</button>?');
            group.classList.remove('valid');
            var btn = group.querySelector('[data-fix]');
            if(btn) btn.addEventListener('click', function(){
              input.value = fixed;
              validateField('email');
              input.focus();
            });
          } else {
            setMsg(group, '✓ Looks good');
          }
        } else {
          setMsg(group, val.length > 0 ? 'Enter a valid email address.' : '');
        }
        return emailOk;
      }
      if(key === 'phone'){
        var digits = val.replace(/\D/g, '');
        var phoneOk = digits.length >= 7 && digits.length <= 15;
        group.classList.toggle('invalid', val.length > 0 && !phoneOk);
        group.classList.toggle('valid', phoneOk);
        setMsg(group, val.length > 0 && !phoneOk ? 'Enter a valid phone number.' : (phoneOk ? '✓ Looks good' : ''));
        return phoneOk;
      }
      if(key === 'rooms'){
        var roomsOk = !!selectedRooms;
        group.classList.toggle('invalid', false);
        return roomsOk;
      }
      return true;
    }

    function updateProgress(){
      var done = 0;
      REQUIRED.forEach(function(key){ if(validateField(key)) done++; });
      if(progressEl) progressEl.textContent = done + ' of ' + REQUIRED.length;
      if(barFillEl) barFillEl.style.width = (done / REQUIRED.length * 100) + '%';
      return done === REQUIRED.length;
    }

    /* live validation as the user types/blurs */
    ['name', 'company', 'email', 'phone'].forEach(function(key){
      var group = fieldGroup(key);
      if(!group) return;
      var input = group.querySelector('input');
      input.addEventListener('input', updateProgress);
      input.addEventListener('blur', function(){ validateField(key); });
    });

    /* Enter key moves to the next field instead of submitting early */
    var focusOrder = ['name', 'company', 'email', 'phone'];
    focusOrder.forEach(function(key, i){
      var group = fieldGroup(key);
      if(!group) return;
      var input = group.querySelector('input');
      input.addEventListener('keydown', function(e){
        if(e.key === 'Enter'){
          e.preventDefault();
          var nextKey = focusOrder[i + 1];
          if(nextKey){
            var nextInput = fieldGroup(nextKey).querySelector('input');
            if(nextInput) nextInput.focus();
          } else {
            document.querySelectorAll('.room-chip')[0].focus();
          }
        }
      });
    });

    /* room chips: pick a range, get an instant plan suggestion */
    var roomsInput = fieldGroup('rooms').querySelector('input[type="hidden"]');
    form.querySelectorAll('.room-chip').forEach(function(chip){
      chip.addEventListener('click', function(){
        form.querySelectorAll('.room-chip').forEach(function(c){ c.classList.remove('active'); });
        chip.classList.add('active');
        selectedRooms = chip.getAttribute('data-rooms');
        roomsInput.value = selectedRooms;
        if(roomsHint) roomsHint.textContent = PLAN_BY_ROOMS[selectedRooms];
        if(planEl) planEl.textContent = PLAN_BY_ROOMS[selectedRooms];
        updateProgress();
      });
    });

    /* product chips: multi-select, purely informational */
    var otherChip = form.querySelector('#product-chip-other');
    var otherInput = form.querySelector('#product-other-input');
    form.querySelectorAll('.product-chip').forEach(function(chip){
      chip.addEventListener('click', function(){
        var name = chip.getAttribute('data-product');
        chip.classList.toggle('active');
        if(chip.classList.contains('active')) selectedProducts.push(name);
        else selectedProducts = selectedProducts.filter(function(p){ return p !== name; });
        if(chip === otherChip && otherInput){
          otherInput.classList.toggle('show', chip.classList.contains('active'));
          if(chip.classList.contains('active')) otherInput.focus();
          else otherInput.value = '';
        }
      });
    });

    updateProgress();

    form.addEventListener('submit', function(e){
      e.preventDefault();
      var allValid = updateProgress();
      if(!allValid){
        var firstInvalid = REQUIRED.find(function(key){ return !validateField(key); });
        if(firstInvalid){
          var group = fieldGroup(firstInvalid);
          var input = group.querySelector('input:not([type="hidden"])');
          if(input) input.focus();
          else group.scrollIntoView({behavior:'smooth', block:'center'});
        }
        if(statusEl){
          statusEl.textContent = 'A few fields still need your attention above.';
          statusEl.className = 'form-status error';
        }
        return;
      }
      submitBtn.disabled = true;
      submitBtn.textContent = 'Sending…';

      var otherChipEl = form.querySelector('#product-chip-other');
      var otherInputEl = form.querySelector('#product-other-input');
      var messageEl = form.querySelector('#sales-form-message');
      var websiteEl = form.querySelector('#sales-form-website');
      var tokenEl = form.querySelector('input[name="__RequestVerificationToken"]');

      var body = new URLSearchParams();
      body.set('FullName', fieldGroup('name').querySelector('input').value.trim());
      body.set('HotelName', fieldGroup('company').querySelector('input').value.trim());
      body.set('Email', fieldGroup('email').querySelector('input').value.trim());
      body.set('Phone', fieldGroup('phone').querySelector('input').value.trim());
      body.set('RoomsRange', selectedRooms);
      body.set('InterestedIn', selectedProducts.join(','));
      body.set('OtherInterest', otherChipEl && otherChipEl.classList.contains('active') && otherInputEl ? otherInputEl.value.trim() : '');
      body.set('Message', messageEl ? messageEl.value.trim() : '');
      body.set('Website', websiteEl ? websiteEl.value : '');
      if(tokenEl) body.set('__RequestVerificationToken', tokenEl.value);

      fetch('/contact/submit', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Requested-With': 'XMLHttpRequest' },
        body: body.toString()
      })
        .then(function(res){ return res.json().then(function(data){ return { ok: res.ok, data: data }; }); })
        .then(function(result){
          submitBtn.disabled = false;
          submitBtn.textContent = submitLabel;
          if(!result.ok){
            if(statusEl){
              statusEl.textContent = (result.data && result.data.message) || 'Something went wrong, please try again or call us directly.';
              statusEl.className = 'form-status error';
            }
            return;
          }
          form.reset();
          selectedRooms = '';
          selectedProducts = [];
          form.querySelectorAll('.room-chip, .product-chip').forEach(function(c){ c.classList.remove('active'); });
          form.querySelectorAll('.field').forEach(function(g){ g.classList.remove('valid', 'invalid'); });
          form.querySelectorAll('.field-msg').forEach(function(m){ m.innerHTML = ''; });
          if(otherInputEl) otherInputEl.classList.remove('show');
          if(roomsHint) roomsHint.textContent = 'Pick a range, we\'ll suggest the right plan.';
          if(planEl) planEl.textContent = 'Tell us your room count';
          updateProgress();
          if(statusEl){
            statusEl.textContent = (result.data && result.data.message) || 'Thanks, your message is on its way to our sales team.';
            statusEl.className = 'form-status success';
          }
        })
        .catch(function(){
          submitBtn.disabled = false;
          submitBtn.textContent = submitLabel;
          if(statusEl){
            statusEl.textContent = 'Could not reach the server, please check your connection and try again.';
            statusEl.className = 'form-status error';
          }
        });
    });
  }

  /* ---------- Quick Enquiry popup (homepage-only mini contact form) ---------- */
  function initQuickEnquiryPopup(){
    var overlay = document.getElementById('quick-enquiry-overlay');
    var form = document.getElementById('quick-enquiry-form');
    if(!overlay || !form) return;

    var closeBtn = document.getElementById('quick-enquiry-close');
    var statusEl = form.querySelector('.form-status');
    var submitBtn = form.querySelector('button[type="submit"]');
    var submitLabel = submitBtn ? submitBtn.textContent : 'Send';
    var REQUIRED = ['name', 'phone'];
    var lastFocused = null;
    var selectedRooms = '';

    function fieldGroup(key){ return form.querySelector('.field[data-field="' + key + '"]'); }
    function setMsg(group, msg){
      var el = group.querySelector('.field-msg');
      if(el) el.innerHTML = msg || '';
    }

    function validateField(key){
      var group = fieldGroup(key);
      if(!group) return true;
      var input = group.querySelector('input');
      var val = (input && input.value || '').trim();

      if(key === 'name'){
        var ok = val.length >= 2;
        group.classList.toggle('invalid', val.length > 0 && !ok);
        group.classList.toggle('valid', ok);
        setMsg(group, val.length > 0 && !ok ? 'Enter at least 2 characters.' : '');
        return ok;
      }
      if(key === 'phone'){
        var digits = val.replace(/\D/g, '');
        var phoneOk = digits.length >= 7 && digits.length <= 15;
        group.classList.toggle('invalid', val.length > 0 && !phoneOk);
        group.classList.toggle('valid', phoneOk);
        setMsg(group, val.length > 0 && !phoneOk ? 'Enter a valid phone number.' : '');
        return phoneOk;
      }
      if(key === 'email'){
        var emailOk = val.length === 0 || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(val);
        group.classList.toggle('invalid', val.length > 0 && !emailOk);
        group.classList.toggle('valid', val.length > 0 && emailOk);
        setMsg(group, val.length > 0 && !emailOk ? 'Enter a valid email address.' : '');
        return emailOk;
      }
      return true;
    }

    ['name', 'phone', 'email'].forEach(function(key){
      var group = fieldGroup(key);
      if(!group) return;
      var input = group.querySelector('input');
      input.addEventListener('blur', function(){ validateField(key); });
    });

    var qeRoomsGroup = fieldGroup('rooms');
    if(qeRoomsGroup){
      var qeRoomsInput = qeRoomsGroup.querySelector('input[type="hidden"]');
      qeRoomsGroup.querySelectorAll('.room-chip').forEach(function(chip){
        chip.addEventListener('click', function(){
          qeRoomsGroup.querySelectorAll('.room-chip').forEach(function(c){ c.classList.remove('active'); });
          chip.classList.add('active');
          selectedRooms = chip.getAttribute('data-rooms');
          if(qeRoomsInput) qeRoomsInput.value = selectedRooms;
        });
      });
    }

    function open(){
      lastFocused = document.activeElement;
      overlay.classList.add('active');
      document.body.style.overflow = 'hidden';
      var nameInput = fieldGroup('name');
      if(nameInput) nameInput.querySelector('input').focus();
    }
    function close(){
      overlay.classList.remove('active');
      document.body.style.overflow = '';
      if(lastFocused) lastFocused.focus();
    }

    if(closeBtn) closeBtn.addEventListener('click', close);
    overlay.addEventListener('click', function(e){
      if(e.target === overlay) close();
    });
    document.addEventListener('keydown', function(e){
      if(e.key === 'Escape' && overlay.classList.contains('active')) close();
    });

    form.addEventListener('submit', function(e){
      e.preventDefault();
      var allValid = REQUIRED.map(validateField).every(Boolean) && validateField('email');
      if(!allValid){
        var firstInvalid = REQUIRED.find(function(key){ return !validateField(key); });
        if(firstInvalid){
          var group = fieldGroup(firstInvalid);
          var input = group.querySelector('input');
          if(input) input.focus();
        }
        if(statusEl){
          statusEl.textContent = 'A few fields still need your attention above.';
          statusEl.className = 'form-status error';
        }
        return;
      }
      submitBtn.disabled = true;
      submitBtn.textContent = 'Sending…';

      var websiteEl = form.querySelector('#qe-website');
      var tokenEl = form.querySelector('input[name="__RequestVerificationToken"]');

      var body = new URLSearchParams();
      body.set('FullName', fieldGroup('name').querySelector('input').value.trim());
      body.set('Phone', fieldGroup('phone').querySelector('input').value.trim());
      body.set('HotelName', fieldGroup('company') ? fieldGroup('company').querySelector('input').value.trim() : '');
      body.set('Email', fieldGroup('email') ? fieldGroup('email').querySelector('input').value.trim() : '');
      body.set('RoomsRange', selectedRooms);
      body.set('Website', websiteEl ? websiteEl.value : '');
      body.set('FormType', 'quick');
      if(tokenEl) body.set('__RequestVerificationToken', tokenEl.value);

      fetch('/contact/submit', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-Requested-With': 'XMLHttpRequest' },
        body: body.toString()
      })
        .then(function(res){ return res.json().then(function(data){ return { ok: res.ok, data: data }; }); })
        .then(function(result){
          submitBtn.disabled = false;
          submitBtn.textContent = submitLabel;
          if(!result.ok){
            if(statusEl){
              statusEl.textContent = (result.data && result.data.message) || 'Something went wrong, please try again or call us directly.';
              statusEl.className = 'form-status error';
            }
            return;
          }
          form.reset();
          form.querySelectorAll('.field').forEach(function(g){ g.classList.remove('valid', 'invalid'); });
          form.querySelectorAll('.field-msg').forEach(function(m){ m.innerHTML = ''; });
          if(statusEl){
            statusEl.textContent = (result.data && result.data.message) || 'Thanks, your message is on its way to our team.';
            statusEl.className = 'form-status success';
          }
          window.setTimeout(close, 1800);
        })
        .catch(function(){
          submitBtn.disabled = false;
          submitBtn.textContent = submitLabel;
          if(statusEl){
            statusEl.textContent = 'Could not reach the server, please check your connection and try again.';
            statusEl.className = 'form-status error';
          }
        });
    });

    /* Open immediately when the homepage loads. A 0ms timeout (rather than
       calling open() inline) still lets the overlay element exist/paint
       first, so the CSS transition it animates in with actually runs. */
    window.setTimeout(open, 0);
  }

  /* ---------- Login form (UI only, never authenticates) ---------- */
  function initLoginForm(){
    var form = document.querySelector('#login-form');
    if(!form) return;
    form.addEventListener('submit', function(e){
      e.preventDefault();
      var btn = form.querySelector('button[type="submit"]');
      var note = form.querySelector('.login-note');
      btn.disabled = true;
      btn.textContent = 'Checking…';
      setTimeout(function(){
        btn.disabled = false;
        btn.textContent = 'Log In';
        if(note){
          note.classList.add('show');
        }
      }, 800);
    });
  }

  /* ---------- Pricing toggle (per-room / per-property) ---------- */
  function initPricingToggle(){
    var toggle = document.querySelector('#pricing-toggle');
    if(!toggle) return;
    var labels = document.querySelectorAll('[data-price-mode]');
    toggle.addEventListener('change', function(){
      var mode = toggle.checked ? 'annual' : 'monthly';
      labels.forEach(function(el){
        el.textContent = el.getAttribute('data-price-mode-' + mode) || el.textContent;
      });
      document.querySelectorAll('.toggle-label').forEach(function(l){
        l.classList.toggle('active', l.getAttribute('data-for') === mode);
      });
    });
  }

  /* ---------- FAQ accordion ---------- */
  function initFAQ(){
    document.querySelectorAll('.faq-item').forEach(function(item){
      var q = item.querySelector('.faq-q');
      if(!q) return;
      q.addEventListener('click', function(){
        var isOpen = item.classList.contains('open');
        item.closest('.faq-list').querySelectorAll('.faq-item').forEach(function(i){ i.classList.remove('open'); });
        if(!isOpen) item.classList.add('open');
      });
    });
  }

  /* ---------- Occupancy ring animation trigger ---------- */
  function initOccRings(){
    var rings = document.querySelectorAll('.occ-ring .fg');
    if(!rings.length) return;
    var io = new IntersectionObserver(function(entries){
      entries.forEach(function(entry){
        if(entry.isIntersecting){
          var el = entry.target;
          var offset = el.getAttribute('data-offset') || 60;
          requestAnimationFrame(function(){ el.style.strokeDashoffset = offset; });
          io.unobserve(el);
        }
      });
    }, {threshold:0.5});
    rings.forEach(function(r){ io.observe(r); });
  }

  /* ---------- Blog filters ---------- */
  function initBlogFilters(){
    var filters = document.querySelectorAll('.blog-filter');
    var cards = document.querySelectorAll('.blog-card');
    if(!filters.length || !cards.length) return;
    filters.forEach(function(btn){
      btn.addEventListener('click', function(){
        filters.forEach(function(b){ b.classList.remove('active'); });
        btn.classList.add('active');
        var filter = btn.getAttribute('data-filter');
        cards.forEach(function(card){
          var match = filter === 'all' || card.getAttribute('data-category') === filter;
          card.classList.toggle('blog-hide', !match);
        });
      });
    });
  }

  /* ---------- Announcement bar (dismissible, remembered for the session) ---------- */
  function initAnnounceBar(){
    var bar = document.querySelector('.announce-bar');
    if(!bar) return;
    var closeBtn = bar.querySelector('.announce-bar__close');
    if(sessionStorage.getItem('announceDismissed') === '1'){
      bar.classList.add('hide');
      return;
    }
    if(closeBtn){
      closeBtn.addEventListener('click', function(){
        bar.classList.add('hide');
        sessionStorage.setItem('announceDismissed', '1');
      });
    }
  }

  /* ---------- Cookie consent notice (site-wide, remembered once dismissed) ---------- */
  function initCookieNotice(){
    if(localStorage.getItem('cookieNoticeDismissed') === '1') return;
    if(document.querySelector('.cookie-notice')) return;

    // main.js is shared by every page at every folder depth (root, products/,
    // blog-articles/), so the link to the privacy policy has to be relative
    // to wherever this particular page actually lives, not hardcoded.
    var depth = /\/(products|blog-articles)\//.test(location.pathname) ? '../' : '';

    var bar = document.createElement('div');
    bar.className = 'cookie-notice';
    bar.setAttribute('role', 'region');
    bar.setAttribute('aria-label', 'Cookie notice');
    bar.innerHTML =
      '<p>We use cookies to improve your user experience. Cookies are small text files that are saved on your computer or mobile device when you visit the site. ' +
      '<a href="' + depth + 'privacy-policy.html">Read more</a></p>' +
      '<button type="button" class="cookie-notice__close" aria-label="Dismiss">Got it</button>';
    document.body.appendChild(bar);

    bar.querySelector('.cookie-notice__close').addEventListener('click', function(){
      bar.classList.add('cookie-notice--hide');
      localStorage.setItem('cookieNoticeDismissed', '1');
      setTimeout(function(){ bar.remove(); }, 300);
    });
  }

  /* ---------- init ---------- */
  document.addEventListener('DOMContentLoaded', function(){
    markActiveNav();
    initNavDockPlatform();
    initNavDockSolutions();
    initDock();
    initPlatformMenu();
    initSolutionsMenu();
    initTopbarBurger();
    initProgress();
    initReveal();
    initCounters();
    initKineticTypography();
    initDeptTabs();
    initAppFeatureReadMore();
    initCapTabs();
    initEngineList();
    initMagnetic();
    initGridSpotlight();
    initHeroDemo();
    initFloatyCycle();
    initProductModal();
    initContactForm();
    initQuickEnquiryPopup();
    initLoginForm();
    initPricingToggle();
    initFAQ();
    initOccRings();
    initBlogFilters();
    initAnnounceBar();
    initCookieNotice();
  });
})();
