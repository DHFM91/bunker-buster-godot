// Reference plans for MapPlan.cs, produced by the browser game's own MAP PLAN code.
// Usage: node map-plan-reference.mjs [path/to/bunker-buster.js] > map-plan-reference.json
import { readFileSync } from "node:fs";

const src = readFileSync(process.argv[2] || new URL("../../bunker-buster/bunker-buster.js", import.meta.url), "utf8");
const START = "// ===== MAP PLAN";
const END = "// ===== END MAP PLAN =====";
const a = src.indexOf(START), b = src.indexOf(END);
if (a < 0 || b < 0 || b < a) throw new Error("MAP PLAN markers not found");
const section = src.slice(a, b);

const columns = 7, rows = 10, room = 5, stride = 8, wallTop = 9;
const midCol = Math.floor(columns / 2);
const firstCell = midCol, lastCell = (rows - 1) * columns + midCol;
const { planLayout } = new Function("columns", "rows", "room", "stride", "wallTop", "midCol", "firstCell", "lastCell",
  section + "\nreturn {planLayout};")(columns, rows, room, stride, wallTop, midCol, firstCell, lastCell);

function mulberry32(a) {
  return function () {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Canonical shape. The truck shift is a multiple of 2^-32, so it is written as an exact integer (shiftBits).
function canon(p) {
  return {
    passages: p.passages.map(([x, y]) => [x, y]),
    spaces: p.spaces.map(s => {
      const o = { kind: s.kind, col: s.col, row: s.row, w: s.w, h: s.h };
      if (s.kind === "yard") { o.along = s.truck.along; o.shiftBits = Math.round(s.truck.shift * 4294967296); o.cover = s.cover; }
      return o;
    }),
    owner: Array.from(p.owner),
    barred: p.barred.map(([x, y]) => [x, y]),
    vents: p.vents.map(([x, y]) => [x, y]),
    reinforced: p.reinforced.map(([x, y]) => [x, y]),
    windows: p.windows.map(([x, y, z]) => [x, y, z]),
    posts: p.posts.map(q => ({ cell: q.cell, facing: q.facing })),
    props: p.props.map(q => {
      if (q.type === "lamp" || q.type === "sandbags") return { type: q.type, cell: q.cell, side: q.side };
      if (q.type === "crates") return { type: q.type, cell: q.cell, corner: q.corner };
      return { type: q.type, cell: q.cell, corner: q.corner, fuel: q.fuel };
    }),
    reach: p.reach,
  };
}

const cases = [];
for (const seed of [1, 12345, 777, 2026]) cases.push({ name: `seed ${seed} default`, seed, opts: {} });
const themed = [
  ["{yards:2}", { yards: 2 }],
  ["{yards:2, windows:.3}", { yards: 2, windows: .3 }],
  ["{windows:.2}", { windows: .2 }],
  ["{reinforced:.5, windows:.06, vents:5}", { reinforced: .5, windows: .06, vents: 5 }],
  ["{fuel:.8}", { fuel: .8 }],
  ["{vents:6, gates:.85, windows:.28}", { vents: 6, gates: .85, windows: .28 }],
];
for (const [label, opts] of themed) cases.push({ name: `seed 12345 ${label}`, seed: 12345, opts });

const out = cases.map(c => ({ name: c.name, seed: c.seed, opts: c.opts, plan: canon(planLayout(mulberry32(c.seed), c.opts)) }));
process.stdout.write(JSON.stringify(out, null, 1) + "\n");
