using System.Collections.Generic;
using HWC.Sim;
using HWC.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HWC.Gameplay
{
    /// <summary>
    /// The order card's ROUTE line says which way each knock throws the items: after each knock, a little box
    /// with an arrow into the wall it throws them against (<see cref="Knocks"/>). Pointing at the line shows a
    /// card that says so, with the side knocks listed by wall.
    /// </summary>
    public sealed partial class Hud
    {
        bool routeCardShown;

        static string RouteSummary(LevelDef lv)
        {
            var knocks = Knocks.Of(lv);
            var parts = new List<string>();
            for (int li = 0; li < lv.Route.Legs.Count; li++)
            {
                var leg = lv.Route.Legs[li];
                var evs = new List<string>();
                foreach (var k in knocks)
                    if (k.Leg == li) evs.Add(Glyphs.Ok ? $"{k.Name} {Glyphs.Tag(Glyphs.Knock(k.Wall))}" : k.Name);
                string legName = leg.Kind == LegKind.Van ? "Van" : leg.Kind == LegKind.Depot ? "Depot" : leg.Kind == LegKind.Doorstep ? "Doorstep" :
                                 leg.Kind == LegKind.Ship ? "Ferry" : leg.Kind == LegKind.Plane ? "Air mail" : "Catapult";
                parts.Add($"<b>{legName}</b> <color=#6A5A4A>({string.Join(", ", evs)})</color>");
            }
            return "<b>ROUTE</b>  " + string.Join("  →  ", parts);
        }

        /// <summary>The side knocks by wall, for the route card: "robot arm" on the left, "hard brake, chute"
        /// on the right; or that every knock is into the floor.</summary>
        static string RouteWalls(LevelDef lv)
        {
            var by = new Dictionary<KnockWall, List<string>>();
            foreach (var k in Knocks.Of(lv))
            {
                if (k.Wall == KnockWall.Floor) continue;
                if (!by.TryGetValue(k.Wall, out var l)) by[k.Wall] = l = new List<string>();
                if (!l.Contains(k.Name)) l.Add(k.Name);
            }
            if (by.Count == 0) return $"{Glyphs.Tag(Glyphs.KnockFloor)} Every knock on this route is into the floor.";
            var lines = new List<string>();
            foreach (var (w, label) in new[] { (KnockWall.Left, "left wall"), (KnockWall.Right, "right wall"), (KnockWall.Sides, "both sides") })
                if (by.TryGetValue(w, out var l)) lines.Add($"{Glyphs.Tag(Glyphs.Knock(w))} <b>{label}:</b> {string.Join(", ", l)}");
            lines.Add($"{Glyphs.Tag(Glyphs.KnockFloor)} <b>floor:</b> the rest");
            return string.Join("\n", lines);
        }

        void ShowRouteCard(bool on)
        {
            var lv = G.Packing.Level;
            if (!on || lv == null || !Glyphs.Ok)
            {
                if (routeCardShown) { routeCardShown = false; itemCard.gameObject.SetActive(false); }
                return;
            }
            routeCardShown = true;
            itemCard.gameObject.SetActive(true);
            FillCardTrip(null, -1);
            KnockWall icon = KnockWall.Floor;
            foreach (var k in Knocks.Of(lv)) if (k.Wall != KnockWall.Floor) { icon = k.Wall; break; }
            cardIcon.sprite = Glyphs.Sprite(Glyphs.Knock(icon));
            cardName.text = "THE ROUTE";
            cardBlurb.text = "Each knock's box shows the wall it throws your items into.";
            cardStats.text = "";
            string walls = RouteWalls(lv);
            cardTrip.gameObject.SetActive(true);
            cardTrip.text = walls;
            int lines = walls.Split('\n').Length;
            PlaceItemCard(CardH - 36f + lines * 28f + 8f);
            cardTrip.rectTransform.anchoredPosition = new Vector2(18, -200);
            cardTrip.rectTransform.sizeDelta = new Vector2(344, lines * 28f + 8f);
        }

        const float RouteH = 60f;

        /// <summary>Makes the ROUTE line's box as tall as its text needs at the normal size (LARGER TEXT then
        /// grows it only as far as that box allows); returns how much taller than usual it is.</summary>
        float FitRoute()
        {
            bool auto = routeText.enableAutoSizing;
            float size = routeText.fontSize;
            routeText.enableAutoSizing = false;
            routeText.fontSize = 18;
            float h = Mathf.Ceil(routeText.GetPreferredValues(routeText.text, 480, 0).y) + 2f;
            routeText.enableAutoSizing = auto;
            routeText.fontSize = size;
            float grow = Mathf.Max(0f, h - RouteH);
            routeText.rectTransform.sizeDelta = new Vector2(480, RouteH + grow);
            return grow;
        }

        /// <summary>For the self-tests: the ROUTE line's text and the sprites it really draws.</summary>
        public string RouteText => routeText.text;
        public string RouteDrawn => Glyphs.Drawn(routeText);
        public RectTransform RouteRect => routeText.rectTransform;
        public bool RouteCardShown => routeCardShown && itemCard.gameObject.activeInHierarchy;
        public string ItemCardTitle => cardName.text;
        public string ItemCardTripText => cardTrip.gameObject.activeInHierarchy ? cardTrip.text : "";
        public string ItemCardTripDrawn => Glyphs.Drawn(cardTrip);

        /// <summary>Pointing at the ROUTE line (mouse or the pad's cursor) shows the route card.</summary>
        sealed class RouteHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Hud Hud;
            public void OnPointerEnter(PointerEventData e) => Hud.ShowRouteCard(true);
            public void OnPointerExit(PointerEventData e) => Hud.ShowRouteCard(false);
            void OnDisable() { if (Hud != null) Hud.ShowRouteCard(false); }
        }

        void MakeRouteHover()
        {
            routeText.raycastTarget = true;
            routeText.gameObject.AddComponent<RouteHover>().Hud = this;
        }
    }
}
