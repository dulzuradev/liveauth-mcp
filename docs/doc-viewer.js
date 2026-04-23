// doc-viewer.js — client-side markdown renderer for docs.liveauth.app
// Hash-based routing (#doc/filename) so Caddy always serves index.html

(async function() {
  const MARKDOWN_FILES = [
    { path: 'GETTING-STARTED.md',           title: 'Getting Started',        icon: 'rocket_launch' },
    { path: 'L402-MACAROON-SPEC.md',       title: 'L402 Macaroon Spec',     icon: 'vpn_key' },
    { path: 'add-l402-to-any-mcp-tool.md', title: 'Add L402 to MCP (5 min)', icon: 'bolt' },
    { path: 'mcp-liveauth-gate.md',        title: 'MCP Gate Design',        icon: 'hub' },
    { path: 'demo.html',                   title: 'Live Demo',              icon: 'play_arrow' },
  ];

  function getCurrentDoc() {
    const hash = window.location.hash;
    if (!hash || hash === '#' || hash === '#/') return 'GETTING-STARTED.md';
    const match = hash.match(/^#doc\/(.+)$/);
    return match ? decodeURIComponent(match[1]) : 'GETTING-STARTED.md';
  }

  async function loadDoc(docPath) {
    const contentEl = document.getElementById('doc-content');
    const titleEl  = document.getElementById('doc-title');
    const crumbEl  = document.getElementById('doc-breadcrumb');

    let normalized = (docPath === 'index.html' || docPath === 'index.md') ? 'GETTING-STARTED.md' : docPath;

    const doc = MARKDOWN_FILES.find(d => d.path === normalized);
    const title = doc ? doc.title : normalized.replace(/\.md$/, '').replace(/-/g, ' ');
    if (titleEl)  titleEl.textContent = title;
    if (crumbEl)  crumbEl.textContent = title;
    if (contentEl) contentEl.innerHTML = '<p style="color:var(--text-secondary);">Loading...</p>';

    await loadScript('https://cdn.jsdelivr.net/npm/marked/marked.min.js');

    // Handle standalone HTML pages (e.g. demo.html) via iframe
    if (normalized === 'demo.html') {
      if (contentEl) {
        contentEl.innerHTML = '<div class="doc-body" style="padding:0;height:calc(100vh - 160px);"><iframe src="demo.html" style="width:100%;height:100%;border:none;border-radius:8px;" loading="lazy"></iframe></div>';
      }
      return;
    }

    try {
      const res = await fetch(normalized);
      if (!res.ok) {
        if (contentEl) contentEl.innerHTML = '<div class="error-state"><span class="material-icons" style="font-size:48px;color:var(--btc-orange);">error_outline</span><h3>Document not found</h3><p>' + normalized + ' not found.</p><a href="#doc/GETTING-STARTED.md">Go to Getting Started →</a></div>';
        return;
      }
      const md  = await res.text();
      const html = marked.parse(md);
      if (contentEl) contentEl.innerHTML = '<div class="doc-body markdown-body">' + html + '</div>';

      // Add copy buttons to code blocks
      document.querySelectorAll('.doc-body pre').forEach(pre => {
        const wrapper = document.createElement('div');
        wrapper.className = 'code-wrapper';
        pre.parentNode.insertBefore(wrapper, pre);
        wrapper.appendChild(pre);

        const btn = document.createElement('button');
        btn.className = 'copy-btn';
        btn.innerHTML = '<span class="material-icons">content_copy</span>';
        btn.title = 'Copy code';
        btn.onclick = () => {
          navigator.clipboard.writeText(pre.textContent);
          btn.innerHTML = '<span class="material-icons">check</span>';
          setTimeout(() => { btn.innerHTML = '<span class="material-icons">content_copy</span>'; }, 2000);
        };
        wrapper.appendChild(btn);
      });

    } catch (err) {
      if (contentEl) contentEl.innerHTML = '<div class="error-state"><span class="material-icons" style="font-size:48px;color:var(--btc-orange);">wifi_off</span><h3>Failed to load</h3><p>' + err.message + '</p></div>';
    }
  }

  function buildSidebar() {
    const sidebar = document.getElementById('doc-nav');
    if (!sidebar) return;
    const current = getCurrentDoc();
    const iconMap = { rocket_launch:'rocket_launch', vpn_key:'vpn_key', bolt:'bolt', hub:'hub', play_arrow:'play_arrow' };
    sidebar.innerHTML = MARKDOWN_FILES.map(doc => {
      const active = doc.path === current;
      return '<a href="#doc/' + encodeURIComponent(doc.path) + '" ' +
             'class="nav-item' + (active ? ' active' : '') + '" ' +
             'onclick="event.preventDefault();window.location.hash=\'doc/' + encodeURIComponent(doc.path) + '\'">' +
             '<span class="material-icons nav-icon">' + (iconMap[doc.icon] || 'article') + '</span>' +
             '<span>' + doc.title + '</span></a>';
    }).join('');
  }

  function loadScript(src) {
    return new Promise((resolve, reject) => {
      if (document.querySelector('script[src="' + src + '"]')) { resolve(); return; }
      const s = document.createElement('script');
      s.src = src; s.onload = resolve; s.onerror = reject;
      document.head.appendChild(s);
    });
  }

  buildSidebar();
  loadDoc(getCurrentDoc());
  window.addEventListener('hashchange', () => { buildSidebar(); loadDoc(getCurrentDoc()); });
})();
