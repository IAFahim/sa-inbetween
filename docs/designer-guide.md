# Designer guide: bridge two clips in Unity

You do not need a terminal. You pick two clips you already have, and Inbetween makes the motion that carries the character from one to the other. The result is an ordinary clip you can play like any other.

## One-time setup (your technical artist does this once)

1. Put the packages `com.sa.unity` and `com.sa.inbetween.sa` in the project.
2. Put the **tools folder** in the project root and name it `SaInbetween`. It holds `sa-inbetween`, `sa-soma-bake`, `mmm_v6_soma_fp32.onnx`, `humanoid-v1.safamily` and `soma_rest.pose`. Or keep it anywhere and choose it in `Edit > Project Settings > SA Inbetween`, where every line should say *found*.

## Make a bridge

1. In the Project window select the clip the character is leaving, then ctrl-click the clip it is going to.
2. Choose `Assets > Create > SA > Inbetween Bridge` (or right-click > Create > SA). A recipe named *A_to_B* appears next to the clips.
3. In the Inspector press **Generate**. It takes about 10 seconds. The bridge clip appears next to the recipe, named after it.
4. Watch it right away in the Inspector preview (see below), no Play mode needed. **Preview on character** still plays it on your SA character; that one needs the character in the scene and enters Play mode.

The recipe starts from the **end** of the first clip and moves to the **start** of the second. That is what you want when the two clips play one after the other.

## Change it

- **Start from / Move to**: any clip, and a place in it: end of clip, start of clip, or a time in seconds. A time lets you leave a clip mid-motion.
- **Add another goal**: chain moves. The character goes to goal 1, then goal 2, up to four.
- **Duration**: how long each move takes, from 0.6 to 1.9 seconds. A longer transition needs more goals.
- **Land exactly on the goal**: keeps the end pose on target. Turn it off for a looser landing.
- **Swap start and goal**: flips the direction.

When you change anything the recipe says *Settings changed since the last generate*. Nothing is regenerated behind your back; press **Generate again** when you are ready. The result is repeatable: the same recipe gives the same clip.

## Preview without Play mode

Once the recipe has a result, the Inspector shows a preview strip under **Result**:

- **Play / Pause** and a **scrub slider** with the time and length. Dragging the slider pauses.
- A small 3D view with the SA skeleton playing the clip, root motion included. Drag in it to orbit, scroll to zoom. The camera follows the hips.
- **Ghost start and goal** draws the start pose in faded green and the goal pose in faded orange, so you can see how close the bridge leaves and lands. The goal ghost stands where the bridge ends.
- **Show in Scene view** draws the same skeleton and ghosts as lines in the Scene view, at the scene origin, while the recipe is selected.

The preview reads the clip directly, so it works in any scene, with no character at all.

## Try variations

The same settings always give the same clip, so pressing Generate again will not give you something new. **Make variations** does:

1. Press **Make variations**. It reads the start and goal once, then makes up to four versions one after another: as set, one step shorter, one step longer, and one where the model picks the length within that range. A version that comes out identical to an earlier one is replaced by one with the other landing (exact or loose). Each step is 4 frames, about 0.13 s; durations stay within 0.6 to 1.9 s.
2. Each version appears as a card with its own small preview, its length and frame count. All cards play in step with the main preview.
3. Press **Keep this one** on the card you like. It becomes the recipe's result (the same clip file as before, so everything using the clip picks it up), the recipe's Duration and Land exactly change to match, and the recipe says *Up to date*. The other versions are thrown away.
4. Changed your mind? **Edit > Undo Keep variation** (ctrl-Z) puts back the previous clip file and the recipe settings. Redo works too. If the recipe had no result before, undo removes the clip.

Undo cannot bring back the versions you did not keep; press Make variations again for those (they come out the same). **Discard** throws away all versions without keeping one. Versions you have not decided on survive closing Unity; they live in `Library/SaInbetween`.

A kept *model picks* version has no exact duration setting; the recipe gets the closest one, so a later Generate again can differ slightly.

## Make a loop

Select one clip and choose **Inbetween Bridge**. The recipe goes from the end of the clip back to its start.

## If something goes wrong

- *Setup needed*: the message names the missing tool file. Open Project Settings.
- *Generating failed*: the log under the message says why. The usual cause is a wrong model file.
- Result looks off at the seam: use **At time** on the start so the bridge leaves the clip a little earlier, or lengthen the duration.
- The character does not move in Preview: the scene needs exactly one SA shared-family character. The player's Status field says what it is waiting for.
- Do not enter Play mode while generating.
- *Cannot preview this clip*: the result file is not an SA humanoid clip, or it is damaged. Generate again.

## Use your own character and clips (Mixamo, Synty, any Humanoid)

1. In **Start from** or **Move to**, change the source kind to **Unity clip**.
2. Pick the **Unity clip** (an FBX take or a `.anim`) and the **Character** (the model or prefab the clip belongs to, with a Humanoid avatar).
3. Choose **at frame** and type the frame number you see in the Animation window, for example frame 60 of a jump.
4. The first Generate converts the clip once (cached in `Library/SaInbetween`); later runs reuse it.

The start keeps the clip's real speed, including running and jumping, so the bridge continues the motion instead of restarting it. The converter maps bones through the Humanoid avatar, so Mixamo's `mixamorig:` names need no setup.

## Save the result as a Unity clip

Press **Export > Animation Clip** on the recipe (or right-click a `.saclip` > Export). A `.anim` named after the recipe appears next to it. It starts at the origin facing forward, so place it where the jump left off. The exported clip is a Generic clip: play it on the same character without the Humanoid avatar, or on rigs with identical bone names. A Humanoid `.anim` and FBX export are not built yet.

Measured on a Synty jump, starting mid-air at the apex (frame 41, hips 1.43 m) and landing: the bridge's first frame matches the jump to 0.0001 cm, it ends on the landing pose exactly, hips height is within 6.5 cm of the real animation, and touchdown lands on the same frame. When the start is a fast run and the goal is standing still, the bridge slows down over the 0.8 s, as it should; make the goal a running pose to keep speed.

## What it does not do yet

- The editor preview shows the SA skeleton, not your character's mesh. Use Preview on character for the final look.
- A frozen pose from a file, capturing the current pose of a character in the scene, and a Timeline track are planned. Humanoid `.anim` and FBX export are planned.
- The neck is a single bone on the SA rig, so a source clip's neck is smoothed slightly when it is read back. The head's position can move by up to about 2 cm at the seam.

## Tools folder, for the technical artist

`tools/make_pose_library.py` writes `soma_rest.pose` (a SOMA skeleton rest pose, which the baker needs for the joint offsets and finger rotations) together with a sample pose library:

```bash
~/GitHub/motionbricks/.venv/bin/python tools/make_pose_library.py --out ~/sa-inbetween-library
cp ~/sa-inbetween-library/soma_rest.pose ~/sa-inbetween-tools/
```

The model, the rest pose and the sample poses are NVIDIA data. Keep them out of git.
