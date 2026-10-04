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
            lampGo.transform.localPosition = new Vector3(-0.9f, 1.9f, -0.6f);
            lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = Palette.Hex("FFD3A0");
            lamp.intensity = 2.2f;
            lamp.range = 5f;
            lamp.shadows = LightShadows.None;

            ItemShelf = new GameObject("itemShelf").transform;
            ItemShelf.SetParent(transform, false);
        }

        public void SetupFor(LevelDef lv)
        {
            if (Box == null || Box.W != lv.W || Box.H != lv.H)
            {
                if (Box != null) Destroy(Box.gameObject);
                Box = BoxView.Create(transform, lv.W, lv.H);
            }
            Box.transform.SetParent(transform, false);
            Box.transform.localPosition = new Vector3(0, Box.OuterHalfHeight, 0);
            Box.transform.localRotation = Quaternion.identity;
            Box.gameObject.SetActive(true);
            Box.SetFlaps(0f, true);
            Box.SetTape(0f, true);
            Box.ShowGrid(Game.I.Save.ShowGrid);
            BuildShelf(lv);
            Frame(lv, true);
        }

        void BuildShelf(LevelDef lv)
        {
            foreach (Transform c in ItemShelf) Destroy(c.gameObject);
            ShelfSlots.Clear();
            float boxRight = lv.W * BoxView.Cell * 0.5f + BoxView.Wall;
            int n = lv.Items.Length;
            int cols = n > 3 ? 2 : 1;
            int rows = (n + cols - 1) / cols;
            float slotW = 0.62f, slotH = 0.62f;
            float x0 = boxRight + 0.32f;
            ItemShelf.localPosition = new Vector3(x0, 0, 0.05f);
            // wooden shelf boards
            var wood = Mat.Lit(Palette.Wood, 0.3f);
            for (int r = 0; r <= rows; r++)
            {
                MeshGen.Make("board", MeshGen.RoundedBox(new Vector3(cols * slotW + 0.06f, 0.035f, 0.42f), 0.01f), wood, ItemShelf,
                    new Vector3(cols * slotW * 0.5f - 0.03f, r * slotH - 0.0175f, 0));
            }
            for (int c = 0; c <= cols; c++)
            {
                MeshGen.Make("side", MeshGen.RoundedBox(new Vector3(0.035f, rows * slotH, 0.42f), 0.01f), wood, ItemShelf,
                    new Vector3(c * slotW - 0.03f, rows * slotH * 0.5f, 0));
            }
            MeshGen.Make("back", MeshGen.RoundedBox(new Vector3(cols * slotW, rows * slotH, 0.02f), 0.005f), Mat.Lit(Palette.WoodDark, 0.2f), ItemShelf,
                new Vector3(cols * slotW * 0.5f - 0.03f, rows * slotH * 0.5f, 0.2f));
            for (int i = 0; i < n; i++)
            {
                int r = rows - 1 - i / cols, c = i % cols;
                ShelfSlots.Add(ItemShelf.TransformPoint(new Vector3(c * slotW + slotW * 0.5f - 0.03f, r * slotH, -0.02f)));
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
            float shelfW = cols * 0.62f + 0.32f;
            float left = -boxW * 0.5f - 0.15f, right = boxW * 0.5f + shelfW + 0.12f;
            float top = Mathf.Max(boxH + 0.25f, rows * 0.62f + 0.1f), bottom = -0.32f;
            float cx = (left + right) * 0.5f, cy = (top + bottom) * 0.5f;
            float width = right - left, height = top - bottom;
            float fov = 24f;
            float aspect = Mathf.Max(1.2f, rig.Cam.aspect);
            float distH = height * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float distW = width * 0.5f / (Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect);
            float dist = Mathf.Max(distH, distW) * 1.18f + 0.3f;
            var target = new Vector3(cx, cy + 0.05f, 0);
            var pos = target + new Quaternion(0, 0, 0, 1) * new Vector3(0, dist * 0.16f, -dist);
            rig.LookAt(pos, target, fov);
            rig.PosSharpness = 5f;
            if (snap) rig.Snap();
        }

        public Vector3 SlotPosition(int i) => i < ShelfSlots.Count ? ShelfSlots[i] : ItemShelf.position;
    }
}
