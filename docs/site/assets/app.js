'use strict';

/*
 * DropWheel download page.
 * - Language toggle (EN / العربية) remembered in localStorage.
 * - SHA-256 is never hard-coded: it is read from the release's SHA256SUMS.txt.
 *   If the request fails (CORS, offline, release not published yet) the page
 *   shows a link to SHA256SUMS.txt instead of a possibly wrong hash.
 */
(function () {
  var LANG_KEY = 'dropwheel.download.lang';
  var SUMS_URL = 'https://github.com/Faisaldhd/dropwheel/releases/latest/download/SHA256SUMS.txt';
  var TITLES = {
    en: 'DropWheel — Download for Windows',
    ar: 'DropWheel — التحميل لنظام ويندوز'
  };
  var SETUP_LINE = /^([0-9a-f]{64})\s+\*?(DropWheel-Setup(?:-[0-9A-Za-z.]+)?\.exe)\s*$/i;
  var CHECKSUM_TIMEOUT_MS = 8000;

  function readLanguage() {
    try {
      var value = window.localStorage.getItem(LANG_KEY);
      return value === 'ar' || value === 'en' ? value : null;
    } catch (error) {
      return null;
    }
  }

  function saveLanguage(lang) {
    try {
      window.localStorage.setItem(LANG_KEY, lang);
    } catch (error) {
      /* Storage can be unavailable (private mode, blocked cookies); the toggle still works. */
    }
  }

  function applyLanguage(lang, persist) {
    var isArabic = lang === 'ar';
    var root = document.documentElement;

    root.lang = isArabic ? 'ar' : 'en';
    root.dir = isArabic ? 'rtl' : 'ltr';
    document.title = isArabic ? TITLES.ar : TITLES.en;

    var blocks = document.querySelectorAll('[data-language]');
    for (var i = 0; i < blocks.length; i += 1) {
      blocks[i].hidden = blocks[i].getAttribute('data-language') !== lang;
    }

    var buttons = document.querySelectorAll('[data-lang-button]');
    for (var j = 0; j < buttons.length; j += 1) {
      var active = buttons[j].getAttribute('data-lang-button') === lang;
      buttons[j].setAttribute('aria-pressed', active ? 'true' : 'false');
    }

    if (persist) {
      saveLanguage(lang);
    }
  }

  function setStatus(block, text) {
    var status = block.querySelector('[data-hash-status]');
    if (!status) {
      return;
    }
    status.textContent = text || '';
    status.hidden = !text;
  }

  function showChecksum(blocks, hash, fileName) {
    for (var i = 0; i < blocks.length; i += 1) {
      var block = blocks[i];
      var value = block.querySelector('[data-hash-value]');
      var file = block.querySelector('[data-hash-file]');
      var fallback = block.querySelector('[data-hash-fallback]');

      if (value) {
        value.textContent = hash;
        value.hidden = false;
      }
      if (file) {
        file.textContent = fileName;
        file.hidden = false;
      }
      if (fallback) {
        fallback.hidden = true;
      }
      setStatus(block, '');
    }
  }

  function showChecksumFallback(blocks) {
    for (var i = 0; i < blocks.length; i += 1) {
      var block = blocks[i];
      var value = block.querySelector('[data-hash-value]');
      var file = block.querySelector('[data-hash-file]');
      var fallback = block.querySelector('[data-hash-fallback]');

      if (value) {
        value.textContent = '';
        value.hidden = true;
      }
      if (file) {
        file.textContent = '';
        file.hidden = true;
      }
      if (fallback) {
        fallback.hidden = false;
      }
      setStatus(block, '');
    }
  }

  function findSetupLine(text) {
    var lines = text.split(/\r?\n/);
    var match = null;

    for (var i = 0; i < lines.length; i += 1) {
      var candidate = SETUP_LINE.exec(lines[i].trim());
      if (!candidate) {
        continue;
      }
      /* Prefer the unversioned alias when the release provides one. */
      if (!match || candidate[2].toLowerCase() === 'dropwheel-setup.exe') {
        match = candidate;
      }
    }

    return match;
  }

  function loadChecksum() {
    var blocks = document.querySelectorAll('[data-checksum]');
    if (!blocks.length) {
      return;
    }

    for (var i = 0; i < blocks.length; i += 1) {
      setStatus(blocks[i], blocks[i].getAttribute('data-status-checking') || '');
    }

    var controller = typeof window.AbortController === 'function' ? new window.AbortController() : null;
    var timer = controller
      ? window.setTimeout(function () {
          controller.abort();
        }, CHECKSUM_TIMEOUT_MS)
      : 0;

    function finish() {
      if (timer) {
        window.clearTimeout(timer);
        timer = 0;
      }
    }

    try {
      window
        .fetch(SUMS_URL, {
          cache: 'no-store',
          credentials: 'omit',
          referrerPolicy: 'no-referrer',
          signal: controller ? controller.signal : undefined
        })
        .then(function (response) {
          if (!response.ok) {
            throw new Error('HTTP ' + response.status);
          }
          return response.text();
        })
        .then(function (text) {
          var match = findSetupLine(text);
          if (!match) {
            throw new Error('No DropWheel-Setup line in SHA256SUMS.txt');
          }
          showChecksum(blocks, match[1].toLowerCase(), match[2]);
        })
        .catch(function () {
          showChecksumFallback(blocks);
        })
        .then(finish);
    } catch (error) {
      finish();
      showChecksumFallback(blocks);
    }
  }

  var storedLanguage = readLanguage();
  applyLanguage(storedLanguage || 'en', false);

  var buttons = document.querySelectorAll('[data-lang-button]');
  for (var b = 0; b < buttons.length; b += 1) {
    buttons[b].addEventListener('click', function () {
      var lang = this.getAttribute('data-lang-button');
      if (lang === 'en' || lang === 'ar') {
        applyLanguage(lang, true);
      }
    });
  }

  window.addEventListener('storage', function (event) {
    if (event.key === LANG_KEY && (event.newValue === 'en' || event.newValue === 'ar')) {
      applyLanguage(event.newValue, false);
    }
  });

  loadChecksum();
})();
