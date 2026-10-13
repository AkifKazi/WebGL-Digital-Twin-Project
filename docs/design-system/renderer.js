/*
 * WebGL2 port of Assets/Materials/UI/UIShape.shader.
 *
 * Every shape is one quad grown past its rect by the reach of its glow and shadow, drawn per pixel
 * from a signed distance, exactly as in Unity: shadow, outer glow, fill, hatch, inner glow, bottom
 * light, stroke. The Unity project is in Linear colour space, so the page is drawn into an sRGB
 * framebuffer (blending happens in linear light) and encoded on the way to the screen.
 *
 * Sizes are CSS pixels; callers scale canvas units before drawing. Coordinates inside the shader
 * run from the shape's centre with y up, like the Unity shader.
 */
(function () {
  'use strict';

  const VERT = `#version 300 es
  precision highp float;
  uniform vec2 uView;     // canvas size, css px
  uniform vec4 uRect;     // centre x, centre y (y down), half width, half height
  uniform float uReach;
  out vec2 vLocal;
  void main() {
    vec2 c = vec2((gl_VertexID & 1) == 0 ? -1.0 : 1.0, (gl_VertexID & 2) == 0 ? -1.0 : 1.0);
    vec2 corner = c * (uRect.zw + uReach);
    vLocal = corner;
    vec2 screen = vec2(uRect.x + corner.x, uRect.y - corner.y);
    gl_Position = vec4(screen.x / uView.x * 2.0 - 1.0, 1.0 - screen.y / uView.y * 2.0, 0.0, 1.0);
  }`;

  const FRAG = `#version 300 es
  precision highp float;
  in vec2 vLocal;
  out vec4 outColor;

  uniform vec4 uRect;      // only the half size is used here
  uniform vec4 uShape;     // corner radius, stroke width, glow size, shadow softness
  uniform vec4 uExtra;     // shadow offset xy, inner glow, gradient end
  uniform vec4 uAnim;      // hatch, glow multiplier, bottom light, highlight
  uniform vec4 uCut;       // chamfer (or tail size), chamfer corner mask, gradient direction + tail side * 4, light height
  uniform vec4 uFill;
  uniform vec4 uStroke;
  uniform vec4 uGlow;      // rgb premultiplied by strength, strength
  uniform vec4 uShadow;
  uniform float uKind;     // 0 box, 1 icon
  uniform float uHatchSpacing;
  uniform vec4 uIcon;      // icon: shadow offset xy, glow reach, shadow softness
  uniform sampler2D uTex;

  float SdBox(vec2 p, vec2 halfSize, float radius, float chamfer, float cornerMask) {
    radius = min(radius, min(halfSize.x, halfSize.y));
    vec2 q = abs(p) - halfSize + radius;
    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
    if (chamfer > 0.0) {
      float bit = p.y >= 0.0 ? (p.x < 0.0 ? 1.0 : 2.0) : (p.x >= 0.0 ? 4.0 : 8.0);
      float on = mod(floor(cornerMask / bit), 2.0);
      chamfer = min(chamfer, min(halfSize.x, halfSize.y) * 2.0);
      float cutDistance = (abs(p.x) + abs(p.y) - (halfSize.x + halfSize.y - chamfer)) * 0.70710678;
      d = on > 0.5 ? max(d, cutDistance) : d;
    }
    return d;
  }

  float SdTriangle(vec2 p, vec2 p0, vec2 p1, vec2 p2) {
    vec2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
    vec2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
    vec2 pq0 = v0 - e0 * clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
    vec2 pq1 = v1 - e1 * clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
    vec2 pq2 = v2 - e2 * clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
    float s = sign(e0.x * e2.y - e0.y * e2.x);
    vec2 d = min(min(vec2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                     vec2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                     vec2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
    return -sqrt(d.x) * sign(d.y);
  }

  // The box with its speech tail, as UIShape.shader draws it (side 1 bottom right, 2 bottom left).
  float SdShape(vec2 p, vec2 halfSize, float radius, float cut, float cornerMask, float tailSide) {
    if (tailSide < 0.5) return SdBox(p, halfSize, radius, cut, cornerMask);
    vec2 q = vec2(tailSide < 1.5 ? p.x : -p.x, p.y);
    float size = min(cut, min(halfSize.x, halfSize.y));
    vec2 corner = vec2(halfSize.x, -halfSize.y);
    float tail = SdTriangle(q, corner + vec2(-size, 0.0), corner + vec2(size, 0.0), corner + vec2(-size, size * 2.0));
    return min(SdBox(p, halfSize, radius, 0.0, 0.0), tail);
  }

  vec4 Sprite(vec2 at) {
    vec2 inside = step(0.0, at) * step(at, vec2(1.0));
    return textureLod(uTex, clamp(at, 0.0, 1.0), 0.0) * (inside.x * inside.y);
  }

  vec4 FragIcon() {
    vec2 halfSize = max(uRect.zw, vec2(1e-3));
    vec2 at = vLocal / (halfSize * 2.0) + 0.5;
    vec2 unit = 1.0 / (halfSize * 2.0);

    vec4 texel = Sprite(at);
    float bodyAlpha = texel.a * uFill.a;
    vec4 color = vec4(texel.rgb * uFill.rgb * bodyAlpha, bodyAlpha);

    vec2 offset = uIcon.xy * unit;
    float shadowAlpha = Sprite(at - offset).a * uShadow.a;
    if (uIcon.w > 0.0) {
      vec2 spread = uIcon.w * 0.5 * unit;
      shadowAlpha = (shadowAlpha
        + Sprite(at - offset + vec2(spread.x, 0.0)).a * uShadow.a
        + Sprite(at - offset - vec2(spread.x, 0.0)).a * uShadow.a
        + Sprite(at - offset + vec2(0.0, spread.y)).a * uShadow.a
        + Sprite(at - offset - vec2(0.0, spread.y)).a * uShadow.a) * 0.2;
    }
    color += vec4(uShadow.rgb, 1.0) * shadowAlpha * (1.0 - bodyAlpha);

    if (uGlow.a > 0.001) {
      vec2 reach = uIcon.z * unit;
      float sum = 0.0;
      for (int i = 0; i < 8; i++) {
        float angle = float(i) * 0.78539816;
        vec2 direction = vec2(cos(angle), sin(angle));
        sum += Sprite(at + direction * reach * 0.4).a * 0.6;
        sum += Sprite(at + direction * reach).a * 0.4;
      }
      float glow = clamp(sum / 8.0, 0.0, 1.0) * (1.0 - bodyAlpha);
      color.rgb += uGlow.rgb * glow * uAnim.y;
    }
    return color;
  }

  vec4 FragBox(float pixel) {
    vec2 p = vLocal;
    vec2 halfSize = uRect.zw;
    float aa = pixel * 0.75;

    vec2 drawnHalf = max(halfSize, vec2(pixel * 0.5));
    float thin = clamp(halfSize.x * 2.0 / pixel, 0.0, 1.0) * clamp(halfSize.y * 2.0 / pixel, 0.0, 1.0);

    float radius = uShape.x;
    float directionAndTail = floor(uCut.z + 0.5);
    float tailSide = floor(directionAndTail / 4.0);
    float d = SdShape(p, drawnHalf, radius, uCut.x, floor(uCut.y + 0.5), tailSide);
    float body = clamp(0.5 - d / aa * 0.5, 0.0, 1.0);
    float outside = max(d, 0.0);

    float softness = uShape.w;
    float ds = SdShape(p - uExtra.xy, drawnHalf, radius, uCut.x, floor(uCut.y + 0.5), tailSide);
    float shadowCover = 1.0 - smoothstep(-softness * 0.5 - aa, softness * 0.5 + aa, ds);
    vec4 color = vec4(uShadow.rgb, 1.0) * (uShadow.a * shadowCover * thin);

    float glowSize = max(uShape.z, 1e-3);
    float highlight = -uAnim.w;
    float outsideOnly = (1.0 - body) * step(1e-3, uShape.z);
    float hot = exp(-outside / max(glowSize * 0.06, 0.6));
    float core = exp(-outside / (glowSize * 0.2));
    float haze = pow(clamp(1.0 - outside / glowSize, 0.0, 1.0), 1.8);
    float glow = (hot * 0.25 + core * 0.45 + haze * 0.4) * outsideOnly;
    color.rgb += uGlow.rgb * glow * uAnim.y;
    color.rgb += mix(uGlow.rgb, uGlow.aaa, 0.75) * hot * highlight * 0.9 * outsideOnly * uAnim.y;

    float direction = directionAndTail - tailSide * 4.0;
    vec2 uv = p / max(halfSize, vec2(1e-3)) * 0.5 + 0.5;
    float along = direction < 0.5 ? 1.0 - uv.y : direction < 1.5 ? uv.y : direction < 2.5 ? uv.x : 1.0 - uv.x;
    float gradient = mix(1.0, uExtra.w, clamp(along, 0.0, 1.0));
    float fillAlpha = uFill.a * gradient * body * thin;
    vec3 fillColor = uFill.rgb * mix(1.0, gradient, 0.35);
    color = vec4(fillColor * fillAlpha, fillAlpha) + color * (1.0 - fillAlpha);

    if (uAnim.x > 0.0) {
      float stripe = fract((p.x + p.y) / uHatchSpacing);
      float band = clamp((abs(stripe - 0.5) - 0.25) * uHatchSpacing / max(pixel, 1e-3) + 0.5, 0.0, 1.0);
      float hatchAlpha = uAnim.x * band * body * uStroke.a;
      color = vec4(uStroke.rgb * hatchAlpha, hatchAlpha) + color * (1.0 - hatchAlpha);
    }

    float innerReach = max(glowSize * 0.45, 2.0);
    float innerDistance = SdBox(p, drawnHalf, max(radius, innerReach * 1.5), 0.0, 0.0);
    float innerGlow = exp(-max(-innerDistance, 0.0) / innerReach) * body * uExtra.z;
    vec3 innerColor = mix(uGlow.rgb, uGlow.aaa, highlight * 0.5);
    color.rgb += innerColor * innerGlow * uAnim.y * thin;

    if (uAnim.z > 0.0) {
      float rise = max(p.y + drawnHalf.y, 0.0);
      float height = max(uCut.w * drawnHalf.y * 2.0, 2.0);
      float lamp = exp(-rise / (height * 0.35)) * 0.6 + pow(clamp(1.0 - rise / height, 0.0, 1.0), 2.0) * 0.4;
      float edge = clamp(1.0 - rise / max(1.5, pixel * 1.5), 0.0, 1.0);
      vec3 lightColor = uGlow.a > 0.001 ? uGlow.rgb / uGlow.a : uStroke.a > 0.0 ? uStroke.rgb : uFill.rgb;
      color.rgb += lightColor * (lamp * 0.55 + edge * 0.6) * uAnim.z * body * thin;
    }

    if (uShape.y > 0.0) {
      float width = max(uShape.y, pixel);
      float innerBody = clamp(0.5 - (d + width) / aa * 0.5, 0.0, 1.0);
      float strokeAlpha = (body - innerBody) * uStroke.a * thin;
      color = vec4(uStroke.rgb * strokeAlpha, strokeAlpha) + color * (1.0 - strokeAlpha);
    }
    return color;
  }

  void main() {
    float pixel = max(max(fwidth(vLocal.x), fwidth(vLocal.y)), 1e-4);
    outColor = uKind > 0.5 ? FragIcon() : FragBox(pixel);
  }`;

  const BLIT_VERT = `#version 300 es
  out vec2 vUv;
  void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
  }`;

  // Premultiplied linear light in, premultiplied sRGB out.
  const BLIT_FRAG = `#version 300 es
  precision highp float;
  in vec2 vUv;
  out vec4 outColor;
  uniform sampler2D uScene;
  vec3 Encode(vec3 c) {
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
  }
  void main() {
    vec4 c = texture(uScene, vUv);
    vec3 straight = c.a > 0.0 ? clamp(c.rgb / c.a, 0.0, 1.0) : vec3(0.0);
    outColor = vec4(Encode(straight) * c.a, c.a);
  }`;

  function compile(gl, type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS))
      throw new Error(gl.getShaderInfoLog(shader));
    return shader;
  }

  function program(gl, vert, frag) {
    const p = gl.createProgram();
    gl.attachShader(p, compile(gl, gl.VERTEX_SHADER, vert));
    gl.attachShader(p, compile(gl, gl.FRAGMENT_SHADER, frag));
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS))
      throw new Error(gl.getProgramInfoLog(p));
    const uniforms = {};
    const count = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < count; i++) {
      const name = gl.getActiveUniform(p, i).name;
      uniforms[name] = gl.getUniformLocation(p, name);
    }
    return { p, u: uniforms };
  }

  const toLinear = (v) => (v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4));

  class Renderer {
    constructor(canvas) {
      const gl = canvas.getContext('webgl2', { alpha: true, premultipliedAlpha: true, antialias: false });
      if (!gl) throw new Error('WebGL2 is not available');
      this.canvas = canvas;
      this.gl = gl;
      this.shape = program(gl, VERT, FRAG);
      this.blit = program(gl, BLIT_VERT, BLIT_FRAG);
      this.vao = gl.createVertexArray();
      this.target = null;
      this.textures = new Map();
      this.white = this.makeTexture(null);
      this.resize();
    }

    makeTexture(image) {
      const gl = this.gl;
      const tex = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, tex);
      gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
      if (image) gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image);
      else gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([255, 255, 255, 255]));
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
      return tex;
    }

    /** Loads a sprite from a data URI; icons draw once it has arrived. */
    sprite(name, src) {
      if (this.textures.has(name)) return this.textures.get(name);
      const entry = { tex: this.white, ready: false };
      this.textures.set(name, entry);
      const image = new Image();
      image.onload = () => {
        entry.tex = this.makeTexture(image);
        entry.ready = true;
        if (this.onSprite) this.onSprite();
      };
      image.src = src;
      return entry;
    }

    resize() {
      const gl = this.gl;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const w = Math.max(1, Math.round(window.innerWidth * dpr));
      const h = Math.max(1, Math.round(window.innerHeight * dpr));
      this.dpr = dpr;
      this.cssW = window.innerWidth;
      this.cssH = window.innerHeight;
      // Sized in the same units as the page coordinates the shapes are placed with.
      this.canvas.style.width = this.cssW + 'px';
      this.canvas.style.height = this.cssH + 'px';
      if (this.canvas.width === w && this.canvas.height === h && this.target) return;
      this.canvas.width = w;
      this.canvas.height = h;
      if (this.target) {
        gl.deleteTexture(this.target.tex);
        gl.deleteFramebuffer(this.target.fbo);
      }
      // sRGB storage: blending runs in linear light, like the Linear colour space project.
      const tex = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, tex);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.SRGB8_ALPHA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      const fbo = gl.createFramebuffer();
      gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
      gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
      this.target = { tex, fbo, w, h };
    }

    begin() {
      const gl = this.gl;
      const t = this.target;
      gl.bindFramebuffer(gl.FRAMEBUFFER, t.fbo);
      gl.viewport(0, 0, t.w, t.h);
      gl.disable(gl.SCISSOR_TEST);
      gl.clearColor(0, 0, 0, 0);
      gl.clear(gl.COLOR_BUFFER_BIT);
      gl.enable(gl.BLEND);
      gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
      gl.useProgram(this.shape.p);
      gl.bindVertexArray(this.vao);
      gl.uniform2f(this.shape.u.uView, this.cssW, this.cssH);
      gl.activeTexture(gl.TEXTURE0);
      gl.uniform1i(this.shape.u.uTex, 0);
    }

    /** Clips what follows to a css-px rect (a stage), or to nothing. */
    clip(rect) {
      const gl = this.gl;
      if (!rect) { gl.disable(gl.SCISSOR_TEST); return; }
      const d = this.dpr;
      gl.enable(gl.SCISSOR_TEST);
      gl.scissor(Math.floor(rect.left * d), Math.floor((this.cssH - rect.bottom) * d),
        Math.ceil(rect.width * d), Math.ceil(rect.height * d));
    }

    /**
     * Draws one shape. rect: css px {left, top, width, height}. look: numbers in css px (already
     * scaled), colours as [r, g, b, a] in sRGB 0-1. tint multiplies fill, stroke, glow and shadow.
     */
    draw(rect, look, tint, kind, spriteName, time) {
      const gl = this.gl;
      const u = this.shape.u;
      const fade = tint[3] * tint[3];
      const glowColor = look.glow;
      const glowStrength = look.glowStrength * glowColor[3] * fade;
      const tailSize = kind === 0 && look.tail > 0 ? (look.tailSize || 0) : 0;
      const reach = Math.max(look.glowSize, Math.hypot(look.shadowOffset[0], look.shadowOffset[1]) + look.shadowSoftness)
        + tailSize + 2 / this.dpr + 1;
      const hx = rect.width / 2, hy = rect.height / 2;
      if (rect.left > this.cssW + reach || rect.left + rect.width < -reach ||
          rect.top > this.cssH + reach || rect.top + rect.height < -reach) return;

      const strokeBase = look.stroke;
      const highlight = look.highlight;
      const mix = (a, b, t) => a + (b - a) * t;
      const lin = (c, t) => [toLinear(c[0]) * t[0], toLinear(c[1]) * t[1], toLinear(c[2]) * t[2]];
      const tl = [toLinear(tint[0]), toLinear(tint[1]), toLinear(tint[2])];
      const fill = lin(look.fill, tl);
      const stroke = [
        mix(toLinear(strokeBase[0]), 1, highlight * 0.8) * tl[0],
        mix(toLinear(strokeBase[1]), 1, highlight * 0.8) * tl[1],
        mix(toLinear(strokeBase[2]), 1, highlight * 0.8) * tl[2]];
      const glow = lin(glowColor, tl);
      const shadow = lin(look.shadow, [1, 1, 1]);

      const breathe = 0.5 - 0.5 * Math.cos(time * look.pulseSpeed * 6.2831853);
      gl.uniform4f(u.uRect, rect.left + hx, rect.top + hy, hx, hy);
      gl.uniform1f(u.uReach, reach);
      gl.uniform4f(u.uFill, fill[0], fill[1], fill[2], look.fill[3] * tint[3]);
      gl.uniform4f(u.uStroke, stroke[0], stroke[1], stroke[2], strokeBase[3] * tint[3]);
      gl.uniform4f(u.uGlow, glow[0] * glowStrength, glow[1] * glowStrength, glow[2] * glowStrength, glowStrength);
      gl.uniform4f(u.uShadow, shadow[0], shadow[1], shadow[2], look.shadow[3] * fade);
      gl.uniform1f(u.uKind, kind);
      gl.uniform1f(u.uHatchSpacing, look.hatchSpacing);
      gl.uniform4f(u.uAnim, look.hatch, 1 + look.pulse * 2 * breathe, look.bottomLight * tint[3], -highlight);
      if (kind === 1) {
        const entry = this.textures.get(spriteName);
        gl.bindTexture(gl.TEXTURE_2D, entry ? entry.tex : this.white);
        gl.uniform4f(u.uIcon, look.shadowOffset[0], look.shadowOffset[1], look.glowSize, look.shadowSoftness);
        gl.uniform4f(u.uShape, 0, 0, 0, 0);
        gl.uniform4f(u.uExtra, 0, 0, 0, 1);
        gl.uniform4f(u.uCut, 0, 0, 0, 0);
      } else {
        gl.bindTexture(gl.TEXTURE_2D, this.white);
        gl.uniform4f(u.uShape, look.cornerRadius, look.strokeWidth, look.glowSize, look.shadowSoftness);
        gl.uniform4f(u.uExtra, look.shadowOffset[0], look.shadowOffset[1], look.innerGlow, look.gradientEnd);
        gl.uniform4f(u.uCut, tailSize > 0 ? tailSize : look.chamfer, tailSize > 0 ? 0 : look.chamferCorners,
          look.gradientStart + (tailSize > 0 ? look.tail * 4 : 0), look.bottomLightHeight);
        gl.uniform4f(u.uIcon, 0, 0, 0, 0);
      }
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    }

    /** A stage's solid backdrop, rounded; colour is sRGB hex-derived [r, g, b]. */
    backdrop(rect, rgb, radius) {
      this.draw(rect, {
        fill: [rgb[0], rgb[1], rgb[2], 1], stroke: [0, 0, 0, 0], glow: [0, 0, 0, 0], shadow: [0, 0, 0, 0],
        cornerRadius: radius, chamfer: 0, chamferCorners: 0, strokeWidth: 0, glowSize: 0, glowStrength: 0,
        innerGlow: 0, highlight: 0, bottomLight: 0, bottomLightHeight: 0, shadowOffset: [0, 0],
        shadowSoftness: 0, gradientStart: 0, gradientEnd: 1, hatch: 0, hatchSpacing: 9, pulse: 0, pulseSpeed: 0.6
      }, [1, 1, 1, 1], 0, null, 0);
    }

    end() {
      const gl = this.gl;
      gl.disable(gl.SCISSOR_TEST);
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.viewport(0, 0, this.canvas.width, this.canvas.height);
      gl.disable(gl.BLEND);
      gl.useProgram(this.blit.p);
      gl.activeTexture(gl.TEXTURE0);
      gl.bindTexture(gl.TEXTURE_2D, this.target.tex);
      gl.uniform1i(this.blit.u.uScene, 0);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    }
  }

  window.ShapeRenderer = Renderer;
})();
