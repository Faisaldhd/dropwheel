'use strict';

/*
 * DropWheel site behaviour.
 * - Theme and language switches (remembered in localStorage).
 * - Tool filters, motion clips and a copy button.
 * No analytics, no cookies, no network requests of any kind.
 */
(function () {
  var root = document.documentElement;
  var THEME_KEY = 'dropwheel.site.theme';
  var LANG_KEY = 'dropwheel.site.lang';
  var TOTAL_TOOLS = 26;

  var META = {
    en: {
      title: 'DropWheel — 26 file tools at your cursor for Windows',
      description: 'DropWheel puts 26 file tools on one radial wheel: convert, crop, edit, compress, protect and lock. Everything runs on your PC. Free and open source for Windows 10/11 64-bit.'
    },
    ar: {
      title: 'DropWheel — 26 أداة ملفات عند مؤشرك لويندوز',
      description: 'يضع DropWheel 26 أداة ملفات على عجلة واحدة: تحويل واقتصاص وتعديل وضغط وحماية وقفل. كل شيء يعمل على جهازك. مجاني ومفتوح المصدر لويندوز 10/11 64 بت.'
    }
  };

  var THEME_COLORS = { dark: '#120D1A', light: '#F5F1FB' };
  var reduceQuery = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
  var lightQuery = window.matchMedia ? window.matchMedia('(prefers-color-scheme: light)') : null;

  function isReduced() {
    return !!(reduceQuery && reduceQuery.matches);
  }

  function currentTheme() {
    return root.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
  }

  function currentLang() {
    return root.getAttribute('lang') === 'ar' ? 'ar' : 'en';
  }

  function isLocked() {
    return root.getAttribute('data-theme-locked') === 'true';
  }

  function store(key, value) {
    try {
      window.localStorage.setItem(key, value);
    } catch (error) {
      /* Private mode: the toggle still works for this page view. */
    }
  }

  function setMeta(attribute, name, value) {
    var element = document.querySelector('meta[' + attribute + '="' + name + '"]');
    if (element && value) {
      element.setAttribute('content', value);
    }
  }

  /* ---------------------------------------------------------------- theme */

  function themeButtonLabel() {
    if (currentTheme() === 'dark') {
      return currentLang() === 'ar' ? 'التبديل إلى المظهر الفاتح' : 'Switch to light theme';
    }
    return currentLang() === 'ar' ? 'التبديل إلى المظهر الداكن' : 'Switch to dark theme';
  }

  function paintThemeImages() {
    var dark = currentTheme() === 'dark';

    var images = document.querySelectorAll('img[data-dark][data-light]');
    for (var i = 0; i < images.length; i += 1) {
      var image = images[i];
      var source = dark ? image.getAttribute('data-dark') : image.getAttribute('data-light');
      var media = image.closest ? image.closest('.tool-media') : null;
      if (media) {
        var play = media.querySelector('.tool-play');
        if (play && play.getAttribute('aria-pressed') === 'true') {
          var clip = dark ? play.getAttribute('data-clip-dark') : play.getAttribute('data-clip-light');
          if (clip) {
            source = clip;
          }
        }
      }
      if (source && image.getAttribute('src') !== source) {
        image.setAttribute('src', source);
      }
    }

    var pictures = document.querySelectorAll('picture[data-motion]');
    for (var j = 0; j < pictures.length; j += 1) {
      paintMotionPicture(pictures[j]);
    }
  }

  function addSource(picture, media, source) {
    var element = document.createElement('source');
    element.setAttribute('type', 'image/webp');
    if (media) {
      element.setAttribute('media', media);
    }
    element.setAttribute('srcset', source);
    var image = picture.querySelector('img');
    picture.insertBefore(element, image);
  }

  function paintMotionPicture(picture) {
    var image = picture.querySelector('img');
    if (!image) {
      return;
    }
    var dark = currentTheme() === 'dark';
    var poster = picture.getAttribute(dark ? 'data-poster-dark' : 'data-poster-light');
    var clip = picture.getAttribute(dark ? 'data-clip-dark' : 'data-clip-light');

    var old = picture.querySelectorAll('source');
    for (var i = 0; i < old.length; i += 1) {
      old[i].parentNode.removeChild(old[i]);
    }

    if (isReduced() || !clip) {
      addSource(picture, null, poster);
      image.setAttribute('src', poster);
      return;
    }

    addSource(picture, '(prefers-reduced-motion: reduce)', poster);
    addSource(picture, null, clip);
    image.setAttribute('src', poster);
  }

  function setTheme(theme, persist) {
    var next = theme === 'light' ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    setMeta('name', 'theme-color', THEME_COLORS[next]);
    if (persist) {
      root.setAttribute('data-theme-locked', 'true');
      store(THEME_KEY, next);
    }
    paintThemeImages();
    applyLabels();
  }

  /* ---------------------------------------------------------------- language */

  function applyLabels() {
    var lang = currentLang();
    var arabic = lang === 'ar';

    var labelled = document.querySelectorAll('[data-label-en]');
    for (var i = 0; i < labelled.length; i += 1) {
      var element = labelled[i];
      if (element.hasAttribute('data-theme-toggle')) {
        continue;
      }
      var text = arabic ? element.getAttribute('data-label-ar') : element.getAttribute('data-label-en');
      if (text) {
        element.setAttribute('aria-label', text);
      }
    }

    var images = document.querySelectorAll('img[data-alt-en]');
    for (var j = 0; j < images.length; j += 1) {
      var alt = arabic ? images[j].getAttribute('data-alt-ar') : images[j].getAttribute('data-alt-en');
      if (alt) {
        images[j].setAttribute('alt', alt);
      }
    }

    var themeButton = document.querySelector('[data-theme-toggle]');
    if (themeButton) {
      themeButton.setAttribute('aria-label', themeButtonLabel());
    }

    var buttons = document.querySelectorAll('[data-lang-button]');
    for (var k = 0; k < buttons.length; k += 1) {
      buttons[k].setAttribute('aria-pressed', buttons[k].getAttribute('data-lang-button') === lang ? 'true' : 'false');
    }
  }

  function applyMeta(lang) {
    var meta = META[lang] || META.en;
    document.title = meta.title;
    setMeta('name', 'description', meta.description);
    setMeta('property', 'og:title', meta.title);
    setMeta('property', 'og:description', meta.description);
    setMeta('property', 'og:locale', lang === 'ar' ? 'ar_AR' : 'en_US');
    setMeta('property', 'og:locale:alternate', lang === 'ar' ? 'en_US' : 'ar_AR');
    setMeta('name', 'twitter:title', meta.title);
    setMeta('name', 'twitter:description', meta.description);
    setMeta('name', 'twitter:image:alt', meta.title);
    setMeta('property', 'og:image:alt', lang === 'ar'
      ? 'أيقونة DropWheel: عجلة بنفسجية مقسّمة مع سهم يهبط على أحد القطاعات.'
      : 'The DropWheel app icon: a violet segmented wheel with an arrow dropping onto one slice.');
  }

  function setLanguage(lang, persist) {
    var next = lang === 'ar' ? 'ar' : 'en';
    root.setAttribute('lang', next);
    root.setAttribute('dir', next === 'ar' ? 'rtl' : 'ltr');
    if (persist) {
      store(LANG_KEY, next);
    }
    applyMeta(next);
    applyLabels();
    updatePlayLabels();
    updateFilterStatus();
  }

  /* ---------------------------------------------------------------- filters */

  function setFilter(filter, announce) {
    var cards = document.querySelectorAll('.tool-card');
    var visible = 0;
    for (var i = 0; i < cards.length; i += 1) {
      var show = filter === 'all' || cards[i].getAttribute('data-cat') === filter;
      cards[i].hidden = !show;
      if (show) {
        visible += 1;
      }
    }

    var chips = document.querySelectorAll('[data-filter]');
    for (var j = 0; j < chips.length; j += 1) {
      var active = chips[j].getAttribute('data-filter') === filter;
      chips[j].classList.toggle('is-active', active);
      chips[j].setAttribute('aria-pressed', active ? 'true' : 'false');
    }

    if (announce) {
      updateFilterStatus();
    }
    return visible;
  }

  function updateFilterStatus() {
    var status = document.getElementById('tools-status');
    if (!status) {
      return;
    }
    var visible = 0;
    var cards = document.querySelectorAll('.tool-card');
    for (var i = 0; i < cards.length; i += 1) {
      if (!cards[i].hidden) {
        visible += 1;
      }
    }
    status.textContent = currentLang() === 'ar'
      ? 'عرض ' + visible + ' من ' + TOTAL_TOOLS + ' أداة'
      : 'Showing ' + visible + ' of ' + TOTAL_TOOLS + ' tools';
  }

  /* ---------------------------------------------------------------- motion */

  function playLabels(pressed) {
    var arabic = currentLang() === 'ar';
    if (pressed) {
      return {
        label: arabic ? 'إظهار الصورة الثابتة' : 'Show the still image',
        text: arabic ? 'إيقاف' : 'Still'
      };
    }
    return {
      label: arabic ? 'تشغيل الحركة' : 'Play motion',
      text: arabic ? 'حركة' : 'Motion'
    };
  }

  function paintPlayButton(button) {
    var pressed = button.getAttribute('aria-pressed') === 'true';
    var labels = playLabels(pressed);
    button.setAttribute('aria-label', labels.label);
    var text = button.querySelector('.tool-play__text');
    if (text) {
      text.textContent = labels.text;
    }
  }

  function setPlayState(button, pressed) {
    var media = button.closest ? button.closest('.tool-media') : null;
    var image = media ? media.querySelector('img') : null;
    var dark = currentTheme() === 'dark';

    button.setAttribute('aria-pressed', pressed ? 'true' : 'false');
    if (image) {
      var source = pressed
        ? button.getAttribute(dark ? 'data-clip-dark' : 'data-clip-light')
        : button.getAttribute(dark ? 'data-poster-dark' : 'data-poster-light');
      if (source) {
        image.setAttribute('src', source);
      }
    }
    paintPlayButton(button);
  }

  function updatePlayLabels() {
    var buttons = document.querySelectorAll('.tool-play');
    for (var i = 0; i < buttons.length; i += 1) {
      paintPlayButton(buttons[i]);
    }
  }

  function applyMotionPreference() {
    var buttons = document.querySelectorAll('.tool-play');
    for (var i = 0; i < buttons.length; i += 1) {
      buttons[i].hidden = isReduced();
      if (isReduced() && buttons[i].getAttribute('aria-pressed') === 'true') {
        setPlayState(buttons[i], false);
      }
    }
    paintThemeImages();
  }

  /* ---------------------------------------------------------------- copy */

  function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text);
    }
    return Promise.reject(new Error('clipboard unavailable'));
  }

  function wireCopy(button) {
    var target = document.getElementById(button.getAttribute('data-copy-target'));
    var status = document.getElementById('copy-status');
    if (!target) {
      return;
    }

    button.addEventListener('click', function () {
      var text = target.textContent.replace(/\s+/g, '');
      copyText(text).then(function () {
        var textNode = button.querySelector('.copy-button__text');
        if (textNode) {
          textNode.textContent = currentLang() === 'ar' ? 'تم النسخ' : 'Copied';
        }
        if (status) {
          status.textContent = currentLang() === 'ar' ? 'نُسخت البصمة إلى الحافظة.' : 'Hash copied to the clipboard.';
        }
        window.setTimeout(function () {
          if (textNode) {
            textNode.textContent = currentLang() === 'ar' ? 'نسخ' : 'Copy';
          }
          if (status) {
            status.textContent = '';
          }
        }, 2200);
      }).catch(function () {
        if (status) {
          status.textContent = currentLang() === 'ar'
            ? 'تعذّر النسخ. حدّد البصمة يدويًا.'
            : 'Copy failed. Select the hash manually.';
        }
      });
    });
  }

  /* ---------------------------------------------------------------- wiring */

  var themeToggle = document.querySelector('[data-theme-toggle]');
  if (themeToggle) {
    themeToggle.addEventListener('click', function () {
      setTheme(currentTheme() === 'dark' ? 'light' : 'dark', true);
    });
  }

  var languageButtons = document.querySelectorAll('[data-lang-button]');
  for (var l = 0; l < languageButtons.length; l += 1) {
    languageButtons[l].addEventListener('click', function () {
      var next = this.getAttribute('data-lang-button');
      if (next === 'en' || next === 'ar') {
        setLanguage(next, true);
      }
    });
  }

  var chips = document.querySelectorAll('[data-filter]');
  for (var c = 0; c < chips.length; c += 1) {
    chips[c].addEventListener('click', function () {
      setFilter(this.getAttribute('data-filter'), true);
    });
  }

  var plays = document.querySelectorAll('.tool-play');
  for (var p = 0; p < plays.length; p += 1) {
    plays[p].addEventListener('click', function () {
      setPlayState(this, this.getAttribute('aria-pressed') !== 'true');
    });
  }

  var copies = document.querySelectorAll('[data-copy-target]');
  for (var s = 0; s < copies.length; s += 1) {
    wireCopy(copies[s]);
  }

  var header = document.querySelector('.site-header');
  function paintHeader() {
    if (header) {
      header.classList.toggle('is-scrolled', window.scrollY > 8);
    }
  }
  window.addEventListener('scroll', paintHeader, { passive: true });
  paintHeader();

  var year = document.getElementById('year');
  if (year) {
    year.textContent = String(new Date().getFullYear());
  }

  if (reduceQuery) {
    if (reduceQuery.addEventListener) {
      reduceQuery.addEventListener('change', applyMotionPreference);
    } else if (reduceQuery.addListener) {
      reduceQuery.addListener(applyMotionPreference);
    }
  }

  if (lightQuery) {
    var onSystemTheme = function () {
      if (!isLocked()) {
        setTheme(lightQuery.matches ? 'light' : 'dark', false);
      }
    };
    if (lightQuery.addEventListener) {
      lightQuery.addEventListener('change', onSystemTheme);
    } else if (lightQuery.addListener) {
      lightQuery.addListener(onSystemTheme);
    }
  }

  /* ---------------------------------------------------------------- start */

  setTheme(currentTheme(), false);
  setLanguage(currentLang(), false);
  setFilter('all', false);
  updateFilterStatus();
  applyMotionPreference();
  updatePlayLabels();
})();
