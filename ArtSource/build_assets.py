"""Builds every Blender model for Handle With Care and exports FBX into Assets/Resources/Models.

    blender -b -P ArtSource/build_assets.py -- [groups...] [--only a,b] [--preview DIR] [--no-export]

groups: items boxes station stages (default: all). --preview renders each model from the game's
camera angle and writes a labelled contact sheet per group to DIR.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

import hwc_lib as L  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "Resources", "Models")
BLEND = os.path.join(ROOT, "ArtSource", "blend")


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    groups, only, preview, export, bake, res = [], None, None, True, True, 1024
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--no-bake":
            bake = False
        elif a == "--res":
            res = int(argv[i + 1])
            i += 1
        elif a == "--only":
            only = set(argv[i + 1].split(","))
            i += 1
        elif a == "--preview":
            preview = argv[i + 1]
            i += 1
        elif a == "--no-export":
            export = False
        else:
            groups.append(a)
        i += 1
    return groups or ["items", "boxes", "station", "stages"], only, preview, export, bake, res


def builders_for(group):
    if group == "items":
        import items
        return items.ALL
    if group == "boxes":
        import boxes
        return boxes.ALL
    if group == "station":
        import station
        return station.ALL
    if group == "stages":
        import stages
        return stages.ALL
    raise SystemExit("unknown group " + group)


def contact_sheet(paths, labels, out_path, cols=6, cell=256):
    import numpy as np
    rows = (len(paths) + cols - 1) // cols
    sheet = np.ones((rows * (cell + 24), cols * cell, 4), dtype=np.float32) * 0.15
    sheet[..., 3] = 1
    for i, p in enumerate(paths):
        img = bpy.data.images.load(p)
        w, h = img.size
        px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
        # nearest-neighbour downscale to cell
        ys = (np.arange(cell) * h / cell).astype(int)
        xs = (np.arange(cell) * w / cell).astype(int)
        small = px[ys][:, xs]
        r, c = i // cols, i % cols
        y0 = (rows - 1 - r) * (cell + 24) + 24
        sheet[y0:y0 + cell, c * cell:(c + 1) * cell] = small
        bpy.data.images.remove(img)
    out = bpy.data.images.new("sheet", cols * cell, rows * (cell + 24), alpha=True)
    out.pixels = sheet.ravel()
    out.filepath_raw = out_path
    out.file_format = "PNG"
    out.save()
    with open(out_path + ".txt", "w") as f:
        for i, lab in enumerate(labels):
            f.write(f"{i // cols},{i % cols}: {lab}\n")


def main():
    groups, only, preview, export, bake, res = parse_args()
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(BLEND, exist_ok=True)
    for group in groups:
        builders = builders_for(group)
        rendered, labels = [], []
        gathered = []
        for name, fn in builders.items():
            if only and name not in only:
                continue
            L.reset_scene()
            objs = fn()
            if bake:
                import pbr
                for o in objs:
                    for c in [o] + list(o.children_recursive):
                        if c.type == "MESH" and any(s.material and s.material.name.startswith("p_") for s in c.material_slots):
                            pbr.bake(c, res=res)
            if export:
                fname = objs[0].name
                L.export_fbx(os.path.join(OUT, fname + ".fbx"), objs)
                print(f"[build] {group}/{name} -> {fname}.fbx")
            if preview:
                os.makedirs(preview, exist_ok=True)
                cam = L.setup_preview_scene(512)
                path = os.path.join(preview, f"{group}_{name}.png")
                L.frame_and_render(cam, objs, path)
                rendered.append(path)
                labels.append(name)
            gathered.append((name, fn))
        if preview and rendered:
            contact_sheet(rendered, labels, os.path.join(preview, f"sheet_{group}.png"))
            print(f"[build] contact sheet {group}: {len(rendered)} models")
        # one .blend per group with every model laid out in a row (editable source)
        if export and not only and gathered:
            L.reset_scene()
            x = 0.0
            for name, fn in gathered:
                for o in fn():
                    if o.parent is None:
                        o.location.x += x
                x += 1.2
            bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND, group + ".blend"), compress=True)


main()
