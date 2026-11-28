namespace Suran.QQ;

/// <summary>BreezeTTS 的 br-* 组件样式（从 QChatUiAssets 的 breeze-radio.css 提取，设置页用到的部分）。</summary>
internal static class BreezeUiAssets
{
    public const string Css = """
.br-page {
  
  

  
  --br-radius: 21px;

  --br-sans: "Segoe UI Variable", "Segoe UI", "Microsoft YaHei UI", "PingFang SC", "Hiragino Sans GB", sans-serif;
  --br-serif: "Iowan Old Style", "Palatino Linotype", "Noto Serif CJK SC", "Songti SC", "SimSun", serif;
  --br-display: "Segoe UI Variable Display", "Segoe UI", "Arial Black", "Helvetica Neue", "PingFang SC", "Microsoft YaHei UI", sans-serif;
  --br-mono: "Cascadia Code", "SFMono-Regular", Consolas, "Liberation Mono", monospace;
  --br-ease: cubic-bezier(.22, 1, .36, 1);
  --br-quick: cubic-bezier(.2, 1, .3, 1);

  
  --br-intro-exit-at: 2300;
  --br-intro-exit-ms: 820;
  --br-intro-skip-ms: 180;

  position: relative;
  isolation: isolate;
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  min-height: 100%;
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
  color: var(--br-ink, #3b2533);
  background:
    radial-gradient(ellipse at 10% 8%, rgba(var(--br-accent-rgb, 224, 68, 141), .10), transparent 36%),
    radial-gradient(ellipse at 93% 2%, rgba(var(--br-secondary-rgb, 140, 92, 184), .09), transparent 42%),
    var(--br-bg, #fff1f7);
  color-scheme: var(--br-scheme, light);
  font-family: var(--br-sans, "Segoe UI", "Microsoft YaHei UI", "PingFang SC", sans-serif);
  font-size: 14px;
  line-height: 1.6;
  -webkit-font-smoothing: antialiased;
  -webkit-tap-highlight-color: transparent;
  scrollbar-width: thin;
  scrollbar-color: var(--br-line, #f2cfe0) transparent;
}

.br-page,
.br-page *,
.br-page *::before,
.br-page *::after { box-sizing: border-box; }

.br-page :where(button, input, textarea, select) { font: inherit; color: inherit; }

.br-page :where(button, select, summary) { cursor: pointer; }

.br-page :where(button) { border: 0; background: none; padding: 0; }

.br-page :where(button:disabled) { cursor: not-allowed; opacity: .55; }

.br-page :where(a) { color: var(--br-accent, #e0448d); text-decoration-thickness: 1px; text-underline-offset: 2px; }

.br-page :where(img, svg, video) { display: block; max-width: 100%; }

.br-page :where(img) { height: auto; }

.br-page :where(h1, h2, h3, h4, h5, h6) { margin: 0; line-height: 1.3; }

.br-page :where(p) { margin: 0; }

.br-page :where(small) { font-size: 11px; }

.br-page :where(hr) { border: 0; border-top: 1px dashed var(--br-line, #f2cfe0); margin: 18px 0; }

.br-page :where(ul, ol) { margin: 0; padding-left: 1.25em; }

.br-page :where([hidden]) { display: none !important; }

.br-page :where(:focus-visible) { outline: 2px solid var(--br-accent, #e0448d); outline-offset: 3px; }

.br-page ::selection { background: rgba(var(--br-accent-rgb, 224, 68, 141), .22); color: var(--br-ink, #3b2533); }

.br-page::-webkit-scrollbar { width: 10px; height: 10px; }

.br-page::-webkit-scrollbar-track { background: transparent; }

.br-page::-webkit-scrollbar-thumb {
  background: var(--br-line, #f2cfe0);
  border: 3px solid var(--br-bg, #fff1f7);
  border-radius: 999px;
}

.br-page .br-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 24px;
  padding: 4px 10px;
  border: 1px solid var(--br-line, #f2cfe0);
  border-radius: 999px;
  background: var(--br-field, #fff6fb);
  color: var(--br-muted, #7c6272);
  font-family: var(--br-mono, "Cascadia Code", "SFMono-Regular", Consolas, monospace);
  font-size: 9px;
  letter-spacing: .1em;
  line-height: 1;
  text-transform: uppercase;
  white-space: nowrap;
}

.br-page .br-badge::before {
  content: "";
  width: 5px;
  height: 5px;
  flex: 0 0 auto;
  border-radius: 50%;
  background: currentColor;
  opacity: .7;
}

.br-page .br-badge.br-ok { color: var(--br-accent, #e0448d); background: var(--br-mint, #e6f0ea); border-color: transparent; }

.br-page .br-badge.br-warn { color: var(--br-warn, #b26a3d); background: var(--br-paper, #fff9fc); border-color: var(--br-paper-edge, #f1d2e2); }

.br-page .br-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: clamp(14px, 2vw, 20px);
  align-items: start;
}

.br-page .br-grid > button.br-panel {
  text-align: left;
  cursor: pointer;
  transition: border-color .24s ease, transform .3s var(--br-ease, cubic-bezier(.22, 1, .36, 1));
}

.br-page .br-grid > button.br-panel:hover { border-color: var(--br-accent, #e0448d); transform: translateY(-2px); }

.br-page .br-grid > button.br-panel h3 {
  margin: 0 0 5px;
  font-family: var(--br-serif, "Iowan Old Style", "Palatino Linotype", "Noto Serif CJK SC", "Songti SC", "SimSun", serif);
  font-size: 18px;
  font-weight: 500;
  letter-spacing: -.02em;
}

.br-page .br-grid > button.br-panel p { color: var(--br-muted, #7c6272); font-size: 11px; line-height: 1.7; }

.br-page .br-grid > button.br-panel strong {
  display: block;
  margin-top: 10px;
  color: var(--br-accent, #e0448d);
  font-size: 11px;
  font-weight: 500;
}

.br-page .br-panel {
  position: relative;
  min-width: 0;
  padding: clamp(18px, 2vw, 24px);
  border: 1px solid var(--br-line-soft, #fbe2ee);
  border-radius: var(--br-radius, 21px);
  background: var(--br-surface, #fffafd);
}

.br-page .br-panel::before {
  content: "";
  position: absolute;
  inset: 0;
  border-radius: inherit;
  background: linear-gradient(180deg, rgba(255, 255, 255, .30), transparent 34%);
  pointer-events: none;
  opacity: .55;
}

.br-page .br-panel > * { position: relative; z-index: 1; }

.br-page .br-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
}

.br-page .br-panel-title {
  display: flex;
  align-items: center;
  gap: 9px;
  min-width: 0;
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  letter-spacing: -.01em;
}

.br-page .br-panel-title::before {
  content: "";
  width: 7px;
  height: 7px;
  flex: 0 0 auto;
  background: var(--br-accent, #e0448d);
  clip-path: polygon(50% 0, 58% 42%, 100% 50%, 58% 58%, 50% 100%, 42% 58%, 0 50%, 42% 42%);
}

.br-page .br-panel-title small {
  color: var(--br-dim, #a08494);
  font-family: var(--br-mono, "Cascadia Code", "SFMono-Regular", Consolas, monospace);
  font-size: 9px;
  font-weight: 400;
  letter-spacing: .12em;
  text-transform: uppercase;
}

.br-page .br-main > .br-panel-header {
  position: relative;
  flex-wrap: wrap;
  margin-bottom: 2px;
  padding: 2px 2px 0;
}

.br-page .br-main > .br-panel-header > div { min-width: 0; }

.br-page .br-main > .br-panel-header .br-panel-title {
  margin: 2px 0 0;
  font-family: var(--br-serif, "Iowan Old Style", "Palatino Linotype", "Noto Serif CJK SC", "Songti SC", "SimSun", serif);
  font-size: clamp(24px, 2.6vw, 32px);
  font-weight: 500;
  letter-spacing: -.025em;
  line-height: 1.2;
}

.br-page .br-main > .br-panel-header > .br-button { margin-left: auto; }

.br-page .br-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 40px;
  padding: 10px 18px;
  border: 1px solid var(--br-line, #f2cfe0);
  border-radius: 999px;
  background: var(--br-surface-2, #ffedf6);
  color: var(--br-ink, #3b2533);
  font-family: var(--br-sans, "Segoe UI", "Microsoft YaHei UI", "PingFang SC", sans-serif);
  font-size: 12px;
  font-weight: 500;
  letter-spacing: .01em;
  line-height: 1.2;
  text-decoration: none;
  white-space: nowrap;
  transition: background .24s ease, border-color .24s ease, color .24s ease,
              transform .24s var(--br-ease, cubic-bezier(.22, 1, .36, 1)), box-shadow .24s ease;
}

.br-page .br-button:hover:not(:disabled) { transform: translateY(-2px); }

.br-page .br-button:active:not(:disabled) { transform: translateY(0); }

.br-page .br-button.br-primary {
  background: var(--br-sun, #ffadd1);
  border-color: var(--br-sun, #ffadd1);
  color: var(--br-sun-ink, #4a2438);
  box-shadow: 0 4px 0 rgba(130, 70, 52, .08);
}

.br-page .br-button.br-primary:hover:not(:disabled) {
  transform: translateY(-3px) rotate(-1deg);
  box-shadow: 0 8px 18px rgba(var(--br-accent-rgb, 224, 68, 141), .18);
}

.br-page .br-button.br-small { min-height: 32px; padding: 6px 13px; font-size: 11px; gap: 6px; }

.br-page .br-button.br-ghost { background: transparent; border-color: transparent; color: var(--br-muted, #7c6272); }

.br-page .br-button.br-ghost:hover:not(:disabled) { background: var(--br-surface-2, #ffedf6); color: var(--br-ink, #3b2533); }

.br-page .br-button.br-danger {
  background: transparent;
  border-color: var(--br-danger, #cf3a63);
  color: var(--br-danger, #cf3a63);
}

.br-page .br-button.br-danger:hover:not(:disabled) { background: rgba(var(--br-accent-rgb, 224, 68, 141), .07); }

.br-page .br-button :where(svg, img) { width: 15px; height: 15px; flex: 0 0 auto; }

.br-page .br-field {
  display: flex;
  flex-direction: column;
  gap: 7px;
  min-width: 0;
}

.br-page .br-field > :where(label, span, small) {
  color: var(--br-muted, #7c6272);
  font-size: 11px;
  line-height: 1.5;
}

.br-page .br-field > :where(label, strong) { color: var(--br-ink, #3b2533); font-weight: 500; }

.br-page .br-field > :where(label, strong) small { margin-left: 5px; color: var(--br-dim, #a08494); font-weight: 400; }

.br-page .br-field-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

.br-page .br-input,
.br-page .br-textarea,
.br-page .br-select {
  width: 100%;
  min-height: 40px;
  padding: 9px 13px;
  border: 1px solid var(--br-line, #f2cfe0);
  border-radius: 12px;
  background: var(--br-field, #fff6fb);
  color: var(--br-ink, #3b2533);
  font-family: var(--br-sans, "Segoe UI", "Microsoft YaHei UI", "PingFang SC", sans-serif);
  font-size: 12px;
  line-height: 1.5;
  outline: none;
  transition: border-color .2s ease, box-shadow .2s ease, background .2s ease;
}

.br-page .br-input::placeholder,
.br-page .br-textarea::placeholder { color: var(--br-dim, #a08494); opacity: .9; }

.br-page .br-input:hover,
.br-page .br-textarea:hover,
.br-page .br-select:hover { border-color: var(--br-paper-edge, #f1d2e2); }

.br-page .br-input:focus,
.br-page .br-textarea:focus,
.br-page .br-select:focus {
  border-color: var(--br-accent, #e0448d);
  box-shadow: 0 0 0 3px rgba(var(--br-accent-rgb, 224, 68, 141), .13);
  background: var(--br-surface, #fffafd);
}

.br-page .br-input:disabled,
.br-page .br-textarea:disabled,
.br-page .br-select:disabled { opacity: .6; cursor: not-allowed; }

.br-page .br-textarea { min-height: 104px; resize: vertical; line-height: 1.75; }

.br-page .br-select {
  appearance: none;
  -webkit-appearance: none;
  padding-right: 34px;
  background-image:
    linear-gradient(45deg, transparent 50%, var(--br-muted, #7c6272) 50%),
    linear-gradient(135deg, var(--br-muted, #7c6272) 50%, transparent 50%);
  background-position:
    calc(100% - 17px) calc(50% - 2px),
    calc(100% - 12px) calc(50% - 2px);
  background-size: 5px 5px, 5px 5px;
  background-repeat: no-repeat;
}

.br-page .br-toggle {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  min-height: 30px;
  color: var(--br-muted, #7c6272);
  font-size: 12px;
  cursor: pointer;
  user-select: none;
}

.br-page .br-toggle input {
  appearance: none;
  -webkit-appearance: none;
  position: relative;
  flex: 0 0 auto;
  width: 38px;
  height: 22px;
  margin: 0;
  border: 1px solid var(--br-line, #f2cfe0);
  border-radius: 999px;
  background: var(--br-surface-3, #ffe1ee);
  cursor: pointer;
  transition: background .24s ease, border-color .24s ease;
}

.br-page .br-toggle input::after {
  content: "";
  position: absolute;
  top: 2px;
  left: 2px;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: var(--br-surface, #fffafd);
  box-shadow: 0 1px 3px rgba(49, 34, 51, .22);
  transition: transform .24s var(--br-ease, cubic-bezier(.22, 1, .36, 1));
}

.br-page .br-toggle input:checked {
  background: var(--br-accent, #e0448d);
  border-color: var(--br-accent, #e0448d);
}

.br-page .br-toggle input:checked::after { transform: translateX(16px); }

.br-page .br-toggle input:disabled { opacity: .5; cursor: not-allowed; }

.br-page .br-toggle:has(input:disabled) { opacity: .6; cursor: not-allowed; }

.br-page .br-pills {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 7px;
}

.br-page .br-pills > button,
.br-page .br-pills > span {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  min-height: 28px;
  padding: 4px 11px;
  border: 1px solid var(--br-line, #f2cfe0);
  border-radius: 999px;
  background: transparent;
  color: var(--br-muted, #7c6272);
  font-size: 10px;
  line-height: 1.2;
  white-space: nowrap;
}

.br-page .br-pills > button:hover { border-color: var(--br-accent, #e0448d); color: var(--br-ink, #3b2533); }

.br-page .br-pills > button[aria-pressed="true"],
.br-page .br-pills > button[aria-selected="true"],
.br-page .br-pills > .active {
  color: var(--br-accent, #e0448d);
  background: rgba(var(--br-accent-rgb, 224, 68, 141), .09);
  border-color: rgba(var(--br-accent-rgb, 224, 68, 141), .32);
}

.br-page .br-note {
  margin: 0;
  color: var(--br-muted, #7c6272);
  font-size: 11px;
  line-height: 1.8;
}

.br-page .br-note strong { color: var(--br-ink, #3b2533); font-weight: 500; }

.br-page .br-empty {
  display: grid;
  place-items: center;
  gap: 9px;
  min-height: 150px;
  padding: 30px 22px;
  border: 1px dashed var(--br-line, #f2cfe0);
  border-radius: 18px;
  background: rgba(var(--br-accent-rgb, 224, 68, 141), .025);
  color: var(--br-muted, #7c6272);
  font-size: 12px;
  line-height: 1.8;
  text-align: center;
}

.br-page .br-empty strong { color: var(--br-ink, #3b2533); font-weight: 500; }

.br-page .br-empty .br-button { margin-top: 4px; }

.br-page .br-empty h2 {
  margin: 0;
  font-family: var(--br-serif, "Iowan Old Style", "Palatino Linotype", "Noto Serif CJK SC", "Songti SC", "SimSun", serif);
  font-size: clamp(19px, 2vw, 23px);
  font-weight: 500;
  letter-spacing: -.02em;
}

.br-page .br-empty p { max-width: 420px; }

.br-page .br-empty > span:first-child { color: var(--br-accent, #e0448d); font-size: 20px; }

.br-page .br-panel.br-field-grid > .br-card-actions { grid-column: 1 / -1; }

.br-page .br-panel.br-field-grid > .br-card-actions > .br-button:first-child { margin-left: 0; }

.br-page .br-code {
  max-width: 100%;
  margin: 0;
  padding: 11px 13px;
  overflow: auto;
  border: 1px solid var(--br-line, #f2cfe0);
  border-radius: 12px;
  background: var(--br-field, #fff6fb);
  color: var(--br-ink, #3b2533);
  font-family: var(--br-mono, "Cascadia Code", "SFMono-Regular", Consolas, monospace);
  font-size: 11px;
  line-height: 1.75;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.br-page .br-card-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: auto;
  padding: 10px 16px;
  border-top: 1px dashed var(--br-line, #f2cfe0);
}

.br-page .br-card-actions .br-note { margin-right: auto; }

.br-page .br-savebar {
  position: sticky;
  bottom: 14px;
  z-index: 30;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 11px;
  width: min(1358px, calc(100% - 24px));
  margin: 4px auto 16px;
  padding: 10px 13px 10px 22px;
  border: 1px solid var(--br-line, #f2cfe0);
  border-radius: 999px;
  background: var(--br-glass, rgba(255,247,252,.94));
  box-shadow: 0 8px 30px rgba(75, 45, 47, .10);
  -webkit-backdrop-filter: blur(18px);
  backdrop-filter: blur(18px);
}

.br-page .br-savebar > .br-note { margin-right: auto; font-size: 10px; }

.br-page .br-savebar > div:first-child {
  display: flex;
  flex: 1 1 220px;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.br-page .br-savebar > div:first-child strong { font-size: 11px; font-weight: 600; line-height: 1.4; }

.br-page .br-savebar > div:first-child small { color: var(--br-dim, #a08494); font-size: 9px; line-height: 1.5; }

.br-page .br-savebar > div:last-child {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-left: auto;
}

.br-page .br-savebar > div:last-child .br-button { min-height: 34px; padding: 7px 14px; font-size: 11px; }

.br-page .br-savebar > .br-note::before {
  content: "";
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-right: 8px;
  border-radius: 50%;
  background: var(--br-accent, #e0448d);
  vertical-align: .08em;
}

.br-page.br-compact .br-kicker { margin-bottom: 12px; }

.br-page.br-compact .br-panel { padding: 18px 20px; }

.br-page .br-hero {
  position: relative;
  isolation: isolate;
  min-height: clamp(430px, 41vw, 486px);
  overflow: hidden;
  border: 1px solid var(--br-paper-edge, #f1d2e2);
  border-radius: 6px 48px 38px 42px;
  background: var(--br-hero-bg, #ffeaf3);
  color: var(--br-hero-ink, #3a2130);
  transform: translateZ(0);
  box-shadow: 12px 14px 0 rgba(var(--br-accent-rgb, 224, 68, 141), .055);
}

.br-page .br-hero::before {
  content: "";
  position: absolute;
  bottom: 14px;
  left: 24px;
  z-index: 2;
  width: 118px;
  height: 34px;
  background-image: radial-gradient(circle, rgba(var(--br-accent-rgb, 224, 68, 141), .22) 0 1.2px, transparent 1.6px);
  background-size: 7px 7px;
  opacity: .55;
  transform: rotate(-4deg);
  pointer-events: none;
}

.br-page .br-hero-art {
  position: absolute;
  inset: 0;
  z-index: 0;
  overflow: hidden;
  pointer-events: none;
}

.br-page .br-hero-art::before {
  content: "";
  position: absolute;
  inset: -16px -30px -16px -14px;
  background-image: var(--br-hero-image);
  background-position: 58% 42%;
  background-size: cover;
  transform: translate3d(calc(var(--br-mx, 0) * 5px), calc(var(--br-my, 0) * 3px), 0) scale(1.055);
  transition: transform .9s var(--br-quick);
  will-change: transform;
}

.br-page .br-hero-art::after {
  content: "";
  position: absolute;
  inset: 0;
  background:
    linear-gradient(90deg,
      rgb(var(--br-hero-wash, 255, 234, 243)) 1%,
      rgba(var(--br-hero-wash, 255, 234, 243), .98) 20%,
      rgba(var(--br-hero-wash, 255, 234, 243), .84) 31%,
      rgba(var(--br-hero-wash, 255, 234, 243), .30) 43%,
      rgba(var(--br-hero-wash, 255, 234, 243), 0) 56%
    ),
    linear-gradient(0deg, rgba(var(--br-hero-wash, 255, 234, 243), .34), transparent 26%);
}

.br-page .br-hero-copy {
  position: relative;
  z-index: 3;
  width: 54%;
  max-width: 700px;
  padding: 38px 42px 30px;
}

.br-page .br-kicker {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 18px;
  color: var(--br-accent, #e0448d);
  font-family: var(--br-mono);
  font-size: 9px;
  font-weight: 500;
  letter-spacing: .18em;
  line-height: 1.4;
  text-transform: uppercase;
}

.br-page .br-kicker::before {
  content: "";
  width: 25px;
  height: 1px;
  flex: 0 0 auto;
  background: currentColor;
  opacity: .75;
}

.br-page .br-hero-title {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0;
  margin: 0;
  color: var(--br-hero-ink, #3a2130);
  font-family: var(--br-serif, "Iowan Old Style", "Palatino Linotype", "Noto Serif CJK SC", "Songti SC", "SimSun", serif);
  font-size: clamp(38px, 4.05vw, 60px);
  font-weight: 500;
  letter-spacing: -.05em;
  line-height: 1.04;
}

.br-page .br-title-line {
  position: relative;
  display: block;
  max-width: 100%;
}

.br-page .br-title-line--accent {
  margin-top: .06em;
  margin-left: -.06em;
  font-family: var(--br-display, "Segoe UI", "Arial Black", "Helvetica Neue", "PingFang SC", sans-serif);
  font-size: 1.1em;
  font-weight: 800;
  letter-spacing: -.06em;
  line-height: .96;
  color: var(--br-accent, #e0448d);
  transform: rotate(-1.2deg);
  transform-origin: left center;
  text-shadow: .035em .045em 0 rgba(var(--br-accent-rgb, 224, 68, 141), .13);
}

.br-page .br-hero-title em {
  position: relative;
  isolation: isolate;
  display: inline-block;
  color: inherit;
  font-style: normal;
  transform: rotate(-.8deg);
}

.br-page .br-hero-title em::after {
  content: "";
  position: absolute;
  right: -5%;
  bottom: .02em;
  left: -4%;
  z-index: -1;
  height: .34em;
  border: 0;
  border-radius: 55% 45% 52% 48%;
  background: linear-gradient(96deg, rgba(var(--br-secondary-rgb, 140, 92, 184), .42), rgba(var(--br-accent-rgb, 224, 68, 141), .22) 62%, transparent);
  transform: rotate(-3.5deg);
  pointer-events: none;
}

.br-page .br-hero-title b {
  display: inline-block;
  margin-left: .02em;
  color: var(--br-secondary, #8c5cb8);
  font-weight: 800;
  transform: translateY(-.05em) rotate(2deg);
}

.br-page .br-hero-title br { display: block; }

.br-page .br-hero-lead {
  max-width: 35ch;
  margin-top: 16px;
  color: var(--br-muted, #7c6272);
  font-size: 12.5px;
  line-height: 1.95;
}

.br-page .br-hero-actions .br-note {
  flex: 1 0 100%;
  margin-top: 4px;
  color: var(--br-dim, #a08494);
  font-family: var(--br-mono);
  font-size: 9px;
  letter-spacing: .08em;
  text-transform: uppercase;
}

.br-page .br-hero-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 22px;
}

.br-page .br-hero .br-button.br-primary {
  background: var(--br-hero-ink, #3a2130);
  border-color: var(--br-hero-ink, #3a2130);
  color: var(--br-hero-bg, #ffeaf3);
  box-shadow: 0 4px 14px rgba(62, 38, 61, .13);
}

.br-page .br-hero .br-button.br-primary:hover:not(:disabled) {
  background: var(--br-secondary, #8c5cb8);
  border-color: var(--br-secondary, #8c5cb8);
  color: var(--br-surface, #fffafd);
  box-shadow: 0 7px 19px rgba(62, 38, 61, .19);
}

.br-page .br-hero-record {
  position: absolute;
  right: clamp(24px, 7%, 84px);
  bottom: 104px;
  z-index: 2;
  width: clamp(84px, 9.5vw, 128px);
  aspect-ratio: 1;
  display: grid;
  place-items: center;
  border-radius: 50%;
  background:
    repeating-radial-gradient(circle at 50% 50%, rgba(255, 252, 247, .16) 0 1px, transparent 1px 5px),
    conic-gradient(from 200deg, var(--br-ink, #3b2533), var(--br-secondary, #8c5cb8) 38%, var(--br-ink, #3b2533) 72%, var(--br-accent, #e0448d) 100%);
  box-shadow: 0 12px 26px rgba(52, 32, 48, .22), inset 0 0 0 1px rgba(255, 252, 247, .14);
  opacity: .92;
  transform: rotate(-11deg);
  pointer-events: none;
}

.br-page .br-hero-record::after {
  content: "";
  position: absolute;
  width: 13%;
  aspect-ratio: 1;
  border-radius: 50%;
  background: var(--br-hero-bg, #ffeaf3);
  box-shadow: inset 0 0 0 1px rgba(0, 0, 0, .18);
}

.br-page .br-hero-record-label {
  position: relative;
  z-index: 1;
  display: grid;
  place-items: center;
  width: 46%;
  aspect-ratio: 1;
  border-radius: 50%;
  border: 1px dashed rgba(var(--br-accent-rgb, 224, 68, 141), .55);
  background: var(--br-paper, #fff9fc);
  color: var(--br-accent, #e0448d);
  font-family: var(--br-mono);
  font-size: 7px;
  font-weight: 600;
  letter-spacing: .12em;
  line-height: 1.25;
  text-align: center;
  text-transform: uppercase;
}

.br-page .br-hero-edition {
  position: absolute;
  top: 22px;
  right: 18px;
  z-index: 3;
  max-height: 168px;
  overflow: hidden;
  padding: 10px 5px;
  border-left: 1px solid rgba(var(--br-accent-rgb, 224, 68, 141), .3);
  color: var(--br-dim, #a08494);
  font-family: var(--br-mono);
  font-size: 8px;
  font-weight: 600;
  letter-spacing: .22em;
  line-height: 1.5;
  text-align: right;
  text-transform: uppercase;
  writing-mode: vertical-rl;
  pointer-events: none;
}

.br-page .br-hero-ticket {
  position: absolute;
  right: 26px;
  bottom: 27px;
  z-index: 3;
  display: block;
  min-width: 183px;
  padding: 14px 22px 15px;
  border: 1px solid rgba(var(--br-accent-rgb, 224, 68, 141), .26);
  border-left: 3px solid rgba(var(--br-accent-rgb, 224, 68, 141), .34);
  border-radius: 2px 14px 14px 2px;
  background: rgba(255, 250, 241, .94);
  box-shadow: 8px 9px 0 rgba(var(--br-accent-rgb, 224, 68, 141), .10), 0 10px 22px rgba(92, 60, 76, .10);
  color: #59475a;
  text-align: left;
  transform: rotate(-5deg);
  -webkit-backdrop-filter: blur(14px);
  backdrop-filter: blur(14px);
}

.br-page .br-hero-ticket::before {
  content: "";
  position: absolute;
  top: -9px;
  left: 35%;
  width: 50px;
  height: 17px;
  background: rgba(223, 189, 153, .44);
  transform: rotate(-5deg);
}

.br-page .br-hero-ticket small {
  display: block;
  color: var(--br-dim, #a08494);
  font-family: var(--br-mono);
  font-size: 8px;
  letter-spacing: .1em;
  text-transform: uppercase;
}

.br-page .br-hero-ticket strong {
  font-family: var(--br-serif, "Iowan Old Style", "Palatino Linotype", "Noto Serif CJK SC", "Songti SC", "SimSun", serif);
  font-size: 16px;
  font-weight: 600;
  letter-spacing: .1em;
}

.br-page .br-hero-ticket span {
  margin-left: 10px;
  color: var(--br-accent, #e0448d);
  font-family: var(--br-serif, "Iowan Old Style", "Palatino Linotype", "Noto Serif CJK SC", "Songti SC", "SimSun", serif);
  font-size: 13px;
  font-style: italic;
}

.br-page .br-hero .br-creature,
.br-page .br-creature {
  position: absolute;
  bottom: 26px;
  left: 38px;
  z-index: 4;
  width: 50px;
  height: 44px;
  border: 1px solid rgba(var(--br-secondary-rgb, 140, 92, 184), .28);
  border-radius: 47% 50% 43% 44%;
  background-color: var(--br-lilac, #f7e3f3);
  background-image:
    radial-gradient(circle at 35% 45%, var(--br-ink, #3b2533) 0 1.5px, transparent 2px),
    radial-gradient(circle at 65% 45%, var(--br-ink, #3b2533) 0 1.5px, transparent 2px),
    radial-gradient(ellipse at 31% 57%, var(--br-sun, #ffadd1) 0 3px, transparent 3.4px),
    radial-gradient(ellipse at 69% 57%, var(--br-sun, #ffadd1) 0 3px, transparent 3.4px);
  cursor: pointer;
  transform: rotate(-6deg);
  animation: br-creature 5.6s ease-in-out infinite;
}

.br-page .br-hero .br-creature {
  overflow: hidden;
  color: transparent;
  font-size: 0;
  line-height: 0;
  text-indent: -999px;
}

.br-page .br-panel .br-kicker,
.br-page .br-panel-header .br-kicker,
.br-page .br-dialog-header .br-kicker {
  margin-bottom: 5px;
  font-size: 8px;
  letter-spacing: .16em;
}

.br-page.br-compact .br-hero { min-height: 236px; }

.br-page.br-compact .br-hero-copy { width: 58%; padding: 22px 30px; }

.br-page.br-compact .br-hero-title { font-size: clamp(24px, 3vw, 34px); line-height: 1.25; }

.br-page.br-compact .br-title-line--accent { font-size: 1.06em; }

.br-page.br-compact .br-kicker { margin-bottom: 12px; }

.br-page.br-compact .br-air-note,
.br-page.br-compact .br-hero-ticket,
.br-page.br-compact .br-hero-record,
.br-page.br-compact .br-hero-edition,
.br-page.br-compact .br-creature { display: none; }

.br-page.br-art-off .br-hero { background: linear-gradient(115deg, var(--br-paper, #fff9fc), var(--br-lilac, #f7e3f3)); }

.br-page.br-art-off .br-hero::before { display: none; }

.br-page.br-art-off .br-hero-art::before { display: none; }

.br-page.br-art-off .br-hero-art::after { background: none; }

.br-page.br-art-off .br-air-note,
.br-page.br-art-off .br-hero-ticket,
.br-page.br-art-off .br-creature { display: none; }

.br-page.br-art-off .br-hero-title { color: var(--br-ink, #3b2533); }

.br-page.br-art-off .br-hero-ticket { display: none; }

.br-page.br-art-off .br-hero-record { right: 6%; bottom: 82px; opacity: .8; }

.br-page.br-motion-off .br-hero-art::before { transform: none; transition: none; }

.br-page.br-motion-off .br-hero::before { transform: none; }



.br-page .br-nav {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 5px;
  min-width: 0;
  padding: 6px clamp(18px, 3vw, 42px) 10px;
  border-bottom: 1px solid var(--br-line-soft, #fbe2ee);
  background: var(--br-glass, rgba(255,247,252,.94));
  -webkit-backdrop-filter: blur(18px) saturate(1.1);
  backdrop-filter: blur(18px) saturate(1.1);
}

.br-page .br-nav button {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  min-height: 36px;
  padding: 8px 13px;
  border: 1px solid transparent;
  border-radius: 999px;
  background: transparent;
  color: var(--br-muted, #7c6272);
  font-size: 12px;
  line-height: 1;
  white-space: nowrap;
  transition: background .22s ease, color .22s ease, transform .22s ease, border-color .22s ease;
}

.br-page .br-nav button:hover { color: var(--br-ink, #3b2533); background: var(--br-surface-2, #ffedf6); transform: translateY(-1px); }

.br-page .br-nav button[aria-current="page"] {
  color: var(--br-surface, #fffafd);
  background: var(--br-ink, #3b2533);
  border-color: var(--br-ink, #3b2533);
  box-shadow: 0 4px 12px rgba(55, 35, 50, .10);
}

.br-page .br-nav-index {
  display: inline-grid;
  place-items: center;
  min-width: 17px;
  height: 17px;
  padding: 0 4px;
  border: 1px solid var(--br-line, #f2cfe0);
  border-radius: 999px;
  background: var(--br-paper, #fff9fc);
  color: var(--br-dim, #a08494);
  font-family: currentColor;
  font-size: 8px;
  font-weight: 600;
  letter-spacing: .04em;
  line-height: 1;
  transition: background .22s ease, color .22s ease, border-color .22s ease;
}

.br-page .br-nav button:hover .br-nav-index {
  border-color: var(--br-accent, #e0448d);
  color: var(--br-accent, #e0448d);
}

.br-page .br-nav button[aria-current="page"] .br-nav-index {
  border-color: transparent;
  background: var(--br-accent, #e0448d);
  color: var(--br-accent-ink, #fff6fb);
}

.br-page .br-status {
  display: inline-flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-height: 26px;
  padding: 4px 10px;
  border-radius: 999px;
  background: currentColor;
  color: var(--br-muted, #7c6272);
  font-family: currentColor;
  font-size: 9px;
  letter-spacing: .08em;
  line-height: 1;
  white-space: nowrap;
}

.br-page .br-status .br-button {
  min-height: 24px;
  padding: 3px 9px;
  border-radius: 999px;
  font-size: 10px;
}

.br-page .br-status::before {
  content: "";
  width: 5px;
  height: 5px;
  flex: 0 0 auto;
  border-radius: 50%;
  background: var(--br-warn, #b26a3d);
}

.br-page .br-status.br-ok { color: var(--br-accent, #e0448d); }

.br-page .br-status.br-ok::before { background: var(--br-accent, #e0448d); }

.br-page .br-status.br-error { color: var(--br-danger, #cf3a63); }

.br-page .br-status.br-error::before { background: var(--br-danger, #cf3a63); }

.br-page .br-status.br-warn { color: var(--br-warn, #b26a3d); }

.br-page .br-status.br-warn::before { background: var(--br-warn, #b26a3d); }

.br-page.br-motion-off .br-nav button:hover { transform: none; }


""";
}