using System;
using System.Collections.Generic;

namespace HWC.Sim
{
    /// <summary>
    /// Deterministic 2D rigid-body world of axis-aligned rectangles inside a moving box.
    /// Coordinates are box-local cells: x right, y up, the interior spans [0,W] x [0,H].
    /// </summary>
    public sealed class World
    {
        struct Contact
        {
            public int A, B;
            public V2 N;            // from A to B
            public float Pen;
            public float Pn, Pt;
            public float Mu, E, H;
            public float MassN, MassT;
            public float VnTarget;
            public float Vn0;
            public int Axis;        // 0 +x, 1 -x, 2 +y, 3 -y (direction of N)
        }

        public readonly int W, H;
        public readonly List<Body> Bodies = new List<Body>();
        public readonly List<Incident> Incidents = new List<Incident>();
        public readonly List<BumpFx> Bumps = new List<BumpFx>();
        public int Tick;
        public bool DamageEnabled;
        public int CurrentLeg = -1, CurrentEvent = -1;
        public int RouteStartTick;

        // box frame state for this tick
        public V2 GLocal, DvLocal, AEff;

        readonly List<Contact> contacts = new List<Contact>(256);
        float[] cachePn = new float[0], cachePt = new float[0];
        sbyte[] cacheAxis = new sbyte[0];
        int[] lastBumpTick = new int[0];
        int n2;
        readonly int crushLen;
        readonly int strapLen;
        Rng rng;

        public World(int w, int h, uint seed)
        {
            W = w; H = h;
            rng = new Rng(seed * 2654435761u + 17u);
            crushLen = Math.Max(1, (int)Math.Round(SimConst.CrushWindow * SimConst.TickRate));
            strapLen = crushLen;
        }

        // ---- Construction ----------------------------------------------------------------------

        public Body AddStatic(BodyType type, float minX, float minY, float maxX, float maxY, float hardness)
        {
            var b = new Body
            {
                Index = Bodies.Count, Type = type,
                Pos = new V2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f),
                Half = new V2((maxX - minX) * 0.5f, (maxY - minY) * 0.5f),
                Fixed = true, Mass = 0, InvMass = 0,
                Friction = 0.5f, Restitution = 1f, Hardness = hardness, GravityScale = 0,
            };
            Bodies.Add(b);
            return b;
        }

        public const float InsetX = 0.06f;
        public const float InsetY = 0.03f;
        public const float DividerHalf = 0.06f;
        public const float ShelfHalf = 0.03f;

        public Body AddPiece(Placement p, int pieceIndex)
        {
            var d = p.Def;
            float w = p.W, h = p.H;
            var b = new Body
            {
                Index = Bodies.Count, Type = BodyType.Piece, Def = d, PieceIndex = pieceIndex,
                Pos = new V2(p.X + w * 0.5f, p.Y + h * 0.5f),
                Half = new V2(w * 0.5f - InsetX, h * 0.5f - InsetY),
                Mass = d.Mass, InvMass = 1f / d.Mass,
                Friction = d.Friction, Restitution = d.Restitution, Hardness = d.Hardness,
                GravityScale = d.GravityScale,
                Facing = p.Facing == 0 ? 1 : p.Facing,
                CrushRing = new float[crushLen],
                StrapRing = new float[strapLen],
            };
            if (p.Strapped)
            {
                b.Fixed = true;
                b.InvMass = 0;
                b.State |= BodyState.Strapped;
            }
            b.Dir = b.Facing;
            Bodies.Add(b);
            return b;
        }

        /// <summary>Builds walls, dividers, shelves and pieces from a packing.</summary>
        public static World FromPacking(Packing pk, uint seed)
        {
            var w = new World(pk.W, pk.H, seed);
            const float T = 2f;
            w.AddStatic(BodyType.Wall, -T, -T, pk.W + T, 0, SimConst.WallHardness);          // floor
            w.AddStatic(BodyType.Wall, -T, pk.H, pk.W + T, pk.H + T, SimConst.WallHardness); // ceiling
            w.AddStatic(BodyType.Wall, -T, 0, 0, pk.H, SimConst.WallHardness);               // left
            w.AddStatic(BodyType.Wall, pk.W, 0, pk.W + T, pk.H, SimConst.WallHardness);      // right
            var dividers = new List<int>(pk.Dividers);
            dividers.Sort();
            foreach (int d in dividers)
                w.AddStatic(BodyType.Divider, d - DividerHalf, 0, d + DividerHalf, pk.H, SimConst.DividerHardness);
            foreach (var s in pk.Shelves)
            {
                pk.ShelfSpan(s, out int x0, out int x1);
                float a = x0 == 0 ? 0 : x0 + DividerHalf;
                float b = x1 == pk.W ? pk.W : x1 - DividerHalf;
                w.AddStatic(BodyType.Shelf, a, s.Row - ShelfHalf, b, s.Row + ShelfHalf, SimConst.DividerHardness);
            }
            for (int i = 0; i < pk.Pieces.Count; i++) w.AddPiece(pk.Pieces[i], i);
            w.FinishSetup();
            return w;
        }

        public void FinishSetup()
        {
            int n = Bodies.Count;
            n2 = n;
            cachePn = new float[n * n];
            cachePt = new float[n * n];
            cacheAxis = new sbyte[n * n];
            lastBumpTick = new int[n * n];
            for (int i = 0; i < lastBumpTick.Length; i++) { lastBumpTick[i] = -100000; cacheAxis[i] = -1; }
        }

        public float Time => (Tick - RouteStartTick) * SimConst.Dt;

        // ---- Stepping --------------------------------------------------------------------------

        /// <summary>Advances one tick. gLocal: gravity in box frame; dvLocal: box velocity change in box frame.</summary>
        public void Step(V2 gLocal, V2 dvLocal)
        {
            Tick++;
            float dt = SimConst.Dt;
            GLocal = gLocal;
            DvLocal = dvLocal;
            AEff = gLocal - dvLocal / dt;

            PreStepQuirks();

            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                if (b.Fixed || !b.Active) { b.Vel = V2.Zero; continue; }
                b.Vel = b.Vel + gLocal * (b.GravityScale * dt) - dvLocal;
                float sp = b.Vel.Length;
                if (sp > SimConst.MaxSpeed) b.Vel = b.Vel * (SimConst.MaxSpeed / sp);
                b.VelBefore = b.Vel;
            }

            DetectContacts();
            PrepareContacts();
            for (int it = 0; it < SimConst.VelocityIterations; it++) SolveVelocities();

            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                if (b.Fixed || !b.Active) continue;
                b.Pos = b.Pos + b.Vel * dt;
                if (b.Def != null && b.Def.Has(Quirk.Rolls))
                {
                    // roll along whatever is currently the floor
                    float r = Math.Max(0.2f, Math.Min(b.Half.x, b.Half.y));
                    var up = UpDir;
                    b.RollAngle -= V2.Dot(b.Vel, new V2(up.y, -up.x)) * dt / r;
                }
            }

            for (int it = 0; it < SimConst.PositionIterations; it++) SolvePositions();

            StoreCache();
            SummarizeContacts();
            PostStepQuirks();
        }

        // ---- Contacts --------------------------------------------------------------------------

        void DetectContacts()
        {
            contacts.Clear();
            int n = Bodies.Count;
            for (int i = 0; i < n; i++)
            {
                var a = Bodies[i];
                if (!a.Active) continue;
                for (int j = i + 1; j < n; j++)
                {
                    var b = Bodies[j];
                    if (!b.Active) continue;
                    if (a.Fixed && b.Fixed) continue;
                    float dx = b.Pos.x - a.Pos.x, dy = b.Pos.y - a.Pos.y;
                    float ox = a.Half.x + b.Half.x - Math.Abs(dx);
                    float oy = a.Half.y + b.Half.y - Math.Abs(dy);
                    if (ox <= 0f || oy <= 0f) continue;

                    int key = i * n2 + j;
                    int prevAxis = cacheAxis[key];
                    float tol = SimConst.AxisTolerance;
                    bool useX;
                    if (oy < tol && ox >= tol) useX = false;
                    else if (ox < tol && oy >= tol) useX = true;
                    else if (prevAxis >= 0) useX = prevAxis < 2;
                    else useX = ox < oy;

                    var c = new Contact { A = i, B = j };
                    if (useX)
                    {
                        float s = dx >= 0 ? 1f : -1f;
                        c.N = new V2(s, 0); c.Pen = ox; c.Axis = s > 0 ? 0 : 1;
                    }
                    else
                    {
                        float s = dy >= 0 ? 1f : -1f;
                        c.N = new V2(0, s); c.Pen = oy; c.Axis = s > 0 ? 2 : 3;
                    }
                    c.Mu = MathF.Sqrt(a.Friction * b.Friction);
                    float ea = a.Type == BodyType.Piece ? a.Restitution : 1f;
                    float eb = b.Type == BodyType.Piece ? b.Restitution : 1f;
                    c.E = Math.Min(ea, eb);
                    c.H = Math.Min(a.Hardness, b.Hardness);
                    if (prevAxis == c.Axis)
                    {
                        c.Pn = cachePn[key];
                        c.Pt = cachePt[key];
                    }
                    contacts.Add(c);
                }
            }
        }

        void PrepareContacts()
        {
            for (int k = 0; k < contacts.Count; k++)
            {
                var c = contacts[k];
                var a = Bodies[c.A];
                var b = Bodies[c.B];
                float inv = a.InvMass + b.InvMass;
                c.MassN = inv > 0 ? 1f / inv : 0;
                c.MassT = c.MassN;
                var rv = b.Vel - a.Vel;
                c.Vn0 = V2.Dot(rv, c.N);
                c.VnTarget = c.Vn0 < -SimConst.RestitutionThreshold ? -c.E * c.Vn0 : 0f;

                // warm start (slightly damped for stability)
                const float warm = 0.85f;
                c.Pn *= warm; c.Pt *= warm;
                var t = c.N.Perp;
                var P = c.N * c.Pn + t * c.Pt;
                a.Vel = a.Vel - P * a.InvMass;
                b.Vel = b.Vel + P * b.InvMass;
                contacts[k] = c;
            }
        }

        void SolveVelocities()
        {
            for (int k = 0; k < contacts.Count; k++)
            {
                var c = contacts[k];
                if (c.MassN <= 0) continue;
                var a = Bodies[c.A];
                var b = Bodies[c.B];
                var rv = b.Vel - a.Vel;
                float vn = V2.Dot(rv, c.N);
                float dPn = (c.VnTarget - vn) * c.MassN;
                float pn0 = c.Pn;
                c.Pn = Math.Max(pn0 + dPn, 0f);
                dPn = c.Pn - pn0;
                var P = c.N * dPn;
                a.Vel = a.Vel - P * a.InvMass;
                b.Vel = b.Vel + P * b.InvMass;

                // friction
                var t = c.N.Perp;
                rv = b.Vel - a.Vel;
                float vt = V2.Dot(rv, t);
                float dPt = -vt * c.MassT;
                float maxF = c.Mu * c.Pn;
                float pt0 = c.Pt;
                c.Pt = SimMathUtil.Clamp(pt0 + dPt, -maxF, maxF);
                dPt = c.Pt - pt0;
                P = t * dPt;
                a.Vel = a.Vel - P * a.InvMass;
                b.Vel = b.Vel + P * b.InvMass;
                contacts[k] = c;
            }
        }

        void SolvePositions()
        {
            for (int k = 0; k < contacts.Count; k++)
            {
                var c = contacts[k];
                var a = Bodies[c.A];
                var b = Bodies[c.B];
                float inv = a.InvMass + b.InvMass;
                if (inv <= 0) continue;
                float pen;
                if (c.Axis < 2)
                {
                    float oy = a.Half.y + b.Half.y - Math.Abs(b.Pos.y - a.Pos.y);
                    if (oy <= 0) continue;
                    pen = a.Half.x + b.Half.x - (b.Pos.x - a.Pos.x) * c.N.x;
                }
                else
                {
                    float ox = a.Half.x + b.Half.x - Math.Abs(b.Pos.x - a.Pos.x);
                    if (ox <= 0) continue;
                    pen = a.Half.y + b.Half.y - (b.Pos.y - a.Pos.y) * c.N.y;
                }
                float C = pen - SimConst.Slop;
                if (C <= 0) continue;
                float corr = Math.Min(C * 0.5f, 0.12f) / inv;
                var P = c.N * corr;
                a.Pos = a.Pos - P * a.InvMass;
                b.Pos = b.Pos + P * b.InvMass;
            }
        }

        void StoreCache()
        {
            // clear previous axes of pairs that are no longer in contact
            for (int i = 0; i < cacheAxis.Length; i++) cacheAxis[i] = -1;
            for (int k = 0; k < contacts.Count; k++)
            {
                var c = contacts[k];
                int key = c.A * n2 + c.B;
                cachePn[key] = c.Pn;
                cachePt[key] = c.Pt;
                cacheAxis[key] = (sbyte)c.Axis;
            }
        }

        // ---- Per-body contact summary, jolts, loads ----------------------------------------

        void SummarizeContacts()
        {
            float dt = SimConst.Dt;
            V2 gdir = AEff.Normalized;
            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                b.Supported = false;
                b.ContactImpulse = V2.Zero;
                b.ContactImpulseMag = 0;
                b.WeightedImpulseMag = 0;
                b.CompPosX = b.CompNegX = b.CompPosY = b.CompNegY = 0;
                b.SupportHardness = 1f;
                b.TripDir = 0;
            }

            for (int k = 0; k < contacts.Count; k++)
            {
                var c = contacts[k];
                var a = Bodies[c.A];
                var b = Bodies[c.B];
                Accumulate(b, a, c.N, c, gdir);
                Accumulate(a, b, -c.N, c, gdir);

                // sharp things
                if (c.Pn > 0 || c.Pen > 0.01f)
                {
                    if (a.Has(Quirk.Sharp)) Prick(b, a);
                    if (b.Has(Quirk.Sharp)) Prick(a, b);
                    // two magnets that touch are stuck together for good
                    if (DamageEnabled && a.Has(Quirk.Magnet) && b.Has(Quirk.Magnet) && (!a.Is(BodyState.Stuck) || !b.Is(BodyState.Stuck)))
                    {
                        a.State |= BodyState.Stuck;
                        b.State |= BodyState.Stuck;
                        AddIncident(a, IncidentKind.Stuck, -c.Vn0, 0, b.Index, true);
                        AddIncident(b, IncidentKind.Stuck, -c.Vn0, 0, a.Index, true);
                    }
                }

                // notable bumps for sound/particles
                if (DamageEnabled && -c.Vn0 > 2.2f)
                {
                    int key = c.A * n2 + c.B;
                    if (Tick - lastBumpTick[key] > 10)
                    {
                        lastBumpTick[key] = Tick;
                        float px = (Math.Max(a.MinX, b.MinX) + Math.Min(a.MaxX, b.MaxX)) * 0.5f;
                        float py = (Math.Max(a.MinY, b.MinY) + Math.Min(a.MaxY, b.MaxY)) * 0.5f;
                        Bumps.Add(new BumpFx { Tick = Tick - RouteStartTick, A = c.A, B = c.B, Speed = -c.Vn0, Point = new V2(px, py), Normal = c.N });
                    }
                }
            }

            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                if (!b.IsPiece || !b.Active) continue;
                // jolt = hardness-weighted velocity change from contacts, summed over a 3-tick
                // (12.5 ms) window so an impact counts the same however the solver splits it
                V2 dvc;
                if (b.Fixed)
                {
                    dvc = DvLocal * SimConst.StrapHardness + b.ContactImpulse * (1f / b.Mass) * (b.ContactImpulseMag > 1e-6f ? b.WeightedImpulseMag / b.ContactImpulseMag : 1f);
                }
                else
                {
                    var dv = b.Vel - b.VelBefore;
                    float hAvg = b.ContactImpulseMag > 1e-6f ? b.WeightedImpulseMag / b.ContactImpulseMag : 1f;
                    dvc = dv * hAvg;
                }
                var win = dvc + b.Jw0 + b.Jw1;
                b.Jw1 = b.Jw0;
                b.Jw0 = dvc;
                // ignore the gentle support that just cancels gravity (never the box's own jolts)
                float jolt = Math.Max(0, win.Length - GLocal.Length * dt * 3.6f);
                b.LastJolt = jolt;

                float comp = Math.Max(Math.Min(b.CompPosX, b.CompNegX), Math.Min(b.CompPosY, b.CompNegY));
                if (b.Fixed) comp = b.WeightedImpulseMag;
                float load = comp / dt / SimConst.Gravity;
                b.CrushSum += load - b.CrushRing[b.CrushHead];
                b.CrushRing[b.CrushHead] = load;
                b.CrushHead = (b.CrushHead + 1) % b.CrushRing.Length;

                if (DamageEnabled)
                {
                    if (jolt > b.PeakJolt) b.PeakJolt = jolt;
                    float crush = b.CrushSum / b.CrushRing.Length;
                    if (crush > b.PeakCrush) b.PeakCrush = crush;
                }
            }
        }

        void Accumulate(Body self, Body other, V2 nInto, Contact c, V2 gdir)
        {
            if (!self.IsPiece) return;
            var I = nInto * c.Pn;
            self.ContactImpulse = self.ContactImpulse + I;
            self.ContactImpulseMag += c.Pn;
            self.WeightedImpulseMag += c.Pn * c.H;
            var Iw = I * c.H;
            if (Iw.x > 0) self.CompPosX += Iw.x; else self.CompNegX -= Iw.x;
            if (Iw.y > 0) self.CompPosY += Iw.y; else self.CompNegY -= Iw.y;
            if ((c.Pn > 0 || c.Pen > 0.002f) && V2.Dot(nInto, gdir) < -0.6f)
            {
                self.Supported = true;
                self.SupportHardness = Math.Min(self.SupportHardness, c.H);
            }

            // trip/knock detection for tall items: a sideways hit on only the lower or upper half
            if (self.Has(Quirk.Topples) && !self.Is(BodyState.Toppled) && Math.Abs(nInto.x) > 0.9f && self.Half.y > self.Half.x)
            {
                float dvLat = c.Pn * self.InvMass;
                if (self.Fixed) dvLat = 0;
                if (dvLat > 2.5f)
                {
                    float top = Math.Min(self.MaxY, other.MaxY);
                    float bot = Math.Max(self.MinY, other.MinY);
                    bool grav = AEff.y <= 0;
                    float mid = self.Pos.y;
                    if (grav ? top < mid : bot > mid) self.TripDir = -Math.Sign(nInto.x);       // base knocked: tips the other way
                    else if (grav ? bot > mid : top < mid) self.TripDir = Math.Sign(nInto.x);   // shoulder knocked: tips with the push
                }
            }
        }

        void Prick(Body victim, Body sharp)
        {
            if (!DamageEnabled || !victim.IsPiece || !victim.Active) return;
            if (victim.Def.Kind == PieceKind.Balloon)
            {
                Pop(victim, sharp.Index, 0);
            }
            else if (victim.Def.Kind == PieceKind.Bubble && !victim.Is(BodyState.Popped))
            {
                victim.State |= BodyState.Popped;
                victim.Hardness = 0.75f;
                AddIncident(victim, IncidentKind.Popped, 0, 0, sharp.Index, false);
            }
            else if (victim.Has(Quirk.Sleeper) && !victim.Is(BodyState.Awake))
            {
                Wake(victim, sharp.Index, 0);
            }
        }

        // ---- Quirks ------------------------------------------------------------------------------

        V2 UpDir => Math.Abs(AEff.y) >= Math.Abs(AEff.x) ? new V2(0, AEff.y <= 0 ? 1 : -1) : new V2(AEff.x <= 0 ? 1 : -1, 0);

        void PreStepQuirks()
        {
            float dt = SimConst.Dt;
            var up = UpDir;
            var lat = new V2(up.y, -up.x); // right-hand perpendicular

            // magnets
            for (int i = 0; i < Bodies.Count; i++)
            {
                var a = Bodies[i];
                if (!a.Active || !a.Has(Quirk.Magnet)) continue;
                for (int j = 0; j < Bodies.Count; j++)
                {
                    if (j == i) continue;
                    var b = Bodies[j];
                    if (!b.Active || !b.Has(Quirk.Metal)) continue;
                    if (b.Has(Quirk.Magnet) && j < i) continue; // magnet pairs handled once
                    var d = b.Pos - a.Pos;
                    float dist = d.Length;
                    if (dist > SimConst.MagnetRange || dist < 1e-4f) continue;
                    float f = SimConst.MagnetStrength / Math.Max(dist * dist, 1f);
                    // fade out at the edge of the range so it is not a hard switch
                    float edge = (SimConst.MagnetRange - dist) / 0.75f;
                    if (edge < 1f) f *= Math.Max(0f, edge);
                    var F = d / dist * f;
                    ApplyForce(a, F, dt);
                    ApplyForce(b, -F, dt);
                    if (a.Fixed) StrapLoadExtra(a, F);
                    if (b.Fixed) StrapLoadExtra(b, -F);
                }
            }

            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                if (!b.IsPiece || !b.Active) continue;
                var d = b.Def;
                if (b.Cooldown > 0) b.Cooldown -= dt;

                if (d.Has(Quirk.Sleeper))
                {
                    if (!b.Is(BodyState.Awake))
                    {
                        b.Timer += dt;
                        float period = 2.6f;
                        if (b.Timer >= period)
                        {
                            b.Timer -= period;
                            if (b.Supported && !b.Fixed && DamageEnabled)
                            {
                                b.Vel = b.Vel + lat * (b.Dir * 5.5f);
                                b.Dir = -b.Dir;
                            }
                        }
                    }
                    else if (!b.Fixed)
                    {
                        b.Timer2 += dt;
                        if (b.Timer2 >= 0.45f)
                        {
                            b.Timer2 -= 0.45f;
                            b.Vel = b.Vel + lat * (b.Dir * 7f) + up * 5f;
                            b.Dir = rng.Next01() < 0.7f ? -b.Dir : b.Dir;
                        }
                    }
                }

                if (d.Has(Quirk.Walker) && !b.Fixed && DamageEnabled)
                {
                    b.State |= BodyState.Walking;
                    if (b.Supported)
                    {
                        float v = V2.Dot(b.Vel, lat) * b.Facing;
                        if (v < 2.2f) ApplyForce(b, lat * (b.Facing * 28f * b.Mass), dt);
                        if (Math.Abs(V2.Dot(b.Vel, lat)) < 0.25f) b.StuckTime += dt; else b.StuckTime = 0;
                        if (b.StuckTime > 0.4f) { b.Facing = -b.Facing; b.StuckTime = 0; }
                    }
                }

                if (d.Has(Quirk.Hopper) && !b.Fixed && DamageEnabled)
                {
                    b.Timer += dt;
                    b.State &= ~BodyState.Hopping;
                    if (b.Timer >= 2.3f && b.Supported)
                    {
                        b.Timer = 0;
                        if (Blocked(b, lat * b.Facing)) b.Facing = -b.Facing;
                        b.Vel = b.Vel + up * 11f + lat * (b.Facing * 3f);
                        b.State |= BodyState.Hopping;
                    }
                }

                if (d.Has(Quirk.Sneezer))
                {
                    if (b.SneezeAt >= 0 && Time >= b.SneezeAt)
                    {
                        b.SneezeAt = -1;
                        b.State &= ~BodyState.Windup;
                        Sneeze(b);
                    }
                    if (b.SneezeTick >= 0 && Tick - b.SneezeTick > SimConst.TickRate / 2)
                    {
                        b.State &= ~BodyState.Sneezing;
                        b.SneezeTick = -1;
                    }
                }
            }
        }

        bool Blocked(Body b, V2 dir)
        {
            float minX = b.MinX, maxX = b.MaxX, minY = b.MinY, maxY = b.MaxY;
            const float probe = 0.35f;
            if (dir.x > 0.5f) { minX = b.MaxX; maxX = b.MaxX + probe; }
            else if (dir.x < -0.5f) { maxX = b.MinX; minX = b.MinX - probe; }
            else if (dir.y > 0.5f) { minY = b.MaxY; maxY = b.MaxY + probe; }
            else { maxY = b.MinY; minY = b.MinY - probe; }
            foreach (var o in Bodies)
                if (o != b && o.Active && o.Overlaps(minX, minY, maxX, maxY, 0.02f)) return true;
            return false;
        }

        void ApplyForce(Body b, V2 F, float dt)
        {
            if (b.Fixed || !b.Active) return;
            b.Vel = b.Vel + F * (b.InvMass * dt);
        }

        void StrapLoadExtra(Body b, V2 F) { b.ExtraStrapForce = b.ExtraStrapForce + F; }

        void PostStepQuirks()
        {
            float dt = SimConst.Dt;
            for (int i = 0; i < Bodies.Count; i++)
            {
                var b = Bodies[i];
                if (!b.IsPiece || !b.Active) continue;
                var d = b.Def;

                // straps
                if (b.Fixed && b.Is(BodyState.Strapped))
                {
                    var hold = (GLocal * (b.GravityScale * dt) - DvLocal) * b.Mass + b.ExtraStrapForce * dt + b.ContactImpulse;
                    float load = hold.Length / dt / SimConst.Gravity;
                    b.StrapSum += load - b.StrapRing[b.CrushHead % b.StrapRing.Length];
                    b.StrapRing[b.CrushHead % b.StrapRing.Length] = load;
                    if (DamageEnabled && b.StrapSum / b.StrapRing.Length > SimConst.StrapStrength)
                    {
                        b.Fixed = false;
                        b.InvMass = 1f / b.Mass;
                        b.State &= ~BodyState.Strapped;
                        b.State |= BodyState.StrapSnapped;
                        AddIncident(b, IncidentKind.StrapSnapped, b.StrapSum / b.StrapRing.Length, SimConst.StrapStrength, -1, false);
                    }
                }
                b.ExtraStrapForce = V2.Zero;

                if (!DamageEnabled) continue;

                float crush = b.CrushSum / b.CrushRing.Length;

                // fragile / squishable
                if (d.JoltLimit > 0 && !b.Is(BodyState.Broken) && !b.Is(BodyState.Squished) && !b.Is(BodyState.Popped))
                {
                    if (b.LastJolt > d.JoltLimit)
                    {
                        if (d.Kind == PieceKind.Bubble) PopBubble(b, b.LastJolt, d.JoltLimit);
                        else if (d.Kind == PieceKind.Balloon) Pop(b, -1, b.LastJolt);
                        else if (d.Has(Quirk.Squishable)) Squish(b, b.LastJolt, d.JoltLimit);
                        else Break(b, b.LastJolt, d.JoltLimit);
                    }
                }
                if (d.CrushLimit > 0 && b.Active && !b.Is(BodyState.Broken) && !b.Is(BodyState.Squished) && !b.Is(BodyState.Popped) && crush > d.CrushLimit)
                {
                    if (d.Kind == PieceKind.Bubble) PopBubble(b, crush, d.CrushLimit);
                    else if (d.Kind == PieceKind.Balloon) Pop(b, -1, crush);
                    else if (d.Has(Quirk.Squishable)) Squish(b, crush, d.CrushLimit);
                    else Break(b, crush, d.CrushLimit);
                }

                // sleepers wake
                if (d.Has(Quirk.Sleeper) && !b.Is(BodyState.Awake) && b.LastJolt > d.WakeLimit)
                    Wake(b, -1, b.LastJolt);

                // sneezers: a cold makes them sneeze every few seconds, jolts and tickles set them off too
                if (d.Has(Quirk.Sneezer)) b.Timer += SimConst.Dt;
                if (d.Has(Quirk.Sneezer) && b.SneezeAt < 0 && b.Cooldown <= 0)
                {
                    bool tickle = Tickled(b);
                    bool cold = b.Timer >= SimConst.SneezePeriod;
                    if (cold) b.Timer = 0;
                    if (b.LastJolt > d.SneezeLimit || tickle || cold)
                    {
                        b.SneezeAt = Time + 0.45f;
                        b.State |= BodyState.Windup;
                        AddIncident(b, tickle ? IncidentKind.Tickled : IncidentKind.SneezeWindup, b.LastJolt, d.SneezeLimit, -1, false);
                    }
                }

                // heat
                if (d.Has(Quirk.Melts) && !b.Is(BodyState.Melted))
                {
                    if (NearHeat(b)) b.MeltTime += dt;
                    if (b.MeltTime >= SimConst.MeltTime) Melt(b, -1);
                }
                if (d.Has(Quirk.KeepWarm) && NearHeat(b)) b.WarmTime += dt;

                // toppling
                if (d.Has(Quirk.Topples) && !b.Is(BodyState.Toppled) && !b.Fixed && b.Half.y > b.Half.x && b.Active)
                    CheckTopple(b);

                // care tracking (sleepers measured against wake limit)
                float ratio = 0;
                if (d.JoltLimit > 0 && d.Kind != PieceKind.Bubble) ratio = Math.Max(ratio, b.LastJolt / d.JoltLimit);
                if (d.CrushLimit > 0 && d.Kind != PieceKind.Bubble) ratio = Math.Max(ratio, crush / d.CrushLimit);
                if (d.WakeLimit > 0) ratio = Math.Max(ratio, b.LastJolt / d.WakeLimit);
                if (ratio > b.PeakJoltRatio)
                {
                    b.PeakJoltRatio = ratio;
                    b.PeakTick = Tick - RouteStartTick;
                    b.PeakLeg = CurrentLeg;
                    b.PeakEvent = CurrentEvent;
                }
            }
        }

        bool Tickled(Body dragon)
        {
            // paper pressed against its nose tickles
            float nx = dragon.Facing > 0 ? dragon.MaxX : dragon.MinX;
            float minX = dragon.Facing > 0 ? nx : nx - 0.15f, maxX = dragon.Facing > 0 ? nx + 0.15f : nx;
            foreach (var o in Bodies)
            {
                if (!o.Active || o.Def == null || o.Def.Kind != PieceKind.Paper) continue;
                if (o.Overlaps(minX, dragon.Pos.y - 0.3f, maxX, dragon.Pos.y + 0.3f)) { dragon.Timer2 += SimConst.Dt; break; }
            }
            if (dragon.Timer2 > 3f) { dragon.Timer2 = 0; return true; }
            return false;
        }

        bool NearHeat(Body b)
        {
            foreach (var h in Bodies)
            {
                if (h == b || !h.Active || !h.Has(Quirk.Hot)) continue;
                float gx = Math.Max(0, Math.Abs(h.Pos.x - b.Pos.x) - (h.Half.x + b.Half.x));
                float gy = Math.Max(0, Math.Abs(h.Pos.y - b.Pos.y) - (h.Half.y + b.Half.y));
                if (Math.Max(gx, gy) > SimConst.HeatRange) continue;
                if (HeatBlocked(b, h)) continue;
                return true;
            }
            return false;
        }

        bool HeatBlocked(Body a, Body b)
        {
            foreach (var s in Bodies)
            {
                if (s.Type != BodyType.Divider && s.Type != BodyType.Shelf) continue;
                if (s.Type == BodyType.Divider)
                {
                    float x = s.Pos.x;
                    float lo = Math.Min(a.Pos.x, b.Pos.x), hi = Math.Max(a.Pos.x, b.Pos.x);
                    if (x > lo && x < hi) return true;
                }
                else
                {
                    float y = s.Pos.y;
                    float lo = Math.Min(a.Pos.y, b.Pos.y), hi = Math.Max(a.Pos.y, b.Pos.y);
                    float cx = (a.Pos.x + b.Pos.x) * 0.5f;
                    if (y > lo && y < hi && cx > s.MinX && cx < s.MaxX) return true;
                }
            }
            return false;
        }

        void CheckTopple(Body b)
        {
            if (!b.Supported && b.TripDir == 0) return;
            var g = AEff;
            bool down = g.y <= 0;
            float vert = Math.Abs(g.y);
            float lat = g.x;
            float wh = b.Def.W / (float)b.Def.H;
            int dir = 0;
            if (b.Supported)
            {
                float ratio = Math.Abs(lat) / Math.Max(vert, 1e-3f);
                float mu = MathF.Sqrt(b.Friction * 0.6f);
                if (ratio > wh * 1.02f && mu >= wh * 0.85f) dir = lat > 0 ? 1 : -1;
            }
            if (dir == 0 && b.TripDir != 0) dir = b.TripDir;
            if (dir == 0) return;
            if (ShoulderSupported(b, dir, down)) return;
            Topple(b, dir, down);
        }

        bool ShoulderSupported(Body b, int dir, bool down)
        {
            float x0 = dir > 0 ? b.MaxX : b.MinX - 0.55f;
            float x1 = dir > 0 ? b.MaxX + 0.55f : b.MinX;
            float y0 = down ? b.Pos.y : b.MinY;
            float y1 = down ? b.MaxY : b.Pos.y;
            foreach (var o in Bodies)
            {
                if (o == b || !o.Active) continue;
                if (o.Overlaps(x0, y0, x1, y1, 0.01f)) return true;
            }
            return false;
        }

        bool AreaClear(Body self, float minX, float minY, float maxX, float maxY)
        {
            if (minX < 0 || maxX > W || minY < 0 || maxY > H) return false;
            foreach (var o in Bodies)
            {
                if (o == self || !o.Active) continue;
                if (o.Overlaps(minX, minY, maxX, maxY, 0.01f)) return false;
            }
            return true;
        }

        void Topple(Body b, int dir, bool down)
        {
            var nh = new V2(b.Half.y + InsetY - InsetX, b.Half.x + InsetX - InsetY);
            float pivotX = dir > 0 ? b.MaxX : b.MinX;
            float baseY = down ? b.MinY : b.MaxY;
            bool placed = false;
            V2 np = b.Pos;
            for (int sy = 0; sy <= 40 && !placed; sy++)
            {
                float dy = sy * 0.05f;
                for (int sx = 0; sx <= 24 && !placed; sx++)
                {
                    float shift = sx * 0.05f;
                    float cx = dir > 0 ? pivotX - shift + nh.x : pivotX + shift - nh.x;
                    float cy = down ? baseY + nh.y + dy : baseY - nh.y - dy;
                    if (AreaClear(b, cx - nh.x, cy - nh.y, cx + nh.x, cy + nh.y))
                    {
                        np = new V2(cx, cy);
                        placed = true;
                    }
                }
            }
            b.State |= BodyState.Toppled;
            b.ToppleTick = Tick - RouteStartTick;
            b.ToppleDir = dir;
            if (placed)
            {
                b.Pos = np;
                b.Half = nh;
                b.Vel = new V2(dir * 1.2f, 0);
            }
            AddIncident(b, IncidentKind.Toppled, 0, 0, -1, !b.Def.Has(Quirk.Upright) ? false : true);
            if (b.Def.Has(Quirk.Upright))
            {
                b.State |= BodyState.Spilled;
                AddIncident(b, IncidentKind.Spilled, 0, 0, -1, true);
            }

            // falling over is never "handled with care"
            if (b.PeakJoltRatio < 0.75f)
            {
                b.PeakJoltRatio = 0.75f;
                b.PeakTick = Tick - RouteStartTick; b.PeakLeg = CurrentLeg; b.PeakEvent = CurrentEvent;
            }

            // falling over is itself a hit, softened by whatever it lands on
            float h = 1f;
            float probeMinY = down ? b.MinY - 0.12f : b.MaxY;
            float probeMaxY = down ? b.MinY : b.MaxY + 0.12f;
            if (probeMinY <= 0.001f || probeMaxY >= H - 0.001f) h = SimConst.WallHardness;
            else
            {
                float best = -1;
                foreach (var o in Bodies)
                {
                    if (o == b || !o.Active) continue;
                    if (o.Overlaps(b.MinX, probeMinY, b.MaxX, probeMaxY, 0.01f)) best = Math.Max(best, o.Hardness);
                }
                h = best < 0 ? 1f : best;
            }
            h = Math.Min(h, b.Hardness);
            float fallJolt = 9f * h;
            b.LastJolt = Math.Max(b.LastJolt, fallJolt);
            if (fallJolt > b.PeakJolt) b.PeakJolt = fallJolt;
            if (b.Def.JoltLimit > 0 && fallJolt > b.Def.JoltLimit && !b.Is(BodyState.Broken))
                Break(b, fallJolt, b.Def.JoltLimit);
        }

        void Sneeze(Body d)
        {
            d.State |= BodyState.Sneezing;
            d.SneezeTick = Tick;
            d.Cooldown = 2.2f;
            AddIncident(d, IncidentKind.Sneezed, 0, 0, -1, false);
            float x0 = d.Facing > 0 ? d.MaxX : d.MinX;
            float x1 = x0 + d.Facing * SimConst.FlameLength;
            float minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
            float minY = d.Pos.y - 0.42f, maxY = d.Pos.y + 0.42f;
            var hits = new List<Body>();
            foreach (var o in Bodies)
            {
                if (o == d || !o.Active) continue;
                if (o.Overlaps(minX, minY, maxX, maxY, 0.02f)) hits.Add(o);
            }
            hits.Sort((p, q) =>
            {
                float dp = d.Facing > 0 ? p.MinX - x0 : x0 - p.MaxX;
                float dq = d.Facing > 0 ? q.MinX - x0 : x0 - q.MaxX;
                int c = dp.CompareTo(dq);
                return c != 0 ? c : p.Index.CompareTo(q.Index);
            });
            float reach = SimConst.FlameLength;
            foreach (var o in hits)
            {
                float dist = d.Facing > 0 ? o.MinX - x0 : x0 - o.MaxX;
                if (!o.IsPiece)
                {
                    // cardboard walls, dividers and shelves catch fire: the box is ruined
                    reach = Math.Max(0, dist);
                    if (!d.Is(BodyState.Scorched))
                    {
                        d.State |= BodyState.Scorched;
                        AddIncident(d, IncidentKind.BoxScorched, dist, SimConst.FlameLength, o.Index, true);
                    }
                    break;
                }
                var k = o.Def.Kind;
                if (k == PieceKind.Paper || k == PieceKind.Bubble)
                {
                    o.State |= BodyState.Burned | BodyState.Removed;
                    AddIncident(o, IncidentKind.Burned, 0, 0, d.Index, false);
                    continue;
                }
                if (k == PieceKind.Balloon) { Pop(o, d.Index, 0); continue; }
                if (o.Def.Has(Quirk.Melts)) { Melt(o, d.Index); reach = Math.Max(0, dist); break; }
                if (o.Def.Has(Quirk.Flammable) && !o.Is(BodyState.Scorched))
                {
                    o.State |= BodyState.Scorched;
                    AddIncident(o, IncidentKind.Scorched, 0, 0, d.Index, true);
                }
                reach = Math.Max(0, dist);
                break;
            }
            d.FlameReach = reach;
            if (!d.Fixed)
            {
                d.Vel = d.Vel + new V2(-d.Facing * 4f, 0);
            }
        }

        // ---- Damage helpers ------------------------------------------------------------------

        void Break(Body b, float value, float limit)
        {
            b.State |= BodyState.Broken;
            AddIncident(b, IncidentKind.Broke, value, limit, LastOther(b), true);
        }

        void Squish(Body b, float value, float limit)
        {
            b.State |= BodyState.Squished;
            AddIncident(b, IncidentKind.Squished, value, limit, LastOther(b), true);
        }

        void PopBubble(Body b, float value, float limit)
        {
            if (b.Is(BodyState.Popped)) return;
            b.State |= BodyState.Popped;
            b.Hardness = 0.75f;
            AddIncident(b, IncidentKind.Popped, value, limit, -1, false);
        }

        void Pop(Body b, int other, float value)
        {
            if (b.Is(BodyState.Popped)) return;
            b.State |= BodyState.Popped | BodyState.Removed;
            AddIncident(b, IncidentKind.Popped, value, b.Def.JoltLimit, other, true);
        }

        void Wake(Body b, int other, float value)
        {
            b.State |= BodyState.Awake;
            b.Timer2 = 0.2f;
            AddIncident(b, IncidentKind.Woke, value, b.Def.WakeLimit, other, true);
        }

        void Melt(Body b, int other)
        {
            if (b.Is(BodyState.Melted)) return;
            b.State |= BodyState.Melted | BodyState.Removed;
            AddIncident(b, IncidentKind.Melted, b.MeltTime, SimConst.MeltTime, other, true);
        }

        int LastOther(Body b)
        {
            // the piece pushing hardest on b this tick (for "crushed by the bowling ball")
            int best = -1;
            float bestP = 0;
            foreach (var c in contacts)
            {
                if (c.A != b.Index && c.B != b.Index) continue;
                int o = c.A == b.Index ? c.B : c.A;
                if (c.Pn > bestP) { bestP = c.Pn; best = o; }
            }
            return best;
        }

        public void AddIncident(Body b, IncidentKind kind, float value, float limit, int other, bool failure)
        {
            Incidents.Add(new Incident
            {
                Tick = Tick - RouteStartTick, Time = Time, Body = b.Index, Other = other, Kind = kind,
                Value = value, Limit = limit, Leg = CurrentLeg, Event = CurrentEvent, Where = b.Pos,
                IsFailure = failure && b.IsPiece && !b.Def.IsPadding,
            });
        }

        public ulong StateHash()
        {
            ulong h = SimMathUtil.HashSeed;
            foreach (var b in Bodies)
            {
                h = SimMathUtil.Hash(h, b.Pos.x);
                h = SimMathUtil.Hash(h, b.Pos.y);
                h = SimMathUtil.Hash(h, b.Vel.x);
                h = SimMathUtil.Hash(h, b.Vel.y);
                h = SimMathUtil.Hash(h, (int)b.State);
            }
            return h;
        }
    }
}
