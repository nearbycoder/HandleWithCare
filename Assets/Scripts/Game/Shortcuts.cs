using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace HWC.Gameplay
{
    /// <summary>
    /// Letter shortcuts by the label on the key, not its US position: on a German keyboard the key
    /// labelled Z is where US has Y. Uses the names the keyboard layout reports, and falls back to the
    /// US position when the platform reports none (or the layout has no such letter).
    /// </summary>
    public static class Shortcuts
    {
        /// <summary>Self-tests only: physical key → label, standing in for the platform's layout names.</summary>
        public static Dictionary<Key, string> TestLabels;

        static Keyboard cachedFor;
        static string cachedLayout;
        static Dictionary<Key, string> cachedTest;
        static readonly Dictionary<char, KeyControl> cache = new Dictionary<char, KeyControl>();

        static string LabelOf(KeyControl k) =>
            TestLabels != null && TestLabels.TryGetValue(k.keyCode, out var l) ? l : k.displayName;

        /// <summary>Counts layout changes, so hints can be redrawn.</summary>
        public static int Version { get; private set; }

        /// <summary>Notices a new keyboard or layout; true if it changed since the last call.</summary>
        public static bool Refresh(Keyboard kb)
        {
            if (kb == null) return false;
            string layout = kb.keyboardLayout;
            if (kb == cachedFor && layout == cachedLayout && TestLabels == cachedTest) return false;
            cache.Clear();
            cachedFor = kb; cachedLayout = layout; cachedTest = TestLabels;
            Version++;
            return true;
        }

        /// <summary>The key labelled with this letter (a–z), or the one in its US position.</summary>
        public static KeyControl For(Keyboard kb, char letter)
        {
            if (kb == null) return null;
            Refresh(kb);
            letter = char.ToLowerInvariant(letter);
            if (cache.TryGetValue(letter, out var key)) return key;
            string want = letter.ToString();
            foreach (var k in kb.allKeys)
            {
                if (k == null) continue;
                if (string.Equals(LabelOf(k), want, System.StringComparison.OrdinalIgnoreCase)) { key = k; break; }
            }
            if (key == null) key = kb[Key.A + (letter - 'a')];
            cache[letter] = key;
            return key;
        }

        public static bool Pressed(Keyboard kb, char letter)
        {
            var k = For(kb, letter);
            return k != null && k.wasPressedThisFrame;
        }

        /// <summary>What to print on a hint for this letter: the label of the key that does it.</summary>
        public static string Label(char letter)
        {
            var kb = Keyboard.current;
            var k = For(kb, letter);
            string l = k != null ? LabelOf(k) : null;
            return string.IsNullOrEmpty(l) || l.Length > 2 ? char.ToUpperInvariant(letter).ToString() : l.ToUpperInvariant();
        }

        /// <summary>One line in the player log: what the platform reports for this keyboard.</summary>
        public static void LogLayout()
        {
            var kb = Keyboard.current;
            if (kb == null) { Debug.Log("[Keys] no keyboard"); return; }
            Debug.Log($"[Keys] layout '{kb.keyboardLayout}': Z key labelled '{kb.zKey.displayName}', Y key '{kb.yKey.displayName}'; undo on {For(kb, 'z').keyCode}, redo on {For(kb, 'y').keyCode}");
        }
    }
}
