using System.Collections.Generic;
using HWC.Sim;
using HWC.UI;
using TMPro;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>
    /// The item card remembers the last trip: pointing at an item (on the shelf or in the box) or at a red
    /// cross on the bench adds what happened to it, in the LAST TRIP report's words, and its trails and
    /// crosses stand out.
    /// </summary>
    public sealed partial class Hud
    {
        const float CardH = 250f, CardTripH = 56f, CardY = 10f;
        TextMeshProUGUI cardTrip;

        /// <summary>The last trip shown on the bench, when it was this delivery's.</summary>
        Recording BenchTrip => G.Packing.LastRunShown != null && G.Packing.LastRunShown.Level == G.Packing.Level ? G.Packing.LastRunShown : null;

        void FillCardTrip(PieceKind? kind, int body)
        {
            var rec = BenchTrip;
            if (rec != null && body < 0 && kind.HasValue) body = WorstBody(rec, kind.Value);
            string line = rec != null && body >= 0 ? TripLine(rec, body, out _) : null;
            bool show = line != null;
            cardTrip.gameObject.SetActive(show);
            cardTrip.rectTransform.anchoredPosition = new Vector2(18, -238);   // where the route card may have moved it
            cardTrip.rectTransform.sizeDelta = new Vector2(344, CardTripH);
            // what you can do with it, at the bottom of the card
            float h = show ? CardH + CardTripH : CardH;
            if (cardDoH > 0f)
            {
                cardDo.rectTransform.anchoredPosition = new Vector2(18, -(h - 6f));
                h += cardDoH;
            }
            PlaceItemCard(h);
            if (show)
            {
                TripLine(rec, body, out var tone);
                cardTrip.text = $"<b>LAST TRIP</b>  <color={tone}>{line}</color>";
            }
            G.Packing.HighlightTrip(show ? kind : null, G.Packing.HoverTroubleBody);
        }

        /// <summary>
        /// The card's top stays where it always was (a little above the middle of the left side), and the line
        /// grows it downwards; but never under the LAST TRIP report, which is drawn over it (it used to hide the
        /// card's name on a retry).
        /// </summary>
        void PlaceItemCard(float height)
        {
            float canvasH = ((RectTransform)root).rect.height;
            float top = canvasH * 0.5f - CardY - CardH * 0.5f;
            if (lastTrip.gameObject.activeSelf) top = Mathf.Max(top, -lastTrip.anchoredPosition.y + lastTrip.sizeDelta.y + 12f);
            top = Mathf.Min(top, canvasH - height - 20f);
            itemCard.anchorMin = itemCard.anchorMax = new Vector2(0, 1);
            itemCard.pivot = new Vector2(0, 1);
            itemCard.anchoredPosition = new Vector2(28, -top);
            itemCard.sizeDelta = new Vector2(380, height);
        }
        public RectTransform ItemCardRect => itemCard;

        // ---- what you can do with what the card shows (the controls are otherwise only in the README) ----

        TextMeshProUGUI cardDo;
        float cardDoH;

        void SetCardDo(string text)
        {
            bool on = !string.IsNullOrEmpty(text);
            cardDo.gameObject.SetActive(on);
            cardDoH = 0f;
            if (!on) return;
            cardDo.text = text;
            // as tall as the text needs (LARGER TEXT measures at its larger size), and a little air
            cardDo.rectTransform.sizeDelta = new Vector2(344, 200);
            float need = Mathf.Ceil(cardDo.GetPreferredValues(text, 344, 0).y);
            cardDo.rectTransform.sizeDelta = new Vector2(344, need + 2f);
            cardDoH = need + 12f;
        }

        /// <summary>For the self-tests: the item card's controls line as shown, and the sprites it draws.</summary>
        public string ItemCardDoText => itemCard.gameObject.activeInHierarchy && cardDo.gameObject.activeSelf ? cardDo.text : "";
        public string ItemCardDoDrawn => Glyphs.Drawn(cardDo);

        static string Key(string k) => $"<b>{k}</b>";

        /// <summary>Phrases joined by dots; a phrase never breaks across lines (the dot goes with the next one).</summary>
        static string Phrases(params string[] parts)
        {
            var kept = new List<string>();
            foreach (var p in parts) if (!string.IsNullOrEmpty(p)) kept.Add(p.Replace(' ', '\u00A0'));
            return string.Join("  \u00B7\u00A0\u00A0", kept);
        }

        /// <summary>The controls for an item or padding the card is about: on the shelf, in the box, in hand.</summary>
        string CardDoLine(PieceKind kind, PackingController.CardUse use, bool fromToolbar)
        {
            var def = Catalog.Get(kind);
            bool pad = PadPrompts;
            string P(string s) => PadGlyphs.Format(s);   // after Phrases: button names have no spaces
            if (fromToolbar)
                return def.IsPadding ? (pad ? P(Phrases("<b>{A}</b> takes some, then hold <b>{A}</b> in the box to paint")) : Phrases(Key("Click") + ", then drag in the box to paint")) : null;
            string turn = def.Has(Quirk.Facing) ? "face the other way" : def.Rotatable ? "turn" : null;
            string back = def.IsPadding ? "bin it" : "back to the shelf";
            switch (use)
            {
                case PackingController.CardUse.Shelf:
                    return pad ? P(Phrases("<b>{A}</b> pick it up")) : Phrases(Key("Click") + " to pick it up");
                case PackingController.CardUse.Placed:
                    return pad ? P(Phrases("<b>{A}</b> pick up", "<b>{B}</b> " + back)) : Phrases(Key("Click") + " pick up", Key("Right click") + " " + back);
                case PackingController.CardUse.HoldItem:
                    return pad ? P(Phrases("<b>{A}</b> drop", turn != null ? "<b>{X}</b> " + turn : null, "<b>{B}</b> back to the shelf"))
                               : Phrases(Key("Click") + " drop", turn != null ? Key(KeyLabel("R")) + " " + turn : null, Key("Esc") + " back to the shelf");
                case PackingController.CardUse.HoldPadding:
                    return pad ? P(Phrases("Hold <b>{A}</b> to paint", "hold <b>{B}</b> to erase", "<b>{START}</b> put it down"))
                               : Phrases(Key("Drag") + " to paint", Key("right drag") + " to erase", Key("Esc") + " put it down");
                case PackingController.CardUse.Strap:
                    return pad ? P(Phrases("<b>{A}</b> straps it down, or takes the strap off")) : Phrases(Key("Click") + " straps it down, or takes the strap off");
                default:
                    return null;
            }
        }

        /// <summary>Dividers, shelves and straps, pointed at on the toolbar.</summary>
        string ToolbarDoLine(MaterialSlot s)
        {
            string a = PadPrompts ? PadGlyphs.Format("<b>{A}</b>") : Key("Click");
            string again = PadPrompts ? PadGlyphs.Format("<b>{A}</b>") : "click";
            switch (s)
            {
                case MaterialSlot.Divider: return $"{a}, then a line in the box ({again} one to take it out)";
                case MaterialSlot.Shelf: return $"{a}, then a row in the box ({again} one to take it out)";
                case MaterialSlot.Strap: return $"{a}, then an item in the box";   // (prose: these wrap anywhere)
                default: return null;
            }
        }
        public RectTransform LastTripRect => lastTrip;

        /// <summary>The body of this kind the card speaks for: the first to fail, else the one closest to its
        /// limit (two magnets, two teacups). -1 when the trip had none.</summary>
        public static int WorstBody(Recording rec, PieceKind kind)
        {
            int best = -1; float bestFail = float.MaxValue, bestCare = -1f;
            foreach (var it in rec.Outcome.Items)
            {
                if (it.Kind != kind) continue;
                float failAt = it.Failed ? FirstFailure(rec, it.Body)?.Time ?? 0f : float.MaxValue;
                if (failAt < bestFail || (failAt == bestFail && it.Care > bestCare)) { best = it.Body; bestFail = failAt; bestCare = it.Care; }
            }
            return best;
        }

        static Incident? FirstFailure(Recording rec, int body)
        {
            Incident? first = null;
            foreach (var inc in rec.Incidents)
                if (inc.IsFailure && inc.Body == body && (first == null || inc.Time < first.Value.Time)) first = inc;
            return first;
        }

        /// <summary>What happened to one item on a trip, as the LAST TRIP report says it (without the name):
        /// "Shattered at the hard brake (jolt 13.7/9)", "Rattled: 72% at the pothole", "Perfect: 41% at the
        /// cobblestones". Null for a body that isn't an item.</summary>
        public static string TripLine(Recording rec, int body, out string tone)
        {
            tone = "#2E8B57";
            var it = rec.Outcome.Items.Find(i => i.Body == body);
            if (it == null) return null;
            if (it.Failed)
            {
                tone = "#B03A2E";
                var inc = FirstFailure(rec, body);
                if (inc == null) return Cap(StatusWord(it.Status).ToLowerInvariant());
                return Cap(IncidentLine(rec, inc.Value));
            }
            string at = it.PeakLeg >= 0 ? " at " + EventName(rec, it.PeakLeg, it.PeakEvent) : "";
            if (it.Care >= SimConst.CareFraction) { tone = "#B07A1A"; return $"Rattled: {it.Care * 100:0}%{at}"; }
            return it.Care < 0.005f ? "Perfect: not a scratch" : $"Perfect: {it.Care * 100:0}%{at}";
        }

        /// <summary>A failure as the report words it, after the item's name: "shattered at the hard brake (jolt 13.7/9)".</summary>
        static string IncidentLine(Recording rec, Incident inc)
        {
            string how = inc.Kind == IncidentKind.Stuck ? "stuck to the other magnet" : StatusWordForIncident(inc.Kind).ToLowerInvariant();
            string detail = inc.Limit > 0 && (inc.Kind == IncidentKind.Broke || inc.Kind == IncidentKind.Woke || inc.Kind == IncidentKind.Squished) ? $" (jolt {inc.Value:0.#}/{inc.Limit:0.#})" : "";
            return $"{how} at {EventName(rec, inc.Leg, inc.Event)}{detail}";
        }

        // ---- for the self-tests ----------------------------------------------------------------------------
        public bool ItemCardShown => itemCard.gameObject.activeInHierarchy;
        public string ItemCardName => cardName.text;
        public string ItemCardStats => cardStats.text;
        public string ItemCardTrip => cardTrip.gameObject.activeSelf ? cardTrip.text : "";
    }
}
