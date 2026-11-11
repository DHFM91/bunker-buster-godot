// Bunker Buster asset exporter.
// Rebuilds the browser game's procedural art and synthesized sounds as files for the Godot port.
// Usage (any browser page): const files = await exportBunkerBusterAssets(gameSourceText);
//   -> { "textures/concrete.png": Blob, "sprites/guard_sheet.png": Blob, "audio/rifle_shot.wav": Blob, ... }
// gameSourceText is the contents of bunker-buster.js; the exporter evaluates only the pure
// drawing functions from it, so the exported art always matches the game exactly.

async function exportBunkerBusterAssets(src) {
  const slice = (from, to) => {
    const a = src.indexOf(from), b = src.indexOf(to, a + 1);
    if (a < 0 || b < 0) throw new Error("exporter: marker not found: " + from);
    return src.slice(a, b);
  };
  const code =
    slice("let colors = [", "function rollPalette") +
    "const dustColor=[0,0,0],rubbleColor=[0,0,0],groundFog=[0,0,0],textureSize=64;\n" +
    slice("// ===== TEXTURES", "// ===== END TEXTURES") +
    slice("const G={", "// ===== Light:");
  const game = new Function(code + "\nreturn {colors, palette, buildTextures, guardPixel, corpsePixel, intelPixel};")();
  const files = {};

  const canvasBlob = c => (c.convertToBlob ? c.convertToBlob({ type: "image/png" })
    : new Promise(r => c.toBlob(r, "image/png")));
  const makeCanvas = (w, h) => (typeof OffscreenCanvas !== "undefined" ? new OffscreenCanvas(w, h)
    : Object.assign(document.createElement("canvas"), { width: w, height: h }));

  // ---- textures: 64x64 tiles straight from the game's generator (default colour scheme) ----
  const textures = game.buildTextures();
  const texNames = { 1: "floor", 2: "concrete", 3: "outer_wall", 4: "extraction_pad", 6: "crate", 7: "rubble", 8: "sandbag", 9: "drum", 10: "reinforced", 11: "bars", 12: "yard", 13: "truck", 14: "truck_dark" };
  for (const [id, name] of Object.entries(texNames)) {
    const c = makeCanvas(64, 64), ctx = c.getContext("2d"), img = ctx.createImageData(64, 64);
    const t = textures[id];
    for (let i = 0; i < 64 * 64; i++) {
      img.data[i * 4] = t[i * 3]; img.data[i * 4 + 1] = t[i * 3 + 1]; img.data[i * 4 + 2] = t[i * 3 + 2]; img.data[i * 4 + 3] = 255;
    }
    ctx.putImageData(img, 0, 0);
    files[`textures/${name}.png`] = await canvasBlob(c);
  }

  // ---- sprite rasterizer: same outline + shading rules the game's billboard renderer uses ----
  function raster(w, h, colorAt, outline = true, shadeIt = true) {
    const c = makeCanvas(w, h), ctx = c.getContext("2d"), img = ctx.createImageData(w, h);
    const du = 1 / w, dv = 1 / h;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const u = (x + .5) / w, v = (y + .5) / h, col = colorAt(u, v);
      if (!col) continue;
      let k = 1;
      if (outline && (!colorAt(u - du, v) || !colorAt(u + du, v) || !colorAt(u, v - dv) || !colorAt(u, v + dv))) k = .48;
      const s = shadeIt ? k * (1.12 - Math.abs(u - .42) * .5 - v * .12) : k;
      const j = (y * w + x) * 4;
      img.data[j] = Math.min(255, col[0] * s); img.data[j + 1] = Math.min(255, col[1] * s);
      img.data[j + 2] = Math.min(255, col[2] * s); img.data[j + 3] = 255;
    }
    ctx.putImageData(img, 0, 0);
    return c;
  }
  async function sheet(cellW, cellH, rows, name) {
    const cols = Math.max(...rows.map(r => r.length));
    const c = makeCanvas(cellW * cols, cellH * rows.length), ctx = c.getContext("2d");
    rows.forEach((row, r) => row.forEach((fn, i) => ctx.drawImage(raster(cellW, cellH, fn), i * cellW, r * cellH)));
    files[name] = await canvasBlob(c);
  }

  // Guard sheet: rows = front, back, side (facing right; flip for left).
  // Columns = walk frames (step 0, +.6, 0, -.6), aiming, firing.
  const W = 48, H = 89;
  const frames = view => [0, .6, 0, -.6].map(step => (u, v) => game.guardPixel(view, u, v, false, step, 0))
    .concat([(u, v) => game.guardPixel(view, u, v, false, 0, 1), (u, v) => game.guardPixel(view, u, v, true, 0, 0)]);
  await sheet(W, H, [frames("front"), frames("back"), frames("right")], "sprites/guard_sheet.png");
  files["sprites/guard_downed.png"] = await canvasBlob(raster(104, 23, (u, v) => game.corpsePixel(u, v)));
  files["sprites/intel_case.png"] = await canvasBlob(raster(40, 30, (u, v) => game.intelPixel(u, v, 1)));

  // Breach charge (matches the in-game sprite)
  files["sprites/breach_charge.png"] = await canvasBlob(raster(32, 24, (u, v) => {
    if (u > .55 && u < .86 && v > .18 && v < .56) {
      if (u > .62 && u < .74 && v > .26 && v < .4) return [255, 70, 40];
      return v < .22 ? [80, 86, 82] : [30, 34, 32];
    }
    if (v < .12) return Math.abs(u - .3) < .03 || Math.abs(u - .4) < .03 ? (u < .35 ? [200, 50, 40] : [60, 90, 190]) : null;
    if (u < .08 || u > .92) return null;
    if (Math.abs(u - .2) < .05 || Math.abs(u - .45) < .05) return [82, 90, 62];
    return v > .7 ? [176, 160, 120] : [214, 198, 152];
  }));

  // Alert icons ("!" spotted, "?" heard something)
  files["sprites/icon_spotted.png"] = await canvasBlob(raster(10, 22, (u, v) =>
    Math.abs(u - .5) < .2 && (v < .62 || v > .76) ? [255, 214, 64] : null, true, false));
  files["sprites/icon_heard.png"] = await canvasBlob(raster(15, 22, (u, v) =>
    ["111", "001", "011", "000", "010"][Math.min(4, (v * 5) | 0)][Math.min(2, (u * 3) | 0)] === "1" ? [255, 214, 64] : null, true, false));

  // Objective arrow (same shape as the HUD arrow), 64x64 with transparency
  {
    const c = makeCanvas(64, 64), ctx = c.getContext("2d");
    ctx.scale(64 / 24, 64 / 24);
    ctx.beginPath(); ctx.moveTo(12, 1.5); ctx.lineTo(22, 12.5); ctx.lineTo(15.8, 12.5); ctx.lineTo(15.8, 22.5);
    ctx.lineTo(8.2, 22.5); ctx.lineTo(8.2, 12.5); ctx.lineTo(2, 12.5); ctx.closePath();
    ctx.fillStyle = "#f2b76b"; ctx.fill(); ctx.lineJoin = "round"; ctx.lineWidth = 1.6; ctx.strokeStyle = "#0b1414"; ctx.stroke();
    files["sprites/objective_arrow.png"] = await canvasBlob(c);
  }

  // ---- audio: the game's synthesized tones, rendered offline to 16-bit mono WAV ----
  // Each tone: [frequency, duration, volume, wave, endFrequency, delay] (same as the game's tone()).
  const SOUNDS = {
    rifle_shot: [[190, .14, .13, "sawtooth", 33]],
    guard_shot: [[118, .09, .045, "square", 38]],
    reload: [[180, .13, .045, "square", 55]],
    charge_plant: [[840, .1, .09, "square", 490]],
    fuse_beep: [[780, .07, .055, "square", 630]],
    explosion: [[95, .45, .25, "sawtooth", 24]],
    alert_spotted: [[620, .08, .06, "square", 620], [930, .13, .06, "square", 930, .09]],
    rifle_raise: [[1500, .025, .05, "square", 1300], [980, .04, .05, "square", 760, .11]],
    alarm: [0, 1, 2].flatMap(k => [[880, .16, .06, "square", 880, k * .36], [660, .16, .06, "square", 660, k * .36 + .18]]),
    noise_heard: [[440, .09, .03, "square", 520, .25]],
    intel_pickup: [[500, .24, .1, "triangle", 780]],
    player_hit: [[220, .12, .06, "sawtooth", 90]],
    collapse_rumble: [[70, .6, .09, "sawtooth", 38], [140, .25, .03, "square", 60, .1]],
    debris_thud: [[110, .12, .06, "triangle", 40]],
  };
  const RATE = 22050;
  for (const [name, tones] of Object.entries(SOUNDS)) {
    const length = Math.ceil((Math.max(...tones.map(t => (t[5] || 0) + t[1])) + .02) * RATE);
    const ctx = new OfflineAudioContext(1, length, RATE);
    for (const [f, d, vol, type, end = 55, delay = 0] of tones) {
      const o = ctx.createOscillator(), g = ctx.createGain();
      o.type = type; o.frequency.setValueAtTime(f, delay); o.frequency.exponentialRampToValueAtTime(end, delay + d);
      g.gain.setValueAtTime(vol, delay); g.gain.exponentialRampToValueAtTime(.001, delay + d);
      o.connect(g).connect(ctx.destination); o.start(delay); o.stop(delay + d);
    }
    const data = (await ctx.startRendering()).getChannelData(0);
    let peak = 0; for (const s of data) peak = Math.max(peak, Math.abs(s));
    const gain = peak > 0 ? .8 / peak : 1;              // normalise: game volumes are tiny by design
    const buf = new ArrayBuffer(44 + data.length * 2), dv = new DataView(buf);
    const str = (o, s) => [...s].forEach((ch, i) => dv.setUint8(o + i, ch.charCodeAt(0)));
    str(0, "RIFF"); dv.setUint32(4, 36 + data.length * 2, true); str(8, "WAVEfmt ");
    dv.setUint32(16, 16, true); dv.setUint16(20, 1, true); dv.setUint16(22, 1, true);
    dv.setUint32(24, RATE, true); dv.setUint32(28, RATE * 2, true); dv.setUint16(32, 2, true); dv.setUint16(34, 16, true);
    str(36, "data"); dv.setUint32(40, data.length * 2, true);
    for (let i = 0; i < data.length; i++) dv.setInt16(44 + i * 2, Math.max(-1, Math.min(1, data[i] * gain)) * 32767, true);
    files[`audio/${name}.wav`] = new Blob([buf], { type: "audio/wav" });
  }
  return files;
}
if (typeof module !== "undefined") module.exports = { exportBunkerBusterAssets };
