# Designer guide: bridge two clips in Unity

You do not need a terminal. You pick two clips you already have, and Inbetween makes the motion that carries the character from one to the other. The result is an ordinary clip you can play like any other.

## One-time setup (your technical artist does this once)

1. Put the packages `com.sa.unity` and `com.sa.inbetween.sa` in the project.
2. Put the **tools folder** in the project root and name it `SaInbetween`. It holds `sa-inbetween`, `sa-soma-bake`, `mmm_v6_soma_fp32.onnx`, `humanoid-v1.safamily` and `soma_rest.pose`. Or keep it anywhere and choose it in `Edit > Project Settings > SA Inbetween`, where every line should say *found*.

## Make a bridge

1. In the Project window select the clip the character is leaving, then ctrl-click the clip it is going to.
2. Choose `Assets > Create > SA > Inbetween Bridge` (or right-click > Create > SA). A recipe named *A_to_B* appears next to the clips.
3. In the Inspector press **Generate**. It takes about 10 seconds. The bridge clip appears next to the recipe, named after it.
4. Press **Preview on character** to see it. The scene needs your SA character; the preview enters Play mode.

The recipe starts from the **end** of the first clip and moves to the **start** of the second. That is what you want when the two clips play one after the other.

## Change it

- **Start from / Move to**: any clip, and a place in it: end of clip, start of clip, or a time in seconds. A time lets you leave a clip mid-motion.
- **Add another goal**: chain moves. The character goes to goal 1, then goal 2, up to four.
- **Duration**: how long each move takes, from 0.6 to 1.9 seconds. A longer transition needs more goals.
- **Land exactly on the goal**: keeps the end pose on target. Turn it off for a looser landing.
- **Swap start and goal**: flips the direction.

When you change anything the recipe says *Settings changed since the last generate*. Nothing is regenerated behind your back; press **Generate again** when you are ready. The result is repeatable: the same recipe gives the same clip.

## Make a loop

Select one clip and choose **Inbetween Bridge**. The recipe goes from the end of the clip back to its start.

## If something goes wrong

- *Setup needed*: the message names the missing tool file. Open Project Settings.
- *Generating failed*: the log under the message says why. The usual cause is a wrong model file.
- Result looks off at the seam: use **At time** on the start so the bridge leaves the clip a little earlier, or lengthen the duration.
- The character does not move in Preview: the scene needs exactly one SA shared-family character. The player's Status field says what it is waiting for.
- Do not enter Play mode while generating.

## What it does not do yet

- No in-editor ghost preview; Preview plays on the character in Play mode.
- Poses come from existing clips. Capturing a pose from the scene, from a Unity `.anim`, or from a Timeline is planned.
- The neck is a single bone on the SA rig, so a source clip's neck is smoothed slightly when it is read back. The head's position can move by up to about 2 cm at the seam.

## Tools folder, for the technical artist

`tools/make_pose_library.py` writes `soma_rest.pose` (a SOMA skeleton rest pose, which the baker needs for the joint offsets and finger rotations) together with a sample pose library:

```bash
~/GitHub/motionbricks/.venv/bin/python tools/make_pose_library.py --out ~/sa-inbetween-library
cp ~/sa-inbetween-library/soma_rest.pose ~/sa-inbetween-tools/
```

The model, the rest pose and the sample poses are NVIDIA data. Keep them out of git.
