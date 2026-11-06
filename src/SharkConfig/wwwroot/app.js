"use strict";
// SharkConfig v0.4 — interface. Fala com C# (UI/Bridge.cs) via WebView2 postMessage.

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const fmt = (n) => Number(n).toLocaleString("pt-BR");
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
const clone = (o) => JSON.parse(JSON.stringify(o));
const store = { get(k) { try { return localStorage.getItem(k); } catch { return null; } }, set(k, v) { try { localStorage.setItem(k, v); } catch {} } };

// ---------------- encaixe na janela: quadro 1200×760 escalado inteiro ----------------
const FRAME_W = 1200, FRAME_H = 760;
function fit() {
  const s = Math.max(0.6, Math.min(1.6, Math.min(innerWidth / FRAME_W, innerHeight / FRAME_H)));
  document.body.style.zoom = s;
  const app = $("#app");
  app.style.width = innerWidth / s + "px";   // a sobra horizontal/vertical vira espaço, não barra de rolagem
  app.style.height = innerHeight / s + "px";
}
let fitQueued = false;
addEventListener("resize", () => { if (!fitQueued) { fitQueued = true; requestAnimationFrame(() => { fitQueued = false; fit(); }); } });

// ---------------- ponte ----------------
const bridge = (() => {
  const wv = window.chrome?.webview;
  let seq = 0;
  const pending = new Map(), listeners = {};
  wv?.addEventListener("message", (e) => {
    const m = e.data;
    if (m.event) return (listeners[m.event] || []).forEach((f) => f(m));
    const p = pending.get(m.id);
    if (!p) return;
    pending.delete(m.id);
    m.ok ? p.res(m.result) : p.rej(new Error(m.error));
  });
  return {
    call(cmd, args) {
      if (!wv) return Promise.reject(new Error("Abra esta interface pelo SharkConfig.exe."));
      return new Promise((res, rej) => { const id = ++seq; pending.set(id, { res, rej }); wv.postMessage({ id, cmd, args }); });
    },
    on(ev, f) { (listeners[ev] ??= []).push(f); },
  };
})();

// ---------------- estado ----------------
// kind: "mouse" (X8) ou "keyboard" (X68 HE, páginas em kb.js); avail: o que está ligado para trocar
const S = { opt: null, kind: "mouse", device: null, cfg: null, saved: null, warnings: [], hex: "", page: "overview", btn: 0, busy: false, logs: [],
  keys: [], avail: { mouse: false, keyboard: false }, model: Number(store.get("model") ?? 0) };
const pagesNow = () => (S.kind === "keyboard" ? KB_PAGES : PAGES);
const viewsNow = () => (S.kind === "keyboard" ? KB_VIEWS : VIEWS);
const bindNow = () => (S.kind === "keyboard" ? KB_BIND : BIND);
const iconsNow = () => (S.kind === "keyboard" ? KB_ICON : ICON);
const devNoun = () => (S.kind === "keyboard" ? "teclado" : "mouse");

// Fotos oficiais (Picture/Buttoninterface/mouse{i}.png, 432×356). Posições em % da imagem.
// Fotos oficiais (Picture/Buttoninterface/mouse{i}.png) recortadas para 202×356 (x 115–317 do original 432×356).
// Posições em % da imagem recortada.
const SPOTS = [
  { x: 27.7, y: 20.5, side: "left" },  // 0 esquerdo
  { x: 72.3, y: 20.5, side: "right" }, // 1 direito
  { x: 50, y: 12.4, side: "right" },   // 2 roda
  { x: 7.9, y: 51.4, side: "left" },   // 3 lateral traseiro
  { x: 7.9, y: 37.8, side: "left" },   // 4 lateral frontal
];
// O firmware tem um 6º slot de botão (0x60 + 4·5), mas o X8 Ultra não tem esse botão no corpo:
// a interface não o mostra e nunca o altera (o valor lido é preservado).
const LEDS = [{ x: 50, y: 5.7 }];      // indicador de DPI na frente, junto à roda
const BUTTON_NAMES = ["Botão esquerdo", "Botão direito", "Roda", "Lateral traseiro", "Lateral frontal"];

const ICON = {
  overview: "M12 3.5a5.5 5.5 0 0 1 5.5 5.5v6a5.5 5.5 0 0 1-11 0V9A5.5 5.5 0 0 1 12 3.5zM12 7v2.5",
  performance: "M4.5 15.5a7.5 7.5 0 1 1 15 0M12 15.5l3.5-4.5",
  dpi: "M12 4v3M12 17v3M4 12h3M17 12h3M12 9.5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5z",
  buttons: "M8.5 4.5l9.5 8.5-4.6.9 2.8 5.6-1.9.9-2.8-5.6-3 3.3z",
  lighting: "M12 4l1.6 4.4L18 10l-4.4 1.6L12 16l-1.6-4.4L6 10l4.4-1.6z",
  advanced: "M5 8h9M18 8h1M5 16h3M12 16h7M16 6v4M10 14v4",
};
const PAGES = {
  overview: { title: "Visão geral", sub: () => S.device ? connectionText() : "Nenhum mouse conectado" },
  performance: { title: "Desempenho", sub: () => "Taxa de atualização, sensor e energia" },
  dpi: { title: "Sensibilidade", sub: () => `Até ${S.opt?.dpi.maxStages ?? 6} estágios, de 50 a 42.000 DPI` },
  buttons: { title: "Botões", sub: () => "Clique em um botão no mouse para trocar a função" },
  lighting: { title: "Iluminação", sub: () => S.device?.dongleLight ? "Indicador de DPI e LED do receptor" : "Indicador de DPI" },
  advanced: { title: "Avançado", sub: () => "Memória do mouse, aparência e registro" },
};

// engrenagem desenhada por código: 8 dentes arredondados em volta de um anel
const GEAR = (() => {
  const pts = [], teeth = 8, ro = 9.2, ri = 7.1;
  for (let i = 0; i < teeth * 4; i++) {
    const a = (i / (teeth * 4)) * Math.PI * 2 - Math.PI / 2, r = (i % 4 < 2) ? ro : ri;
    pts.push(`${(12 + r * Math.cos(a)).toFixed(2)} ${(12 + r * Math.sin(a)).toFixed(2)}`);
  }
  return `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M${pts.join("L")}Z"/><circle cx="12" cy="12" r="3"/></svg>`;
})();
const gearButton = () => `<button class="gear" data-settings aria-label="Aparência" title="Aparência">${GEAR}</button>`;

const stageColor = () => S.cfg?.colors ? S.cfg.colors[S.cfg.currentStage] : "#0A84FF";
const hz = (code) => S.opt?.pollingRates.find((p) => p.code === code)?.hz ?? 0;
const photoSrc = () => `img/mouse${S.model}.png`;
const viaDongle = () => /dongle/.test(S.device.description);
const connectionText = () => viaDongle() ? "Conectado pelo receptor 8K" : "Conectado pelo cabo";

// ---------------- alterações ----------------
function changeCount() {
  if (!S.cfg || !S.saved) return 0;
  let n = 0;
  for (const k of Object.keys(S.cfg)) {
    const a = S.cfg[k], b = S.saved[k];
    if (Array.isArray(a)) a.forEach((v, i) => { if (v !== b[i]) n++; });
    else if (a !== b) n++;
  }
  return n;
}
function refreshChanges() {
  const n = changeCount();
  const pill = $("#changes");
  pill.classList.toggle("show", n > 0);
  pill.inert = n === 0;   // escondida fora da tela: não pode receber foco (Tab não a alcança)
  $("#changes-text").textContent = n === 1 ? "1 alteração não aplicada" : `${n} alterações não aplicadas`;
  document.documentElement.style.setProperty("--stage", stageColor());
}
function set(mutator, rerender = false) {
  mutator(S.cfg);
  refreshChanges();
  if (rerender) render(); else updateLeds();
}

// ---------------- foto ----------------
function photo({ spots = false, leds = false } = {}) {
  const c = S.cfg;
  const ledHtml = leds && c ? LEDS.map((l) => {
    const op = c.ledEffect === 0 ? 0 : 0.3 + 0.7 * (c.ledBrightness / 10);
    return `<i class="led ${c.ledEffect === 2 ? "breathe" : ""}" style="left:${l.x}%;top:${l.y}%;opacity:${op};--speed:${(6 - c.ledSpeed) * 0.9 + 0.8}s"></i>`;
  }).join("") : "";
  const label = (id) => S.opt.buttons.find((b) => b.id === id)?.label ?? "Função personalizada";
  const spotHtml = spots ? SPOTS.map((h, i) => `
    <button class="hs" data-hs="${i}" style="left:${h.x}%;top:${h.y}%" aria-pressed="${i === S.btn}" aria-label="${BUTTON_NAMES[i]}">${i + 1}</button>
    <span class="tag ${h.side} ${i === S.btn ? "sel" : ""}" style="--x:${h.x}%;top:${h.y}%">${esc(label(c.buttons[i]))}</span>`).join("") : "";
  return `<div class="photo ${c ? "" : "off"}">
    <i class="floor"></i><img class="shot" src="${photoSrc()}" alt="Attack Shark X8 Ultra" draggable="false">
    ${ledHtml}${spotHtml}</div>`;
}
function updateLeds() {
  // atualização barata do preview de LED sem refazer a página
  if (S.page !== "lighting" || !S.cfg) return;
  const c = S.cfg, op = c.ledEffect === 0 ? 0 : 0.3 + 0.7 * (c.ledBrightness / 10);
  $$(".photo .led").forEach((l) => { l.style.opacity = op; l.classList.toggle("breathe", c.ledEffect === 2); l.style.setProperty("--speed", `${(6 - c.ledSpeed) * 0.9 + 0.8}s`); });
}

// ---------------- shell ----------------
// só troca o HTML quando ele muda: a barra lateral é igual em quase todo render (sem refazer DOM, imagem e foco)
const setHTML = (el, html) => { if (el._html !== html) { el.innerHTML = html; el._html = html; } };
// bateria no estilo do iPhone: cápsula com o número dentro, preenchida até o nível, e o polo à direita
const batteryGlyph = (p) => `<div class="ios-battery ${p <= 20 ? "low" : ""}" style="--lvl:${Math.max(0, Math.min(100, p))}%" role="img" aria-label="Bateria ${p}%">
  <span>${p}</span><i></i></div>`;
const KB_GLYPH =`<svg class="kb-glyph" viewBox="0 0 34 40" aria-hidden="true"><rect x="1" y="11" width="32" height="18" rx="3.5"/><path d="M6 17h2M11 17h2M16 17h2M21 17h2M26 17h2M9 23h16"/></svg>`;
function renderRail() {
  const d = S.device, bat = d?.battery, kb = S.kind === "keyboard";
  setHTML($("#device"), `
    ${kb ? KB_GLYPH : `<img src="${photoSrc()}" alt="">`}
    <div><div class="device-name">${kb ? esc(d?.name ?? "Teclado") : "X8 Ultra"}</div>
      <div class="device-status"><i class="dot ${d ? "on" : ""}"></i>${!d ? "Desconectado" : kb ? "Cabo USB" : viaDongle() ? "Receptor 8K" : "Cabo USB"}</div></div>
    ${bat ? batteryGlyph(bat.percent) : ""}`);
  // navegação: estrutura fixa; só o estado muda (o botão focado continua o mesmo elemento)
  const P = pagesNow(), I = iconsNow();
  setHTML($("#nav"), Object.entries(P).map(([key, p]) => `
    <button data-page="${key}"><svg viewBox="0 0 24 24"><path d="${I[key]}"/></svg>${p.title}</button>`).join(""));
  $$("#nav [data-page]").forEach((b) => {
    const key = b.dataset.page;
    if (S.page === key) b.setAttribute("aria-current", "page"); else b.removeAttribute("aria-current");
    b.disabled = !S.cfg && key !== "overview" && key !== "advanced";
  });
  // Início: fecha o dispositivo e volta à tela de escolha (mouse ou teclado)
  setHTML($("#rail-foot"), `${d ? `<button class="btn block plain" id="go-home">Início</button>` : ""}
    ${d ? `<button class="btn block" id="reread">Ler o ${devNoun()} de novo</button>` : ""}
    <div class="foot-row"><small>${d?.firmware ? `Firmware ${d.firmware}` : `SharkConfig ${S.opt?.version ?? ""}`}</small>${gearButton()}</div>`);
  $("#apply").textContent = `Aplicar no ${devNoun()}`;
  $("#sheet-title").textContent = `Aplicar no ${devNoun()}`;
}

function render() {
  // sem dispositivo: tela de conexão (sem barra lateral nem cabeçalho); com dispositivo: layout normal
  document.body.classList.toggle("no-device", !S.cfg);
  const P = pagesNow();
  if (!P[S.page]) S.page = "overview";
  const page = S.cfg ? S.page : "overview";
  $("#title").textContent = P[page].title;
  $("#subtitle").textContent = P[page].sub();
  renderRail();
  const view = $("#view");
  view.innerHTML = viewsNow()[page]();
  bindNow()[page]?.(view);
  refreshChanges();
}

// ---------------- componentes ----------------
const seg = (name, items, value) => `<div class="seg" data-seg="${name}">${items.map((it) =>
  `<button data-v="${it.v}" aria-pressed="${String(it.v) === String(value)}">${it.label}</button>`).join("")}</div>`;
const toggle = (name, on) => `<label class="switch"><input type="checkbox" data-toggle="${name}" ${on ? "checked" : ""}><i></i></label>`;
const select = (name, items, value) => `<div class="select"><select data-select="${name}">${items.map((it) =>
  `<option value="${it.v}" ${String(it.v) === String(value) ? "selected" : ""}>${esc(it.label)}</option>`).join("")}</select></div>`;
const row = (title, hint, control) => `<div class="row"><div class="label">${title}${hint ? `<span>${hint}</span>` : ""}</div>${control}</div>`;
const pct = (v, min, max) => `${((v - min) / (max - min)) * 100}%`;
const slider = (name, v, min, max, out) => `<div class="slider"><input type="range" data-range="${name}" min="${min}" max="${max}" value="${v}" style="--p:${pct(v, min, max)}"><output>${out}</output></div>`;
// trilha de DPI em escala raiz quadrada: espalha os valores baixos, onde ficam os estágios de uso real
const dpiPos = (v) => { const o = S.opt.dpi; return `${Math.sqrt((v - o.min) / (o.max - o.min)) * 100}%`; };
const dpiAt = (f) => { const o = S.opt.dpi; return o.min + Math.pow(Math.min(1, Math.max(0, f)), 2) * (o.max - o.min); };
const snapDpi = (x) => { const o = S.opt.dpi; x = Math.min(o.max, Math.max(o.min, x)); const st = x > o.seg1 ? 100 : 50; return Math.round(x / st) * st; };

function bindControls(root) {
  $$("[data-seg]", root).forEach((g) => g.addEventListener("click", (e) => {
    const b = e.target.closest("button"); if (!b) return;
    $$("button", g).forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
    set((c) => { c[g.dataset.seg] = Number(b.dataset.v); }, g.dataset.seg === "pollingCode" || g.dataset.seg === "dongleStatus");
  }));
  $$("[data-toggle]", root).forEach((i) => i.addEventListener("change", () => set((c) => { c[i.dataset.toggle] = i.checked; })));
  $$("[data-select]", root).forEach((s) => s.addEventListener("change", () => set((c) => { c[s.dataset.select] = Number(s.value); })));
  $$("[data-range]", root).forEach((r) => r.addEventListener("input", () => {
    const min = Number(r.min), max = Number(r.max), v = Number(r.value), name = r.dataset.range;
    r.style.setProperty("--p", pct(v, min, max));
    r.nextElementSibling.textContent = OUT[name](v);
    set((c) => { c[name] = v; });
    if (name === "debounce") $("#debounce-warn").hidden = v >= 4;
  }));
}
const OUT = { debounce: (v) => `${v} ms`, ledBrightness: (v) => `${v} de 10`, ledSpeed: (v) => `${v} de 5` };

// LED de status do receptor 8K: só aparece pelo receptor, quando ele responde ao registro de luz
function dongleGroup() {
  if (!S.device?.dongleLight || !S.cfg.dongleStatus) return "";
  const modes = S.opt.dongleStatusModes ?? [];
  const cur = modes.find((m) => m.mode === S.cfg.dongleStatus);
  return `<div class="group"><h3>LED do receptor</h3>${seg("dongleStatus", modes.map((m) => ({ v: m.mode, label: m.label })), S.cfg.dongleStatus)}
    <p class="note">${esc(cur?.info ?? "")}</p></div>`;
}

// ---------------- páginas ----------------
const VIEWS = {
  overview() {
    // sem dispositivo aberto: tela inicial para escolher o mouse ou o teclado (kb.js)
    if (!S.cfg) return chooser();
    // Visão geral é só informativa: nada aqui altera a configuração.
    const c = S.cfg, d = S.device;
    return `<div class="overview">
      <div class="facts left">
        <div class="fact big"><div class="v">${fmt(c.dpi[c.currentStage])}<small>DPI</small></div><div class="k">Estágio ${c.currentStage + 1} de ${c.stageCount}</div></div>
        <div class="fact"><div class="k">Taxa de atualização</div><div class="v">${fmt(hz(c.pollingCode))}<small>Hz</small></div></div>
      </div>
      <div class="stage">${photo()}</div>
      <div class="facts">
        <div class="fact"><div class="k">Bateria</div><div class="v">${d.battery ? d.battery.percent : "—"}<small>%</small></div></div>
        <div class="fact"><div class="k">Distância de levantamento</div><div class="v">${S.opt.lod[c.lod].replace(" mm", "").replace(".", ",")}<small>mm</small></div></div>
        <div class="fact"><div class="k">Motion Sync</div><div class="v">${c.motionSync ? "Ligado" : "Desligado"}</div></div>
      </div></div>`;
  },

  performance() {
    const c = S.cfg, o = S.opt;
    const rates = o.pollingRates.map((p) => ({ v: p.code, label: p.hz >= 1000 ? `${p.hz / 1000}K` : String(p.hz) }));
    const times = o.sleepOptions.map((s) => ({ v: s.units, label: s.label }));
    const high = hz(c.pollingCode) > 1000 && !/cabo/.test(S.device.description);
    return `<div class="workspace" style="--insp:640px"><div class="stage">${photo()}</div>
      <div class="inspector"><div class="cols">
        <div>
          <div class="group"><h3>Taxa de atualização (Hz)</h3>${seg("pollingCode", rates, c.pollingCode)}
            ${high ? '<p class="note warn">Acima de 1000 Hz sem fio, a bateria dura bem menos.</p>' : ""}</div>
          <div class="group"><h3>Modo do sensor</h3>${seg("sensorMode", o.sensorModes.map((m, i) => ({ v: i, label: m })), c.sensorMode)}
            <p class="note">LP economiza bateria. HP e Corded priorizam resposta.</p></div>
          <div class="group"><h3>Distância de levantamento</h3>${seg("lod", o.lod.map((m, i) => ({ v: i, label: m.replace(".", ",") })), c.lod)}</div>
          <div class="group"><h3>Rastreamento</h3><div class="rows">
            ${row("Motion Sync", "", toggle("motionSync", c.motionSync))}
            ${row("Correção de linha reta", "", toggle("angleSnap", c.angleSnap))}
            ${row("Controle de ondulação", "", toggle("ripple", c.ripple))}
          </div></div>
        </div>
        <div>
          <div class="group"><h3>Energia</h3><div class="rows">
            ${row("Desempenho máximo", "Sensor no modo mais rápido", toggle("performance", c.performance))}
            ${row("Voltar ao normal após", "", select("performanceTimeout", times.slice(0, 8), c.performanceTimeout))}
            ${row("Dormir após", "Tempo parado até o repouso", select("sleep", times, c.sleep))}
          </div></div>
          <div class="group"><h3>Cliques</h3><div class="rows"><div class="row stack">
            <div class="label">Tempo de resposta<span>Debounce dos botões</span></div>${slider("debounce", c.debounce, S.opt.limits.debounce.min, S.opt.limits.debounce.max, OUT.debounce(c.debounce))}
          </div></div><p class="note warn" id="debounce-warn" ${c.debounce < 4 ? "" : "hidden"}>Valores muito baixos podem gerar clique duplo.</p></div>
        </div>
      </div></div></div>`;
  },

  dpi() {
    const c = S.cfg, o = S.opt.dpi;
    // DPI só é gravável quando o modelo foi identificado como PAW3950 (codificação conhecida)
    const lock = !S.device.dpiSupported ? "disabled" : "";
    const ticks = [50, 1000, 2500, 5000, 10000, 20000, 30000, 42000].map((t) =>
      `<span class="tick" style="left:${dpiPos(t)}">${t >= 1000 ? fmt(t / 1000) + "K" : t}</span>`).join("");
    const marks = c.dpi.slice(0, c.stageCount).map((v, i) =>
      `<button class="mark ${i === c.currentStage ? "cur" : ""}" data-mark="${i}" ${lock} style="left:${dpiPos(v)};--c:${c.colors[i]}" aria-label="Estágio ${i + 1}: ${fmt(v)} DPI"><span>${fmt(v)}</span></button>`).join("");
    const rows = c.dpi.slice(0, c.stageCount).map((v, i) => `<div class="row"><div class="srow" style="width:100%">
        <label class="swatch" style="--c:${c.colors[i]}" title="Cor do estágio"><input type="color" data-color="${i}" value="${c.colors[i]}"></label>
        <span>Estágio ${i + 1}</span>
        <input class="num" type="number" data-dpi="${i}" value="${v}" min="${o.min}" max="${o.max}" step="${v >= o.seg1 ? 100 : 50}" aria-label="DPI do estágio ${i + 1}" ${lock}>
        <button class="use" data-current="${i}" aria-pressed="${i === c.currentStage}">${i === c.currentStage ? "Ativo" : "Usar"}</button>
        <button class="del" data-remove="${i}" ${c.stageCount <= 1 || lock ? "disabled" : ""} aria-label="Remover estágio ${i + 1}">×</button>
      </div></div>`).join("");
    return `<div class="dpi-page">
      <div class="ruler"><div class="track" id="track"><i class="line"></i><i class="hi" style="left:${dpiPos(o.seg1)}" title="Acima de 30.000 o passo é 100"></i>${ticks}${marks}</div></div>
      <div class="workspace"><div class="stage">${photo()}</div>
        <div class="inspector"><h2>Estágios</h2><p class="sub">Arraste na régua ou digite. Um botão com a função Alternar estágios percorre a lista.</p>
          ${lock ? '<p class="note warn" style="margin:-6px 2px 0">O modelo deste mouse não foi identificado como PAW3950. Por segurança, os valores de DPI ficam só para leitura; cores e estágio ativo podem ser trocados.</p>' : ""}
          <div class="rows">${rows}</div>
          <div class="acts-row"><button class="btn" id="add-stage" ${c.stageCount >= o.maxStages || lock ? "disabled" : ""}>Adicionar estágio</button></div>
        </div></div></div>`;
  },

  buttons() {
    const c = S.cfg, sel = S.btn;
    const groups = [...new Set(S.opt.buttons.map((b) => b.group))];
    const list = groups.map((g) => `<div class="group"><h3>${g}</h3><div class="choices">${S.opt.buttons.filter((b) => b.group === g).map((b) =>
      `<button class="choice" role="menuitemradio" data-choice="${b.id}" aria-checked="${c.buttons[sel] === b.id}">${esc(b.label)}</button>`).join("")}</div></div>`).join("");
    return `<div class="workspace" style="--insp:420px"><div class="stage">${photo({ spots: true })}</div>
      <div class="inspector"><h2>${BUTTON_NAMES[sel]}</h2>
        ${sel === 0 ? '<p class="sub" style="color:var(--caution)">Deixe o clique esquerdo em algum botão.</p>' : ""}
        <div class="groups">${list}</div></div></div>`;
  },

  lighting() {
    const c = S.cfg;
    return `<div class="workspace"><div class="stage">${photo({ leds: true })}</div>
      <div class="inspector"><div class="group"><h3>Efeito</h3>${seg("ledEffect", S.opt.ledEffects.map((m, i) => ({ v: i, label: m })), c.ledEffect)}
        <p class="note">A cor segue o estágio de DPI ativo. Troque as cores em Sensibilidade.</p></div>
        <div class="group"><h3>Ajustes</h3><div class="rows">
          <div class="row stack"><div class="label">Brilho</div>${slider("ledBrightness", c.ledBrightness, 1, S.opt.limits.brightnessLevels, OUT.ledBrightness(c.ledBrightness))}</div>
          <div class="row stack"><div class="label">Velocidade da respiração</div>${slider("ledSpeed", c.ledSpeed, S.opt.limits.ledSpeed.min, S.opt.limits.ledSpeed.max, OUT.ledSpeed(c.ledSpeed))}</div>
        </div></div>${dongleGroup()}</div></div>`;
  },

  advanced() {
    const warn = S.warnings.length ? `<p class="note warn">${S.warnings.map(esc).join("<br>")}</p>` : "";
    const models = Array.from({ length: 18 }, (_, i) =>
      `<button class="model" data-model="${i}" aria-pressed="${i === S.model}" aria-label="Foto ${i + 1}"><img src="img/mouse${i}.png" alt="" loading="lazy" decoding="async"></button>`).join("");
    return `<div class="adv">
      <div class="inspector"><h2>Memória do mouse</h2><p class="sub">A configuração fica gravada no próprio mouse. Os backups vão para a pasta do app.</p>
        <div class="acts-row"><button class="btn" id="export" ${S.cfg ? "" : "disabled"}>Exportar backup</button><button class="btn" id="import" ${S.cfg ? "" : "disabled"}>Carregar backup</button></div>
        ${warn}<pre class="hex">${S.hex ? esc(S.hex) : "Leia um mouse para ver a memória."}</pre></div>
      <div class="right">
        <div class="inspector"><h2>Aparência</h2><p class="sub">${S.device?.model != null ? "Detectada pelo modelo do mouse. Pode trocar se quiser." : "Escolha a foto que corresponde ao seu mouse."}</p><div class="models">${models}</div></div>
        <div class="inspector"><h2>Registro</h2><div class="log" id="log">${S.logs.slice(-60).map(logLine).join("") || "<div>Sem atividade ainda.</div>"}</div></div>
      </div></div>`;
  },
};

const BIND = {
  performance: bindControls,
  lighting: bindControls,
  dpi(v) {
    const track = $("#track", v);
    $$("[data-mark]:not(:disabled)", v).forEach((m) => m.addEventListener("pointerdown", (e) => {
      const i = Number(m.dataset.mark); m.setPointerCapture(e.pointerId);
      m.onpointermove = (ev) => {
        const r = track.getBoundingClientRect();
        const val = snapDpi(dpiAt((ev.clientX - r.left) / r.width));
        S.cfg.dpi[i] = val; m.style.left = dpiPos(val); m.firstChild.textContent = fmt(val);
        const inp = $(`[data-dpi="${i}"]`, v); if (inp) inp.value = val;
        refreshChanges();
      };
      m.onpointerup = () => { m.onpointermove = m.onpointerup = null; render(); };
    }));
    $$("[data-dpi]", v).forEach((inp) => inp.addEventListener("change", () => set((c) => { c.dpi[Number(inp.dataset.dpi)] = snapDpi(Number(inp.value)); }, true)));
    $$("[data-color]", v).forEach((inp) => inp.addEventListener("input", () => {
      const i = Number(inp.dataset.color);
      inp.parentElement.style.setProperty("--c", inp.value);
      $(`[data-mark="${i}"]`, v)?.style.setProperty("--c", inp.value);
      set((c) => { c.colors[i] = inp.value.toUpperCase(); });
    }));
    $$("[data-current]", v).forEach((b) => b.addEventListener("click", () => set((c) => { c.currentStage = Number(b.dataset.current); }, true)));
    $$("[data-remove]", v).forEach((b) => b.addEventListener("click", () => set((c) => {
      const i = Number(b.dataset.remove);
      c.dpi.push(c.dpi.splice(i, 1)[0]); c.colors.push(c.colors.splice(i, 1)[0]); // mantém 8 entradas
      c.stageCount--;
      if (c.currentStage > i) c.currentStage--;
      c.currentStage = Math.min(c.currentStage, c.stageCount - 1);
    }, true)));
    $("#add-stage", v)?.addEventListener("click", () => set((c) => {
      c.dpi[c.stageCount] = snapDpi(c.dpi[c.stageCount - 1] + 800); c.stageCount++;
    }, true));
  },
  buttons(v) {
    $$("[data-hs]", v).forEach((b) => b.addEventListener("click", () => { S.btn = Number(b.dataset.hs); render(); }));
    $$("[data-choice]", v).forEach((b) => b.addEventListener("click", () => set((c) => { c.buttons[S.btn] = b.dataset.choice; }, true)));
  },
  advanced(v) {
    $$("[data-model]", v).forEach((b) => b.addEventListener("click", () => { S.model = Number(b.dataset.model); store.set("model", S.model); render(); }));
    $("#export", v)?.addEventListener("click", () => run(async () => toast("Backup salvo em " + await bridge.call("exportBackup"))));
    $("#import", v)?.addEventListener("click", () => run(async () => {
      const cfg = await bridge.call("importBackup");
      if (cfg) { S.cfg = cfg; render(); toast("Backup carregado. Aplique no mouse para gravar."); }
    }));
  },
};

// ---------------- transições (View Transitions: o mouse desliza entre as telas) ----------------
// plain: crossfade único da tela, sem elementos nomeados. Usado quando o mouse só existe numa das
// telas (Avançado não tem foto): animado sozinho, ele surgia/sumia como silhueta cinza por cima da outra.
function transition(update, plain = false) {
  if (!document.startViewTransition || appearance.reduceMotion) return update();
  const root = document.documentElement;
  if (plain) root.classList.add("vt-plain");
  document.startViewTransition(update).finished.finally(() => root.classList.remove("vt-plain"));
}

// ---------------- ações ----------------
const hasPhoto = (page) => page !== "advanced";
function go(page) {
  if (page === S.page) return;
  transition(() => { S.page = page; render(); }, !(hasPhoto(S.page) && hasPhoto(page)));
}

async function run(fn) {
  if (S.busy) return;
  S.busy = true; document.body.style.cursor = "progress";
  try { await fn(); } catch (e) { toast(e.message, true); }
  finally { S.busy = false; document.body.style.cursor = ""; $("#progress").classList.remove("on"); }
}

function loadState(st) {
  $("#progress").classList.remove("on");   // leitura terminou (também na conexão automática)
  const arriving = !S.cfg || S.kind !== (st.kind ?? "mouse");
  const apply = () => {
    if (S.kind !== (st.kind ?? "mouse")) { S.keys = []; S.page = "overview"; }
    S.kind = st.kind ?? "mouse";
    S.device = st.device; S.cfg = st.config; S.warnings = st.warnings; S.hex = st.hex;
    if (S.cfg?.colors) S.cfg.colors = S.cfg.colors.map((x) => x.toUpperCase());
    if (S.cfg?.ledColor) S.cfg.ledColor = S.cfg.ledColor.toUpperCase();
    S.saved = S.cfg ? clone(S.cfg) : null;
    if (S.kind === "keyboard" && !S.cfg) toast(`${st.device.name}: modelo ainda não suportado.`, true);
    if (st.device.model != null && store.get("model") == null) S.model = st.device.model; // foto pelo MID do mouse
    bridge.call("available").then((a) => { S.avail = a; renderRail(); }).catch(() => {});
    if (arriving) S.page = "overview";
    render();
  };
  arriving ? transition(apply) : apply();   // ao conectar: o mouse desliza e a barra lateral entra
  if (st.warnings.length) toast(`${st.warnings.length} campo(s) com leitura inválida. Veja em Avançado.`, true);
}

function deviceGone() {
  closeModal("#sheet");
  transition(() => { S.device = S.cfg = S.saved = null; S.warnings = []; S.hex = ""; S.page = "overview"; S.kind = "mouse"; S.keys = []; render(); });
  bridge.call("available").then((a) => { S.avail = a; if (!S.cfg) render(); }).catch(() => {});
}

// ---------------- folhas modais com animação (abrir: mola; fechar: rápido) ----------------
// Com uma folha aberta, o resto da janela fica inerte: Tab circula só dentro da folha.
// A folha que está fechando também fica inerte (ainda visível, mas já não é interativa).
let modalReturnFocus = null;
function syncInert() {
  const open = $$(".modal").some((m) => !m.hidden && !m.classList.contains("closing"));
  $("#app").inert = open;
  return open;
}
function openModal(sel, focusSel) {
  const m = $(sel);
  if (!syncInert()) modalReturnFocus = document.activeElement;
  m.classList.remove("closing");
  m.hidden = false; m.inert = false;
  syncInert();
  $(focusSel ?? "button", m)?.focus({ preventScroll: true });
  requestAnimationFrame(() => requestAnimationFrame(() => m.classList.add("open")));
}
function closeModal(sel) {
  const m = $(sel);
  if (m.hidden || m.classList.contains("closing")) return;
  m.classList.add("closing"); m.classList.remove("open");
  m.inert = true;
  if (!syncInert() && modalReturnFocus?.isConnected) modalReturnFocus.focus({ preventScroll: true });
  setTimeout(() => { if (m.classList.contains("closing")) { m.hidden = true; m.classList.remove("closing"); } }, 230);
}

// ---------------- aparência: tema e transparência ----------------
let appearance = { theme: "dark", transparency: 0, reduceMotion: false };
// onPaint: troca a moldura nativa. Sem animação, junto com as cores; com crossfade, no meio dele
// (a barra de título do Windows não faz fade — trocar no 1º quadro deixava barra clara sobre conteúdo escuro).
function applyAppearance(a, animate = false, onPaint = null) {
  const root = document.documentElement;
  root.dataset.motion = a.reduceMotion ? "reduce" : "full";
  $("#reduce-motion").checked = !!a.reduceMotion;
  const paint = () => {
    root.dataset.theme = a.theme;
    root.style.setProperty("--opacity", String(1 - a.transparency / 100));
    $$("[data-theme-choice]").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.themeChoice === a.theme)));
  };
  if (animate && a.theme !== root.dataset.theme) switchTheme(paint, onPaint);
  else { paint(); onPaint?.(); }
  const r = $("#transparency");
  r.value = a.transparency; r.style.setProperty("--p", pct(a.transparency, 0, 60));
  $("#transparency-out").textContent = `${a.transparency}%`;
}
// Troca de tema como no macOS: o quadro inteiro faz um único crossfade. A classe theme-switch tira as
// transições de cor e os nomes de View Transition enquanto dura (ver styles.css).
const THEME_FADE_MS = 300;   // igual a ::view-transition-*(root) com .theme-switch em styles.css
function switchTheme(paint, onMid) {
  const root = document.documentElement;
  root.classList.add("theme-switch");
  const done = () => root.classList.remove("theme-switch");
  if (!document.startViewTransition || appearance.reduceMotion) {
    paint(); onMid?.(); void document.body.offsetWidth; done();   // troca seca, tudo no mesmo quadro
    return;
  }
  const vt = document.startViewTransition(paint);
  // ready = o crossfade começou; a moldura troca com o conteúdo a ~50% (descontado ~1 quadro de atraso do DWM)
  vt.ready.then(() => setTimeout(() => onMid?.(), THEME_FADE_MS / 2 - 16), () => onMid?.());
  vt.finished.finally(done);
}
let saveTimer;
function changeAppearance(patch) {
  appearance = { ...appearance, ...patch };
  const send = () => bridge.call("setAppearance", appearance).catch(() => {});
  clearTimeout(saveTimer);
  if ("theme" in patch) applyAppearance(appearance, true, send);   // moldura troca junto com a página
  else {
    applyAppearance(appearance);
    saveTimer = setTimeout(send, 120);   // slider: só quando para, para não martelar o DWM e o disco
  }
}

async function applyFlow() {
  const ops = await bridge.call("preview", S.cfg);
  if (!ops.length) { S.saved = clone(S.cfg); refreshChanges(); return; }
  $("#sheet-text").textContent = `${ops.length === 1 ? "1 pacote vai" : ops.length + " pacotes vão"} para a memória do ${devNoun()}. Antes disso, um backup da configuração atual é salvo.`;
  $("#sheet-packets").textContent = ops.map((o) => `${o.address}  ${o.bytes}`).join("\n");
  openModal("#sheet", "#sheet-confirm");
}

function toast(text, error = false) {
  const t = $("#toast");
  t.textContent = text; t.classList.toggle("error", error); t.classList.add("show");
  clearTimeout(toast.h); toast.h = setTimeout(() => t.classList.remove("show"), 3600);
}
const logLine = (l) => `<div title="${esc(l.text)}"><time>${l.time}</time>${esc(l.text)}</div>`;

// fotos oficiais são opcionais (não vão no repositório): sem elas, usa a silhueta neutra
document.addEventListener("error", (e) => {
  const img = e.target;
  if (img.tagName !== "IMG") return;
  // foto do teclado ausente: some, e o gabinete fica só com a placa e as teclas (a silhueta é de mouse)
  if (img.classList.contains("kbd-shot")) { img.hidden = true; return; }
  if (!img.src.endsWith("placeholder.svg")) img.src = "img/placeholder.svg";
}, true);

// ---------------- eventos globais ----------------
document.addEventListener("click", (e) => {
  const nav = e.target.closest("[data-page]"); if (nav && !nav.disabled) return go(nav.dataset.page);
  if (e.target.closest("[data-settings]")) return openModal("#settings", '[data-theme-choice][aria-pressed="true"]');
  if (e.target.closest("#reread")) return run(async () => loadState(await bridge.call("read")));
});
$("#discard").addEventListener("click", () => { S.cfg = clone(S.saved); render(); });
$("#apply").addEventListener("click", () => run(applyFlow));
$("#sheet-cancel").addEventListener("click", () => closeModal("#sheet"));
$("#sheet-confirm").addEventListener("click", () => {
  closeModal("#sheet");
  run(async () => { const r = await bridge.call("apply", S.cfg); loadState(r.state); toast(`Aplicado no ${devNoun()} (${r.count} ${r.count === 1 ? "pacote" : "pacotes"})`); });
});
document.addEventListener("click", (e) => {
  const pick = e.target.closest("[data-open]");
  if (pick && !pick.disabled) return run(async () => {
    $("#progress").classList.add("on");
    loadState(await bridge.call("switchTo", pick.dataset.open));
  });
  if (e.target.closest("#go-home")) {
    if (changeCount() > 0 && !confirm("Há alterações não aplicadas. Voltar ao início e descartá-las?")) return;
    return run(async () => { S.avail = await bridge.call("disconnect"); deviceGone(); });
  }
});
// aparência
$("#theme-seg").addEventListener("click", (e) => { const b = e.target.closest("[data-theme-choice]"); if (b) changeAppearance({ theme: b.dataset.themeChoice }); });
$("#transparency").addEventListener("input", (e) => changeAppearance({ transparency: Number(e.target.value) }));
$("#reduce-motion").addEventListener("change", (e) => changeAppearance({ reduceMotion: e.target.checked }));
// Iniciar com o Windows (chave Run do usuário); o C# devolve o estado real depois de gravar
$("#startup").addEventListener("change", (e) => bridge.call("setStartup", e.target.checked)
  .then((on) => { e.target.checked = !!on; }).catch((err) => { e.target.checked = !e.target.checked; toast(err.message, true); }));
$("#settings-done").addEventListener("click", () => closeModal("#settings"));
// clicar fora do painel fecha
$$(".modal").forEach((m) => m.addEventListener("pointerdown", (e) => { if (e.target === m) closeModal("#" + m.id); }));
document.addEventListener("keydown", (e) => { if (e.key === "Escape") { closeModal("#sheet"); closeModal("#settings"); } });

bridge.on("progress", (m) => { $("#progress").classList.add("on"); $("#progress i").style.setProperty("--p", m.value / 100); });
bridge.on("device", (m) => m.state ? loadState(m.state) : deviceGone());
bridge.on("notice", (m) => toast(m.text, true));
// leitura periódica da bateria (bandeja): atualiza a bateria da barra lateral
bridge.on("battery", (m) => { if (S.device && S.kind === "mouse") { S.device.battery = m.battery; renderRail(); } });
// o Windows avisou que algo entrou ou saiu: atualiza os cartões da tela inicial
bridge.on("available", (m) => { S.avail = { mouse: m.mouse, keyboard: m.keyboard }; if (!S.cfg) render(); });
bridge.on("log", (m) => {
  S.logs.push(m); if (S.logs.length > 200) S.logs.splice(0, S.logs.length - 200);
  const el = $("#log");
  if (el) { el.insertAdjacentHTML("beforeend", logLine(m)); while (el.childElementCount > 60) el.firstElementChild.remove(); }
});

// ---------------- início ----------------
(async () => {
  fit();
  const q = new URLSearchParams(location.search);
  try { S.opt = await bridge.call("init"); } catch { S.opt = null; }
  if (S.opt?.appearance) { appearance = S.opt.appearance; }
  $("#startup").checked = !!S.opt?.startup;
  applyAppearance(appearance);
  if (q.get("page") && PAGES[q.get("page")]) S.page = q.get("page");
  render();
  // tela inicial: mostra o que está ligado; o C# avisa sozinho quando algo entra ou sai
  try { S.avail = await bridge.call("available"); render(); }
  catch (e) { toast(e.message, true); }
})();
