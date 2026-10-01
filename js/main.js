document.addEventListener('DOMContentLoaded', () => {
  const mobileToggle = document.querySelector('.nav-toggle');
  const mobileNav = document.querySelector('.mobile-nav');

  if (mobileToggle && mobileNav) {
    mobileToggle.addEventListener('click', () => {
      const open = mobileNav.classList.toggle('is-open');
      mobileToggle.setAttribute('aria-expanded', String(open));
    });
    mobileNav.querySelectorAll('a').forEach(link => link.addEventListener('click', () => {
      mobileNav.classList.remove('is-open');
      mobileToggle.setAttribute('aria-expanded', 'false');
    }));
  }

  document.addEventListener('click', event => {
    const anchor = event.target instanceof Element ? event.target.closest('a') : null;
    if (!anchor || !window.posthog || typeof window.posthog.capture !== 'function') return;

    if (typeof isWindowsInstallerDownloadUrl === 'function' && isWindowsInstallerDownloadUrl(anchor.href)) {
      window.posthog.capture('WebsiteDownloadClicked', { platform: 'windows' });
      return;
    }

    try {
      if (new URL(anchor.href, location.href).hostname.toLowerCase() === 'github.com') {
        window.posthog.capture('WebsiteGitHubClicked');
      }
    } catch { }
  });

  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const revealEls = document.querySelectorAll('.reveal');
  if (reduced || !('IntersectionObserver' in window)) {
    revealEls.forEach(el => el.classList.add('is-visible'));
  } else {
    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          observer.unobserve(entry.target);
        }
      });
    }, { threshold: 0.12 });
    revealEls.forEach(el => observer.observe(el));
  }
});
