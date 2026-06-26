import { defineConfig } from 'vitepress'

// Docs site for KRINT, built from the markdown in this folder. Served at the domain root on
// Cloudflare Pages, so no `base` is needed. Build: `vitepress build` (output: .vitepress/dist).
export default defineConfig({
  title: 'KRINT',
  description: 'Self-hosted database-provisioning platform. One click. One key. Your database is ready.',
  lastUpdated: true,
  cleanUrls: true,
  // README-style links elsewhere point at "docs/*.md"; inside the site links resolve fine, but keep
  // the build from failing on the odd absolute/anchor link.
  ignoreDeadLinks: true,
  head: [
    ['link', { rel: 'icon', href: '/favicon.svg' }],
  ],
  themeConfig: {
    nav: [
      { text: 'Self-host', link: '/self-host' },
      { text: 'Engines', link: '/engines' },
      { text: 'Developer setup', link: '/dev-setup' },
      { text: 'Nodes', link: '/nodes' },
    ],
    // Grouped Diátaxis-style: get-it-running first, task guides next, concepts last.
    sidebar: [
      {
        text: 'Get started',
        items: [
          { text: 'Self-hosting', link: '/self-host' },
          { text: 'Desktop app', link: '/desktop' },
          { text: 'Supported engines', link: '/engines' },
        ],
      },
      {
        text: 'Guides',
        items: [
          { text: 'Declarative instances', link: '/declarative-instances' },
          { text: 'Developer setup', link: '/dev-setup' },
        ],
      },
      {
        text: 'Concepts',
        items: [
          { text: 'Nodes', link: '/nodes' },
        ],
      },
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/PianoNic/KRINT' },
    ],
    search: { provider: 'local' },
    editLink: {
      pattern: 'https://github.com/PianoNic/KRINT/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
    footer: {
      message: 'Made with care by PianoNic.',
      copyright: 'KRINT',
    },
  },
})
