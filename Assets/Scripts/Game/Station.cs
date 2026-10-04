using System.Collections.Generic;
using HWC.Sim;
using HWC.Visuals;
using UnityEngine;

namespace HWC.Gameplay
{
    /// <summary>The packing bench: workbench, back wall, the box, and the shelf of items to pack.</summary>
    public sealed class Station : MonoBehaviour
    {
        public BoxView Box;
        public Transform ItemShelf;
        public readonly List<Vector3> ShelfSlots = new List<Vector3>();
        GameObject env;
        Light lamp;

        public static Station Create(Transform parent)
        {
            var go = new GameObject("Station");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<Station>();
            s.BuildEnvironment();
            return s;
        }

        void BuildEnvironment()
        {
            env = ModelLibrary.Spawn("station", transform);
            if (env == null)
            {
                env = new GameObject("station_placeholder");
                env.transform.SetParent(transform, false);
                // bench top at y = 0
                MeshGen.Make("bench", MeshGen.RoundedBox(new Vector3(7f, 0.12f, 1.6f), 0.02f), Mat.Lit(Palette.Wood, 0.35f), env.transform, new Vector3(0, -0.06f, 0.2f));
                MeshGen.Make("benchFront", MeshGen.RoundedBox(new Vector3(7f, 0.9f, 0.1f), 0.02f), Mat.Lit(Palette.WoodDark, 0.3f), env.transform, new Vector3(0, -0.55f, -0.58f));
                MeshGen.Make("wall", MeshGen.RoundedBox(new Vector3(9f, 5f, 0.1f), 0.01f), Mat.Lit(Palette.Wall, 0.1f), env.transform, new Vector3(0, 2.2f, 1.0f));
                MeshGen.Make("floor", MeshGen.RoundedBox(new Vector3(12f, 0.1f, 8f), 0.01f), Mat.Lit(Palette.Hex("5B4636"), 0.2f), env.transform, new Vector3(0, -1.05f, 0f));
            }
            var lampGo = new GameObject("lamp");
            lampGo.transform.SetParent(transform, false);
            lampGo.transform.localPosition = new Vector3(-1.5f, 0.65f, 0.25f);
            lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = Palette.Hex("FFD3A0");
            lamp.intensity = 1.6f;
            lamp.range = 3.5f;
            lamp.shadows = LightShadows.None;

            ItemShelf = new GameObject("itemShelf").transform;
            ItemShelf.SetParent(transform, false);
        }

        public void SetupFor(LevelDef lv)
        {
            LightingPreset.For(null).Apply(Game.I.Sun, Game.I.Rig.Cam);
            if (Box == null || Box.W != lv.W || Box.H != lv.H)
            {
                if (Box != null) Destroy(Box.gameObject);
                Box = BoxView.Create(transform, lv.W, lv.H);
            }
            Box.transform.SetParent(transform, false);
            Box.transform.localPosition = new Vector3(0, Box.OuterHalfHeight, 0);
            Box.transform.localRotation = Quaternion.identity;
            Box.gameObject.SetActive(true);
            Box.ShowFront(false);
            Box.SetFlaps(0f, true);
            Box.SetTape(0f, true);
            Box.ShowGrid(Game.I.Save.ShowGrid);
            BuildShelf(lv);
            Frame(lv, true);
        }

        const float Slot = 0.62f;

        void BuildShelf(LevelDef lv)
        {
            foreach (Transform c in ItemShelf) Destroy(c.gameObject);
            ShelfSlots.Clear();
            float boxRight = lv.W * BoxView.Cell * 0.5f + BoxView.Wall;
            int n = lv.Items.Length;
            int cols = n > 3 ? 2 : 1;
            int rows = (n + cols - 1) / cols;
            ItemShelf.localPosition = new Vector3(boxRight + 0.3f, 0.035f, 0.05f);
            var model = ModelLibrary.Spawn($"itemshelf_{cols}x{rows}", ItemShelf);
            if (model == null)
            {
                var wood = Mat.Lit(Palette.Wood, 0.3f);
                for (int r = 0; r <= rows; r++)
                    MeshGen.Make("board", MeshGen.RoundedBox(new Vector3(cols * Slot + 0.06f, 0.035f, 0.42f), 0.01f), wood, ItemShelf, new Vector3(cols * Slot * 0.5f, r * Slot - 0.0175f, 0));
            }
            for (int i = 0; i < n; i++)
            {
                int r = rows - 1 - i / cols, c = i % cols;
                ShelfSlots.Add(ItemShelf.TransformPoint(new Vector3(c * Slot + Slot * 0.5f, r * Slot, -0.03f)));
            }
        }

        public void Frame(LevelDef lv, bool snap)
        {
            var rig = Game.I.Rig;
            rig.Anchor = Vector3.zero;
            float boxW = lv.W * BoxView.Cell, boxH = lv.H * BoxView.Cell;
            int n = lv.Items.Length;
            int cols = n > 3 ? 2 : 1;
            int rows = (n + cols - 1) / cols;
            float shelfW = cols * Slot + 0.36f;
            float left = -boxW * 0.5f - 0.15f, right = boxW * 0.5f + shelfW + 0.12f;
            float top = Mathf.Max(boxH + 0.3f, rows * Slot + 0.12f), bottom = -0.42f;
            float cx = (left + right) * 0.5f, cy = (top + bottom) * 0.5f;
            float width = right - left, height = top - bottom;
            float fov = 24f;
            float aspect = Mathf.Max(1.2f, rig.Cam.aspect);
            float distH = height * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float distW = width * 0.5f / (Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect);
            float dist = Mathf.Max(distH, distW) * 1.18f + 0.3f;
            var target = new Vector3(cx, cy + 0.05f, 0);
            var pos = target + new Vector3(0, dist * 0.2f, -dist);
            rig.LookAt(pos, target, fov);
            rig.PosSharpness = 5f;
            if (snap) rig.Snap();
        }

        /// <summary>Title backdrop: a sealed parcel on the bench, the shelf hidden.</summary>
        public void ShowTitle()
        {
            LightingPreset.For(null).Apply(Game.I.Sun, Game.I.Rig.Cam);
            if (Box == null || Box.W != 4 || Box.H != 3)
            {
                if (Box != null) Destroy(Box.gameObject);
                Box = BoxView.Create(transform, 4, 3);
            }
            foreach (var p in Box.Pieces) if (p != null) Destroy(p.gameObject);
            Box.Pieces.Clear();
            Box.ClearStatics();
            Box.transform.SetParent(transform, false);
            Box.transform.localPosition = new Vector3(0.55f, Box.OuterHalfHeight, -0.05f);
            Box.transform.localRotation = Quaternion.Euler(0, -18f, 0);
            Box.ShowGrid(false);
            Box.ShowFront(true);
            Box.SetFlaps(1f, true);
            Box.SetTapeStyle(Game.I.Save.Tape);
            Box.SetTape(1f, true);
            foreach (Transform c in ItemShelf) Destroy(c.gameObject);
            var rig = Game.I.Rig;
            rig.Anchor = Vector3.zero;
            rig.LookAt(new Vector3(-0.25f, 1.05f, -2.6f), new Vector3(0.15f, 0.42f, 0.1f), 30f);
            rig.Snap();
            rig.PosSharpness = 1.5f;
        }

        public Vector3 SlotPosition(int i) => i < ShelfSlots.Count ? ShelfSlots[i] : ItemShelf.position;
    }
}
