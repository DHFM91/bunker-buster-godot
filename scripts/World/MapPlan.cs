using System;
using System.Collections.Generic;

namespace BunkerBuster
{
    /// <summary>
    /// Settings that a level theme can change when planning a layout.
    /// The defaults match the browser game's PLAN_DEFAULTS.
    /// </summary>
    public sealed class PlanOptions
    {
        /// <summary>Fixed number of courtyards. Zero means pick one or two at random.</summary>
        public int Yards = 0;

        /// <summary>Chance that a solid wall is reinforced.</summary>
        public double Reinforced = 0.22;

        /// <summary>Chance that a solid wall gets a window slot.</summary>
        public double Windows = 0.14;

        /// <summary>Largest number of crawl vents to place.</summary>
        public int Vents = 3;

        /// <summary>Chance that an extra loop passage is closed off with a barred gate.</summary>
        public double Gates = 0.5;

        /// <summary>Chance that a pair of barrels is explosive.</summary>
        public double Fuel = 0.35;
    }

    /// <summary>
    /// A merged area made of several neighbouring rooms: the spawn foyer, an atrium,
    /// a courtyard or a long hall.
    /// </summary>
    public sealed class Space
    {
        /// <summary>One of "foyer", "atrium", "yard" or "hall".</summary>
        public string Kind = "";

        /// <summary>Grid column of the top-left room.</summary>
        public int Col;

        /// <summary>Grid row of the top-left room.</summary>
        public int Row;

        /// <summary>Width in rooms.</summary>
        public int W;

        /// <summary>Height in rooms.</summary>
        public int H;

        /// <summary>Yards only: the direction the parked truck lies, "x" or "z". Null for other kinds.</summary>
        public string Along;

        /// <summary>Yards only: how far the truck is shifted along its axis, from 0 to just under 1.</summary>
        public double TruckShift;

        /// <summary>Yards only: how many pieces of low cover to place (1 to 3).</summary>
        public int Cover;
    }

    /// <summary>A piece of room dressing: a lamp, crates, barrels or sandbags.</summary>
    public sealed class Prop
    {
        /// <summary>One of "lamp", "crates", "barrels" or "sandbags".</summary>
        public string Type = "";

        /// <summary>The room the prop sits in.</summary>
        public int Cell;

        /// <summary>Wall side (0 = +z, 1 = +x, 2 = -z, 3 = -x), or -1 when not used.</summary>
        public int Side = -1;

        /// <summary>Room corner (0 to 3), or -1 when not used.</summary>
        public int Corner = -1;

        /// <summary>Barrels only: true when the barrels are explosive.</summary>
        public bool Fuel;
    }

    /// <summary>
    /// The complete room-grid plan for one level: which rooms connect, which are merged,
    /// where the special walls, vents, guards and props go.
    /// </summary>
    public sealed class LayoutPlan
    {
        /// <summary>Doorways between neighbouring rooms.</summary>
        public List<(int A, int B)> Passages = new List<(int A, int B)>();

        /// <summary>Merged areas, in the order they were claimed (the foyer is first).</summary>
        public List<Space> Spaces = new List<Space>();

        /// <summary>For each room, the index of the merged area it belongs to, or -1 for none.</summary>
        public int[] Owner = new int[MapPlan.Columns * MapPlan.Rows];

        /// <summary>Passages that are closed with a barred gate.</summary>
        public List<(int A, int B)> Barred = new List<(int A, int B)>();

        /// <summary>Walls with a low crawl vent through them.</summary>
        public List<(int A, int B)> Vents = new List<(int A, int B)>();

        /// <summary>Walls that are reinforced.</summary>
        public List<(int A, int B)> Reinforced = new List<(int A, int B)>();

        /// <summary>Walls with a window slot; Barred says whether the window has bars.</summary>
        public List<(int A, int B, bool Barred)> Windows = new List<(int A, int B, bool Barred)>();

        /// <summary>Guard posts: the room and the direction (0 to 3) the sentry faces.</summary>
        public List<(int Cell, int Facing)> Posts = new List<(int Cell, int Facing)>();

        /// <summary>Lamps and clutter.</summary>
        public List<Prop> Props = new List<Prop>();

        /// <summary>Number of room steps from the foyer to the intel room, or -1 if it cannot be reached.</summary>
        public int Reach;
    }

    /// <summary>
    /// Plans the whole level on the 7 by 10 room grid, without any voxels.
    /// This is a port of the "MAP PLAN" section of the browser game. The random generator
    /// is called in exactly the same order and number of times as in the JavaScript,
    /// so the same seed gives the same plan.
    /// </summary>
    public static class MapPlan
    {
        /// <summary>Rooms across.</summary>
        public const int Columns = 7;

        /// <summary>Rooms deep.</summary>
        public const int Rows = 10;

        /// <summary>Inside size of a room in voxels.</summary>
        public const int Room = 5;

        /// <summary>Distance between the starts of neighbouring rooms in voxels.</summary>
        public const int Stride = 8;

        /// <summary>Wall height in voxels.</summary>
        public const int WallTop = 9;

        /// <summary>The middle column.</summary>
        public const int MidCol = Columns / 2;

        /// <summary>The spawn room (extraction pad).</summary>
        public const int FirstCell = MidCol;

        /// <summary>The far room that holds the intel.</summary>
        public const int LastCell = (Rows - 1) * Columns + MidCol;

        /// <summary>Fewest room steps allowed from the foyer to the intel once merges and vents are counted.</summary>
        public const int MinReach = 21;

        private const int Total = Columns * Rows;

        /// <summary>Gives the centre of a room in voxel units (x across, z deep).</summary>
        public static (double X, double Z) CellCenter(int cell)
        {
            return (1 + cell % Columns * Stride + Room / 2.0,
                    1 + cell / Columns * Stride + Room / 2.0);
        }

        private static int PairKey(int a, int b)
        {
            return a < b ? a * 1000 + b : b * 1000 + a;
        }

        private static int CellOf(int col, int row)
        {
            return row * Columns + col;
        }

        /// <summary>
        /// Builds the maze with a random depth-first walk, retrying until the intel is far enough
        /// away and the middle column is not used too much. Falls back to a snake path.
        /// </summary>
        public static List<(int A, int B)> GenerateMaze(Func<double> random)
        {
            for (int attempt = 0; attempt < 96; attempt++)
            {
                var distance = new int[Total];
                Array.Fill(distance, -1);
                var route = new List<int> { FirstCell };
                var passages = new List<(int A, int B)>();
                distance[FirstCell] = 0;
                int centralOpen = 0;
                while (route.Count > 0)
                {
                    int cell = route[route.Count - 1];
                    int col = cell % Columns;
                    int row = cell / Columns;
                    var options = new List<int>();
                    if (col > 0 && distance[cell - 1] < 0) options.Add(cell - 1);
                    if (col < Columns - 1 && distance[cell + 1] < 0) options.Add(cell + 1);
                    if (row > 0 && distance[cell - Columns] < 0) options.Add(cell - Columns);
                    if (row < Rows - 1 && distance[cell + Columns] < 0) options.Add(cell + Columns);
                    if (options.Count == 0)
                    {
                        route.RemoveAt(route.Count - 1);
                        continue;
                    }
                    int next = options[(int)Math.Floor(random() * options.Count)];
                    passages.Add((cell, next));
                    distance[next] = distance[cell] + 1;
                    if (col == MidCol && next % Columns == MidCol) centralOpen++;
                    route.Add(next);
                }
                if (distance[LastCell] >= 34 && centralOpen <= 4) return passages;
            }

            // Bounded fallback keeps the same guarantees if the search finds no match.
            var snake = new int[Total];
            for (int i = 0; i < Total; i++)
            {
                int snakeRow = i / Columns;
                int snakeCol = i % Columns;
                snake[i] = snakeRow * Columns + (snakeRow % 2 != 0 ? Columns - 1 - snakeCol : snakeCol);
            }
            var fallback = new List<(int A, int B)>();
            for (int i = 1; i < Total; i++) fallback.Add((snake[i - 1], snake[i]));
            return fallback;
        }

        /// <summary>
        /// Plans a level, trying up to 20 times to reach the minimum walking distance.
        /// Returns the first plan that is long enough, otherwise the longest one found.
        /// </summary>
        public static LayoutPlan Plan(Func<double> random, PlanOptions opts)
        {
            PlanOptions o = opts ?? new PlanOptions();
            LayoutPlan best = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                LayoutPlan plan = PlanOnce(random, o);
                if (plan.Reach >= MinReach) return plan;
                if (best == null || plan.Reach > best.Reach) best = plan;
            }
            return best;
        }

        private static LayoutPlan PlanOnce(Func<double> random, PlanOptions o)
        {
            List<(int A, int B)> passages = GenerateMaze(random);
            int treeCount = passages.Count;

            // A few extra openings turn the perfect maze into loops.
            int extraOpenings = (int)Math.Floor(Total * 0.08 + 0.5);
            for (int k = 0; k < extraOpenings; k++)
            {
                int loopCell = (int)Math.Floor(random() * Total);
                bool loopRight = random() < 0.5;
                int loopCol = loopCell % Columns;
                int loopRow = loopCell / Columns;
                if (loopRight && loopCol < Columns - 1) passages.Add((loopCell, loopCell + 1));
                else if (!loopRight && loopRow < Rows - 1) passages.Add((loopCell, loopCell + Columns));
            }

            // The same quirk as the JavaScript: the key set is built from every passage so far,
            // which includes the extras, so the extras list ends up empty.
            var treeKeys = new HashSet<int>();
            foreach (var passage in passages) treeKeys.Add(PairKey(passage.A, passage.B));
            var extras = new List<(int A, int B)>();
            for (int i = treeCount; i < passages.Count; i++)
            {
                if (!treeKeys.Contains(PairKey(passages[i].A, passages[i].B))) extras.Add(passages[i]);
            }

            var owner = new int[Total];
            Array.Fill(owner, -1);
            var spaces = new List<Space>();

            bool IsFree(int fc, int fr, int fw, int fh)
            {
                if (fc < 0 || fr < 0 || fc + fw > Columns || fr + fh > Rows) return false;
                for (int yy = fr; yy < fr + fh; yy++)
                {
                    for (int xx = fc; xx < fc + fw; xx++)
                    {
                        if (owner[CellOf(xx, yy)] >= 0 || CellOf(xx, yy) == LastCell) return false;
                    }
                }
                return true;
            }

            void Claim(int cc, int cr, int cw, int ch, string kind)
            {
                for (int yy = cr; yy < cr + ch; yy++)
                {
                    for (int xx = cc; xx < cc + cw; xx++) owner[CellOf(xx, yy)] = spaces.Count;
                }
                spaces.Add(new Space { Kind = kind, Col = cc, Row = cr, W = cw, H = ch });
            }

            // Spawn foyer: a clear 3x2-room hall against the far wall with four ways out.
            Claim(MidCol - 1, 0, 3, 2, "foyer");
            int leftRow = random() < 0.5 ? 0 : 1;
            int rightRow = random() < 0.5 ? 0 : 1;
            passages.Add((CellOf(MidCol - 1, 1), CellOf(MidCol - 1, 2)));
            passages.Add((CellOf(MidCol, 1), CellOf(MidCol, 2)));
            passages.Add((CellOf(MidCol + 1, 1), CellOf(MidCol + 1, 2)));
            passages.Add((CellOf(MidCol - 2, leftRow), CellOf(MidCol - 1, leftRow)));
            passages.Add((CellOf(MidCol + 1, rightRow), CellOf(MidCol + 2, rightRow)));

            // Central atrium with reinforced pillars.
            if (random() < 0.6 && IsFree(MidCol - 1, 4, 3, 2)) Claim(MidCol - 1, 4, 3, 2, "atrium");

            // Courtyards: open yards with vehicles and low cover.
            int yardCount = o.Yards != 0 ? o.Yards : 1 + (random() < 0.5 ? 1 : 0);
            int[][] yardSizes = { new[] { 2, 2 }, new[] { 3, 2 }, new[] { 2, 3 } };
            for (int yardIndex = 0; yardIndex < yardCount; yardIndex++)
            {
                for (int tryIndex = 0; tryIndex < 12; tryIndex++)
                {
                    int[] size = yardSizes[(int)Math.Floor(random() * 3)];
                    int yardW = size[0];
                    int yardH = size[1];
                    int yardC = (int)Math.Floor(random() * (Columns - yardW + 1));
                    int yardR = 3 + (int)Math.Floor(random() * (Rows - 4 - yardH));
                    if (IsFree(yardC, yardR, yardW, yardH))
                    {
                        Claim(yardC, yardR, yardW, yardH, "yard");
                        break;
                    }
                }
            }

            // A long hall: four rooms in a row knocked through.
            if (random() < 0.5)
            {
                for (int hallTry = 0; hallTry < 10; hallTry++)
                {
                    int hallR = 3 + (int)Math.Floor(random() * 5);
                    int hallC = (int)Math.Floor(random() * (Columns - 3));
                    if (IsFree(hallC, hallR, 4, 1))
                    {
                        Claim(hallC, hallR, 4, 1, "hall");
                        break;
                    }
                }
            }

            var linked = new HashSet<int>();
            foreach (var passage in passages) linked.Add(PairKey(passage.A, passage.B));

            bool SameSpace(int sa, int sb)
            {
                return owner[sa] >= 0 && owner[sa] == owner[sb];
            }

            bool IsOpen(int oa, int ob)
            {
                return linked.Contains(PairKey(oa, ob)) || SameSpace(oa, ob);
            }

            // Every pair of side-by-side rooms, in a fixed order.
            var pairs = new List<(int A, int B)>();
            for (int pairCell = 0; pairCell < Total; pairCell++)
            {
                int pairCol = pairCell % Columns;
                int pairRow = pairCell / Columns;
                if (pairCol < Columns - 1) pairs.Add((pairCell, pairCell + 1));
                if (pairRow < Rows - 1) pairs.Add((pairCell, pairCell + Columns));
            }

            // Barred gates on loop passages.
            var barred = new List<(int A, int B)>();
            foreach (var extra in extras)
            {
                if (!SameSpace(extra.A, extra.B) && owner[extra.A] < 0 && owner[extra.B] < 0 && random() < o.Gates)
                {
                    barred.Add((extra.A, extra.B));
                }
            }

            // Crawl vents: knee-high gaps through a solid wall.
            var vents = new List<(int A, int B)>();
            var ventKeys = new HashSet<int>();
            for (int ventTry = 0; ventTry < 40 && vents.Count < o.Vents; ventTry++)
            {
                var ventPair = pairs[(int)Math.Floor(random() * pairs.Count)];
                if (IsOpen(ventPair.A, ventPair.B) || ventKeys.Contains(PairKey(ventPair.A, ventPair.B))
                    || owner[ventPair.A] == 0 || owner[ventPair.B] == 0) continue;
                vents.Add((ventPair.A, ventPair.B));
                ventKeys.Add(PairKey(ventPair.A, ventPair.B));
            }

            // Reinforced walls and windows.
            var reinforced = new List<(int A, int B)>();
            var windows = new List<(int A, int B, bool Barred)>();
            foreach (var wallPair in pairs)
            {
                if (IsOpen(wallPair.A, wallPair.B) || ventKeys.Contains(PairKey(wallPair.A, wallPair.B))) continue;
                double reinforceRoll = random();
                double windowRoll = random();
                bool windowBars = random() < 0.5;
                if (wallPair.A == LastCell || wallPair.B == LastCell || reinforceRoll < o.Reinforced)
                {
                    reinforced.Add((wallPair.A, wallPair.B));
                }
                if (windowRoll < o.Windows && owner[wallPair.A] != 0 && owner[wallPair.B] != 0)
                {
                    windows.Add((wallPair.A, wallPair.B, windowBars));
                }
            }

            // Guard posts: one always watches the way in from just past the foyer.
            var posts = new List<(int Cell, int Facing)> { (CellOf(MidCol, 2), 0) };
            for (int postTry = 0; postTry < 30 && posts.Count < 3; postTry++)
            {
                int postCell = (int)Math.Floor(random() * Total);
                int postFacing = (int)Math.Floor(random() * 4);
                if (postCell / Columns < 3 || owner[postCell] >= 0 || postCell == LastCell
                    || posts.Exists(p => p.Cell == postCell)) continue;
                posts.Add((postCell, postFacing));
            }

            // Room dressing. Sides: 0 +z, 1 +x, 2 -z, 3 -x. Corners 0 to 3.
            var props = new List<Prop>();
            for (int propCell = 0; propCell < Total; propCell++)
            {
                double lamp = random();
                int side = (int)Math.Floor(random() * 4);
                double clutter = random();
                int corner = (int)Math.Floor(random() * 4);
                double fuel = random();
                if (owner[propCell] == 0 || posts.Exists(p => p.Cell == propCell)) continue;
                if (lamp < 0.45) props.Add(new Prop { Type = "lamp", Cell = propCell, Side = side });
                if (propCell == LastCell) continue;
                if (clutter < 0.3) props.Add(new Prop { Type = "crates", Cell = propCell, Corner = corner });
                else if (clutter < 0.5) props.Add(new Prop { Type = "barrels", Cell = propCell, Corner = corner, Fuel = fuel < o.Fuel });
                else if (clutter < 0.6) props.Add(new Prop { Type = "sandbags", Cell = propCell, Side = side });
            }

            // Yards: one parked truck and some low cover each.
            foreach (Space yardSpace in spaces)
            {
                if (yardSpace.Kind != "yard") continue;
                yardSpace.Along = random() < 0.5 ? "x" : "z";
                yardSpace.TruckShift = random();
                yardSpace.Cover = 1 + (int)Math.Floor(random() * 3);
            }

            // How far is the intel from the foyer once merges and vents are counted? Gates stay shut.
            var shut = new HashSet<int>();
            foreach (var gate in barred) shut.Add(PairKey(gate.A, gate.B));
            var dist = new int[Total];
            Array.Fill(dist, -1);
            var queue = new List<int> { FirstCell };
            dist[FirstCell] = 0;
            for (int q = 0; q < queue.Count; q++)
            {
                int current = queue[q];
                int currentCol = current % Columns;
                int currentRow = current / Columns;
                int[] neighbours =
                {
                    currentCol > 0 ? current - 1 : -1,
                    currentCol < Columns - 1 ? current + 1 : -1,
                    currentRow > 0 ? current - Columns : -1,
                    currentRow < Rows - 1 ? current + Columns : -1,
                };
                foreach (int neighbour in neighbours)
                {
                    if (neighbour < 0 || dist[neighbour] >= 0) continue;
                    int key = PairKey(current, neighbour);
                    if (!((IsOpen(current, neighbour) && !shut.Contains(key)) || ventKeys.Contains(key))) continue;
                    dist[neighbour] = dist[current] + 1;
                    queue.Add(neighbour);
                }
            }

            return new LayoutPlan
            {
                Passages = passages,
                Spaces = spaces,
                Owner = owner,
                Barred = barred,
                Vents = vents,
                Reinforced = reinforced,
                Windows = windows,
                Posts = posts,
                Props = props,
                Reach = dist[LastCell],
            };
        }
    }
}
