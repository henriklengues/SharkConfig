"use strict";
// SharkConfig — páginas do teclado HE (X68 HE). Carregado depois de app.js e usa os mesmos utilitários.

const KB_ICON = {
  overview: "M3.5 7.5h17v9h-17zM7 10.5h1M10 10.5h1M13 10.5h1M16 10.5h1M8 13.5h8",
  performance: ICON.performance,
  keys: "M12 4v9M8.5 9.5L12 13l3.5-3.5M5 16.5h14M5 19.5h14",
  lighting: ICON.lighting,
  advanced: ICON.advanced,
};
const KB_PAGES = {
  overview: { title: "Visão geral", sub: () => S.device ? "Conectado pelo cabo" : "Nenhum teclado conectado" },
  performance: { title: "Desempenho", sub: () => "Taxa de atualização" },
  keys: { title: "Teclas", sub: () => "Ponto de acionamento, Rapid Trigger e zona morta por tecla" },
  lighting: { title: "Iluminação", sub: () => "Efeito, cor, brilho e velocidade" },
  advanced: { title: "Avançado", sub: () => "Memória do teclado, backup e registro" },
};

const isKb = () => S.kind === "keyboard";
const kbLayout = () => S.device?.layout;
const kbLim = () => S.device.limits;
// 2 casas, ou 3 quando o passo de 0,005 mm pede (ex.: 1,205)
const mm = (v) => { const n = Number(v); return n.toFixed(Math.abs(n * 100 - Math.round(n * 100)) > 1e-6 ? 3 : 2).replace(".", ","); };
const kbHz = (code) => S.opt?.keyboard.reportRates.find((r) => r.code === code)?.hz ?? 0;
const kbEffect = (mode) => S.opt?.keyboard.effects.find((e) => e.mode === mode);
const selectable = (i) => { const k = kbLayout().keys[i]; return k.present && !k.advanced; };

// ---------------- desenho do teclado ----------------
// opts.select: teclas clicáveis; opts.values: texto pequeno em cada tecla; opts.light: prévia da iluminação
// opts.layout: desenha sem dispositivo (tela inicial), com o layout que vem de init
function keyboard(opts = {}) {
  const L = opts.layout ?? kbLayout();
  if (!L) return "";
  const c = opts.layout ? null : S.cfg, sel = new Set(S.keys);
  // opts.demo: arco-íris fixo (cartão da tela inicial, antes de ler o teclado)
  const demo = opts.demo ? { ledMode: 1, ledRainbow: true } : null;
  const light = demo ? kbLight(demo, L) : opts.light && c ? kbLight(c, L) : null;
  const keys = L.keys.map((k, i) => {
    const style = `left:${(k.x / L.width) * 100}%;top:${(k.y / L.height) * 100}%;width:${(k.w / L.width) * 100}%;height:${(k.h / L.height) * 100}%` +
      (light ? `;--kc:${light(k)}` : "");
    const cls = ["key", k.code === "Space" ? "space" : "", sel.has(i) && opts.select ? "sel" : "", !k.present ? "absent" : "", k.advanced ? "adv" : "",
      c && c.rt[i] && opts.values ? "rt" : ""].filter(Boolean).join(" ");
    const val = opts.values && c && k.present ? `<small>${opts.values(i)}</small>` : "";
    const tag = opts.select ? "button" : "div";
    const extra = opts.select ? ` data-key="${i}" aria-pressed="${sel.has(i)}" ${selectable(i) ? "" : "disabled"} aria-label="${esc(k.label || k.code)}"` : "";
    return `<${tag} class="${cls}" style="${style}"${extra}${k.advanced ? ' title="Modo avançado configurado no app oficial"' : ""}><span>${esc(k.label)}</span>${val}</${tag}>`;
  }).join("");
  const lightCls = light ? ` lit fx-${(demo ?? c).ledMode}` : "";
  const vars = `--ar:${L.width / L.height};--speed:${c ? (5 - c.ledSpeed) * 0.8 + 0.8 : 3}s;--bright:${c ? c.ledBrightness / 4 : 1}`;
  // com foto (L.frame): a foto do produto é o corpo do teclado e as teclas do app cobrem as da foto,
  // nas mesmas posições (medidas na foto, ver KeyboardModels.cs); sem foto: gabinete desenhado
  if (L.frame) {
    const p = L.plate, pc = (v, t) => `${(v / t) * 100}%`;
    return `<div class="kbd framed${lightCls}" style="${vars}">
      <i class="kbd-floor"></i><div class="kbd-frame"><img class="kbd-shot" src="${L.frame}" alt="" draggable="false">
        <div class="kbd-plate" style="left:${pc(p.x, L.width)};top:${pc(p.y, L.height)};width:${pc(p.w, L.width)};height:${pc(p.h, L.height)}"></div><div class="kbd-keys">${keys}</div></div></div>`;
  }
  return `<div class="kbd${lightCls}" style="${vars}">
    <i class="kbd-floor"></i><div class="kbd-case"><i class="kbd-loop"></i><div class="kbd-plate"><div class="kbd-keys">${keys}</div></div><i class="kbd-lip"></i></div></div>`;
}

// cor de cada tecla na prévia: cor escolhida, ou arco-íris pela posição horizontal
function kbLight(c, L = kbLayout()) {
  const fx = kbEffect(c.ledMode);
  if (c.ledMode === 0) return () => "transparent";
  // arco-íris como na foto do X68 HE: amarelo à esquerda, verde, ciano, azul, roxo e magenta à direita
  if (c.ledRainbow || !fx?.color) return (k) => `hsl(${Math.round(45 + ((k.x + k.w / 2) / L.width) * 270)} 100% 58%)`;
  return () => c.ledColor;
}

function updateKeyLabels() {
  if (S.page !== "keys") return;
  $$(".kbd [data-key]").forEach((b) => {
    const i = Number(b.dataset.key), s = b.querySelector("small");
    if (s) s.textContent = mm(S.cfg.travel[i]);
    b.classList.toggle("rt", S.cfg.rt[i]);
  });
}

// ---------------- páginas ----------------
const KB_VIEWS = {
  overview() {
    if (!S.cfg) return chooser();
    const c = S.cfg, L = kbLayout();
    const vals = L.keys.map((k, i) => k.present ? c.travel[i] : null).filter((v) => v != null);
    const lo = Math.min(...vals), hi = Math.max(...vals);
    const rtCount = L.keys.filter((k, i) => k.present && c.rt[i]).length;
    return `<div class="kb-page overview-kb">
      <div class="stage">${keyboard({ light: true })}</div>
      <div class="facts row4">
        <div class="fact big"><div class="k">Ponto de acionamento</div><div class="v">${lo === hi ? mm(lo) : `${mm(lo)}–${mm(hi)}`}<small>mm</small></div></div>
        <div class="fact"><div class="k">Taxa de atualização</div><div class="v">${fmt(kbHz(c.reportRate))}<small>Hz</small></div></div>
        <div class="fact"><div class="k">Rapid Trigger</div><div class="v">${rtCount ? `${rtCount}<small>teclas</small>` : "Desligado"}</div></div>
        <div class="fact"><div class="k">Iluminação</div><div class="v">${esc(kbEffect(c.ledMode)?.label ?? "—")}</div></div>
      </div></div>`;
  },

  performance() {
    const c = S.cfg;
    const rates = S.opt.keyboard.reportRates.map((r) => ({ v: r.code, label: r.hz >= 1000 ? `${r.hz / 1000}K` : String(r.hz) }));
    return `<div class="kb-page">
      <div class="stage">${keyboard()}</div>
      <div class="inspector wide"><div class="group"><h3>Taxa de atualização (Hz)</h3>${seg("reportRate", rates, c.reportRate)}
        <p class="note">8000 Hz dá a menor latência, mas usa mais CPU. Se algum jogo engasgar, teste 1000 Hz.</p></div></div></div>`;
  },

  keys() {
    const c = S.cfg, lim = kbLim(), sel = S.keys;
    const first = sel[0];
    const quick = `<div class="acts-row">
      <button class="btn" data-pick="all">Todas</button><button class="btn" data-pick="wasd">WASD</button>
      <button class="btn" data-pick="letters">Letras</button><button class="btn plain" data-pick="none">Limpar</button></div>`;
    const anyAdv = kbLayout().keys.some((k) => k.advanced);
    let body;
    if (first == null) body = `<p class="sub kb-hint">Clique nas teclas para selecionar. Os ajustes valem para todas as selecionadas.</p>`;
    else {
      const rt = c.rt[first];
      const mmSlider = (name, v, min, max) => `<div class="slider"><input type="range" data-mm="${name}" min="${min}" max="${max}" step="${lim.step}" value="${v}" style="--p:${pct(v, min, max)}"><output>${mm(v)} mm</output></div>`;
      body = `<div class="cols cols3">
        <div>
          <div class="group"><h3>Acionamento</h3><div class="rows">
            <div class="row stack"><div class="label">Ponto de acionamento<span>Profundidade em que a tecla registra</span></div>${mmSlider("travel", c.travel[first], lim.travelMin, lim.travelMax)}</div>
            ${rt ? "" : `<div class="row stack"><div class="label">Ponto de soltura<span>Profundidade em que a tecla solta</span></div>${mmSlider("lift", c.lift[first], lim.travelMin, lim.travelMax)}</div>`}
          </div></div>
        </div>
        <div>
          <div class="group"><h3>Zona morta</h3><div class="rows">
            <div class="row stack"><div class="label">Zona morta no fundo<span>Ignora o curso final da tecla</span></div>${mmSlider("deadZone", c.deadZone[first], lim.deadZoneMin, lim.deadZoneMax)}</div>
          </div></div>
        </div>
        <div>
          <div class="group"><h3>Rapid Trigger</h3><div class="rows">
            ${row("Rapid Trigger", "Aciona e solta pelo movimento, não por um ponto fixo", toggle("rt", rt))}
            ${rt ? `<div class="row stack"><div class="label">Sensibilidade ao pressionar</div>${mmSlider("rtPress", c.rtPress[first], lim.rtMin, lim.rtMax)}</div>
            <div class="row stack"><div class="label">Sensibilidade ao soltar</div>${mmSlider("rtLift", c.rtLift[first], lim.rtMin, lim.rtMax)}</div>` : ""}
          </div><p class="note">Valores menores deixam a tecla mais sensível.</p></div>
        </div></div>`;
    }
    return `<div class="kb-page">
      <div class="stage">${keyboard({ select: true, values: (i) => mm(c.travel[i]) })}</div>
      <div class="inspector wide"><div class="kb-head"><h2>${first == null ? "Nenhuma tecla selecionada" : sel.length === 1 ? esc(kbLayout().keys[first].label || "Espaço") : `${sel.length} teclas`}</h2>${quick}</div>
        ${anyAdv ? '<p class="note warn" style="margin:-8px 2px 0">Teclas apagadas usam DKS, Mod-Tap, Toggle ou SnapTap, configurados no app oficial. O SharkConfig não altera essas teclas.</p>' : ""}
        ${body}</div></div>`;
  },

  lighting() {
    const c = S.cfg, k = S.opt.keyboard, fx = kbEffect(c.ledMode);
    const effects = k.effects.map((e) => ({ v: e.mode, label: e.label }));
    const dirs = fx?.directions?.length ? `<div class="group"><h3>Direção</h3>${seg("ledDirection", fx.directions.map((d, i) => ({ v: i, label: d })), c.ledDirection)}</div>` : "";
    const on = c.ledMode !== 0;
    return `<div class="kb-page">
      <div class="stage">${keyboard({ light: true })}</div>
      <div class="inspector wide"><div class="cols">
        <div>
          <div class="group"><h3>Efeito</h3><div class="rows">${row("Efeito", "", select("ledMode", effects, c.ledMode))}</div></div>
          ${dirs}
        </div>
        <div>
          ${on ? `<div class="group"><h3>Ajustes</h3><div class="rows">
            <div class="row stack"><div class="label">Brilho</div>${slider("ledBrightness", c.ledBrightness, 0, k.led.maxBrightness, `${c.ledBrightness} de ${k.led.maxBrightness}`)}</div>
            ${fx?.speed ? `<div class="row stack"><div class="label">Velocidade</div>${slider("ledSpeed", c.ledSpeed, 0, k.led.maxSpeed, `${c.ledSpeed} de ${k.led.maxSpeed}`)}</div>` : ""}
            ${fx?.color ? `${row("Arco-íris", "Ignora a cor escolhida", toggle("ledRainbow", c.ledRainbow))}
              ${c.ledRainbow ? "" : row("Cor", "", `<label class="swatch big" style="--c:${c.ledColor}"><input type="color" data-kbcolor value="${c.ledColor}"></label>`)}` : ""}
          </div></div>` : '<p class="note">A iluminação está desligada.</p>'}
        </div></div></div></div>`;
  },

  advanced() {
    const warn = S.warnings.length ? `<p class="note warn">${S.warnings.map(esc).join("<br>")}</p>` : "";
    const d = S.device;
    return `<div class="adv">
      <div class="inspector"><h2>Memória do teclado</h2><p class="sub">A configuração fica gravada no próprio teclado. Os backups vão para a pasta do app.</p>
        <div class="acts-row"><button class="btn" id="export" ${S.cfg ? "" : "disabled"}>Exportar backup</button><button class="btn" id="import" ${S.cfg ? "" : "disabled"}>Carregar backup</button></div>
        ${warn}<pre class="hex">${S.hex ? esc(S.hex) : "Leia um teclado para ver a memória."}</pre></div>
      <div class="right">
        <div class="inspector"><h2>Dispositivo</h2><div class="rows">
          ${row("Modelo", "", `<span>${esc(d?.name ?? "—")}${d?.revision ? ` <small style="color:var(--graphite)">(${esc(d.revision)})</small>` : ""}</span>`)}
          ${row("Id do modelo", "", `<span>${d?.id ?? "—"}</span>`)}
          ${row("Firmware", "", `<span>${d?.firmware ?? "—"}</span>`)}
          ${row("Passo de curso", "", `<span>${d ? mm(d.limits.step) + " mm" : "—"}</span>`)}
        </div></div>
        <div class="inspector"><h2>Registro</h2><div class="log" id="log">${S.logs.slice(-60).map(logLine).join("") || "<div>Sem atividade ainda.</div>"}</div></div>
      </div></div>`;
  },
};

// ---------------- tela inicial: só os dispositivos reconhecidos neste PC ----------------
function chooser() {
  const a = S.avail, kbL = S.opt?.keyboard?.layout;
  const status = (text) => `<div class="pick-status"><i class="dot on"></i>${text}</div>`;
  const cards = [];
  if (a.mouse) cards.push(`<button class="pick" data-open="mouse" aria-label="Configurar o mouse X8 Ultra">
        <div class="pick-art"><div class="photo"><i class="floor"></i><img class="shot" src="${photoSrc()}" alt="" draggable="false"></div></div>
        <div class="pick-name">X8 Ultra</div>${status(a.mouse === "dongle" ? "Receptor 8K" : "Cabo USB")}</button>`);
  if (a.keyboard) cards.push(`<button class="pick" data-open="keyboard" aria-label="Configurar o teclado X68 HE">
        <div class="pick-art">${kbL ? keyboard({ layout: kbL, demo: true }) : ""}</div>
        <div class="pick-name">X68 HE</div>${status("Cabo USB")}</button>`);
  if (!cards.length) return `<div class="connect home none">
    <div class="empty"><h2>Nenhum dispositivo encontrado</h2><p>Conecte um mouse X8 Ultra ou um teclado X68 HE.</p></div>${gearButton()}</div>`;
  return `<div class="connect home">
    <div class="empty"><h2>${cards.length > 1 ? "Escolha o dispositivo" : "Dispositivo encontrado"}</h2><p>${cards.length > 1 ? "Clique no que quer configurar." : "Clique para configurar."}</p></div>
    <div class="picker">${cards.join("")}</div>${gearButton()}</div>`;
}

const LETTERS ="QWERTYUIOPASDFGHJKLZXCVBNM".split("").map((l) => "Key" + l);
function pickKeys(kind) {
  const L = kbLayout();
  const idx = (pred) => L.keys.map((k, i) => (pred(k) && selectable(i) ? i : -1)).filter((i) => i >= 0);
  if (kind === "all") S.keys = idx(() => true);
  else if (kind === "wasd") S.keys = idx((k) => ["KeyW", "KeyA", "KeyS", "KeyD"].includes(k.code));
  else if (kind === "letters") S.keys = idx((k) => LETTERS.includes(k.code));
  else S.keys = [];
}

// mm: aplica a todas as teclas selecionadas; reflete no texto das teclas sem refazer a página
function bindMm(root) {
  $$("[data-mm]", root).forEach((r) => r.addEventListener("input", () => {
    const min = Number(r.min), max = Number(r.max), v = Number(r.value), name = r.dataset.mm;
    r.style.setProperty("--p", pct(v, min, max));
    r.nextElementSibling.textContent = `${mm(v)} mm`;
    set((c) => { for (const i of S.keys) c[name][i] = v; });
    updateKeyLabels();
  }));
}

const KB_BIND = {
  performance: bindControls,
  keys(v) {
    $$("[data-key]", v).forEach((b) => b.addEventListener("click", () => {
      const i = Number(b.dataset.key);
      S.keys = S.keys.includes(i) ? S.keys.filter((x) => x !== i) : [...S.keys, i];
      render();
    }));
    $$("[data-pick]", v).forEach((b) => b.addEventListener("click", () => { pickKeys(b.dataset.pick); render(); }));
    $$("[data-toggle='rt']", v).forEach((t) => t.addEventListener("change", () => set((c) => { for (const i of S.keys) c.rt[i] = t.checked; }, true)));
    bindMm(v);
  },
  lighting(v) {
    $$("[data-select]", v).forEach((s) => s.addEventListener("change", () => set((c) => { c[s.dataset.select] = Number(s.value); c.ledDirection = 0; }, true)));
    $$("[data-seg]", v).forEach((g) => g.addEventListener("click", (e) => {
      const b = e.target.closest("button"); if (!b) return;
      set((c) => { c[g.dataset.seg] = Number(b.dataset.v); }, true);
    }));
    $$("[data-toggle]", v).forEach((i) => i.addEventListener("change", () => set((c) => { c[i.dataset.toggle] = i.checked; }, true)));
    $$("[data-range]", v).forEach((r) => r.addEventListener("input", () => {
      const min = Number(r.min), max = Number(r.max), val = Number(r.value), name = r.dataset.range;
      r.style.setProperty("--p", pct(val, min, max));
      r.nextElementSibling.textContent = `${val} de ${max}`;
      set((c) => { c[name] = val; });
      const kb = $(".kbd", v);
      kb?.style.setProperty(name === "ledSpeed" ? "--speed" : "--bright", name === "ledSpeed" ? `${(5 - val) * 0.8 + 0.8}s` : String(val / 4));
    }));
    $$("[data-kbcolor]", v).forEach((inp) => inp.addEventListener("input", () => {
      inp.parentElement.style.setProperty("--c", inp.value);
      set((c) => { c.ledColor = inp.value.toUpperCase(); });
      $$(".kbd .key", v).forEach((k) => k.style.setProperty("--kc", inp.value));
    }));
  },
  advanced: BIND.advanced,
};
