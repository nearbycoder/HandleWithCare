using System;
using System.Linq;
using HWC.Sim;

/// <summary>
/// Where no careless sample turns up: how close random rearrangements of her packing come to rattling. Of
/// the ones that arrive under par, the highest care any item reaches (the care star needs it under 65%).
/// </summary>
static class CarelessScan
{
    public static string Explain(LevelDef lv, int tries = 300)
    {
        uint state = lv.Seed * 2246822519u + 4242u;
        int Next(int n) { state = state * 1664525u + 1013904223u; return (int)((state >> 8) % (uint)n); }
        var src = Hints.Source(lv);
        int arrived = 0, failed = 0;
        float best = 0f;
        for (int a = 0; a < tries; a++)
        {
            var pk = src.Clone();
            int steps = 1 + Next(4);
            for (int s = 0; s < steps; s++)
            {
                var items = Enumerable.Range(0, pk.Pieces.Count).Where(i => !pk.Pieces[i].Def.IsPadding).ToList();
                var pads = Enumerable.Range(0, pk.Pieces.Count).Where(i => pk.Pieces[i].Def.IsPadding).ToList();
                int op = Next(3);
                if (op == 0 && pads.Count > 0) pk.Pieces.RemoveAt(pads[Next(pads.Count)]);
                else if (op == 1 && pk.Dividers.Count + pk.Shelves.Count > 0)
                {
                    int j = Next(pk.Dividers.Count + pk.Shelves.Count);
                    if (j < pk.Dividers.Count) pk.Dividers.RemoveAt(j); else pk.Shelves.RemoveAt(j - pk.Dividers.Count);
                }
                else if (items.Count > 0)
                {
                    int i = items[Next(items.Count)];
                    var p = pk.Pieces[i];
                    p.X = Next(pk.W); p.Y = Next(pk.H);
                    if (pk.CanPlace(p, i)) pk.Pieces[i] = p;
                }
            }
            if (pk.Validate(lv) != null) continue;
            var o = Simulator.Run(lv, pk, false).Outcome;
            if (!o.UnderBudget) continue;
            if (!o.Delivered) { failed++; continue; }
            arrived++;
            best = Math.Max(best, o.WorstCare);
        }
        return $"of {arrived + failed} random rearrangements under par, {failed} fail and {arrived} arrive, the closest at {best * 100:0}%";
    }
}
