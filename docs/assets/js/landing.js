// The landing page's motion (index.html): what rises into view as you scroll, the hero's window settling flat, the tour's
// index following the step in view, and the header's edge once the page has scrolled. Without JavaScript everything just
// shows, and with reduced motion nothing moves (landing.css).
(function () {
  var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var watch = 'IntersectionObserver' in window;
  var rising = document.querySelectorAll('[data-reveal]');

  if (reduce || !watch) {
    rising.forEach(function (element) { element.classList.add('is-in'); });
  } else {
    var reveal = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-in');
          reveal.unobserve(entry.target);
        }
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.1 });
    rising.forEach(function (element) { reveal.observe(element); });
  }

  var links = document.querySelectorAll('.story-index a');
  if (watch && links.length) {
    var current = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        links.forEach(function (link) {
          link.classList.toggle('is-current', link.getAttribute('href') === '#' + entry.target.id);
        });
      });
    }, { rootMargin: '-45% 0px -50% 0px' });
    document.querySelectorAll('.story-step').forEach(function (step) { current.observe(step); });
  }

  var bar = document.querySelector('.topbar');
  function edge() { bar.classList.toggle('is-scrolled', window.scrollY > 8); }
  window.addEventListener('scroll', edge, { passive: true });
  edge();
})();
