(function () {
  var root = document.documentElement;

  function systemTheme() {
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  }

  function savedTheme() {
    try { return localStorage.getItem('voteglass-theme'); } catch (e) { return null; }
  }

  function currentTheme() {
    return savedTheme() || systemTheme();
  }

  function apply(theme) {
    root.setAttribute('data-theme', theme);
  }

  // Apply before the page paints
  apply(currentTheme());

  // Blazor's page navigation can wipe the attribute, so put it back if that happens
  new MutationObserver(function () {
    var value = root.getAttribute('data-theme');
    if (value !== 'light' && value !== 'dark') {
      apply(currentTheme());
    }
  }).observe(root, { attributes: true, attributeFilter: ['data-theme'] });

  window.voteGlassTheme = {
    toggle: function () {
      var next = root.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
      apply(next);
      try { localStorage.setItem('voteglass-theme', next); } catch (e) {}
    }
  };

  // ----- Copy button (used on the Test inbox) -----
  window.voteGlassCopy = function (btn) {
    var text = btn.getAttribute('data-code');
    if (!text || !navigator.clipboard) return;
    navigator.clipboard.writeText(text).then(function () {
      var old = btn.textContent;
      btn.textContent = 'Copied!';
      setTimeout(function () { btn.textContent = old; }, 1500);
    });
  };

  // ----- Show/hide button on every password box -----
  var EYE = '<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>';
  var EYE_OFF = '<svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>';

  function addEyes() {
    document.querySelectorAll('input[type="password"]:not([data-pw-eye])').forEach(function (input) {
      input.setAttribute('data-pw-eye', '1');
      if (input.parentNode.classList.contains('gl-pw')) return;

      var wrap = document.createElement('div');
      wrap.className = 'gl-pw';
      input.parentNode.insertBefore(wrap, input);
      wrap.appendChild(input);

      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'gl-eye';
      btn.setAttribute('aria-label', 'Show password');
      btn.innerHTML = EYE;
      btn.addEventListener('click', function () {
        var show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        btn.innerHTML = show ? EYE_OFF : EYE;
        btn.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
      });
      wrap.appendChild(btn);
    });
  }

  function start() {
    addEyes();
    // Blazor swaps page content when you navigate, so check again whenever the page changes
    new MutationObserver(addEyes).observe(document.body, { childList: true, subtree: true });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();