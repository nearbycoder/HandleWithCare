using HWC.Sim;
using UnityEngine;

namespace HWC.Visuals
{
    /// <summary>The game's colour language.</summary>
    public static class Palette
    {
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c);
            return c;
        }

        public static readonly Color Kraft = Hex("C8955A");
        public static readonly Color KraftDark = Hex("A8743F");
        public static readonly Color KraftLight = Hex("DDB27C");
        public static readonly Color Cream = Hex("F3E9D2");
        public static readonly Color Paper = Hex("FBF6EA");
        public static readonly Color PostalRed = Hex("D9483B");
        public static readonly Color PostalRedDark = Hex("A8322A");
        public static readonly Color Teal = Hex("2F8F8B");
        public static readonly Color TealDark = Hex("1F6563");
        public static readonly Color Ink = Hex("1F2A44");
        public static readonly Color InkSoft = Hex("3B4766");
        public static readonly Color Sticky = Hex("F6D55C");
        public static readonly Color Good = Hex("4CAF6E");
        public static readonly Color Bad = Hex("E0503F");
        public static readonly Color Gold = Hex("F2B632");
        public static readonly Color Wood = Hex("9C6B43");
        public static readonly Color WoodDark = Hex("6E4528");
        public static readonly Color Wall = Hex("E9D8BC");

        public static Color ItemColor(PieceKind k)
        {
            switch (k)
            {
                case PieceKind.Paper: return Hex("EFE6D2");
                case PieceKind.Bubble: return Hex("CFE8F5");
                case PieceKind.Foam: return Hex("8FB7C9");
                case PieceKind.Teacup: return Hex("F7F3EC");
                case PieceKind.Books: return Hex("B8423A");
                case PieceKind.Teddy: return Hex("B27A4A");
                case PieceKind.Vase: return Hex("3D8FA8");
                case PieceKind.BowlingBall: return Hex("4B2E6E");
                case PieceKind.Armadillo: return Hex("9E8F7A");
                case PieceKind.Magnet: return Hex("D9483B");
                case PieceKind.Potion: return Hex("8E4FC4");
                case PieceKind.Cake: return Hex("F4A7B9");
                case PieceKind.Balloon: return Hex("E8443A");
                case PieceKind.Cactus: return Hex("5E9E4A");
                case PieceKind.Robot: return Hex("A9B4BE");
                case PieceKind.IceSwan: return Hex("BFE6F2");
                case PieceKind.LavaLamp: return Hex("F28A2E");
                case PieceKind.BouncyBall: return Hex("2FC4B2");
                case PieceKind.SnowGlobe: return Hex("DDEFF7");
                case PieceKind.Frog: return Hex("6DBF4A");
                case PieceKind.Dragon: return Hex("D64B3A");
                case PieceKind.DragonEgg: return Hex("7A5BA6");
            }
            return Color.magenta;
        }
    }
}
