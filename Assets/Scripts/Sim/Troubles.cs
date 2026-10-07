using System.Collections.Generic;

namespace HWC.Sim
{
    /// <summary>A moment of a trip worth jumping to: a failure, or a near miss that cost the care star.</summary>
    public struct Trouble
    {
        public float Time;
        public int Frame;           // the recorded frame it shows in
        public int Body;
        public bool Failure;        // false: a near miss (the item arrived, but went past the care line here)
        public float Care;          // the item's care after this knock (near misses)
        public V2 Where;            // where the item was, in cells
    }

    /// <summary>
    /// The troubles of a trip, for the replay's timeline, NEXT TROUBLE and the bench's marks: every failure,
    /// and every near miss. A near miss is a knock that took an item that still arrived past the 65% line
    /// of the "handled with care" star, or higher once past it; knocks less than a second apart count as
    /// one (counted from its first frame). The care value only ever goes up, so the last near miss of an item
    /// is its worst knock, the one the LAST TRIP report names. Pure C#, so SimCheck can check it.
    /// </summary>
    public static class Troubles
    {
        /// <summary>Near misses of one item closer together than this are one knock.</summary>
        public const float MergeSeconds = 1f;
        const float FrameSeconds = SimConst.FrameEvery * SimConst.Dt;

        /// <summary>Every trouble of the trip, in time order (failures and near misses).</summary>
        public static List<Trouble> Of(Recording rec)
        {
            var list = new List<Trouble>();
            if (rec == null) return list;
            foreach (var inc in rec.Incidents)
            {
                if (!inc.IsFailure) continue;
                list.Add(new Trouble { Time = inc.Time, Frame = Recording.FrameOfTick(inc.Tick), Body = inc.Body, Failure = true, Where = inc.Where });
            }
            list.AddRange(NearMisses(rec));
            list.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Body.CompareTo(b.Body));
            return list;
        }

        /// <summary>The near misses of a trip, item by item (each item's in time order).</summary>
        public static List<Trouble> NearMisses(Recording rec)
        {
            var list = new List<Trouble>();
            if (rec == null || rec.Outcome == null || rec.Frames.Count == 0) return list;
            int mergeFrames = (int)(MergeSeconds / FrameSeconds + 0.5f);
            foreach (var it in rec.Outcome.Items)
            {
                if (it.Failed || it.Care < SimConst.CareFraction) continue;
                int b = it.Body, startFrame = int.MinValue, open = -1;
                float prev = 0f;
                for (int f = 0; f < rec.Frames.Count; f++)
                {
                    var fr = rec.Frames[f][b];
                    float care = fr.Care;
                    if (care > prev && care >= SimConst.CareFraction)
                    {
                        if (open >= 0 && f - startFrame <= mergeFrames)
                        {
                            // the same knock: it counts from its first frame, at its worst
                            var t = list[open]; t.Care = care; list[open] = t;
                        }
                        else
                        {
                            open = list.Count;
                            startFrame = f;
                            list.Add(new Trouble { Time = Recording.TimeOfFrame(f), Frame = f, Body = b, Failure = false, Care = care, Where = fr.Pos });
                        }
                    }
                    prev = care;
                }
            }
            return list;
        }

        /// <summary>The first trouble of one item (its failure or its first near miss), or null.</summary>
        public static Trouble? FirstOf(Recording rec, int body)
        {
            foreach (var t in Of(rec)) if (t.Body == body) return t;
            return null;
        }

        /// <summary>
        /// For SimCheck: null when the trip's near misses follow the rules above, otherwise what's wrong: each
        /// item that arrived at 65% or more has at least one, the others have none, each is at a frame where the
        /// item's care rose to the line or above, and the last one shows the item's peak knock.
        /// </summary>
        public static string Check(Recording rec)
        {
            var near = NearMisses(rec);
            foreach (var it in rec.Outcome.Items)
            {
                var mine = near.FindAll(t => t.Body == it.Body);
                bool want = !it.Failed && it.Care >= SimConst.CareFraction;
                if (!want) { if (mine.Count > 0) return $"{it.Kind} ({it.Status}, {it.Care:0.00}) has {mine.Count} near miss(es)"; continue; }
                if (mine.Count == 0) return $"{it.Kind} arrived at {it.Care:0.00} with no near miss";
                foreach (var t in mine)
                {
                    float c = rec.Frames[t.Frame][it.Body].Care, before = t.Frame > 0 ? rec.Frames[t.Frame - 1][it.Body].Care : 0f;
                    if (c < SimConst.CareFraction || c <= before) return $"{it.Kind}'s near miss at {t.Time:0.00}s isn't a knock past the line ({before:0.00} -> {c:0.00})";
                }
                var last = mine[mine.Count - 1];
                if (last.Care != it.Care) return $"{it.Kind}'s last near miss reaches {last.Care:0.000}, its peak is {it.Care:0.000}";
                // the peak tick (what the report names) falls within the last knock
                float peakAt = it.PeakTick * SimConst.Dt;
                if (it.PeakTick >= 0 && (peakAt < last.Time - FrameSeconds * 1.5f || peakAt > last.Time + MergeSeconds + FrameSeconds * 1.5f))
                    return $"{it.Kind}'s last near miss is at {last.Time:0.00}s, its peak knock at {peakAt:0.00}s";
            }
            return null;
        }
    }
}
