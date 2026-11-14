# Designer guide: bridge two clips in Unity

You do not need a terminal. You pick two clips you already have, and Inbetween makes the motion that carries the character from one to the other. The result is an ordinary clip you can play like any other.

## Your first 10 minutes

1. Your technical artist has done the one-time setup below (or follow `docs/install.md` yourself).
2. Drag a Humanoid character and a jump animation into the Project window. Mixamo works: download a character
   (FBX for Unity) and a "Jump" animation. Any Humanoid FBX works (Synty, your own).
3. Choose **SA > Inbetween > Create Demo Scene** in the menu bar. The **First Bridge** window opens.
   - If it says your files are not Humanoid yet, press **Set to Humanoid** (it sets Rig > Animation Type to Humanoid
     on them and reimports), then **Make demo scene**.
   - If a line says *Tools folder: missing*, press **Open Project Settings**: it says which file is missing and where
     it goes.
4. The window says what it picked, for example *From 'Jump' at frame 41, the top of the jump (hips 0.80 m up) to the
   first frame of 'Idle'*. A new scene `Assets/SA Inbetween Demo/Inbetween Demo.unity` holds your character, a
   ground and a camera; the recipe `Jump_MidAir_to_Landing` is next to it.
5. Press **1 Generate** (about 10 seconds), **2 Preview** (the character lands, in the window; drag to turn it; a faded
   green copy shows the start pose and a faded orange one the goal), and
   **3 Save** (writes the Generic and Humanoid `.anim`, an `.fbx` with the clip as a take, and sets the demo character to
   play the Humanoid clip). Without the FBX Exporter (or, on Linux, the `libxml2.so.2` link) Save still writes the
   `.anim` files and says why the FBX was skipped.
6. Press **Play** at the top of Unity: the character plays the bridge in a loop in the Game view.
7. Press **Play** again to stop, then **4 Play in SA runtime**: it sets up an SA character in a SubScene next to the demo
   character (once, about a second), enters Play mode and plays the `.saclip` on SA's ECS animation runtime on the right,
   while Unity's Animator plays the Humanoid `.anim` on the left.

Before showing it to anyone, choose **SA > Inbetween > Demo Backup > Save** once the demo is made. It copies the finished
demo folders (`Assets/SA Inbetween Demo`, `Assets/SA Inbetween Generation Demo`, and the SA materials) to
`SA Inbetween Demo Backup` next to `Assets`. If Generate is slow or fails later, **Use the backup** in the First Bridge
window (or **SA > Inbetween > Demo Backup > Restore**) puts that result back in a second. Every step of Generate also
stops on its own after two minutes with a plain message, so the window never waits forever.

From here, make your own bridges as below. Every message says what to do next; if one does not, press
**Copy diagnostics** (in the window, the recipe Inspector, or **SA > Inbetween > Copy Diagnostics**) and send it with
**SA > Inbetween > Report a Bug**.

## One-time setup (your technical artist does this once)

Follow `docs/install.md`: four package lines in `Packages/manifest.json`, the **tools folder** `SaInbetween` next to `Assets` (it holds `sa-inbetween`, `sa-soma-bake`, `humanoid-v1.safamily`, and the two NVIDIA files `mmm_v6_soma_fp32.onnx` and `soma_rest.pose`), and a look at `Edit > Project Settings > SA Inbetween`, where every line should say *found*.

## Make a bridge

1. In the Project window select the clip the character is leaving, then ctrl-click the clip it is going to. A clip can be a `.saclip` or a Unity clip: an FBX take (unfold the FBX to see it) or an `.anim`. With Unity clips, also ctrl-click the character model you want to see and export the result on (see "Use your own character and clips" below). If you select only FBX files and no clips, each FBX's first take is used.
2. Choose `Assets > Create > SA > Inbetween Bridge` (or right-click > Create > SA). A recipe named *A_to_B* appears next to the first clip, with both clips filled in. An FBX with one take gives its file name, because Mixamo takes are all named alike (`mixamo.com` or `Scene`).
3. In the Inspector press **Generate**. It takes about 10 seconds. The bridge clip appears next to the recipe, named after it.
4. Watch it right away in the Inspector preview (see below), no Play mode needed. **Preview on character** still plays it on your SA character; that one needs the character in the scene and enters Play mode.

The recipe starts from the **end** of the first clip and moves to the **start** of the second. That is what you want when the two clips play one after the other.

## Change it

- **Start from / Move to**: any clip, and a place in it: end of clip, start of clip, or a time in seconds. A time lets you leave a clip mid-motion.
- **Add another goal**: chain moves. The character goes to goal 1, then goal 2, up to four.
- **Duration**: how long each move takes, from 0.6 to 1.9 seconds. A longer transition needs more goals.
- **Land exactly on the goal**: keeps the end pose on target. Turn it off for a looser landing.
- **Let the model pick the length**: the model chooses how long each move takes, within one step (about 0.13 s) of
  Duration. The result is still repeatable: the same recipe gives the same clip.
- **Swap start and goal**: flips the direction.

When you change anything the recipe says *Settings changed since the last generate*. Nothing is regenerated behind your back; press **Generate again** when you are ready. The result is repeatable: the same recipe gives the same clip.

## Preview without Play mode

Once the recipe has a result, the Inspector shows a preview strip under **Result**:

- **Play / Pause** and a **scrub slider** with the time and length. Dragging the slider pauses.
- A small 3D view with **your character** playing the clip, root motion included, over a ground grid. Drag in it to orbit, scroll to zoom. The camera follows the hips. The character is the recipe's **Character** field, filled in from a Unity clip source; with no character the view shows the SA skeleton instead.
- **Ghost start and goal** draws the start pose in faded green and the goal pose in faded orange, on your character's mesh (or on the SA skeleton when there is no character), so you can see how close the bridge leaves and lands. Each ghost stands where the bridge starts or ends, at its own height.
- **Show in Scene view** draws the same skeleton and ghosts as lines in the Scene view, at the scene origin, while the recipe is selected.

The preview reads the clip directly and never adds anything to your scene. **Preview on character** (Play mode) also leaves the open scene untouched: its player exists only while playing.

## Preview on character (Play mode)

This plays the result on an SA character in the running ECS world, the way the game will. It needs one SA shared-family character in a SubScene of the open scene. Set it up once per character:

1. Select the character model in the Project window and choose `Tools > SA > Set Up Character`. It saves `Assets/SA/Characters/<model> SA.prefab` and leaves your model unchanged. A warning `NO CLIPS FOUND` is fine here.
2. Open that prefab.
3. On **Rig Authoring** set **Engine** to **Cpu**.
4. **Add Component > Shared Family Authoring**. Drag the Rig Authoring component into **Rig**, and add one clip of that character (any Humanoid take) to **Clips**. Save the prefab.
5. Drag the prefab into your scene, right-click it in the Hierarchy and choose **New Sub Scene > From Selection...**, and save the scene.
6. Select the recipe and press **Preview on character**. Unity enters Play mode and the character plays the bridge in a loop. The temporary `Inbetween Preview` object's **Status** says *Playing <recipe>*, or what it is waiting for.

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

Every message in the recipe Inspector and the First Bridge window names the problem and the fix. The common ones:

| You see | What to do |
|---|---|
| *Inbetween needs its tools before it can generate. Missing: ...* | The message says where each file goes: the tools archive unpacked as `SaInbetween` next to `Assets`, the model in `SaInbetween/model`, `soma_rest.pose` in `SaInbetween`. **Open Project Settings** shows each file as found or missing. |
| *The model file is not the one Inbetween needs* | The model is a different version or an unfinished download. Download `mmm_v6_soma_fp32.onnx` again (757,864,273 bytes). |
| *This character has no Humanoid Avatar* (yellow) or *Inbetween cannot find a humanoid skeleton on it* | Select the FBX, **Rig > Animation Type: Humanoid**, **Apply**. Non-humanoid rigs are not supported. |
| *'X' is a Legacy clip* | Same fix: set the FBX to Humanoid (or Generic). |
| *... is at frame N, past the end of 'X' (frames 0 to M)* | Choose a frame up to M; the Animation window shows the clip's frames. |
| *The FBX SDK native library could not load because libxml2.so.2 is missing* | Linux only, Export > FBX only: `docs/install.md` step 4. |
| *Generating stopped because Unity reloaded its scripts* | A script changed or Play mode started. Nothing was written; press Generate again. |
| *Settings changed since the last generate (or a source clip changed)* | The result is stale. Press **Generate again**. |
| *Cancelled* | Nothing was written; the previous result, if any, is unchanged. |
| *... failed (exit code N): ...* | Press Generate again. If it repeats, press **Copy diagnostics** and **Report a bug**. |

- Undo (ctrl-Z) right after **Generate** puts back the previous clip and `.anim` (or removes them after a first Generate).
- Renaming or moving a recipe takes its result files (`.saclip`, `.anim`, `_Humanoid.anim`, `.fbx`) along. Moving or renaming the source clips does not make the recipe out of date.
- Play mode does not start while a recipe is generating; press Play again when it says *Up to date*.
- Result looks off at the seam: use **At time** on the start so the bridge leaves the clip a little earlier, or lengthen the duration.
- The character does not move in Preview: the scene needs exactly one SA shared-family character in a SubScene (see "Preview on character" above). The player's Status field says what it is waiting for. A Console error `RigAuthoring Engine must be Cpu for timeline rigs` means step 3 there was skipped.
- *Cannot preview this clip*: the result file is not an SA humanoid clip, or it is damaged. Generate again.

## Use your own character and clips (Mixamo, Synty, any Humanoid)

1. Drag the FBX files (the character and its animations) into the Project window. Select them all, and in the Inspector's **Rig** tab set **Animation Type** to **Humanoid** (Avatar Definition: *Create From This Model*), then press **Apply**. Unity imports a new FBX as Generic; without a Humanoid avatar the bones are matched by name only and **Export > Humanoid clip** stays greyed out. The recipe shows a yellow note while a source character has no Humanoid avatar.
2. Unfold the animation FBX files, select the start take, ctrl-click the goal take and ctrl-click the character model, then choose `Assets > Create > SA > Inbetween Bridge`. Both sources become **Unity clip** sources on the model each take came from, and the recipe's **Character** is the model you picked.
3. On **Start from** choose **at frame** and type the frame number you see in the Animation window, for example frame 60 of a jump.
4. Press **Generate**. The first Generate converts each clip once (cached in `Library/SaInbetween`); later runs reuse it.

You can also build the recipe by hand: in **Start from** or **Move to** change the source kind to **Unity clip**, then pick the **Unity clip** (an FBX take or a `.anim`) and the **character** it belongs to.

The start keeps the clip's real speed, including running and jumping, so the bridge continues the motion instead of restarting it. The converter maps bones through the Humanoid avatar, so Mixamo's `mixamorig:` names need no setup.

Measured on a Mixamo character (Neophyte, Humanoid) from a clean project: start = frame 40 of a stab, goal = start of a sword slash, Duration 1.0 s: the generated clip has 30 frames, Generate took under 10 seconds, and the exported clips match the source frames to 0.04 degrees at both seams (bone directions), while the two source frames differ by 38 degrees on average.

## Save the result as a Unity clip

**Also save a Unity clip (.anim)** is on by default: every Generate and every Keep writes `<recipe>.anim` next to the recipe for the Character. It shows under **Result** as **Unity clip**; open it in the Animation window or drop it on a Timeline track like any clip. The recipe says *out of date* when that file is missing or older than the result. It starts at the origin facing forward, so place it where the jump left off. It is a Generic clip: play it on the character with its Rig set to Generic, or on rigs with identical bone names.

The **Export** buttons make the other formats:

- **Export > Animation Clip** writes the Generic `.anim` again now (also right-click a `.saclip` > Export Animation Clip).
- **Export > Humanoid clip** writes `<recipe>_Humanoid.anim` with muscle curves for the Character's Humanoid avatar. It plays through any Humanoid Animator, also on other characters. The avatar spreads arm and leg twist its own way: bones point the same way as the Generic clip (within 0.6 degrees on the Synty test), but the roll of the upper arm and forearm can differ by up to 35 degrees.
- **Export > FBX** writes `<recipe>.fbx` (the Character with the clip as a Generic take) for other tools. It matches the `.anim` within about 1 degree. It does not reimport as a Humanoid clip (Unity reports a hierarchy mismatch because the exporter adds a root node): for Humanoid retargeting use the Humanoid `.anim`. It needs the package `com.unity.formats.fbx` (Package Manager); on Linux the FBX SDK also needs `libxml2.so.2` (see `docs/install.md`).

Measured on a Synty jump, starting mid-air at the apex (frame 41, hips 1.43 m) and landing: the bridge's first frame matches the jump to 0.0001 cm, it ends on the landing pose exactly, hips height is within 6.5 cm of the real animation, and touchdown lands on the same frame. When the start is a fast run and the goal is standing still, the bridge slows down over the 0.8 s, as it should; make the goal a running pose to keep speed.

## Use a pose instead of a clip

- **Capture**: pose your character in the scene (by hand, or by scrubbing a clip in the Animation window), select it, and press **Capture** next to the start or goal. A two-frame `<recipe>_<start|goal>_pose.saclip` appears next to the recipe and becomes that source, used at its start. `Tools > SA > Inbetween > Capture Pose From Selection` does the same without a recipe. The pose is read relative to the character's own root, so a root-motion clip's height above the floor is not part of it.
- **Pose file**: a `.pose` file (77 SOMA joints, as `sa-soma-bake pose` writes) imports as a pose asset. Choose **Pose** as the source kind and pick it.

Measured on Synty: a capture of frame 41 of the jump matches that clip frame to 0.004 degrees, and a bridge to it lands on it to 0.004 degrees.

## Bridge two clips on a Timeline

Needs the package `com.sa.inbetween.sa.timeline` (Timeline 6.7.0). Your clips stay ordinary Animation Track clips, and the bridge is one too, so it scrubs in the Timeline window and plays in Play mode like any clip.

1. Put the two clips on the same Animation Track, the first one ending before or where the second starts. Bind the track to your character (a model or prefab instance with an Animator).
2. Select the first clip (or both clips), right-click and choose **Add Inbetween bridge**. `Tools > SA > Inbetween > Add Bridge Between Selected Timeline Clips` does the same.
3. Wait about 10 seconds. A clip named *Bridge A > B* appears between them.

What it does:

- **Start** is the frame A ends on (its trimmed end, at A's speed; *end of clip* when A plays to its last frame). **Goal** is the frame B starts on (its clip-in).
- **Length** is the gap between A and B, kept between 0.6 and 1.93 seconds. With no gap (the clips touch or overlap) the bridge is 1 second. B and every clip after it on the same track move so that B starts exactly where the bridge ends: right to open room, left to close a gap longer than 1.93 seconds. The generator works in whole steps, so the bridge can be a few frames longer or shorter than the gap (0.8 s asked gave 0.833 s, 1.333 s gave 1.233 s); B moves by the difference.
- **Root**: the bridge is placed so the hips start exactly where A's end, and B (with every later clip, by the same amount) is moved so its hips start where the bridge's end. Position and turn only, the same as Timeline's *Match Offsets to Previous Clip*, but measured on the hips of a hidden copy, so your scene is not touched.
- The recipe is saved in `<Timeline>_Bridges/` next to the timeline, as `Bridge_<A>_to_<B>.asset` with its clips. On a Humanoid character the track plays `<recipe>_Humanoid.anim` (foot IK off on that clip: the bridge has no IK goal curves); on a Generic one it plays `<recipe>.anim`.
- One Undo step undoes the whole edit (the new clip, the ripple and the offsets).

To change a bridge, change the clips around it (trim A, change B's clip-in, or drag B to make the gap longer or shorter), select the bridge and choose **Regenerate Inbetween bridge**. It reads the clip before and the clip after it again, regenerates only if something changed, and updates the same Timeline clip. The link from the Timeline clip to its recipe is the file name: keep the `.anim` next to its recipe and do not rename either.

Measured on Synty (jump `A_Jump_Walking_RootMotionVertical_Masc_Full`, then `A_Idle_Standing_Masc` 0.8 s later, sampled through the timeline): the hips move 0.0000 cm and 0.0000 degrees across both seams (feet, hand and head below 0.03 cm), against 1.7 cm and 3.5 degrees at the start and 3.9 cm and 13.8 degrees at the end without the root matching.

## Author a generation on a Timeline

Needs `com.sa.inbetween.sa.timeline` 0.2.0. Instead of a recipe, you lay the generation out in the Timeline window: what the
character does before, the gap to fill, what it does after, who it is for and which files to write. **Bake Timeline** makes them.
The files are the same ones the Bridge and the recipe make (`.saclip`, `.anim`, `_Humanoid.anim`, `.fbx`), and the result lands on
the timeline so you can scrub and play it.

### First timeline in two minutes

1. **SA > Inbetween > Create Generation Demo** builds `Assets/SA Inbetween Generation Demo/` from your own Humanoid character and a
   jump in your project (the same picks as Create Demo Scene): two timelines and a scene, with the Timeline window open.
   Or select two clips (and your character model) in the Project window and choose
   **Assets > Create > SA > Inbetween Generation Timeline**.
2. The timeline has three rows:

   | Row | Clips |
   |---|---|
   | **Steps** (SA Inbetween Generation Track) | **Source** (the jump), **Generate** (the gap to fill), **Source** (the landing) |
   | **Bake** (SA Inbetween Generation Track) | **Retarget / Bake** (your character) and **Export** (which files), stretched over the Generate clip |
   | **Generated** (Animation Track) | empty until you bake; then the generated clip at the Generate clip's time, with the Unity clips it starts from and lands on around it (named *... (source)*), so the row plays from start to end |

3. Select the Generate clip. The Inspector says what it will make, for example *Writes 'A_Jump_to_A_Idle': from 'A_Jump' at 2.00 s
   (frame 60) to 'A_Idle' at its first frame, 0.80 s, on PolygonSyntyCharacter.* Press **Bake Timeline** (about 10 seconds).
4. Press Play in the Timeline window: the Generated row plays the jump, the new motion and the landing on your character, one
   after the other. The demo's Export clip also writes an `.fbx` when the FBX Exporter can.

### How the timeline is read

Time is the wiring between clips. Nothing points at anything else; where a clip sits says what it is for.

- A **Generate** clip makes the motion that fills its own time. Its length is the length of the motion (0.6 to 1.93 s per move).
- It **starts** from the pose at its first frame: the Source clip under that time (its pose at that moment), else the Source clip that
  ended last before it (its last frame). So leaving a jump at its top is: let the Generate clip start where the jump is at its top.
- It **lands** on the pose at its end: the Source clip under that time, else the next Source clip (its first frame).
- **Start Pose** over the start of a Generate clip, or **Goal Pose** over its end, replaces that pose: *Source* (the source there),
  *Source Frame* (frame N of that source, as in the Animation window), or *Pose Asset* (a `.pose` file or a captured pose).
- A **Goal Pose** wholly inside a Generate clip is a goal on the way: the character passes through it, up to four moves in all.
  Every move takes the same time, so the Inspector says when such a goal is really reached.
- **Retarget / Bake** and **Export** apply to the Generate clips they overlap. Without a Retarget / Bake clip the character is the one
  bound to the track; without an Export clip you get the `.saclip`, `.anim`, `_Humanoid.anim` and the clip on the Generated row.
- **Label** puts an asset label on every file of the Generate clips under it (search the Project window with `l:YourLabel`).
- Rows are only for tidiness. The order things happen in is fixed: sources, poses, generate, retarget, export, label.

### The clips and their Inspector fields

| Clip | Fields |
|---|---|
| Source | Kind (Unity Clip, Sa Clip, Pose), the clip or pose, Character (optional). Trim it and change its speed like any clip |
| Start Pose, Goal Pose | From (Source, Source Frame, Pose Asset), Frame, Pose |
| Generate | Land Exactly, Keep (As Set, Shorter, Longer, Other Landing, Model Picks: the model chooses the length within one step), Variations (1 to 4) |
| Retarget / Bake | Character (model or prefab), Rig (Auto, Humanoid, Generic: which clip goes on the Generated row), Foot IK |
| Export | `.anim`, Humanoid `.anim`, FBX, Place on Timeline, Place Sources (the clips around it, hips matched) |
| Label | Label |

**Variations** makes that many versions, the kept one first: with Variations 3 and Keep *As Set* you get *As Set*, *Shorter* and
*Longer*. They are written next to each other (`Land.saclip`, `Land_Shorter.saclip`, ...). Switch **Keep** to one of them and bake
again: nothing is generated, the other version is exported and put on the timeline at once.

### Where the files go

Next to the timeline, in `<Timeline>_Generated/`: one recipe per Generate clip, named after the clip
(`<name>.asset`), and its files (`<name>.saclip`, `<name>.anim`, `<name>_Humanoid.anim`, `<name>.fbx`). A Generate clip still called
*Generate* is named after what it joins (`A_Jump_to_A_Idle`). Rename the clip in the Timeline window to name its files. The recipe is
a normal recipe: select it to preview, make variations or export by hand.

`<Timeline>.genplan.json` in the same folder is what the bake read, as text: every clip with its frame numbers and values, every
generation with its start, goals, length and files, and a checksum. The same timeline always gives the same file.

Baking again regenerates only what changed. Bake from the Inspector of a Generate clip or of a Generation Track, from the Timeline
window's right-click menu (**Bake Generation Timeline**), from **SA > Inbetween > Bake Generation Timeline**, or with the timeline
selected in the Project window, **Assets > SA > Bake Generation Timeline**.

### If something is wrong

A clip with a problem turns red in the Timeline window; point at it to read why, or select it to read it in the Inspector. Every
message names the clip, what is wrong and what to do. The common ones:

| You see | What to do |
|---|---|
| *there is nothing to start from* | Put a Source clip that ends at or covers the Generate clip's start, or a Start Pose over its start |
| *there is nothing to land on* | Put a Source clip that starts at or covers its end, or a Goal Pose over its end |
| *one move takes at most 1.93 s* | Shorten the Generate clip, or put a Goal Pose inside it to make two moves |
| *one move takes at least 0.60 s* | Make the Generate clip longer |
| *two Source clips meet its start* | Two sources end at the same moment on different rows; move or delete one |
| *no character is set to play it on* | Bind the track to your character in the scene, or set Character on a Retarget / Bake clip |
| *another Generate clip also writes 'X'* | Rename one of the two Generate clips |
| *it overlaps the Generate clip ...* | Two generations cannot fill the same time; move one |
| *it covers no Generate clip, so it is not used* | Stretch the Retarget / Bake, Export or Label clip over the Generate clips it is for |
| *Inbetween needs its tools before it can bake* | The tools folder: `Edit > Project Settings > SA Inbetween` |

### With Tl

If the project also has Tl (`com.tl.unity`), the Timeline asset's **Tl** section works on generation timelines too:
**When it ends: Loop** makes a Generate clip at the very end land on the first source (a loop), and **When it ends: Next** bakes the
timeline named in **Play next** right after this one. Without Tl nothing changes.

## What it does not do yet

- Generation timelines bake only when you press Bake. On the Generated row each clip is moved so its hips continue from the clip
  before it sideways and in facing; its height stays its own, so the feet stay on the ground and a small step up or down can show
  at a seam (Synty jump: 3.6 cm at the top of the jump, 6.1 cm at touchdown). Source clips play only on the Generated row; the
  Source clips on the Steps row are layout and do not move the character.
- Timeline bridges are not regenerated by themselves when a neighbour changes; press **Regenerate Inbetween bridge**. A bridge that is generating when Unity reloads scripts is not placed; regenerate it.
- The neck is a single bone on the SA rig, so a source clip's neck is smoothed slightly when it is read back. The head's position can move by up to about 2 cm at the seam.

## Tools folder, for the technical artist

`tools/make_pose_library.py` writes `soma_rest.pose` (a SOMA skeleton rest pose, which the baker needs for the joint offsets and finger rotations) together with a sample pose library:

```bash
~/GitHub/motionbricks/.venv/bin/python tools/make_pose_library.py --out ~/sa-inbetween-library
cp ~/sa-inbetween-library/soma_rest.pose ~/sa-inbetween-tools/
```

The model, the rest pose and the sample poses are NVIDIA data. Keep them out of git.
