/* Early theme and language decision.
   Loaded synchronously in <head> so the first paint already has the right
   palette and direction. No network access, no cookies: localStorage only. */
(function () {
  'use strict';

  var root = document.documentElement;
  var THEME_KEY = 'dropwheel.site.theme';
  var LANG_KEY = 'dropwheel.site.lang';
  var storedTheme = null;
  var storedLang = null;

  try {
    storedTheme = window.localStorage.getItem(THEME_KEY);
    storedLang = window.localStorage.getItem(LANG_KEY);
  } catch (error) {
    storedTheme = null;
    storedLang = null;
  }

  var theme = storedTheme === 'light' || storedTheme === 'dark' ? storedTheme : null;
  if (!theme) {
    theme = window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    root.setAttribute('data-theme-locked', 'false');
  } else {
    root.setAttribute('data-theme-locked', 'true');
  }

  var lang = storedLang === 'ar' || storedLang === 'en' ? storedLang : 'en';

  root.setAttribute('data-theme', theme);
  root.setAttribute('lang', lang);
  root.setAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
  root.classList.remove('no-js');
  root.classList.add('js');
})();
