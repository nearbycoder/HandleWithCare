using System.Text;
using HWC.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

namespace HWC.Gameplay
{
    /// <summary>
    /// Gamepad prompts in the pad's own language. Prompts are written with Xbox names ("A", "LB",
    /// "VIEW"); with a PlayStation pad they become the drawn shapes (cross, circle, square, triangle)
    /// and L1/R1, L2/R2, OPTIONS and SHARE (DualShock 4) or CREATE (DualSense). The BUTTON ICONS
    /// setting picks a style for pads that don't say what they are.
    /// </summary>
    public static class PadGlyphs
    {
        public const int Auto = 0, Xbox = 1, PlayStation = 2;
        public static readonly string[] SettingNames = { "AUTO", "XBOX", "PLAYSTATION" };

        public static bool Ps { get; private set; }
        public static bool DualSense { get; private set; }
        /// <summary>Goes up whenever the style changes, so prompts know to redraw.</summary>
        public static int Version { get; private set; }
        static int seenDevice = -1, seenSetting = -1;

        /// <summary>Called every frame: follows the pad in use and the setting.</summary>
        public static void Refresh(int setting)
        {
            var pad = Gamepad.current;
            int id = pad != null ? pad.deviceId : 0;
            if (id == seenDevice && setting == seenSetting) return;
            seenDevice = id; seenSetting = setting;
            bool sony = IsPlayStation(pad, out bool dualSense);
            bool ps = setting == PlayStation || (setting == Auto && sony);
            bool ds = ps && sony && dualSense;
            if (ps == Ps && ds == DualSense) return;
            Ps = ps; DualSense = ds;
            Version++;
        }

        /// <summary>
        /// A DualShock or DualSense: the Input System's own layouts, or (for drivers that report a plain
        /// gamepad) Sony's USB vendor id or name in the device description.
        /// </summary>
        public static bool IsPlayStation(Gamepad pad, out bool dualSense)
        {
            dualSense = false;
            if (pad == null) return false;
            var d = pad.description;
            string all = (pad.GetType().Name + " " + pad.layout + " " + d.manufacturer + " " + d.product).ToLowerInvariant();
            dualSense = all.Contains("dualsense") || all.Contains("ps5");
            if (pad is DualShockGamepad) return true;
            if (all.Contains("dualsense") || all.Contains("dualshock") || all.Contains("playstation") || all.Contains("sony")) return true;
            string caps = d.capabilities ?? "";
            return caps.Contains("\"vendorId\":1356");   // 0x054C, Sony
        }

        /// <summary>One prompt written with Xbox names, "A" or "A / B", in the current style.</summary>
        public static string Label(string xbox)
        {
            if (!Ps || string.IsNullOrEmpty(xbox)) return xbox;
            var parts = xbox.Split(new[] { " / " }, System.StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++) parts[i] = Token(parts[i]);
            return string.Join(" / ", parts);
        }

        /// <summary>Prose with buttons in braces ("press {A}", "{View} to seal"), in the current style.</summary>
        public static string Format(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            var sb = new StringBuilder(text.Length + 32);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open >= 0 ? text.IndexOf('}', open + 1) : -1;
                if (open < 0 || close < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, open - i);
                string tok = text.Substring(open + 1, close - open - 1);
                sb.Append(Ps ? Token(tok) : tok);
                i = close + 1;
            }
            return sb.ToString();
        }

        static string Token(string tok)
        {
            bool upper = tok == tok.ToUpperInvariant();
            string Word(string w) => upper ? w.ToUpperInvariant() : w;
            switch (tok.ToUpperInvariant())
            {
                case "A": return Shape(Glyphs.PsCross, "Cross");
                case "B": return Shape(Glyphs.PsCircle, "Circle");
                case "X": return Shape(Glyphs.PsSquare, "Square");
                case "Y": return Shape(Glyphs.PsTriangle, "Triangle");
                case "LB": return "L1";
                case "RB": return "R1";
                case "LT": return "L2";
                case "RT": return "R2";
                case "VIEW": return Word(DualSense ? "Create" : "Share");
                case "START": return Word("Options");
                default: return tok;
            }
        }

        static string Shape(string sprite, string fallback) => Glyphs.Ok ? Glyphs.Tag(sprite) : fallback;
    }
}
