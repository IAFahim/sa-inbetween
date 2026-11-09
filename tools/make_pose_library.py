# Builds a local pose library for Inbetween Studio from the BVH clips shipped with MotionBricks.
#
#   ~/GitHub/motionbricks/.venv/bin/python tools/make_pose_library.py --out ~/sa-inbetween-library
#
# Each entry is four consecutive poses (30 Hz) so it works as a context or as a target.
# Output is NVIDIA-derived sample data: keep it local, do not commit it.
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

import numpy as np
import warp as wp

from motionbricks.io.bvh import load_bvh
from motionbricks.io.usd import load_skeleton_from_usd
from motionbricks.io.utils import SOMA_SKELETON_PRIM_PATH, get_asset_file

POSES_PER_ENTRY = 4
RATE = 30.0

CLIPS = [
    ("Walk", "locomotion/bs_1d_linear/Neutral_walk_forward_002__A057"),
    ("Jog", "locomotion/bs_1d_linear/neutral_jog_ff_180_R_001__A534"),
    ("Jump 0.5 m", "jump/jump_over_obstacle_0_5m_R_001__A415"),
    ("Sit down", "sitting/sit_on_chair_start_R_001__A047"),
    ("Stand up", "sitting/sit_on_chair_stop_R_001__A047"),
    ("Combat quickstep", "variations/combat_quickstep_ff_idle_180_R_002__A365"),
    ("Zombie walk", "variations/zombie_walk_180_R_003__A330"),
    ("Skipping", "variations/skipping_R_002__A360"),
    ("Dance vogue", "variations/dance_vouge_vogue_sequence_180_R_002__A316"),
]


def sample(clip, t: float) -> np.ndarray:
    out = wp.zeros(clip.skeleton.num_joints if hasattr(clip, "skeleton") else 77, dtype=wp.transform)
    clip.sample(t, out)
    return out.numpy().astype(np.float32).reshape(-1, 7)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--assets", default=str(Path.home() / "GitHub/motionbricks/assets/motions/soma"))
    parser.add_argument("--per-clip", type=int, default=3)
    args = parser.parse_args()
    wp.init()
    wp.set_device("cpu")
    out = Path(args.out).expanduser()
    out.mkdir(parents=True, exist_ok=True)
    skeleton = load_skeleton_from_usd(get_asset_file("human", "soma_base_skel_minimal_no_root.usd"), SOMA_SKELETON_PRIM_PATH)
    entries = []
    for label, rel in CLIPS:
        path = Path(args.assets) / f"{rel}.bvh"
        if not path.exists():
            print("missing", path, file=sys.stderr)
            continue
        clip = load_bvh(str(path), skeleton)
        duration = (clip.num_frames - 1) / clip.sample_rate
        last = duration - (POSES_PER_ENTRY - 1) / RATE
        for k in range(args.per_clip):
            t = 0.0 if args.per_clip == 1 else last * k / (args.per_clip - 1)
            slug = re.sub(r"[^a-z0-9]+", "_", label.lower()).strip("_")
            name = f"{slug}_{k + 1}"
            files = []
            for i in range(POSES_PER_ENTRY):
                pose = sample(clip, t + i / RATE)
                if pose.shape != (77, 7) or not np.isfinite(pose).all():
                    print("bad pose", name, pose.shape, file=sys.stderr)
                    return 1
                f = out / f"{name}_{i}.pose"
                f.write_bytes(pose.astype("<f4").tobytes())
                files.append(f.name)
            entries.append({"name": f"{label} {k + 1}", "group": label, "time": round(t, 2), "files": files})
            print(name, f"t={t:.2f}s")
    first = next(iter(sorted(out.glob("*_0.pose"))), None)
    if first is not None:
        (out / "soma_rest.pose").write_bytes(first.read_bytes())
    (out / "library.json").write_text(json.dumps({"version": 1, "entries": entries}, indent=1))
    print(len(entries), "entries ->", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
