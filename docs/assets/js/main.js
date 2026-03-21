// RecordIt Documentation - Main JS
document.addEventListener('DOMContentLoaded', () => {
  // Smooth scroll for anchor links
  document.querySelectorAll('a[href^="#"]').forEach(a => {
    a.addEventListener('click', e => {
      const id = a.getAttribute('href').slice(1);
      const el = document.getElementById(id);
      if (el) {
        e.preventDefault();
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
    });
  });

  // Active nav link
  const path = window.location.pathname;
  document.querySelectorAll('.nav-links a').forEach(a => {
    a.classList.toggle('active', a.getAttribute('href') === path.split('/').pop() ||
      (path.endsWith('/') && a.getAttribute('href') === 'index.html'));
  });

  // Copy code blocks
  document.querySelectorAll('.code-block').forEach(block => {
    const btn = document.createElement('button');
    btn.textContent = 'Copy';
    btn.style.cssText = `
      position: absolute; top: 8px; right: 8px;
      background: rgba(99,102,241,0.2); border: 1px solid rgba(99,102,241,0.3);
      color: #a3a3a3; border-radius: 4px; padding: 2px 8px;
      font-size: 11px; cursor: pointer; font-family: inherit;
    `;
    block.style.position = 'relative';
    block.appendChild(btn);
    btn.addEventListener('click', () => {
      navigator.clipboard.writeText(block.textContent.replace('Copy', '').trim());
      btn.textContent = 'Copied!';
      setTimeout(() => btn.textContent = 'Copy', 2000);
    });
  });

  // Intersection observer for animations
  const observer = new IntersectionObserver(
    entries => entries.forEach(e => {
      if (e.isIntersecting) {
        e.target.style.opacity = '1';
        e.target.style.transform = 'translateY(0)';
      }
    }),
    { threshold: 0.1 }
  );

  document.querySelectorAll('.feature-card, .app-card, .install-card').forEach(el => {
    el.style.opacity = '0';
    el.style.transform = 'translateY(20px)';
    el.style.transition = 'opacity 0.4s ease, transform 0.4s ease';
    observer.observe(el);
  });
});
