// Reference level stamping, produced by the browser game's own reset() code (sliced out of the game file).
// Usage: node level-reference.mjs [path/to/bunker-buster.js] > level-reference.json
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const src = readFileSync(process.argv[2] || join(here, "..", "..", "bunker-buster", "bunker-buster.js"), "utf8");
function between(start, end, from = 0) {
  const a = src.indexOf(start, from);
  if (a < 0) throw new Error("marker not found: " + start);
  const b = src.indexOf(end, a);
  if (b < 0) throw new Error("marker not found: " + end);
  return src.slice(a, b);
}
const cellCenterPart = between("function cellCenter(cell)", "const keys = new Set()");   // cellCenter, extraction, intel, intelX/Z
const gridPart = between("function inside(x, y, z)", "// ===== MAP PLAN");                    // inside, index, tile, box
const planPart = between("// ===== MAP PLAN", "// ===== END MAP PLAN =====");              // generateMaze, planLayout, planOnce
const stampPart = between("// ===== Stamping the plan into voxels", "function reset(");  // roomBox ... stampTruck
const h3Line = src.split("\n").find(l => l.startsWith("function h3("));
if (!h3Line) throw new Error("h3 not found");
// reset(), up to the voxel/lamp/sentry stamping; the palette roll and run counters are not part of the world.
let resetPart = between("function reset(random=Math.random) {", "  buildLampField();");
resetPart = resetPart.replace("  rollPalette();\n", "").replace("  run={time:0,blocks:0};\n", "");
if (/rollPalette|run=/.test(resetPart)) throw new Error("reset slicing failed");
resetPart += "  return sentries;\n}\n";

// Count writes that would fall outside the world: the game's box/swap do not clip.
let patchedGrid = gridPart.replace("for (let x=x0; x<x1; x++) world[index(x,y,z)] = material;",
  "for (let x=x0; x<x1; x++) { if (!inside(x,y,z)) oob++; world[index(x,y,z)] = material; }");
if (patchedGrid === gridPart) throw new Error("box patch failed");
let patchedStamp = stampPart.replace("if (world[index(x,y,z)]===from) world[index(x,y,z)]=to;",
  "{ if (!inside(x,y,z)) oob++; if (world[index(x,y,z)]===from) world[index(x,y,z)]=to; }");
if (patchedStamp === stampPart) throw new Error("swap patch failed");

const body = [
  "let oob=0;", cellCenterPart, patchedGrid, planPart, patchedStamp, h3Line, resetPart,
  "return {reset, lamps:()=>lamps, oob:()=>oob};",
].join("\n");
const factory = new Function("columns", "rows", "room", "stride", "wallTop", "midCol", "firstCell", "lastCell", "X", "Y", "Z", "world", "theme", body);

const columns = 7, rows = 10, room = 5, stride = 8, wallTop = 9;
const midCol = Math.floor(columns / 2), firstCell = midCol, lastCell = (rows - 1) * columns + midCol;
const X = (columns - 1) * stride + room + 2, Y = 12, Z = (rows - 1) * stride + room + 2;

function mulberry32(a) {
  return function () {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
function fnv1a(bytes) {
  let h = 0x811c9dc5;
  for (let i = 0; i < bytes.length; i++) { h ^= bytes[i]; h = Math.imul(h, 0x01000193); }
  return h >>> 0;
}
const r3 = v => Math.round(v * 1000) / 1000;
const r4 = v => Math.round(v * 10000) / 10000;

const themes = [
  ["relay", {}],
  ["desert", { yards: 2, windows: .3 }],
  ["overgrown", { vents: 6, gates: .85, windows: .28 }],
];
const out = [];
for (const [id, plan] of themes) for (const seed of [1, 12345, 777, 2026]) {
  const world = new Uint8Array(X * Y * Z);
  const game = factory(columns, rows, room, stride, wallTop, midCol, firstCell, lastCell, X, Y, Z, world, { plan });
  const sentries = game.reset(mulberry32(seed));
  if (game.oob() > 0) throw new Error(`${id} ${seed}: ${game.oob()} out-of-range writes`);
  const counts = new Array(16).fill(0);
  for (const v of world) counts[v]++;
  out.push({
    name: `${id} seed ${seed}`, theme: id, seed, opts: plan,
    hash: fnv1a(world),
    counts,
    lamps: game.lamps().map(l => [r3(l.x), r3(l.y), r3(l.z), r3(l.nx), r3(l.nz), r3(l.r), r3(l.power)]),
    sentries: sentries.map(s => [r3(s.x), r3(s.z), s.cell, r4(s.yaw)]),
  });
}
process.stdout.write(JSON.stringify(out, null, 1) + "\n");
