// Line-by-line JavaScript transliteration of scripts/World/VoxelWorld.cs (Box/Swap/Put/Tile/H3/Clear),
// scripts/World/LevelBuilder.cs and the case/serialiser parts of scripts/Tests/LevelSelfTest.cs.
// MapPlan and Mulberry32 come from the existing map-plan-port-check.mjs transliteration.
// Usage: node level-port-check.mjs > /tmp/out.json && cmp /tmp/out.json level-reference.json
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const mp = readFileSync(join(here, "map-plan-port-check.mjs"), "utf8");
const cut = mp.indexOf("// ---- MapPlanSelfTest.Canonical ----");
if (cut < 0) throw new Error("map-plan-port-check.mjs layout changed");
const { Mulberry32, PlanOptions, Plan, CellCenter } = new Function(
  mp.slice(0, cut) + "\nconst CellCenter = cell => ({ X: 1 + (cell % Columns) * Stride + Room / 2.0, Z: 1 + idiv(cell, Columns) * Stride + Room / 2.0 });\n" +
  "return { Mulberry32, PlanOptions, Plan, CellCenter };")();

const Columns = 7, Rows = 10, Room = 5, Stride = 8, WallTop = 9, MidCol = 3;
const FirstCell = MidCol, LastCell = (Rows - 1) * Columns + MidCol;
const idiv = (a, b) => Math.trunc(a / b);
const floor = Math.floor;

// ---- VoxelWorld ----
const SizeX = (Columns - 1) * Stride + Room + 2, SizeY = 12, SizeZ = (Rows - 1) * Stride + Room + 2;
class VoxelWorld {
  constructor() { this.Data = new Uint8Array(SizeX * SizeY * SizeZ); }
  static Inside(x, y, z) { return x >= 0 && y >= 0 && z >= 0 && x < SizeX && y < SizeY && z < SizeZ; }
  static Index(x, y, z) { return x + SizeX * (z + SizeZ * y); }
  Tile(x, y, z) { return VoxelWorld.Inside(x, y, z) ? this.Data[VoxelWorld.Index(x, y, z)] : 0; }
  Clear() { this.Data.fill(0); }
  Box(x0, y0, z0, x1, y1, z1, material) {
    x0 = Math.max(0, x0); y0 = Math.max(0, y0); z0 = Math.max(0, z0);
    x1 = Math.min(SizeX, x1); y1 = Math.min(SizeY, y1); z1 = Math.min(SizeZ, z1);
    for (let y = y0; y < y1; y++)
      for (let z = z0; z < z1; z++)
        for (let x = x0; x < x1; x++)
          this.Data[VoxelWorld.Index(x, y, z)] = material;
  }
  Swap(x0, y0, z0, x1, y1, z1, from, to) {
    for (let y = y0; y < y1; y++)
      for (let z = z0; z < z1; z++)
        for (let x = x0; x < x1; x++)
          if (VoxelWorld.Inside(x, y, z) && this.Data[VoxelWorld.Index(x, y, z)] === from) this.Data[VoxelWorld.Index(x, y, z)] = to;
  }
  Put(x, y0, z, y1, material) {
    if (VoxelWorld.Inside(x, y0, z) && this.Tile(x, y0, z) === 0) this.Box(x, y0, z, x + 1, y1, z + 1, material);
  }
  static H3(a, b, c) { return (Math.imul(a, 73856093) ^ Math.imul(b, 19349663) ^ Math.imul(c, 83492791)) >>> 0; }
}

// ---- LevelBuilder ----
const rect = (X0, Z0, X1, Z1) => ({ X0, Z0, X1, Z1 });
function RoomBox(cell) {
  const { X: x, Z: z } = CellCenter(cell);
  return rect(Math.trunc(x - Room / 2.0), Math.trunc(z - Room / 2.0), Math.trunc(x + Room / 2.0), Math.trunc(z + Room / 2.0));
}
function SpaceBox(s) {
  const a = RoomBox(s.Row * Columns + s.Col), b = RoomBox((s.Row + s.H - 1) * Columns + s.Col + s.W - 1);
  return rect(a.X0, a.Z0, b.X1, b.Z1);
}
function Slab(a, b) {
  const A = RoomBox(Math.min(a, b)), B = RoomBox(Math.max(a, b));
  return Math.abs(a - b) === 1
    ? { Box: rect(A.X1, A.Z0, B.X0, A.Z1), AxisX: true }
    : { Box: rect(A.X0, A.Z1, A.X1, B.Z0), AxisX: false };
}
function AddLamp(level, x, y, z, nx, nz, range = 7, power = 1) {
  level.Lamps.push({ X: x, Y: y, Z: z, Nx: nx, Nz: nz, Range: range, Power: power });
}
function RoomLamp(w, level, cell, side) {
  const b = RoomBox(cell);
  const cx = (b.X0 + b.X1) / 2.0, cz = (b.Z0 + b.Z1) / 2.0;
  const spots = [[cx, b.Z1 - .06, 0, -1], [b.X1 - .06, cz, -1, 0], [cx, b.Z0 + .06, 0, 1], [b.X0 + .06, cz, 1, 0]];
  const x = spots[side][0], z = spots[side][1], nx = spots[side][2], nz = spots[side][3];
  if (w.Tile(floor(x - nx * .5), 7, floor(z - nz * .5)) === 0) return;
  AddLamp(level, x, 6.7, z, nx, nz);
}
function StampTruck(w, level, b, along, shift) {
  const len = 6, wid = 3;
  const alongX = along === "x";
  const spanX = alongX ? len : wid, spanZ = alongX ? wid : len;
  const x0 = b.X0 + 2 + floor(shift * (b.X1 - b.X0 - 4 - spanX + 1));
  const z0 = b.Z0 + 2 + floor((1 - shift) * (b.Z1 - b.Z0 - 4 - spanZ + 1));
  for (let i = 0; i < len; i++)
    for (let j = 0; j < wid; j++) {
      const x = alongX ? x0 + i : x0 + j, z = alongX ? z0 + j : z0 + i;
      w.Box(x, 1, z, x + 1, 2, z + 1, 14);
      if (i < 2) { w.Box(x, 2, z, x + 1, 3, z + 1, 13); w.Box(x, 3, z, x + 1, 4, z + 1, 14); }
      else w.Box(x, 2, z, x + 1, 5, z + 1, 13);
    }
  const t = { X0: x0, Z0: z0, X1: x0 + spanX, Z1: z0 + spanZ, Along: along };
  level.Trucks.push(t);
  return t;
}
function Build(w, theme, random) {
  const level = { Theme: theme, Lamps: [], Sentries: [], Yards: [], Trucks: [], Foyer: null };
  const X = SizeX, Z = SizeZ;
  w.Clear();
  w.Box(0, 0, 0, X, 1, Z, 1);
  w.Box(0, 1, 0, 1, WallTop, Z, 3);
  w.Box(X - 1, 1, 0, X, WallTop, Z, 3);
  w.Box(0, 1, 0, X, WallTop, 1, 3);
  w.Box(0, 1, Z - 1, X, WallTop, Z, 3);
  w.Box(1, 1, 1, X - 1, WallTop, Z - 1, 2);
  for (let cell = 0; cell < Columns * Rows; cell++) {
    const r = RoomBox(cell);
    w.Box(r.X0, 1, r.Z0, r.X1, WallTop, r.Z1, 0);
  }
  const plan = Plan(random, theme.Plan);
  level.Plan = plan;
  for (const [pa, pb] of plan.Passages) {
    const a = RoomBox(pa), b = RoomBox(pb);
    w.Box(Math.min(a.X0, b.X0), 1, Math.min(a.Z0, b.Z0), Math.max(a.X1, b.X1), WallTop, Math.max(a.Z1, b.Z1), 0);
  }
  for (const s of plan.Spaces) {
    const b = SpaceBox(s);
    w.Box(b.X0, 1, b.Z0, b.X1, WallTop, b.Z1, 0);
    if (s.Kind === "foyer") level.Foyer = b;
    if (s.Kind === "atrium") {
      for (let i = 0; i < s.W - 1; i++)
        for (let j = 0; j < s.H - 1; j++) {
          const r = RoomBox((s.Row + j) * Columns + s.Col + i);
          w.Box(r.X1, 1, r.Z1, r.X1 + 3, WallTop, r.Z1 + 3, 10);
        }
    }
    if (s.Kind === "yard") { w.Box(b.X0, 0, b.Z0, b.X1, 1, b.Z1, 12); level.Yards.push(b); }
  }
  for (const [a, b] of plan.Reinforced) {
    const s = Slab(a, b).Box;
    w.Swap(s.X0, 1, s.Z0, s.X1, WallTop, s.Z1, 2, 10);
  }
  for (const [a, b] of plan.Barred) {
    const { Box: s, AxisX: axisX } = Slab(a, b);
    if (axisX) { w.Box(s.X0 + 1, 1, s.Z0, s.X0 + 2, 5, s.Z1, 11); w.Box(s.X0 + 1, 5, s.Z0, s.X0 + 2, WallTop, s.Z1, 2); }
    else { w.Box(s.X0, 1, s.Z0 + 1, s.X1, 5, s.Z0 + 2, 11); w.Box(s.X0, 5, s.Z0 + 1, s.X1, WallTop, s.Z0 + 2, 2); }
  }
  for (const [a, b, bars] of plan.Windows) {
    const { Box: s, AxisX: axisX } = Slab(a, b);
    if (axisX) { w.Box(s.X0, 3, s.Z0 + 1, s.X1, 5, s.Z1 - 1, 0); if (bars) w.Box(s.X0 + 1, 3, s.Z0 + 1, s.X0 + 2, 5, s.Z1 - 1, 11); }
    else { w.Box(s.X0 + 1, 3, s.Z0, s.X1 - 1, 5, s.Z1, 0); if (bars) w.Box(s.X0 + 1, 3, s.Z0 + 1, s.X1 - 1, 5, s.Z0 + 2, 11); }
  }
  for (const [a, b] of plan.Vents) {
    const { Box: s, AxisX: axisX } = Slab(a, b);
    if (axisX) w.Box(s.X0, 1, s.Z0 + 2, s.X1, 3, s.Z0 + 4, 0);
    else w.Box(s.X0 + 2, 1, s.Z0, s.X0 + 4, 3, s.Z1, 0);
  }

  const extraction = CellCenter(FirstCell);
  const intel = CellCenter(LastCell);
  const padX = floor(extraction.X), padZ = floor(extraction.Z);
  w.Box(padX - 2, 0, padZ - 2, padX + 3, 1, padZ + 3, 4);
  const intelX = floor(intel.X), intelZ = floor(intel.Z);
  w.Box(intelX, 1, intelZ, intelX + 1, 2, intelZ + 1, 6);

  if (level.Foyer !== null) {
    const f = level.Foyer;
    const cx = idiv(f.X0 + f.X1, 2);
    for (const dir of [-1, 1]) {
      const nx = dir < 0 ? f.X0 + 3 : f.X1 - 4;
      for (let k = 0; k < 4; k++) w.Put(nx - dir * k, 1, f.Z0 + 7, 3, 8);
      for (let k = 1; k < 3; k++) w.Put(nx, 1, f.Z0 + 7 + k, 3, 8);
      const wallX = dir < 0 ? f.X0 : f.X1 - 1;
      w.Put(wallX, 1, f.Z0, 3, 6); w.Put(wallX - dir, 1, f.Z0, 2, 6); w.Put(wallX, 1, f.Z0 + 1, 2, 6);
      w.Put(wallX, 1, f.Z0 + 5, 3, 9); w.Put(wallX, 1, f.Z0 + 6, 3, 9);
      AddLamp(level, cx + dir * 6.5, 7.2, f.Z0 + .06, 0, 1, 15, 1.5);
    }
  }
  for (const s of plan.Spaces) {
    if (s.Kind !== "yard") continue;
    const b = SpaceBox(s);
    const t = StampTruck(w, level, b, s.Along, s.TruckShift);
    for (let n = 0; n < s.Cover; n++) {
      const h = VoxelWorld.H3(b.X0 + n, b.Z0, n + 7);
      const x = b.X0 + 1 + (h % (b.X1 - b.X0 - 4));
      const z = b.Z0 + 1 + ((h | 0) >> 8) % (b.Z1 - b.Z0 - 3);
      if (x + 3 > t.X0 - 1 && x < t.X1 + 1 && z > t.Z0 - 2 && z < t.Z1 + 1) continue;
      for (let k = 0; k < 3; k++) w.Put(x + k, 1, z, ((h >>> 4) & 1) !== 0 ? 3 : 2, 8);
      w.Put(x, 1, z + 1, 3, 9);
    }
    AddLamp(level, (b.X0 + b.X1) / 2.0, 7.4, b.Z0 + .06, 0, 1, 10, 1.1);
  }
  for (const p of plan.Props) {
    const b = RoomBox(p.Cell);
    if (p.Type === "lamp") { RoomLamp(w, level, p.Cell, p.Side); continue; }
    if (p.Type === "sandbags") {
      const cx = idiv(b.X0 + b.X1, 2), cz = idiv(b.Z0 + b.Z1, 2);
      const spots = [[cx, b.Z1 - 1, 1, 0], [b.X1 - 1, cz, 0, 1], [cx, b.Z0, 1, 0], [b.X0, cz, 0, 1]];
      const ex = spots[p.Side][0], ez = spots[p.Side][1], ox = spots[p.Side][2], oz = spots[p.Side][3];
      const wx = ex + (oz !== 0 ? (p.Side === 1 ? 1 : -1) : 0), wz = ez + (ox !== 0 ? (p.Side === 0 ? 1 : -1) : 0);
      if (w.Tile(wx, 2, wz) === 0) continue;
      for (let k = -1; k <= 1; k++) w.Put(ex + ox * k, 1, ez + oz * k, 3, 8);
      continue;
    }
    const px = (p.Corner & 1) !== 0 ? b.X1 - 1 : b.X0, pz = (p.Corner & 2) !== 0 ? b.Z1 - 1 : b.Z0, inx = (p.Corner & 1) !== 0 ? -1 : 1;
    if (p.Type === "crates") { w.Put(px, 1, pz, (VoxelWorld.H3(px, pz, 1) & 1) !== 0 ? 3 : 2, 6); w.Put(px + inx, 1, pz, 2, 6); }
    else { const m = p.Fuel ? 15 : 9; w.Put(px, 1, pz, 3, m); w.Put(px + inx, 1, pz, 3, m); }
  }
  const facings = [[0, 1], [1, 0], [0, -1], [-1, 0]];
  for (const [cell, facing] of plan.Posts) {
    const b = RoomBox(cell);
    const cx = b.X0 + 2, cz = b.Z0 + 2, fx = facings[facing][0], fz = facings[facing][1];
    for (let dz = -1; dz <= 1; dz++)
      for (let dx = -1; dx <= 1; dx++) {
        if ((dx === 0 && dz === 0) || (dx === fx && fx !== 0) || (dz === fz && fz !== 0)) continue;
        w.Put(cx + dx, 1, cz + dz, 3, 8);
      }
    level.Sentries.push({ X: cx + .5, Z: cz + .5, Cell: cell, Yaw: Math.atan2(fx, fz) });
  }
  return level;
}

// ---- Themes (the three used here; PlanOptions values match Theme.All) ----
const Themes = [
  { Id: "relay", Plan: new PlanOptions() },
  { Id: "desert", Plan: new PlanOptions({ Yards: 2, Windows: 0.3 }) },
  { Id: "overgrown", Plan: new PlanOptions({ Vents: 6, Gates: 0.85, Windows: 0.28 }) },
];

// ---- LevelSelfTest serialiser ----
function Fnv1a(bytes) {
  let h = 0x811c9dc5;
  for (let i = 0; i < bytes.length; i++) { h ^= bytes[i]; h = Math.imul(h, 0x01000193); }
  return h >>> 0;
}
const r3 = v => Math.round(v * 1000) / 1000;
const r4 = v => Math.round(v * 10000) / 10000;
const out = [];
for (const theme of Themes) for (const seed of [1, 12345, 777, 2026]) {
  const w = new VoxelWorld();
  const level = Build(w, theme, new Mulberry32(seed).AsFunc());
  const counts = new Array(16).fill(0);
  for (const v of w.Data) counts[v]++;
  const opts = {};
  if (theme.Id === "desert") Object.assign(opts, { yards: 2, windows: .3 });
  if (theme.Id === "overgrown") Object.assign(opts, { vents: 6, gates: .85, windows: .28 });
  out.push({
    name: `${theme.Id} seed ${seed}`, theme: theme.Id, seed, opts,
    hash: Fnv1a(w.Data),
    counts,
    lamps: level.Lamps.map(l => [r3(l.X), r3(l.Y), r3(l.Z), r3(l.Nx), r3(l.Nz), r3(l.Range), r3(l.Power)]),
    sentries: level.Sentries.map(s => [r3(s.X), r3(s.Z), s.Cell, r4(s.Yaw)]),
  });
}
process.stdout.write(JSON.stringify(out, null, 1) + "\n");
