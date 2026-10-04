// Runs before first paint (loaded in <head>) so the page never flashes the
// wrong theme. The choice is stored per browser; with none stored, the page
// follows the system setting. app.js owns the toggle button.
(function () {
  var theme = null;
  try { theme = localStorage.getItem('ax206-theme'); } catch (e) { /* storage blocked */ }
  if (theme !== 'light' && theme !== 'dark') {
    theme = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  document.documentElement.setAttribute('data-theme', theme);
})();
