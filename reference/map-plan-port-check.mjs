// Line-by-line JavaScript transliteration of scripts/World/Mulberry32.cs, MapPlan.cs and the
// case/serialiser parts of MapPlanSelfTest.cs. Output must be byte-identical to map-plan-reference.json.
// Usage: node map-plan-port-check.mjs > /tmp/out.json && cmp /tmp/out.json map-plan-reference.json

const Columns = 7, Rows = 10, Room = 5, Stride = 8, WallTop = 9;
const MidCol = Math.trunc(Columns / 2);
const FirstCell = MidCol;
const LastCell = (Rows - 1) * Columns + MidCol;
const MinReach = 21;
const Total = Columns * Rows;
const floor = Math.floor;
const idiv = (a, b) => Math.trunc(a / b);   // C# int division (all operands here are non-negative)

class Mulberry32 {
  constructor(seed) { this._state = seed >>> 0; }
  Next() {
    this._state = (this._state + 0x6D2B79F5) >>> 0;
    let t = this._state;
    t = Math.imul((t ^ (t >>> 15)) >>> 0, (t | 1) >>> 0) >>> 0;
    t = (t ^ ((t + Math.imul((t ^ (t >>> 7)) >>> 0, (t | 61) >>> 0)) >>> 0)) >>> 0;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296.0;
  }
  AsFunc() { return () => this.Next(); }
}

class PlanOptions {
  constructor(init = {}) {
    this.Yards = 0; this.Reinforced = 0.22; this.Windows = 0.14; this.Vents = 3; this.Gates = 0.5; this.Fuel = 0.35;
    Object.assign(this, init);
  }
}
class Space { constructor(init) { this.Kind = ""; this.Col = 0; this.Row = 0; this.W = 0; this.H = 0; this.Along = null; this.TruckShift = 0; this.Cover = 0; Object.assign(this, init); } }
class Prop { constructor(init) { this.Type = ""; this.Cell = 0; this.Side = -1; this.Corner = -1; this.Fuel = false; Object.assign(this, init); } }

const PairKey = (a, b) => a < b ? a * 1000 + b : b * 1000 + a;
const CellOf = (col, row) => row * Columns + col;

function GenerateMaze(random) {
  for (let attempt = 0; attempt < 96; attempt++) {
    const distance = new Array(Total).fill(-1);
    const route = [FirstCell];
    const passages = [];
    distance[FirstCell] = 0;
    let centralOpen = 0;
    while (route.length > 0) {
      const cell = route[route.length - 1];
      const col = cell % Columns;
      const row = idiv(cell, Columns);
      const options = [];
      if (col > 0 && distance[cell - 1] < 0) options.push(cell - 1);
      if (col < Columns - 1 && distance[cell + 1] < 0) options.push(cell + 1);
      if (row > 0 && distance[cell - Columns] < 0) options.push(cell - Columns);
      if (row < Rows - 1 && distance[cell + Columns] < 0) options.push(cell + Columns);
      if (options.length === 0) { route.pop(); continue; }
      const next = options[floor(random() * options.length)];
      passages.push([cell, next]);
      distance[next] = distance[cell] + 1;
      if (col === MidCol && next % Columns === MidCol) centralOpen++;
      route.push(next);
    }
    if (distance[LastCell] >= 34 && centralOpen <= 4) return passages;
  }
  const snake = new Array(Total).fill(0);
  for (let i = 0; i < Total; i++) {
    const snakeRow = idiv(i, Columns);
    const snakeCol = i % Columns;
    snake[i] = snakeRow * Columns + (snakeRow % 2 !== 0 ? Columns - 1 - snakeCol : snakeCol);
  }
  const fallback = [];
  for (let i = 1; i < Total; i++) fallback.push([snake[i - 1], snake[i]]);
  return fallback;
}

function Plan(random, opts) {
  const o = opts ?? new PlanOptions();
  let best = null;
  for (let attempt = 0; attempt < 20; attempt++) {
    const plan = PlanOnce(random, o);
    if (plan.Reach >= MinReach) return plan;
    if (best === null || plan.Reach > best.Reach) best = plan;
  }
  return best;
}

function PlanOnce(random, o) {
  const passages = GenerateMaze(random);
  const treeCount = passages.length;

  const extraOpenings = floor(Total * 0.08 + 0.5);
  for (let k = 0; k < extraOpenings; k++) {
    const loopCell = floor(random() * Total);
    const loopRight = random() < 0.5;
    const loopCol = loopCell % Columns;
    const loopRow = idiv(loopCell, Columns);
    if (loopRight && loopCol < Columns - 1) passages.push([loopCell, loopCell + 1]);
    else if (!loopRight && loopRow < Rows - 1) passages.push([loopCell, loopCell + Columns]);
  }

  const treeKeys = new Set();
  for (const passage of passages) treeKeys.add(PairKey(passage[0], passage[1]));
  const extras = [];
  for (let i = treeCount; i < passages.length; i++) {
    if (!treeKeys.has(PairKey(passages[i][0], passages[i][1]))) extras.push(passages[i]);
  }

  const owner = new Array(Total).fill(-1);
  const spaces = [];

  function IsFree(fc, fr, fw, fh) {
    if (fc < 0 || fr < 0 || fc + fw > Columns || fr + fh > Rows) return false;
    for (let yy = fr; yy < fr + fh; yy++) {
      for (let xx = fc; xx < fc + fw; xx++) {
        if (owner[CellOf(xx, yy)] >= 0 || CellOf(xx, yy) === LastCell) return false;
      }
    }
    return true;
  }
  function Claim(cc, cr, cw, ch, kind) {
    for (let yy = cr; yy < cr + ch; yy++) {
      for (let xx = cc; xx < cc + cw; xx++) owner[CellOf(xx, yy)] = spaces.length;
    }
    spaces.push(new Space({ Kind: kind, Col: cc, Row: cr, W: cw, H: ch }));
  }

  Claim(MidCol - 1, 0, 3, 2, "foyer");
  const leftRow = random() < 0.5 ? 0 : 1;
  const rightRow = random() < 0.5 ? 0 : 1;
  passages.push([CellOf(MidCol - 1, 1), CellOf(MidCol - 1, 2)]);
  passages.push([CellOf(MidCol, 1), CellOf(MidCol, 2)]);
  passages.push([CellOf(MidCol + 1, 1), CellOf(MidCol + 1, 2)]);
  passages.push([CellOf(MidCol - 2, leftRow), CellOf(MidCol - 1, leftRow)]);
  passages.push([CellOf(MidCol + 1, rightRow), CellOf(MidCol + 2, rightRow)]);

  if (random() < 0.6 && IsFree(MidCol - 1, 4, 3, 2)) Claim(MidCol - 1, 4, 3, 2, "atrium");

  const yardCount = o.Yards !== 0 ? o.Yards : 1 + (random() < 0.5 ? 1 : 0);
  const yardSizes = [[2, 2], [3, 2], [2, 3]];
  for (let yardIndex = 0; yardIndex < yardCount; yardIndex++) {
    for (let tryIndex = 0; tryIndex < 12; tryIndex++) {
      const size = yardSizes[floor(random() * 3)];
      const yardW = size[0];
      const yardH = size[1];
      const yardC = floor(random() * (Columns - yardW + 1));
      const yardR = 3 + floor(random() * (Rows - 4 - yardH));
      if (IsFree(yardC, yardR, yardW, yardH)) {
        Claim(yardC, yardR, yardW, yardH, "yard");
        break;
      }
    }
  }

  if (random() < 0.5) {
    for (let hallTry = 0; hallTry < 10; hallTry++) {
      const hallR = 3 + floor(random() * 5);
      const hallC = floor(random() * (Columns - 3));
      if (IsFree(hallC, hallR, 4, 1)) {
        Claim(hallC, hallR, 4, 1, "hall");
        break;
      }
    }
  }

  const linked = new Set();
  for (const passage of passages) linked.add(PairKey(passage[0], passage[1]));
  const SameSpace = (sa, sb) => owner[sa] >= 0 && owner[sa] === owner[sb];
  const IsOpen = (oa, ob) => linked.has(PairKey(oa, ob)) || SameSpace(oa, ob);

  const pairs = [];
  for (let pairCell = 0; pairCell < Total; pairCell++) {
    const pairCol = pairCell % Columns;
    const pairRow = idiv(pairCell, Columns);
    if (pairCol < Columns - 1) pairs.push([pairCell, pairCell + 1]);
    if (pairRow < Rows - 1) pairs.push([pairCell, pairCell + Columns]);
  }

  const barred = [];
  for (const extra of extras) {
    if (!SameSpace(extra[0], extra[1]) && owner[extra[0]] < 0 && owner[extra[1]] < 0 && random() < o.Gates) {
      barred.push([extra[0], extra[1]]);
    }
  }

  const vents = [];
  const ventKeys = new Set();
  for (let ventTry = 0; ventTry < 40 && vents.length < o.Vents; ventTry++) {
    const ventPair = pairs[floor(random() * pairs.length)];
    if (IsOpen(ventPair[0], ventPair[1]) || ventKeys.has(PairKey(ventPair[0], ventPair[1]))
      || owner[ventPair[0]] === 0 || owner[ventPair[1]] === 0) continue;
    vents.push([ventPair[0], ventPair[1]]);
    ventKeys.add(PairKey(ventPair[0], ventPair[1]));
  }

  const reinforced = [];
  const windows = [];
  for (const wallPair of pairs) {
    if (IsOpen(wallPair[0], wallPair[1]) || ventKeys.has(PairKey(wallPair[0], wallPair[1]))) continue;
    const reinforceRoll = random();
    const windowRoll = random();
    const windowBars = random() < 0.5;
    if (wallPair[0] === LastCell || wallPair[1] === LastCell || reinforceRoll < o.Reinforced) {
      reinforced.push([wallPair[0], wallPair[1]]);
    }
    if (windowRoll < o.Windows && owner[wallPair[0]] !== 0 && owner[wallPair[1]] !== 0) {
      windows.push([wallPair[0], wallPair[1], windowBars]);
    }
  }

  const posts = [[CellOf(MidCol, 2), 0]];
  for (let postTry = 0; postTry < 30 && posts.length < 3; postTry++) {
    const postCell = floor(random() * Total);
    const postFacing = floor(random() * 4);
    if (idiv(postCell, Columns) < 3 || owner[postCell] >= 0 || postCell === LastCell
      || posts.some(p => p[0] === postCell)) continue;
    posts.push([postCell, postFacing]);
  }

  const props = [];
  for (let propCell = 0; propCell < Total; propCell++) {
    const lamp = random();
    const side = floor(random() * 4);
    const clutter = random();
    const corner = floor(random() * 4);
    const fuel = random();
    if (owner[propCell] === 0 || posts.some(p => p[0] === propCell)) continue;
    if (lamp < 0.45) props.push(new Prop({ Type: "lamp", Cell: propCell, Side: side }));
    if (propCell === LastCell) continue;
    if (clutter < 0.3) props.push(new Prop({ Type: "crates", Cell: propCell, Corner: corner }));
    else if (clutter < 0.5) props.push(new Prop({ Type: "barrels", Cell: propCell, Corner: corner, Fuel: fuel < o.Fuel }));
    else if (clutter < 0.6) props.push(new Prop({ Type: "sandbags", Cell: propCell, Side: side }));
  }

  for (const yardSpace of spaces) {
    if (yardSpace.Kind !== "yard") continue;
    yardSpace.Along = random() < 0.5 ? "x" : "z";
    yardSpace.TruckShift = random();
    yardSpace.Cover = 1 + floor(random() * 3);
  }

  const shut = new Set();
  for (const gate of barred) shut.add(PairKey(gate[0], gate[1]));
  const dist = new Array(Total).fill(-1);
  const queue = [FirstCell];
  dist[FirstCell] = 0;
  for (let q = 0; q < queue.length; q++) {
    const current = queue[q];
    const currentCol = current % Columns;
    const currentRow = idiv(current, Columns);
    const neighbours = [
      currentCol > 0 ? current - 1 : -1,
      currentCol < Columns - 1 ? current + 1 : -1,
      currentRow > 0 ? current - Columns : -1,
      currentRow < Rows - 1 ? current + Columns : -1,
    ];
    for (const neighbour of neighbours) {
      if (neighbour < 0 || dist[neighbour] >= 0) continue;
      const key = PairKey(current, neighbour);
      if (!((IsOpen(current, neighbour) && !shut.has(key)) || ventKeys.has(key))) continue;
      dist[neighbour] = dist[current] + 1;
      queue.push(neighbour);
    }
  }

  return { Passages: passages, Spaces: spaces, Owner: owner, Barred: barred, Vents: vents, Reinforced: reinforced,
    Windows: windows, Posts: posts, Props: props, Reach: dist[LastCell] };
}

// ---- MapPlanSelfTest.Canonical ----
const Num = v => String(v);
const Bool = v => v ? "true" : "false";
function AppendPairs(list) {
  let sb = "[";
  for (let i = 0; i < list.length; i++) {
    if (i > 0) sb += ",";
    sb += "[" + Num(list[i][0]) + "," + Num(list[i][1]) + "]";
  }
  return sb + "]";
}
function Canonical(plan) {
  let sb = "";
  sb += "{\"passages\":" + AppendPairs(plan.Passages);
  sb += ",\"spaces\":[";
  for (let i = 0; i < plan.Spaces.length; i++) {
    const s = plan.Spaces[i];
    if (i > 0) sb += ",";
    sb += "{\"kind\":\"" + s.Kind + "\"";
    sb += ",\"col\":" + Num(s.Col);
    sb += ",\"row\":" + Num(s.Row);
    sb += ",\"w\":" + Num(s.W);
    sb += ",\"h\":" + Num(s.H);
    if (s.Kind === "yard") {
      const shiftBits = Math.round(s.TruckShift * 4294967296.0);
      sb += ",\"along\":\"" + s.Along + "\"";
      sb += ",\"shiftBits\":" + Num(shiftBits);
      sb += ",\"cover\":" + Num(s.Cover);
    }
    sb += "}";
  }
  sb += "]";
  sb += ",\"owner\":[";
  for (let i = 0; i < plan.Owner.length; i++) {
    if (i > 0) sb += ",";
    sb += Num(plan.Owner[i]);
  }
  sb += "]";
  sb += ",\"barred\":" + AppendPairs(plan.Barred);
  sb += ",\"vents\":" + AppendPairs(plan.Vents);
  sb += ",\"reinforced\":" + AppendPairs(plan.Reinforced);
  sb += ",\"windows\":[";
  for (let i = 0; i < plan.Windows.length; i++) {
    const w = plan.Windows[i];
    if (i > 0) sb += ",";
    sb += "[" + Num(w[0]) + "," + Num(w[1]) + "," + Bool(w[2]) + "]";
  }
  sb += "]";
  sb += ",\"posts\":[";
  for (let i = 0; i < plan.Posts.length; i++) {
    const p = plan.Posts[i];
    if (i > 0) sb += ",";
    sb += "{\"cell\":" + Num(p[0]) + ",\"facing\":" + Num(p[1]) + "}";
  }
  sb += "]";
  sb += ",\"props\":[";
  for (let i = 0; i < plan.Props.length; i++) {
    const q = plan.Props[i];
    if (i > 0) sb += ",";
    sb += "{\"type\":\"" + q.Type + "\",\"cell\":" + Num(q.Cell);
    if (q.Type === "lamp" || q.Type === "sandbags") sb += ",\"side\":" + Num(q.Side);
    else if (q.Type === "crates") sb += ",\"corner\":" + Num(q.Corner);
    else sb += ",\"corner\":" + Num(q.Corner) + ",\"fuel\":" + Bool(q.Fuel);
    sb += "}";
  }
  sb += "]";
  sb += ",\"reach\":" + Num(plan.Reach);
  sb += "}";
  return sb;
}

// ---- MapPlanSelfTest.BuildCases ----
const cases = [];
for (const seed of [1, 12345, 777, 2026]) cases.push({ name: "seed " + seed + " default", seed, opts: {}, options: new PlanOptions() });
cases.push({ name: "seed 12345 {yards:2}", seed: 12345, opts: { yards: 2 }, options: new PlanOptions({ Yards: 2 }) });
cases.push({ name: "seed 12345 {yards:2, windows:.3}", seed: 12345, opts: { yards: 2, windows: .3 }, options: new PlanOptions({ Yards: 2, Windows: 0.3 }) });
cases.push({ name: "seed 12345 {windows:.2}", seed: 12345, opts: { windows: .2 }, options: new PlanOptions({ Windows: 0.2 }) });
cases.push({ name: "seed 12345 {reinforced:.5, windows:.06, vents:5}", seed: 12345, opts: { reinforced: .5, windows: .06, vents: 5 }, options: new PlanOptions({ Reinforced: 0.5, Windows: 0.06, Vents: 5 }) });
cases.push({ name: "seed 12345 {fuel:.8}", seed: 12345, opts: { fuel: .8 }, options: new PlanOptions({ Fuel: 0.8 }) });
cases.push({ name: "seed 12345 {vents:6, gates:.85, windows:.28}", seed: 12345, opts: { vents: 6, gates: .85, windows: .28 }, options: new PlanOptions({ Vents: 6, Gates: 0.85, Windows: 0.28 }) });

const out = cases.map(c => ({
  name: c.name, seed: c.seed, opts: c.opts,
  plan: JSON.parse(Canonical(Plan(new Mulberry32(c.seed).AsFunc(), c.options))),
}));
process.stdout.write(JSON.stringify(out, null, 1) + "\n");
