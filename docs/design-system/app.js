/*
 * Digital Twin UI design system, interactive.
 *
 * Everything shown comes from data.js, which Tools/DesignSystemSite/export_design_system.py reads
 * from the Unity assets. The shapes are drawn by renderer.js, a port of the project's UIShape
 * shader; text and layout are DOM. The state logic below mirrors UIComponentStyle.Resolve and
 * UIShape in Unity: a component shows a style per state, hover and press pick the state's hover
 * look, and a change of look blends over the new style's transition.
 */
(function () {
  'use strict';

  const DS = window.DESIGN_SYSTEM;
  const root = document.documentElement;
  const $ = (sel, el = document) => el.querySelector(sel);
  const $$ = (sel, el = document) => Array.from(el.querySelectorAll(sel));

  function h(tag, attrs, ...kids) {
    const el = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v === false || v == null) continue;
      if (k === 'class') el.className = v;
      else if (k === 'style') el.style.cssText = v;
      else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
      else el.setAttribute(k, v === true ? '' : v);
    }
    for (const kid of kids.flat()) if (kid != null && kid !== false) el.append(kid.nodeType ? kid : document.createTextNode(kid));
    return el;
  }

  // ---- viewer settings (DigitalTwinSettings) ---------------------------------------------

  const settings = {
    bg: 'studio',
    contrast: false,
    reduceMotion: window.matchMedia('(prefers-reduced-motion: reduce)').matches
  };
  const BACKDROPS = {
    studio: { rgb: [0.23, 0.27, 0.29], label: 'Studio' },
    bright: { rgb: [0.68, 0.74, 0.77], label: 'Bright scene' },
    night: { rgb: [0.035, 0.06, 0.075], label: 'Night' }
  };

  // ---- tokens ------------------------------------------------------------------------------

  const defaults = JSON.parse(JSON.stringify({ colors: DS.tokens.colors, styles: DS.styles }));
  const hexToRgb = (hex) => [0, 2, 4].map((i) => parseInt(hex.substr(i, 2), 16) / 255);
  const tokenRgb = (name) => {
    const t = DS.tokens.colors.find((c) => c.name === name);
    return t ? hexToRgb(t.hex) : [1, 0, 1];
  };
  const resolveColor = (c) => (c.token ? [...tokenRgb(c.token), c.alpha] : c.custom.slice());
  const cssColor = (c) => {
    const [r, g, b, a] = resolveColor(c);
    return `rgba(${Math.round(r * 255)},${Math.round(g * 255)},${Math.round(b * 255)},${a})`;
  };

  // ---- looks ---------------------------------------------------------------------------------

  const NUM = ['cornerRadius', 'chamfer', 'chamferCorners', 'strokeWidth', 'glowSize', 'glowStrength', 'innerGlow',
    'highlight', 'bottomLight', 'bottomLightHeight', 'shadowSoftness', 'gradientStart', 'gradientEnd', 'hatch',
    'pulse', 'pulseSpeed'];

  function lookOf(style) {
    const look = {
      fill: resolveColor(style.fill), stroke: resolveColor(style.stroke), glow: resolveColor(style.glow),
      shadow: resolveColor(style.shadow), shadowOffset: style.shadowOffset.slice(), kind: style.kind
    };
    for (const k of NUM) look[k] = style[k];
    return look;
  }

  const lerp = (a, b, t) => a + (b - a) * t;
  const lerpArr = (a, b, t) => a.map((v, i) => lerp(v, b[i], t));

  function lerpLook(a, b, t) {
    const out = { kind: b.kind, gradientStart: b.gradientStart, chamferCorners: b.chamferCorners };
    for (const k of ['fill', 'stroke', 'glow', 'shadow', 'shadowOffset']) out[k] = lerpArr(a[k], b[k], t);
    for (const k of NUM) if (!(k in out)) out[k] = lerp(a[k], b[k], t);
    return out;
  }

  /** The viewer's settings applied after blending, as UIShape.Adjusted does. */
  function adjusted(look) {
    const l = Object.assign({}, look, { fill: look.fill.slice(), stroke: look.stroke.slice(), shadow: look.shadow.slice() });
    if (settings.reduceMotion) l.pulse = 0;
    if (settings.contrast) {
      if (l.fill[3] > 0.3) l.fill[3] = lerp(l.fill[3], 1, 0.7);
      if (l.stroke[3] > 0.05) l.stroke[3] = Math.min(1, l.stroke[3] * 1.8 + 0.15);
      l.strokeWidth = Math.max(l.strokeWidth, l.stroke[3] > 0.05 ? 1.25 : 0);
      l.shadow[3] = Math.min(1, l.shadow[3] * 1.4);
    }
    return l;
  }

  function transparent(look) {
    const l = Object.assign({}, look, { fill: look.fill.slice(), stroke: look.stroke.slice(), shadow: look.shadow.slice() });
    l.fill[3] = 0; l.stroke[3] = 0; l.shadow[3] = 0; l.glowStrength = 0; l.bottomLight = 0;
    return l;
  }

  function scaled(look, s) {
    return Object.assign({}, look, {
      cornerRadius: look.cornerRadius * s, chamfer: look.chamfer * s, strokeWidth: look.strokeWidth * s,
      glowSize: look.glowSize * s, shadowSoftness: look.shadowSoftness * s,
      shadowOffset: [look.shadowOffset[0] * s, look.shadowOffset[1] * s], hatchSpacing: 9 * s
    });
  }

  // ---- component state (UIComponentStyle.Resolve) ------------------------------------------

  const compByName = Object.fromEntries(DS.components.map((c) => [c.name, c]));
  const styleOf = (comp, key) => (comp.states[key] ? DS.styles[comp.states[key]] : null);

  function getStyle(comp, state) {
    const pick = {
      hover: 'hover', pressed: 'pressed', selected: 'selected', disabled: 'disabled',
      warning: 'warning', critical: 'critical', unavailable: 'unavailable'
    }[state];
    let s = state === 'pressed' ? (styleOf(comp, 'pressed') || styleOf(comp, 'hover')) : pick ? styleOf(comp, pick) : null;
    return s || styleOf(comp, 'idle');
  }

  function resolveStyle(comp, state, hovered, pressing) {
    if (hovered || pressing) {
      let answer = null;
      if (state === 'idle' || state === 'hover') answer = pressing && styleOf(comp, 'pressed') ? styleOf(comp, 'pressed') : styleOf(comp, 'hover');
      else if (state === 'warning') answer = styleOf(comp, 'warningHover');
      else if (state === 'critical') answer = styleOf(comp, 'criticalHover');
      else if (state === 'selected') answer = styleOf(comp, 'selectedHover');
      if (answer) return answer;
    }
    return getStyle(comp, state);
  }

  // ---- shapes ------------------------------------------------------------------------------

  const shapes = [];
  let focusEl = null;
  let keyboardMode = false;

  class Shape {
    constructor(el, o) {
      this.el = el;
      this.comp = o.component ? compByName[o.component] : null;
      this.styleName = o.style || null;
      this.state = o.state || 'idle';
      this.tint = o.tint || [1, 1, 1];
      this.sprite = o.sprite || null;
      this.owner = o.owner || null;
      this.hovered = false;
      this.pressing = false;
      this.selected = false;
      this.disabled = false;
      this.shown = null;
      this.drawn = null;
      this.from = null;
      this.blendStart = 0;
      this.blendDur = 0;
      this.appear = o.appear || false;
      el.__shape = this;
      el.setAttribute('data-shape', '');
      shapes.push(this);
    }

    effectiveState() {
      if (this.disabled) return 'disabled';
      if (this.selected && (this.state === 'idle' || this.state === 'hover')) return 'selected';
      return this.state;
    }

    target() {
      if (this.styleName) return DS.styles[this.styleName];
      const owner = this.owner && this.owner.__shape;
      const hovered = owner ? owner.hovered : this.hovered;
      const pressing = owner ? owner.pressing : this.pressing;
      const state = this.effectiveState();
      return resolveStyle(this.comp, state, hovered, pressing);
    }

    look(now) {
      const style = this.target();
      const target = lookOf(style);
      if (style !== this.shown) {
        const animate = !settings.reduceMotion && style.transition > 0 && (this.drawn || (this.appear && style.fadeIn));
        this.from = this.drawn ? this.drawn : this.appear ? transparent(target) : null;
        this.blendStart = now;
        this.blendDur = animate ? style.transition : 0;
        this.shown = style;
        this.appear = false;
      }
      let look = target;
      if (this.from && this.blendDur > 0) {
        const t = Math.min(1, (now - this.blendStart) / this.blendDur);
        if (t < 1) look = lerpLook(this.from, target, t * t * (3 - 2 * t));
      }
      this.drawn = look;
      return look;
    }
  }

  function makeShape(el, o) {
    return new Shape(el, o);
  }

  // ---- renderer and frame loop -----------------------------------------------------------

  const canvas = $('#gl');
  let renderer = null;
  let unit = 1;
  let dirty = true;

  function readUnit() {
    unit = parseFloat(getComputedStyle(root).getPropertyValue('--u')) || 1;
  }

  function frame(ms) {
    requestAnimationFrame(frame);
    if (!renderer || document.hidden) return;
    const now = ms / 1000;
    if (renderer.cssW !== window.innerWidth || renderer.cssH !== window.innerHeight) renderer.resize();
    renderer.begin();
    const keyboardRing = keyboardMode && focusEl && focusEl.__shape ? focusEl : null;
    for (const stage of $$('.stage')) {
      const rect = stage.getBoundingClientRect();
      if (rect.bottom < 0 || rect.top > renderer.cssH || rect.width === 0) continue;
      renderer.clip(null);
      const bg = stage.dataset.bg || settings.bg;
      if (bg !== 'none') renderer.backdrop(rect, BACKDROPS[bg].rgb, 12 * unit);
      renderer.clip(rect);
      for (const el of $$('[data-shape]', stage)) {
        const shape = el.__shape;
        if (!shape || el.offsetParent === null && getComputedStyle(el).position !== 'fixed') continue;
        const r = el.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        const tint = typeof shape.tint === 'function' ? shape.tint() : shape.tint;
        const alpha = shape.el.dataset.dim ? parseFloat(shape.el.dataset.dim) : 1;
        const kind = shape.target().kind;
        renderer.draw(r, scaled(adjusted(shape.look(now)), unit), [tint[0], tint[1], tint[2], alpha], kind, shape.sprite, now);
        if (keyboardRing === el) drawFocusRing(el, r, now);
      }
    }
    renderer.end();
  }

  const ringShape = { drawn: null, shown: null, from: null, blendStart: 0, blendDur: 0, lastEl: null };

  function drawFocusRing(el, r, now) {
    const style = styleOf(compByName['Focus Ring'], 'idle');
    const target = lookOf(style);
    if (ringShape.lastEl !== el) {
      ringShape.lastEl = el;
      ringShape.from = transparent(target);
      ringShape.blendStart = now;
      ringShape.blendDur = settings.reduceMotion ? 0 : style.transition;
    }
    let look = target;
    if (ringShape.blendDur > 0) {
      const t = Math.min(1, (now - ringShape.blendStart) / ringShape.blendDur);
      if (t < 1) look = lerpLook(ringShape.from, target, t * t * (3 - 2 * t));
    }
    const gap = 4 * unit;
    const ring = { left: r.left - gap, top: r.top - gap, width: r.width + gap * 2, height: r.height + gap * 2 };
    renderer.clip(null);
    renderer.draw(ring, scaled(adjusted(look), unit), [1, 1, 1, 1], 0, null, now);
  }

  // ---- text styles ---------------------------------------------------------------------------

  function textStyleCss() {
    return DS.tokens.textStyles.map((t) => {
      const weight = t.font === 'SemiBold' ? 600 : 500;
      return `.ts-${t.name.toLowerCase()}{font-family:'Rajdhani',sans-serif;font-weight:${weight};font-size:calc(var(--u)*${t.size});` +
        `letter-spacing:${(t.letterSpacing * 0.01).toFixed(3)}em;${t.uppercase ? 'text-transform:uppercase;' : ''}` +
        `color:${cssColor(t.color)};line-height:1.15;}`;
    }).join('\n');
  }
  const installTextStylesInto = (el) => { el.textContent = textStyleCss(); };
  function installTextStyles() {
    document.head.append(h('style', { id: 'text-styles' }, textStyleCss()));
  }

  // ---- interaction -------------------------------------------------------------------------

  function wireInteractive(el, onActivate) {
    const shape = el.__shape;
    el.setAttribute('tabindex', '0');
    el.setAttribute('role', 'button');
    el.classList.add('interactive');
    el.addEventListener('pointerenter', (e) => { if (e.pointerType === 'mouse' && !shape.disabled) shape.hovered = true; });
    el.addEventListener('pointerleave', () => { shape.hovered = false; shape.pressing = false; });
    el.addEventListener('pointerdown', () => { keyboardMode = false; if (!shape.disabled) shape.pressing = true; });
    el.addEventListener('pointerup', () => { shape.pressing = false; });
    el.addEventListener('pointercancel', () => { shape.pressing = false; shape.hovered = false; });
    el.addEventListener('click', () => { if (!shape.disabled && onActivate) onActivate(shape); });
    el.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); shape.pressing = true; }
    });
    el.addEventListener('keyup', (e) => {
      if (e.key === 'Enter' || e.key === ' ') { shape.pressing = false; if (!shape.disabled && onActivate) onActivate(shape); }
    });
    // Keyboard focus shows the hover look and the Focus Ring; the pointer hides it.
    el.addEventListener('focus', () => { focusEl = el; if (keyboardMode && !shape.disabled) shape.hovered = true; });
    el.addEventListener('blur', () => { if (focusEl === el) focusEl = null; shape.hovered = false; });
  }

  window.addEventListener('keydown', (e) => {
    if (e.key === 'Tab' || e.key.startsWith('Arrow')) {
      keyboardMode = true;
      if (focusEl && focusEl.__shape) focusEl.__shape.hovered = !focusEl.__shape.disabled;
    }
  });
  window.addEventListener('pointerdown', () => {
    keyboardMode = false;
    if (focusEl && focusEl.__shape) focusEl.__shape.hovered = false;
  }, true);

  // ---- component specimens ---------------------------------------------------------------------

  const SIZE = {
    'Leader Line': [140, 2], 'Leader Anchor': [12, 12], 'Accent Bar': [4, 64], Underline: [40, 2], Icon: [28, 28],
    'Icon Button': [44, 44], Badge: [124, 34], Segment: [150, 44], Button: [150, 44], Card: [200, 96],
    'List Row': [224, 72], 'Focus Ring': [180, 88]
  };
  const STATE_LABELS = {
    idle: 'Idle', hover: 'Hover', pressed: 'Pressed', selected: 'Selected', selectedHover: 'Selected hover',
    disabled: 'Disabled', warning: 'Warning', warningHover: 'Warning hover', critical: 'Critical',
    criticalHover: 'Critical hover', unavailable: 'Unavailable'
  };
  const STATE_TONE = {
    warning: 'Warning', warningHover: 'Warning', critical: 'Critical', criticalHover: 'Critical',
    unavailable: 'Neutral', disabled: 'Neutral', selected: 'Accent', selectedHover: 'Accent'
  };
  const sized = (el, w, h2) => {
    if (w) el.style.width = `calc(var(--u) * ${w})`;
    if (h2) el.style.height = `calc(var(--u) * ${h2})`;
    return el;
  };
  const accent = () => tokenRgb('Accent');
  const textTint = () => tokenRgb('Text');

  function specimenText(comp, key) {
    if (comp.name === 'Segment' || comp.name === 'Button') return 'LABEL';
    if (comp.name === 'Badge') return { warning: 'DUE SOON', critical: 'OVERDUE', unavailable: 'NO DATA' }[key] || 'ON TRACK';
    if (comp.name === 'Card') return 'READING';
    if (comp.name === 'List Row') return 'PART NAME';
    return null;
  }

  function textColor(comp, key) {
    const tone = STATE_TONE[key];
    if (tone) return `var(--c-${tone.toLowerCase()})`;
    return ['Card', 'List Row', 'Badge'].includes(comp.name) ? 'var(--c-label)' : 'var(--c-text)';
  }

  /** One component drawn in one state's fixed style, labelled; clicking it opens the inspector. */
  function specimen(comp, key) {
    const styleName = comp.states[key];
    const mark = comp.group === 'Marks';
    const box = sized(h('div', { class: 'sample' }), ...SIZE[comp.name] || [180, 88]);
    const isIcon = comp.name === 'Icon';
    makeShape(box, {
      style: styleName,
      tint: mark ? accent : isIcon ? textTint : [1, 1, 1],
      sprite: isIcon ? 'Cross Section Icon' : null
    });
    const text = specimenText(comp, key);
    if (text) {
      const left = comp.name === 'Card' || comp.name === 'List Row';
      box.append(h('span', {
        class: `ts-${left ? 'label' : comp.name === 'Badge' ? 'label' : 'button'} fill-text ${left ? 'left' : ''}`,
        style: `color:${textColor(comp, key)}`
      }, text));
    }
    if (comp.name === 'Icon Button') {
      const icon = h('div', { class: 'glyph' });
      sized(icon, 20, 20);
      makeShape(icon, {
        tint: accent, sprite: 'Close Icon',
        style: key === 'idle' ? compByName.Icon.states.idle : compByName.Icon.states.hover
      });
      box.append(icon);
    }
    const cell = h('button', {
      class: 'specimen', type: 'button', 'data-style': styleName,
      title: `${comp.name} — ${STATE_LABELS[key]}: inspect style`,
      onclick: () => inspect(styleName, `${comp.name} · ${STATE_LABELS[key]}`)
    }, h('div', { class: 'sample-slot' }, box), h('span', { class: 'cap' }, STATE_LABELS[key]));
    return cell;
  }

  // The live version of a component: hover, press and Tab to try its states.
  function liveComponent(comp) {
    const wrap = h('div', { class: 'live-wrap' });
    let el;
    const interactive = (node, fn) => { wireInteractive(node, fn); return node; };
    switch (comp.name) {
      case 'Button': {
        el = sized(h('div', { class: 'live' }), 150, 44);
        makeShape(el, { component: 'Button' });
        el.append(h('span', { class: 'ts-button fill-text' }, 'CLICK ME'));
        interactive(el, (s) => { s.selected = !s.selected; });
        const off = sized(h('div', { class: 'live' }), 150, 44);
        makeShape(off, { component: 'Button' });
        off.__shape.disabled = true;
        off.append(h('span', { class: 'ts-button fill-text', style: 'color:var(--c-neutral)' }, 'DISABLED'));
        wrap.append(el, off);
        el = null;
        break;
      }
      case 'Segment': {
        const frame = sized(h('div', { class: 'live seg-frame' }), 0, 52);
        makeShape(frame, { component: 'Panel' });
        const items = ['TELEMETRY', 'SERVICE'].map((label, i) => {
          const seg = sized(h('div', { class: 'live seg' }), 130, 44);
          makeShape(seg, { component: 'Segment' });
          seg.append(h('span', { class: 'ts-button fill-text' }, label));
          seg.__shape.selected = i === 0;
          interactive(seg, () => items.forEach((o) => { o.__shape.selected = o === seg; o.querySelector('span').style.color = o === seg ? 'var(--c-accent)' : ''; }));
          if (i === 0) seg.querySelector('span').style.color = 'var(--c-accent)';
          return seg;
        });
        frame.append(...items);
        wrap.append(frame);
        break;
      }
      case 'Icon Button': {
        const b = sized(h('div', { class: 'live' }), 44, 44);
        makeShape(b, { component: 'Icon Button' });
        const g = sized(h('div', { class: 'glyph' }), 20, 20);
        makeShape(g, { component: 'Icon', tint: accent, sprite: 'Pin Icon', owner: b });
        b.append(g);
        interactive(b);
        wrap.append(b);
        break;
      }
      case 'Card': {
        const c = sized(h('div', { class: 'live card' }), 200, 96);
        makeShape(c, { component: 'Card' });
        c.append(h('span', { class: 'ts-label', style: 'color:var(--c-label)' }, 'MOTOR LOAD'), h('span', { class: 'ts-value' }, '79.7 %'));
        interactive(c, (s) => { s.selected = !s.selected; });
        wrap.append(c);
        break;
      }
      case 'List Row': {
        const r = sized(h('div', { class: 'live row' }), 224, 72);
        makeShape(r, { component: 'List Row' });
        r.append(h('span', { class: 'ts-label', style: 'color:var(--c-label)' }, 'PART NAME'));
        interactive(r);
        wrap.append(r);
        break;
      }
      case 'Badge':
        for (const [tone, text] of [['idle', 'ON TRACK'], ['warning', 'DUE SOON'], ['critical', 'OVERDUE'], ['unavailable', 'NO DATA']]) {
          const b = sized(h('div', { class: 'live' }), 110, 34);
          makeShape(b, { component: 'Badge', state: tone });
          b.append(h('span', { class: 'ts-label fill-text', style: `color:${textColor(comp, tone === 'idle' ? 'idle' : tone)}` }, text));
          wrap.append(b);
        }
        break;
      default:
        return null;
    }
    return h('div', { class: 'live-cell' }, wrap, h('span', { class: 'cap live-cap' }, 'Live: hover, press, or Tab to it'));
  }

  // ---- inspector --------------------------------------------------------------------------------

  const FIELDS = [
    ['cornerRadius', 'Corner radius', 0, 40, 0.5], ['strokeWidth', 'Stroke width', 0, 6, 0.05],
    ['glowSize', 'Glow size', 0, 60, 0.5], ['glowStrength', 'Glow strength', 0, 4, 0.01],
    ['innerGlow', 'Inner glow', 0, 1, 0.01], ['highlight', 'Highlight', 0, 1, 0.01],
    ['bottomLight', 'Bottom light', 0, 2, 0.01], ['bottomLightHeight', 'Light height', 0, 1, 0.01],
    ['gradientEnd', 'Gradient end', 0, 2, 0.01], ['hatch', 'Hatch', 0, 1, 0.01],
    ['shadowSoftness', 'Shadow softness', 0, 30, 0.5], ['transition', 'Transition (s)', 0, 1, 0.01],
    ['pulse', 'Pulse', 0, 1, 0.01], ['pulseSpeed', 'Pulse speed', 0.1, 2.5, 0.05]
  ];
  const COLOR_FIELDS = [['fill', 'Fill'], ['stroke', 'Stroke'], ['glow', 'Glow'], ['shadow', 'Shadow']];
  let inspected = null;

  function inspect(styleName, title) {
    inspected = styleName;
    const style = DS.styles[styleName];
    const panel = $('#inspector');
    panel.hidden = false;
    $('#inspector-title').textContent = title;
    $('#inspector-asset').textContent = styleName + '.asset';
    const body = $('#inspector-body');
    body.textContent = '';
    for (const [key, label] of COLOR_FIELDS) {
      const c = style[key];
      const tokenSelect = h('select', { onchange: (e) => { c.token = e.target.value; c.custom = [0, 0, 0, 0]; } },
        ...DS.tokens.colors.map((t) => h('option', { value: t.name, selected: t.name === c.token }, t.name)));
      const alpha = h('input', { type: 'range', min: 0, max: 1, step: 0.01, value: c.alpha, oninput: (e) => { c.alpha = +e.target.value; out.textContent = (+e.target.value).toFixed(2); } });
      const out = h('output', {}, c.alpha.toFixed(2));
      body.append(h('div', { class: 'field color' }, h('label', {}, label), tokenSelect, h('div', { class: 'slider' }, alpha, out)));
    }
    for (const [key, label, min, max, step] of FIELDS) {
      const input = h('input', { type: 'range', min, max, step, value: style[key], oninput: (e) => { style[key] = +e.target.value; out.textContent = fmt(+e.target.value); } });
      const out = h('output', {}, fmt(style[key]));
      body.append(h('div', { class: 'field' }, h('label', {}, label), h('div', { class: 'slider' }, input, out)));
    }
    const offset = style.shadowOffset;
    const oy = h('input', { type: 'range', min: -12, max: 12, step: 0.5, value: offset[1], oninput: (e) => { offset[1] = +e.target.value; out.textContent = fmt(offset[1]); } });
    const out = h('output', {}, fmt(offset[1]));
    body.append(h('div', { class: 'field' }, h('label', {}, 'Shadow offset Y'), h('div', { class: 'slider' }, oy, out)));
    $$('.specimen').forEach((s) => s.classList.toggle('active', s.dataset.style === styleName));
  }
  const fmt = (v) => (Math.round(v * 100) / 100).toString();

  function bindInspector() {
    $('#inspector-close').addEventListener('click', () => { $('#inspector').hidden = true; $$('.specimen.active').forEach((s) => s.classList.remove('active')); });
    $('#inspector-reset').addEventListener('click', () => {
      if (!inspected) return;
      DS.styles[inspected] = JSON.parse(JSON.stringify(defaults.styles[inspected]));
      inspect(inspected, $('#inspector-title').textContent);
    });
    $('#inspector-copy').addEventListener('click', async (e) => {
      const text = JSON.stringify({ [inspected]: DS.styles[inspected] }, null, 2);
      try { await navigator.clipboard.writeText(text); e.target.textContent = 'Copied'; } catch { e.target.textContent = 'Copy failed'; }
      setTimeout(() => { e.target.textContent = 'Copy values'; }, 1400);
    });
  }

  // ---- sections ---------------------------------------------------------------------------------

  function buildTokens() {
    const grid = $('#colour-grid');
    for (const token of DS.tokens.colors) {
      const input = h('input', { type: 'color', value: '#' + token.hex.toLowerCase(), 'aria-label': `Edit ${token.name}`,
        oninput: (e) => {
          token.hex = e.target.value.slice(1).toUpperCase();
          code.textContent = '#' + token.hex;
          installTextStylesInto($('#text-styles'));
          refreshCssTokens();
          $('#tokens-reset').hidden = false;
        } });
      const code = h('code', {}, '#' + token.hex);
      const swatch = h('label', { class: 'swatch', style: `--chip:#${token.hex}` }, input);
      input.addEventListener('input', () => { swatch.style.setProperty('--chip', '#' + token.hex); });
      grid.append(h('div', { class: 'token' }, swatch, h('div', { class: 'token-name' }, token.name),
        h('button', { class: 'copy', type: 'button', title: 'Copy hex', onclick: async () => {
          try { await navigator.clipboard.writeText('#' + token.hex); } catch { /* clipboard unavailable */ }
          code.textContent = 'copied';
          setTimeout(() => { code.textContent = '#' + token.hex; }, 900);
        } }, code),
        h('div', { class: 'use' }, token.use)));
    }
    $('#tokens-reset').addEventListener('click', () => {
      DS.tokens.colors.forEach((t, i) => { t.hex = defaults.colors[i].hex; });
      $('#colour-grid').textContent = '';
      buildTokens();
      installTextStylesInto($('#text-styles'));
      refreshCssTokens();
      $('#tokens-reset').hidden = true;
    });
    const spacing = $('#spacing');
    spacing.textContent = '';
    for (const s of DS.tokens.spacing)
      spacing.append(h('div', { class: 'space' }, h('div', { class: 'bar', style: `width:calc(var(--u)*${s.value*2})` }),
        h('div', { class: 'meta' }, h('strong', {}, `${s.name} · ${s.value}`), h('span', {}, s.use))));
    $('#motion').textContent = `Fast ${DS.tokens.fastMotion}s (hover) · Normal ${DS.tokens.normalMotion}s (selection, alarms)`;
  }

  function refreshCssTokens() {
    for (const t of DS.tokens.colors) root.style.setProperty('--c-' + t.name.toLowerCase().replace(/ /g, '-'), '#' + t.hex);
  }

  function buildType() {
    const list = $('#type-list');
    for (const t of DS.tokens.textStyles) {
      list.append(h('div', { class: 'type-row' },
        h('div', { class: 'type-meta' }, h('strong', {}, t.name), h('span', {}, `${t.font} · ${t.size} · tracking ${t.letterSpacing}${t.uppercase ? ' · caps' : ''}`), h('em', {}, t.use)),
        h('div', { class: `ts-${t.name.toLowerCase()} sample-text` }, t.name === 'Value' ? '79.7 %' : t.name === 'Display' ? '160 h' : 'Motor load')));
    }
  }

  function buildComponents() {
    const host = $('#component-groups');
    for (const group of DS.groups) {
      const section = h('div', { class: 'group' }, h('h3', { class: 'ts-label group-title' }, group));
      for (const comp of DS.components.filter((c) => c.group === group)) {
        const stage = h('div', { class: 'stage specimens' });
        const live = liveComponent(comp);
        if (live) stage.append(live, h('div', { class: 'divider' }));
        for (const key of Object.keys(STATE_LABELS)) if (comp.states[key]) stage.append(specimen(comp, key));
        section.append(h('article', { class: 'component', id: 'c-' + comp.name.toLowerCase().replace(/ /g, '-') },
          h('header', {}, h('h4', {}, comp.name), h('p', {}, comp.description),
            h('p', { class: 'states' }, Object.keys(comp.states).map((k) => STATE_LABELS[k]).join(' · '))),
          stage));
      }
      host.append(section);
    }
  }

  // ---- composition: a telemetry rail and a service sheet --------------------------------------

  function buildCompose() {
    const stage = $('#compose-stage');
    const tone = { normal: 'idle', warning: 'warning', critical: 'critical', unavailable: 'unavailable' };
    const cards = [];
    const mk = (label, value) => {
      const c = h('div', { class: 'live card' });
      sized(c, 220, 104);
      makeShape(c, { component: 'Card' });
      const l = h('span', { class: 'ts-label' }, label);
      const v = h('span', { class: 'ts-value' }, value);
      c.append(l, v);
      wireInteractive(c, (s) => { s.selected = !s.selected; });
      c.setTone = (t) => {
        c.__shape.state = tone[t];
        l.style.color = { normal: 'var(--c-label)', warning: 'var(--c-warning)', critical: 'var(--c-critical)', unavailable: 'var(--c-neutral)' }[t];
        c.__shape.selected = c.__shape.selected && t === 'normal';
      };
      c.setTone('normal');
      c.setValue = (text) => { v.textContent = text; };
      cards.push(c);
      return c;
    };
    const motor = mk('MOTOR LOAD', '79.7 %');
    const belt = mk('BELT SPEED', '0.8 M/S');
    const slip = mk('BELT SLIP', '1.3 %');
    const speed = mk('MOTOR SPEED', '1,420 RPM');

    const rows = [['1', 'BEARINGS', 'REGREASE', '160 H', 'warning'], ['2', 'SCREEN DECK', 'REPLACE', '40 H', 'critical'], ['3', 'DRIVE ROLLER', 'REGREASE', '740 H', 'normal']].map(([n, title, sub, val, t]) => {
      const r = sized(h('div', { class: 'live svc-row' }), 0, 72);
      makeShape(r, { component: 'List Row', state: tone[t] });
      const color = { normal: 'var(--c-accent)', warning: 'var(--c-warning)', critical: 'var(--c-critical)' }[t];
      r.append(h('span', { class: 'ts-display', style: `color:${color};font-size:calc(var(--u)*30)` }, n),
        h('span', { class: 'svc-text' }, h('span', { class: 'ts-title' }, title), h('span', { class: 'ts-caption' }, sub)),
        h('span', { class: 'ts-display', style: `color:${color};font-size:calc(var(--u)*28)` }, val));
      wireInteractive(r);
      return r;
    });

    const seg = sized(h('div', { class: 'live seg-frame' }), 0, 52);
    makeShape(seg, { component: 'Panel' });
    const segs = ['TELEMETRY', 'SERVICE'].map((label, i) => {
      const s = sized(h('div', { class: 'live seg' }), 130, 44);
      makeShape(s, { component: 'Segment' });
      s.__shape.selected = i === 0;
      s.append(h('span', { class: 'ts-button fill-text', style: i === 0 ? 'color:var(--c-accent)' : '' }, label));
      wireInteractive(s, () => segs.forEach((o) => { o.__shape.selected = o === s; o.querySelector('span').style.color = o === s ? 'var(--c-accent)' : ''; }));
      return s;
    });
    seg.append(...segs);

    const sheet = h('div', { class: 'live sheet' });
    makeShape(sheet, { component: 'Sheet' });
    sheet.append(h('div', { class: 'ts-label sheet-title' }, 'Service schedule'), ...rows);

    stage.append(h('div', { class: 'compose-left' }, seg, h('div', { class: 'card-grid' }, ...cards)), sheet);

    // Drive the motor load card from a slider, with the app's thresholds.
    const slider = $('#load-slider');
    const set = () => {
      const v = +slider.value;
      motor.setValue(v.toFixed(1) + ' %');
      motor.setTone(v >= 95 ? 'critical' : v >= 85 ? 'warning' : 'normal');
      $('#load-out').textContent = v.toFixed(1) + ' %';
    };
    slider.addEventListener('input', set);
    set();
    $('#sensor-toggle').addEventListener('click', (e) => {
      const off = e.target.getAttribute('aria-pressed') !== 'true';
      e.target.setAttribute('aria-pressed', off);
      speed.setTone(off ? 'unavailable' : 'normal');
      speed.setValue(off ? '— RPM' : '1,420 RPM');
    });
    // Idle readings drift, like the simulator.
    let t = 0;
    setInterval(() => {
      if (document.hidden) return;
      t += 1;
      belt.setValue((0.8 + 0.05 * Math.sin(t / 3)).toFixed(2) + ' M/S');
      slip.setValue((1.3 + 0.2 * Math.sin(t / 2)).toFixed(1) + ' %');
      if (speed.__shape.state !== 'unavailable') speed.setValue(Math.round(1420 + 12 * Math.sin(t / 2.5)).toLocaleString('en') + ' RPM');
    }, 1000);
  }

  // ---- hero ---------------------------------------------------------------------------------------

  function buildHero() {
    const stage = $('#hero-stage');
    const make = (label, value, t) => {
      const c = sized(h('div', { class: 'live card' }), 200, 96);
      makeShape(c, { component: 'Card', state: { normal: 'idle', warning: 'warning', critical: 'critical', unavailable: 'unavailable' }[t] });
      c.append(h('span', { class: 'ts-label', style: `color:var(--c-${{ normal: 'label', warning: 'warning', critical: 'critical', unavailable: 'neutral' }[t]})` }, label), h('span', { class: 'ts-value' }, value));
      wireInteractive(c, (s) => { s.selected = !s.selected; });
      return c;
    };
    stage.append(h('div', { class: 'card-grid hero-cards' },
      make('MOTOR LOAD', '79.7 %', 'normal'), make('BELT SPEED', '0.8 M/S', 'warning'),
      make('BELT SLIP', '1.3 %', 'critical'), make('MOTOR SPEED', '— RPM', 'unavailable')));
  }

  // ---- header controls ----------------------------------------------------------------------------

  function bindHeader() {
    document.body.dataset.bg = settings.bg;
    $$('[data-bg-choice]').forEach((b) => b.addEventListener('click', () => {
      settings.bg = b.dataset.bgChoice;
      document.body.dataset.bg = settings.bg;
      $$('[data-bg-choice]').forEach((o) => o.setAttribute('aria-pressed', o === b));
    }));
    $('#opt-contrast').addEventListener('click', (e) => {
      settings.contrast = !settings.contrast;
      e.currentTarget.setAttribute('aria-pressed', settings.contrast);
    });
    const motion = $('#opt-motion');
    motion.setAttribute('aria-pressed', settings.reduceMotion);
    motion.addEventListener('click', (e) => {
      settings.reduceMotion = !settings.reduceMotion;
      e.currentTarget.setAttribute('aria-pressed', settings.reduceMotion);
    });
  }

  // ---- start --------------------------------------------------------------------------------------------

  function start() {
    refreshCssTokens();
    installTextStyles();
    buildHero();
    buildTokens();
    buildType();
    buildComponents();
    buildCompose();
    bindInspector();
    bindHeader();
    readUnit();
    // Sections are built here, after the browser has jumped to a #link: jump again.
    if (location.hash && $(location.hash)) $(location.hash).scrollIntoView();
    try {
      renderer = new window.ShapeRenderer(canvas);
      for (const [name, src] of Object.entries(DS.icons)) renderer.sprite(name, src);
      renderer.resize();
      document.body.classList.add('gl-ok');
    } catch (err) {
      console.error(err);
      document.body.classList.add('gl-fail');
      return;
    }
    window.addEventListener('resize', () => { readUnit(); renderer.resize(); });
    requestAnimationFrame(frame);
  }

  if (document.fonts && document.fonts.ready) document.fonts.ready.then(start);
  else window.addEventListener('load', start);
})();
