# Golden vectors for the MotionBricks SOMA engine logic (docs/engine/spec.md).
#
# Run ONLY with the motionbricks venv python, from anywhere:
#   ~/GitHub/motionbricks/.venv/bin/python tools/dump_goldens.py [--out DIR]
# It runs the OFFICIAL engine (TensorRT FP32, batch 1, no CUDA graph) on 12 seeded
# synthetic requests, step by step in the exact order of model/engine.py:114-135,
# dumps every intermediate, then checks tools/engine_reference.py (the spec in NumPy)
# against those dumps. Output is evaluation-only, NVIDIA-derived data: never ship it.
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

import numpy as np
import warp as wp

from motionbricks.io.usd import load_skeleton_from_usd
from motionbricks.io.utils import SOMA_SKELETON_PRIM_PATH, get_asset_file
from motionbricks.model.config import MotionBricksInferenceConfig
from motionbricks.model.contract import valid_frame_count
from motionbricks.model.generation import read_token_count
from motionbricks.model.soma.contract import SOMA_N1_CONTRACT, SOMA_N1_FP32_ONNX_PATH
from motionbricks.model.soma.engine import MotionBricksSOMAEngine
from motionbricks.runtime.target_pose import IKPin, TargetPoseIKCorrector, create_soma_target_pose_ik_rig
from motionbricks.utils.space_conversion_utils import FacingDirectionType, SpaceConverter

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import engine_reference as ref  # noqa: E402

WORKTREE = TOOLS.parent
DEFAULT_OUT = WORKTREE / "testdata" / "eval-only" / "goldens"
CACHE_DIR = WORKTREE / ".trt-cache"
DEG = np.pi / 180.0

# Requests: seed, target pose count, allowed token counts (None = default 8..14),
# heading_alignment, correct_generated_diff, model_to_runtime ("mujoco" | "identity"),
# chain_from (request index whose final last 4 frames become the context), ik (bool),
# turn (target yaw change, deg), distance (m).
REQUESTS = [
    dict(seed=100, targets=4, tokens=None, heading=(1.0, 0.0), correct=True, m2r="mujoco", chain=None, ik=False, turn=0, dist=2.0),
    dict(seed=101, targets=1, tokens=(6,), heading=(1.0, 0.0), correct=True, m2r="mujoco", chain=None, ik=False, turn=20, dist=0.8),
    dict(seed=102, targets=2, tokens=(16,), heading=(1.0, 0.0), correct=True, m2r="mujoco", chain=None, ik=False, turn=-45, dist=3.5),
    dict(seed=103, targets=3, tokens=(10, 12), heading=(1.0, 0.0), correct=True, m2r="mujoco", chain=None, ik=False, turn=90, dist=2.0),
    dict(seed=104, targets=4, tokens=None, heading=(0.0, 1.0), correct=True, m2r="mujoco", chain=None, ik=False, turn=-120, dist=1.5),
    dict(seed=105, targets=1, tokens=None, heading=(0.3, -2.0), correct=True, m2r="mujoco", chain=None, ik=False, turn=10, dist=2.5),
    dict(seed=106, targets=2, tokens=None, heading=(1.0, 0.0), correct=False, m2r="mujoco", chain=None, ik=False, turn=30, dist=2.0),
    dict(seed=107, targets=4, tokens=None, heading=(1.0, 0.0), correct=True, m2r="mujoco", chain=0, ik=False, turn=-30, dist=2.0),
    dict(seed=108, targets=4, tokens=tuple(range(6, 17)), heading=(1.0, 0.0), correct=True, m2r="mujoco", chain=None, ik=False, turn=150, dist=3.0),
    dict(seed=109, targets=1, tokens=None, heading=(1.0, 0.0), correct=True, m2r="mujoco", chain=None, ik=True, turn=0, dist=1.2),
    dict(seed=110, targets=4, tokens=None, heading=(1.0, 0.0), correct=True, m2r="identity", chain=None, ik=False, turn=45, dist=2.0),
    dict(seed=111, targets=2, tokens=(8, 9, 10), heading=(1.0, 0.0), correct=True, m2r="identity", chain=10, ik=False, turn=-60, dist=1.8),
]


def qy(angle):  # rotation about model up (+Y); the SOMA reference pose is Y up
    return np.array([0.0, np.sin(angle / 2), 0.0, np.cos(angle / 2)])


def rotvec_quat(v):
    a = np.linalg.norm(v)
    return np.array([0.0, 0.0, 0.0, 1.0]) if a < 1e-12 else ref.q_axis_angle(v / a, a)


def synth_poses(rng, ref_local, n, start_xz, yaw, speed, offs0, offs1, m2r_np):
    """n model-space poses at 30 Hz walking forward after turning the reference pose by yaw
    about +Y; joint rotations = reference * small offsets; root placed by m2r_np."""
    j = ref_local.shape[0]
    out = np.empty((n, j, 7))
    facing = PHI0 + yaw  # heading angle, atan2(fwd.x, fwd.z), as spec E2
    fwd = np.array([np.sin(facing), np.cos(facing)])
    for f in range(n):
        t = f / max(n - 1, 1)
        for k in range(j):
            off = rotvec_quat(offs0[k] * (1 - t) + offs1[k] * t)
            out[f, k, :3] = ref_local[k, :3]
            out[f, k, 3:] = ref.qnormalize(ref.qmul(ref_local[k, 3:], off))
        xz = start_xz + fwd * speed * f / 30.0
        root = np.concatenate([[xz[0], ref_local[0, 1], xz[1]], ref.qmul(qy(yaw), out[f, 0, 3:])])
        out[f, 0] = ref.tmul(m2r_np, root)
    return out


def random_offsets(rng, j, max_deg):
    axes = rng.normal(size=(j, 3))
    axes /= np.linalg.norm(axes, axis=1, keepdims=True)
    return axes * rng.uniform(0.0, max_deg * DEG, size=(j, 1))


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def wp_tf(np_poses):
    return wp.array(np.ascontiguousarray(np_poses, dtype=np.float32), dtype=wp.transform)


def run_request(engine, skel, req, ctx_local, rng, ref_local):
    """Run one request through the official engine step by step; return dict of arrays."""
    m2r_np = np.array(MUJOCO_M2R if req["m2r"] == "mujoco" else [0, 0, 0, 0, 0, 0, 1], dtype=np.float64)
    j = skel.num_joints
    if ctx_local is None:
        yaw0 = rng.uniform(-np.pi, np.pi)
        start = rng.uniform(-3.0, 3.0, size=2)
        ctx_offs0, ctx_offs1 = random_offsets(rng, j, 12), random_offsets(rng, j, 12)
        ctx_local = synth_poses(rng, ref_local, 4, start, yaw0, 1.2, ctx_offs0, ctx_offs1, m2r_np)
        last_yaw = yaw0
        last_xz = start + np.array([np.sin(PHI0 + yaw0), np.cos(PHI0 + yaw0)]) * 1.2 * 3 / 30.0
    else:
        # Chained: ground position and heading of the last context pose, in model space.
        w = ref.forward_kinematics(ctx_local[3:4], PARENTS, ref.tinv(m2r_np))[0]
        last_xz = w[0, [0, 2]]
        last_yaw = ref.heading_from_hips(w[RIGHT_LEG, :3], w[LEFT_LEG, :3]) - PHI0
    yaw_t = last_yaw + req["turn"] * DEG
    dir_angle = PHI0 + last_yaw + rng.uniform(-0.5, 0.5)
    tgt_start = last_xz + np.array([np.sin(dir_angle), np.cos(dir_angle)]) * req["dist"]
    tgt_local = synth_poses(rng, ref_local, req["targets"], tgt_start, yaw_t, 1.0,
                            random_offsets(rng, j, 15), random_offsets(rng, j, 15), m2r_np)

    cfg_kwargs = dict(correct_generated_diff=req["correct"], heading_alignment=tuple(req["heading"]))
    if req["tokens"] is not None:
        cfg_kwargs["allowed_token_counts"] = tuple(req["tokens"])
    config = MotionBricksInferenceConfig(**cfg_kwargs)

    d = {"context_local": ctx_local.astype(np.float32), "target_local": tgt_local.astype(np.float32)}
    ctx = engine.create_input(skel)
    tgt = engine.create_input(skel)
    out = engine.create_output(skel)
    ctx.update_from_poses(wp_tf(ctx_local), stream=engine.stream)
    tgt.update_from_poses(wp_tf(tgt_local), stream=engine.stream)

    # Same order as MotionBricksEngine.generate_animation (model/engine.py:114-135).
    t0 = time.perf_counter()
    engine._prepare_generation(ctx, tgt, out)
    out.num_frames = 0
    engine._encode_guide_poses(ctx, tgt, config)
    engine.enqueue_async()
    engine._decode_generated_poses(out)
    engine.synchronize()
    d["context_token"] = ctx.poses.numpy()
    d["target_token"] = tgt.poses.numpy()
    d["guide_world"] = engine._guide_world.numpy()
    d["alignment"] = engine._alignment.numpy()
    for name in ("global_root_values", "local_root_values", "local_poses", "has_global_root_values",
                 "has_local_root_values", "has_local_poses", "allowed_pred_num_tokens",
                 "pred_global_motions", "pred_num_tokens"):
        d["net_" + name] = engine.device_tensor(name).numpy().copy()
    n_kernel = int(engine._generated_frame_count.numpy()[0])
    d["decoded_frames"] = out.poses.numpy()[:n_kernel].copy()
    d["contacts"] = out.foot_contacts.numpy()[:n_kernel].copy()
    if config.correct_generated_diff:
        engine._post_process_generated_poses(ctx, tgt, out)
        engine.synchronize()
        d["start_deltas"] = engine._start_deltas.numpy()
        d["end_deltas"] = engine._end_deltas.numpy()
    token_count = read_token_count(engine, config)
    n = valid_frame_count(token_count)
    out.num_frames = n
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    assert n == n_kernel, (n, n_kernel)
    d["final_frames"] = out.poses.numpy()[:n].copy()
    d["final_contacts"] = out.foot_contacts.numpy()[:n].copy()
    d["trimmed_pred"] = d["net_pred_global_motions"][0, ref.BOUNDARY:ref.BOUNDARY + n].copy()

    # Cross-check: the public API gives bit-identical results to the stepwise run.
    out2 = engine.create_output(skel)
    engine.generate_animation(ctx, tgt, out2, config=config)
    same = bool(np.array_equal(out2.poses.numpy()[:n], d["final_frames"])
                and np.array_equal(out2.foot_contacts.numpy()[:n], d["final_contacts"]))

    if req["ik"]:
        corrector = TargetPoseIKCorrector(skel, create_soma_target_pose_ik_rig(skel))
        corrector.capture_pins(wp_tf(tgt_local[0]), IKPin.ALL, stream=engine.stream)
        frames_idx = [n // 2, (3 * n) // 4, n - 2, n - 1]
        weights = [float(ref.smoothstep(0.5, 1.0, k / (n - 1))) for k in frames_idx]
        weights[-1] = 1.0
        ik_in, ik_out = [], []
        for k, w in zip(frames_idx, weights):
            pose = wp_tf(d["final_frames"][k])
            corrector.apply(pose, w, stream=engine.stream)
            engine.synchronize()
            ik_in.append(d["final_frames"][k])
            ik_out.append(pose.numpy())
        d["ik_frames"] = np.array(frames_idx, dtype=np.int32)
        d["ik_weights"] = np.array(weights, dtype=np.float32)
        d["ik_input"] = np.stack(ik_in)
        d["ik_output"] = np.stack(ik_out)
        d["ik_pin_mask"] = np.array([int(IKPin.ALL)], dtype=np.int32)
    meta = dict(req, token_count=token_count, valid_frames=n, public_api_identical=same,
                config=dict(allowed_token_counts=list(config.allowed_token_counts),
                            correct_generated_diff=config.correct_generated_diff,
                            heading_alignment=list(config.heading_alignment)))
    return d, meta, elapsed_ms


def reference_check(d, meta, skel_np, m2r):
    """Run tools/engine_reference.py on the dumped inputs; return max errors per step."""
    parents, ref_local, model_to_pose, pose_to_model, rl, ll = skel_np
    f64 = lambda a: np.asarray(a, dtype=np.float64)  # noqa: E731
    r = {}
    ctx_tok, _ = ref.pad_token(f64(d["context_local"]))
    tgt_tok, _ = ref.pad_token(f64(d["target_local"]))
    r["pad_token_max_abs"] = float(max(np.abs(ctx_tok - d["context_token"]).max(), np.abs(tgt_tok - d["target_token"]).max()))
    r2m = ref.tinv(m2r)
    world = np.concatenate([ref.forward_kinematics(ctx_tok, parents, r2m), ref.forward_kinematics(tgt_tok, parents, r2m)])
    r["fk_pos_mm"], r["fk_rot_deg"] = ref.transform_error(world, f64(d["guide_world"]))
    # Encode from the official guide_world to isolate each step.
    gw = f64(d["guide_world"])
    al = ref.compute_alignment(gw, rl, ll, meta["config"]["heading_alignment"])
    r["alignment_max_abs"] = float(np.abs(al - d["alignment"][0]).max())
    grv, lrv, lp, aligned = ref.encode_inputs(gw, f64(d["alignment"][0]), model_to_pose, rl, ll)
    d["ref_aligned_world"] = aligned.astype(np.float32)
    r["encode_grv_max_abs"] = float(np.abs(grv - d["net_global_root_values"]).max())
    r["encode_lrv_max_abs"] = float(np.abs(lrv - d["net_local_root_values"]).max())
    r["encode_local_poses_max_abs"] = float(np.abs(lp - d["net_local_poses"]).max())
    hg, hl, hp, allowed = ref.conditions(meta["targets"], meta["config"]["allowed_token_counts"])
    r["conditions_equal"] = bool(np.array_equal(hg, d["net_has_global_root_values"]) and np.array_equal(hl, d["net_has_local_root_values"])
                                 and np.array_equal(hp, d["net_has_local_poses"]) and np.array_equal(allowed, d["net_allowed_pred_num_tokens"]))
    pred = f64(d["net_pred_global_motions"][0])
    d["ref_global_rotations"] = ref.decode_global_rotations(pred[ref.BOUNDARY:ref.BOUNDARY + meta["valid_frames"]]).astype(np.float32)
    frames, contacts = ref.decode_output(pred, int(d["net_pred_num_tokens"][0]), f64(d["alignment"][0]), m2r, parents, pose_to_model, ref_local)
    r["decode_pos_mm"], r["decode_rot_deg"] = ref.transform_error(frames, f64(d["decoded_frames"]))
    r["contacts_max_abs"] = float(np.abs(contacts - d["contacts"]).max())
    if meta["correct"]:
        s, e = ref.endpoint_deltas(f64(d["decoded_frames"]), f64(d["context_token"][3]), f64(d["target_token"][0]))
        r["delta_start_pos_mm"], r["delta_start_rot_deg"] = ref.transform_error(s, f64(d["start_deltas"]))
        r["delta_end_pos_mm"], r["delta_end_rot_deg"] = ref.transform_error(e, f64(d["end_deltas"]))
        final = ref.apply_endpoint_correction(f64(d["decoded_frames"]), f64(d["start_deltas"]), f64(d["end_deltas"]))
    else:
        final = f64(d["decoded_frames"])
    r["final_pos_mm"], r["final_rot_deg"] = ref.transform_error(final, f64(d["final_frames"]))
    r["end_to_end_pos_mm"], r["end_to_end_rot_deg"] = ref.transform_error(
        ref.apply_endpoint_correction(frames, *ref.endpoint_deltas(frames, f64(d["context_token"][3]), f64(d["target_token"][0])))
        if meta["correct"] else frames, f64(d["final_frames"]))
    if "ik_output" in d:
        world_t = ref.forward_kinematics(f64(d["target_local"][:1]), parents, ref.identity_transform())[0]
        pinned = [world_t[tip] for _, _, _, tip, _ in IK_CHAINS_IDX]
        errs = [ref.transform_error(ref.apply_ik(f64(p), float(w), int(d["ik_pin_mask"][0]), pinned, IK_CHAINS_IDX, parents), f64(o))
                for p, w, o in zip(d["ik_input"], d["ik_weights"], d["ik_output"])]
        r["ik_pos_mm"] = max(e[0] for e in errs)
        r["ik_rot_deg"] = max(e[1] for e in errs)
    return r


MUJOCO_M2R = None
IK_CHAINS_IDX = None
PARENTS = None
PHI0 = 0.0
RIGHT_LEG = LEFT_LEG = -1


def main():
    global MUJOCO_M2R, IK_CHAINS_IDX, PARENTS, PHI0, RIGHT_LEG, LEFT_LEG
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    wp.init()
    wp.set_device("cuda:0")
    wp.config.quiet = True
    skel = load_skeleton_from_usd(get_asset_file("human", "soma_base_skel_minimal_no_root.usd"), SOMA_SKELETON_PRIM_PATH)
    m2r_mujoco = SpaceConverter(FacingDirectionType.MUJOCO).convert_transform(wp.transform_identity())
    MUJOCO_M2R = [float(x) for x in m2r_mujoco]
    ref_local = skel.reference_local_transforms.numpy().astype(np.float64)
    parents = np.array([skel.joint_parent(i) for i in range(skel.num_joints)], dtype=np.int32)
    model_to_pose, pose_to_model, rl, ll = ref.bind_pose_skeleton(skel.joint_names, parents, SOMA_N1_CONTRACT.joint_names)
    skel_np = (parents, ref_local, model_to_pose, pose_to_model, rl, ll)
    PARENTS, RIGHT_LEG, LEFT_LEG = parents, int(rl), int(ll)
    ref_world = ref.forward_kinematics(ref_local[None], parents, ref.identity_transform())[0]
    PHI0 = float(ref.heading_from_hips(ref_world[rl, :3], ref_world[ll, :3]))
    IK_CHAINS_IDX = tuple((int(pin), skel.joint_index(a), skel.joint_index(b), skel.joint_index(c), hint)
                          for pin, a, b, c, hint in ref.SOMA_IK_CHAINS)

    common = {"skeleton_joint_names": list(skel.joint_names), "parents": parents.tolist(),
              "model_joint_names": list(SOMA_N1_CONTRACT.joint_names), "model_to_pose": model_to_pose.tolist(),
              "pose_to_model": pose_to_model.tolist(), "right_leg_index": int(rl), "left_leg_index": int(ll),
              "model_to_runtime": {"mujoco": MUJOCO_M2R, "identity": [0, 0, 0, 0, 0, 0, 1]}}
    (args.out / "skeleton.json").write_text(json.dumps(common, indent=1) + "\n")
    np.save(args.out / "reference_local.npy", ref_local.astype(np.float32))

    finals, timing, checks = {}, {}, {}
    engines = {}
    t_load = time.perf_counter()
    for key, m2r in (("mujoco", m2r_mujoco), ("identity", wp.transform_identity())):
        engines[key] = MotionBricksSOMAEngine(SOMA_N1_FP32_ONNX_PATH, model_to_runtime=m2r, cache_dir=CACHE_DIR,
                                              bake_cuda_graph=False, logger_severity="WARNING")
    timing["engine_load_s"] = time.perf_counter() - t_load
    try:
        for i, req in enumerate(REQUESTS):
            engine = engines[req["m2r"]]
            rng = np.random.default_rng(req["seed"])
            ctx = None if req["chain"] is None else finals[req["chain"]][-4:].astype(np.float64)
            with wp.ScopedStream(engine.stream):
                d, meta, ms = run_request(engine, skel, req, ctx, rng, ref_local)
            finals[i] = d["final_frames"]
            m2r = np.array(common["model_to_runtime"][req["m2r"]], dtype=np.float64)
            checks[f"req{i:02d}"] = reference_check(d, meta, skel_np, m2r)
            timing[f"req{i:02d}_stepwise_ms"] = ms
            rdir = args.out / f"req{i:02d}"
            rdir.mkdir(exist_ok=True)
            for k, v in sorted(d.items()):
                np.save(rdir / f"{k}.npy", v)
            (rdir / "request.json").write_text(json.dumps(meta, indent=1, sort_keys=True) + "\n")
            print(f"req{i:02d}: tokens={meta['token_count']} frames={meta['valid_frames']} api_identical={meta['public_api_identical']} {ms:.1f} ms")
        # Warm timing of the public API, 50 calls on the last request.
        engine = engines["mujoco"]
        with wp.ScopedStream(engine.stream):
            c, t, o = engine.create_input(skel), engine.create_input(skel), engine.create_output(skel)
            c.update_from_poses(wp_tf(np.load(args.out / "req00" / "context_local.npy")), stream=engine.stream)
            t.update_from_poses(wp_tf(np.load(args.out / "req00" / "target_local.npy")), stream=engine.stream)
            samples = []
            for _ in range(50):
                s = time.perf_counter()
                engine.generate_animation(c, t, o)
                samples.append((time.perf_counter() - s) * 1000.0)
            timing["generate_animation_median_ms"] = float(np.median(samples))
            timing["stage_ms_last_call"] = dict(engine.timer.milliseconds)
    finally:
        for e in engines.values():
            e.close()

    (args.out / "reference_check.json").write_text(json.dumps(checks, indent=1, sort_keys=True) + "\n")
    manifest = {str(p.relative_to(args.out)): sha256(p) for p in sorted(args.out.rglob("*"))
                if p.is_file() and p.name not in ("MANIFEST.sha256.json", "timing.json")}
    aggregate = hashlib.sha256("".join(f"{k}:{v}\n" for k, v in manifest.items()).encode()).hexdigest()
    (args.out / "MANIFEST.sha256.json").write_text(json.dumps({"aggregate": aggregate, "files": manifest}, indent=1) + "\n")
    (args.out / "timing.json").write_text(json.dumps(timing, indent=1) + "\n")
    keys = sorted({k for v in checks.values() for k, x in v.items() if not isinstance(x, bool)})
    worst = {k: max(v[k] for v in checks.values() if k in v) for k in keys}
    print("worst reference errors:", json.dumps(worst, indent=1))
    print("conditions_equal all:", all(v["conditions_equal"] for v in checks.values()))
    print("aggregate sha256:", aggregate, "files:", len(manifest))


if __name__ == "__main__":
    main()
