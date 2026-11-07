# Independent NumPy reference of docs/engine/spec.md.
# It never imports motionbricks or warp. dump_goldens.py runs it against the official
# engine's dumps to prove the spec is complete. Section numbers refer to spec.md.
from __future__ import annotations

import numpy as np

POSES_PER_TOKEN = 4
CONTEXT_AND_TARGET = 8
BOUNDARY = 3
MIN_TOKENS = 6
MAX_TOKENS = 16
SAMPLE_RATE = 30.0
FOOT_CONTACTS = 4
NUM_MODEL_JOINTS = 30
FEATURES = 366
ROTATION_OFFSET_OUT = 5 + (NUM_MODEL_JOINTS - 1) * 3
ROTATION_OFFSET_IN = (NUM_MODEL_JOINTS - 1) * 3


# S2 quaternion and transform primitives. Quaternions are [x, y, z, w]; transforms are
# [px, py, pz, qx, qy, qz, qw].
def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return np.array([
        aw * bx + bw * ax + ay * bz - by * az,
        aw * by + bw * ay + az * bx - bz * ax,
        aw * bz + bw * az + ax * by - bx * ay,
        aw * bw - ax * bx - ay * by - az * bz,
    ])


def qconj(q):
    return np.array([-q[0], -q[1], -q[2], q[3]])


def qrot(q, v):
    c = 2.0 * q[3] * q[3] - 1.0
    d = 2.0 * (q[0] * v[0] + q[1] * v[1] + q[2] * v[2])
    return np.array([
        v[0] * c + q[0] * d + (q[1] * v[2] - q[2] * v[1]) * q[3] * 2.0,
        v[1] * c + q[1] * d + (q[2] * v[0] - q[0] * v[2]) * q[3] * 2.0,
        v[2] * c + q[2] * d + (q[0] * v[1] - q[1] * v[0]) * q[3] * 2.0,
    ])


def qrot_inv(q, v):
    c = 2.0 * q[3] * q[3] - 1.0
    d = 2.0 * (q[0] * v[0] + q[1] * v[1] + q[2] * v[2])
    return np.array([
        v[0] * c + q[0] * d - (q[1] * v[2] - q[2] * v[1]) * q[3] * 2.0,
        v[1] * c + q[1] * d - (q[2] * v[0] - q[0] * v[2]) * q[3] * 2.0,
        v[2] * c + q[2] * d - (q[0] * v[1] - q[1] * v[0]) * q[3] * 2.0,
    ])


def qnormalize(q):
    n = np.sqrt(np.dot(q, q))
    return q / n if n > 0.0 else np.array([0.0, 0.0, 0.0, 1.0])


def vnormalize(v):
    n = np.sqrt(np.dot(v, v))
    return v / n if n > 0.0 else np.zeros(3)


def q_axis_angle(axis, angle):
    h = 0.5 * angle
    s = np.sin(h)
    return np.array([axis[0] * s, axis[1] * s, axis[2] * s, np.cos(h)])


def q_to_axis_angle(q):
    v = q[:3]
    axis = -vnormalize(v) if q[3] < 0.0 else vnormalize(v)
    return axis, 2.0 * np.arctan2(np.sqrt(np.dot(v, v)), abs(q[3]))


def qslerp(q0, q1, t):
    axis, angle = q_to_axis_angle(qmul(qconj(q0), q1))
    return qmul(q0, q_axis_angle(axis, t * angle))


def q_from_matrix(m):
    tr = m[0, 0] + m[1, 1] + m[2, 2]
    if tr >= 0.0:
        h = np.sqrt(tr + 1.0)
        w = 0.5 * h
        h = 0.5 / h
        x = (m[2, 1] - m[1, 2]) * h
        y = (m[0, 2] - m[2, 0]) * h
        z = (m[1, 0] - m[0, 1]) * h
    else:
        d = 0
        if m[1, 1] > m[0, 0]:
            d = 1
        if m[2, 2] > m[d, d]:
            d = 2
        if d == 0:
            h = np.sqrt((m[0, 0] - (m[1, 1] + m[2, 2])) + 1.0)
            x = 0.5 * h
            h = 0.5 / h
            y = (m[0, 1] + m[1, 0]) * h
            z = (m[2, 0] + m[0, 2]) * h
            w = (m[2, 1] - m[1, 2]) * h
        elif d == 1:
            h = np.sqrt((m[1, 1] - (m[2, 2] + m[0, 0])) + 1.0)
            y = 0.5 * h
            h = 0.5 / h
            z = (m[1, 2] + m[2, 1]) * h
            x = (m[0, 1] + m[1, 0]) * h
            w = (m[0, 2] - m[2, 0]) * h
        else:
            h = np.sqrt((m[2, 2] - (m[0, 0] + m[1, 1])) + 1.0)
            z = 0.5 * h
            h = 0.5 / h
            x = (m[2, 0] + m[0, 2]) * h
            y = (m[1, 2] + m[2, 1]) * h
            w = (m[1, 0] - m[0, 1]) * h
    return qnormalize(np.array([x, y, z, w]))


def tmul(a, b):
    return np.concatenate([a[:3] + qrot(a[3:], b[:3]), qmul(a[3:], b[3:])])


def tinv(t):
    qi = qconj(t[3:])
    return np.concatenate([-qrot(qi, t[:3]), qi])


def identity_transform():
    return np.array([0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0])


def blend_transform(t0, t1, w):
    p = t0[:3] + (t1[:3] - t0[:3]) * w
    return np.concatenate([p, qslerp(t0[3:], t1[3:], w)])


# S3 skeleton binding.
def bind_pose_skeleton(skeleton_names, parents, model_joint_names, heading_joints=("RightLeg", "LeftLeg")):
    index = {n: i for i, n in enumerate(skeleton_names)}
    model_to_pose = np.array([index[n] for n in model_joint_names], dtype=np.int32)
    assert model_to_pose[0] == 0
    pose_to_model = np.full(len(skeleton_names), -1, dtype=np.int32)
    for m, p in enumerate(model_to_pose):
        parent = parents[p]
        if parent == -1 or pose_to_model[parent] >= 0:
            pose_to_model[p] = m
    return model_to_pose, pose_to_model, index[heading_joints[0]], index[heading_joints[1]]


# S4 token padding and forward kinematics.
def pad_token(poses):
    n = poses.shape[0]
    return np.stack([poses[min(i, n - 1)] for i in range(POSES_PER_TOKEN)]), n


def forward_kinematics(local, parents, root_tx):
    world = np.empty_like(local)
    for f in range(local.shape[0]):
        world[f, 0] = tmul(root_tx, local[f, 0])
        for j in range(1, local.shape[1]):
            world[f, j] = tmul(world[f, parents[j]], local[f, j])
    return world


# S5 heading and alignment.
def heading_from_hips(right, left):
    across = np.array([right[0] - left[0], 0.0, right[2] - left[2]])
    n = np.sqrt(np.dot(across, across))
    if n <= 1.0e-6:
        return 0.0
    across = across / n
    forward = np.cross(np.array([0.0, 1.0, 0.0]), across)
    return np.arctan2(forward[0], forward[2])


def compute_alignment(world8, right_leg, left_leg, heading_alignment):
    last = POSES_PER_TOKEN - 1
    hips = world8[last, 0, :3]
    current = heading_from_hips(world8[last, right_leg, :3], world8[last, left_leg, :3])
    desired = np.arctan2(heading_alignment[0], heading_alignment[1])
    return np.concatenate([[hips[0], 0.0, hips[2]], q_axis_angle(np.array([0.0, 1.0, 0.0]), current - desired)])


def aligned_position(alignment, p):
    return qrot_inv(alignment[3:], p - alignment[:3])


def align_world(world8, alignment):
    out = np.empty_like(world8)
    qi = qconj(alignment[3:])
    for f in range(world8.shape[0]):
        for j in range(world8.shape[1]):
            out[f, j, :3] = aligned_position(alignment, world8[f, j, :3])
            out[f, j, 3:] = qmul(qi, world8[f, j, 3:])
    return out


# S6 encode.
def encode_inputs(world8, alignment, model_to_pose, right_leg, left_leg):
    grv = np.zeros((1, 8, 5))
    lrv = np.zeros((1, 8, 4))
    lp = np.zeros((1, 8, 267))
    aligned = align_world(world8, alignment)
    for f in range(CONTEXT_AND_TARGET):
        nf = f if f == POSES_PER_TOKEN - 1 else min(f + 1, CONTEXT_AND_TARGET - 1)
        ch, nh = aligned[f, 0, :3], aligned[nf, 0, :3]
        hc = heading_from_hips(aligned[f, right_leg, :3], aligned[f, left_leg, :3])
        hn = heading_from_hips(aligned[nf, right_leg, :3], aligned[nf, left_leg, :3])
        dh = np.arctan2(np.sin(hn - hc), np.cos(hn - hc))
        grv[0, f] = [ch[0], ch[1], ch[2], np.cos(hc), np.sin(hc)]
        lrv[0, f] = [dh * SAMPLE_RATE, (nh[0] - ch[0]) * SAMPLE_RATE, (nh[2] - ch[2]) * SAMPLE_RATE, ch[1]]
        root_ground = np.array([ch[0], 0.0, ch[2]])
        for m, pj in enumerate(model_to_pose):
            pos = aligned[f, pj, :3]
            rot = aligned[f, pj, 3:]
            if m > 0:
                lp[0, f, (m - 1) * 3:(m - 1) * 3 + 3] = pos - root_ground
            o = ROTATION_OFFSET_IN + m * 6
            lp[0, f, o:o + 3] = qrot(rot, np.array([1.0, 0.0, 0.0]))
            lp[0, f, o + 3:o + 6] = qrot(rot, np.array([0.0, 1.0, 0.0]))
    return grv, lrv, lp, aligned


# S7 conditions.
def conditions(num_target_poses, allowed_token_counts):
    bits = 0
    for c in allowed_token_counts:
        bits |= 1 << (c - MIN_TOKENS)
    hg = np.zeros((1, 8), np.int32)
    hl = np.zeros((1, 8), np.int32)
    hp = np.zeros((1, 8), np.int32)
    allowed = np.zeros((1, 11), np.int32)
    for i in range(MAX_TOKENS - MIN_TOKENS + 1):
        if i < CONTEXT_AND_TARGET:
            v = 1 if i < POSES_PER_TOKEN + num_target_poses else 0
            hg[0, i] = v
            hp[0, i] = v
            hl[0, i] = 0 if (i == POSES_PER_TOKEN - 1 or i == POSES_PER_TOKEN + num_target_poses - 1) else v
        allowed[0, i] = (bits >> i) & 1
    return hg, hl, hp, allowed


# S8 decode.
def rotation_from_6d(a, b):
    x = vnormalize(a)
    z = vnormalize(np.cross(x, b))
    y = np.cross(z, x)
    return q_from_matrix(np.stack([x, y, z], axis=1))


def read_rotation(features, frame, offset):
    return rotation_from_6d(features[frame, offset:offset + 3], features[frame, offset + 3:offset + 6])


def decode_global_rotations(pred):
    out = np.zeros((pred.shape[0], NUM_MODEL_JOINTS, 4))
    for f in range(pred.shape[0]):
        for m in range(NUM_MODEL_JOINTS):
            out[f, m] = read_rotation(pred, f, ROTATION_OFFSET_OUT + m * 6)
    return out


def valid_frame_count(token_count):
    return token_count * POSES_PER_TOKEN - 2 * BOUNDARY


def decode_output(pred, token_count, alignment, model_to_runtime, parents, pose_to_model, reference_local):
    n = max(0, min(token_count * POSES_PER_TOKEN, pred.shape[0]) - 2 * BOUNDARY)
    num_joints = len(parents)
    frames = np.zeros((n, num_joints, 7))
    contacts = np.zeros((n, FOOT_CONTACTS))
    for f in range(n):
        r = BOUNDARY + f
        contacts[f] = np.clip(pred[r, FEATURES - FOOT_CONTACTS:FEATURES], 0.0, 1.0)
        hips = np.concatenate([pred[r, 0:3], read_rotation(pred, r, ROTATION_OFFSET_OUT)])
        frames[f, 0] = tmul(model_to_runtime, tmul(alignment, hips))
        for j in range(1, num_joints):
            m = pose_to_model[j]
            if m < 0:
                frames[f, j] = reference_local[j]
                continue
            child = read_rotation(pred, r, ROTATION_OFFSET_OUT + m * 6)
            parent = read_rotation(pred, r, ROTATION_OFFSET_OUT + pose_to_model[parents[j]] * 6)
            frames[f, j, :3] = reference_local[j, :3]
            frames[f, j, 3:] = qnormalize(qmul(qconj(parent), child))
    return frames, contacts


# S9 endpoint correction.
def root_endpoint_delta(generated, guide):
    rot = qmul(qconj(qnormalize(generated[3:])), qnormalize(guide[3:]))
    return np.concatenate([guide[:3] - generated[:3], qnormalize(rot)])


def endpoint_deltas(frames, context_last, target_first):
    j_count = frames.shape[1]
    start = np.zeros((j_count, 7))
    end = np.zeros((j_count, 7))
    last = frames.shape[0] - 1
    start[0] = root_endpoint_delta(frames[0, 0], context_last[0])
    end[0] = root_endpoint_delta(frames[last, 0], target_first[0])
    for j in range(1, j_count):
        start[j] = tmul(tinv(frames[0, j]), context_last[j])
        end[j] = tmul(tinv(frames[last, j]), target_first[j])
    return start, end


def apply_endpoint_correction(frames, start, end):
    n = frames.shape[0]
    out = frames.copy()
    if n < 2:
        return out
    ident = identity_transform()
    for f in range(n):
        ew = f / (n - 1)
        sw = 1.0 - ew
        root = frames[f, 0]
        rot = qmul(qmul(root[3:], qslerp(ident[3:], start[0, 3:], sw)), qslerp(ident[3:], end[0, 3:], ew))
        out[f, 0] = np.concatenate([root[:3] + start[0, :3] * sw + end[0, :3] * ew, qnormalize(rot)])
        for j in range(1, frames.shape[1]):
            c = tmul(frames[f, j], blend_transform(ident, start[j], sw))
            out[f, j] = tmul(c, blend_transform(ident, end[j], ew))
    return out


# S11 optional target pose IK.
SOMA_IK_CHAINS = (
    (1, "LeftArm", "LeftForeArm", "LeftHand", (0.0, -0.25, 0.0)),
    (2, "RightArm", "RightForeArm", "RightHand", (0.0, 0.25, 0.0)),
    (4, "LeftLeg", "LeftShin", "LeftFoot", (0.0, -0.25, 0.0)),
    (8, "RightLeg", "RightShin", "RightFoot", (0.0, 0.25, 0.0)),
)
IK_EPSILON = 1.0e-3


def quat_between_vectors(a, b):
    a = vnormalize(a)
    b = vnormalize(b)
    c = np.cross(a, b)
    return qnormalize(np.array([c[0], c[1], c[2], 1.0 + np.dot(a, b)]))


def solve_two_bone_ik(weight, pa, a_tx, b_tx, c_tx, t_tx, hint):
    weight = min(max(weight, 0.0), 1.0)
    a, b, c = a_tx[:3], b_tx[:3], c_tx[:3]
    t = c + (t_tx[:3] - c) * weight
    l_ab = np.linalg.norm(b - a)
    l_cb = np.linalg.norm(b - c)
    l_at = min(max(np.linalg.norm(t - a), IK_EPSILON), l_ab + l_cb - IK_EPSILON)
    axis = vnormalize(np.cross(c - a, hint - a))
    ac_ab_0 = np.arctan2(np.dot(axis, np.cross(c - a, b - a)), np.dot(c - a, b - a))
    ba_bc_0 = np.arctan2(np.dot(axis, np.cross(a - b, c - b)), np.dot(a - b, c - b))
    ac_ab_1 = np.arccos(np.clip((l_ab * l_ab + l_at * l_at - l_cb * l_cb) / (2.0 * l_ab * l_at), -1.0, 1.0))
    ba_bc_1 = np.arccos(np.clip((l_ab * l_ab + l_cb * l_cb - l_at * l_at) / (2.0 * l_ab * l_cb), -1.0, 1.0))
    rot_a = q_axis_angle(qrot_inv(a_tx[3:], axis), ac_ab_1 - ac_ab_0)
    rot_b = q_axis_angle(qrot_inv(b_tx[3:], axis), ba_bc_1 - ba_bc_0)
    zero = np.zeros(3)
    a_local = tmul(tmul(tinv(pa), a_tx), np.concatenate([zero, rot_a]))
    a_world = tmul(pa, a_local)
    ac_local = qrot_inv(a_world[3:], c - a)
    at_local = qrot_inv(a_world[3:], t - a)
    a_local = tmul(a_local, np.concatenate([zero, quat_between_vectors(ac_local, at_local)]))
    a_world = tmul(pa, a_local)
    b_local = tmul(tmul(tinv(a_tx), b_tx), np.concatenate([zero, rot_b]))
    b_world = tmul(a_world, b_local)
    c_world = tmul(b_world, tmul(tinv(b_tx), c_tx))
    return (
        np.concatenate([a_world[:3], qnormalize(a_world[3:])]),
        np.concatenate([b_world[:3], qnormalize(b_world[3:])]),
        np.concatenate([c_world[:3], qnormalize(qslerp(c_world[3:], t_tx[3:], weight))]),
    )


def smoothstep(e0, e1, x):
    x = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return x * x * (3.0 - 2.0 * x)


def apply_ik(pose, weight, pin_mask, pinned, chains, parents):
    if pin_mask == 0 or weight <= 0.0:
        return pose.copy()
    world = forward_kinematics(pose[None], parents, identity_transform())[0]
    out = pose.copy()
    for k, (pin, root, mid, tip, hint_local) in enumerate(chains):
        if pin_mask & pin == 0:
            continue
        rp = parents[root]
        pa = world[rp] if rp >= 0 else identity_transform()
        hint = tmul(world[mid], np.concatenate([np.asarray(hint_local), [0.0, 0.0, 0.0, 1.0]]))[:3]
        sa, sb, sc = solve_two_bone_ik(weight, pa, world[root], world[mid], world[tip], pinned[k], hint)
        out[root] = tmul(tinv(pa), sa)
        out[mid] = tmul(tinv(sa), sb)
        out[tip] = tmul(tinv(sb), sc)
    return out


# Comparison helpers.
def rotation_error_deg(qa, qb):
    qa = qa / np.linalg.norm(qa, axis=-1, keepdims=True)
    qb = qb / np.linalg.norm(qb, axis=-1, keepdims=True)
    d = np.clip(np.abs(np.sum(qa * qb, axis=-1)), 0.0, 1.0)
    return np.degrees(2.0 * np.arccos(d))


def transform_error(a, b):
    pos_mm = np.linalg.norm(a[..., :3] - b[..., :3], axis=-1) * 1000.0
    rot_deg = rotation_error_deg(a[..., 3:], b[..., 3:])
    return float(pos_mm.max(initial=0.0)), float(rot_deg.max(initial=0.0))
