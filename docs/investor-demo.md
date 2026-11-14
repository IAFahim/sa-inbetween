# Investor demo: a 10-minute script

What to click, what appears, how long it takes, what to say when something is slow, and what the product does not do
yet. Rehearsed on this Linux PC (AMD Ryzen 5 8500G, RTX 3060, Unity 7000.0.0a16) from a clean project on 2026-11-15;
every number below is from that run. Receipts: IAFahim/sa-inbetween#3.

## The evening before (15 minutes, once)

1. Start Unity with the FBX library link, then open the demo project:
   `LD_LIBRARY_PATH=~/.local/lib/unity-libxml2 unityhub` and open `~/investor-demo` (or start the Editor directly:
   `LD_LIBRARY_PATH=~/.local/lib/unity-libxml2 ~/Unity/Hub/Editor/7000.0.0a16/Editor/Unity -projectPath ~/investor-demo`).
   Without the link everything works except the FBX file (Save says so in one line).
2. **Edit > Project Settings > SA Inbetween**: all five lines say *found* (Generator, Baker, Model, Character family,
   Rest pose).
3. Run the whole script below once. That warms the caches (the first Generate reads both clips; later ones skip that).
4. **SA > Inbetween > Demo Backup > Save**. It copies the finished demo (`Assets/SA Inbetween Demo`,
   `Assets/SA Inbetween Generation Demo` and the SA materials) to `~/investor-demo/SA Inbetween Demo Backup`. The
   First Bridge window now shows **Use the backup**.
5. Leave Unity open on the demo scene, or quit it; both are fine.

The project already holds a pre-generated set made this way: the mid-air jump bridge (`.saclip`, Generic and Humanoid
`.anim`, `.fbx`), the Timeline demo baked (jump, bridge and landing on the Generated track, plus the three-takes
timeline), and a second bridge in `Assets/SA Inbetween Demo/More Bridges`. All of it is in the backup.

## The script

| Time | Click | You see | Say | If it goes wrong |
|---|---|---|---|---|
| 0:00 | **SA > Inbetween > Create Demo Scene** | The **First Bridge** window and a new scene: a ground, a camera, the Synty character. The window says what it picked: *From 'A_MOD_BL_Jump_Walking_RMV_Masc_Full' at frame 41, the top of the jump (hips 0.80 m up) to the first frame of 'A_MOD_BL_Land_IdleHard_Masc', in 0.8 s* (about 4 s) | "We pick two moments from clips the team already has: the top of a jump, and a landing." | A Save dialog about the open scene: choose **Save** (or Don't Save if it is a scratch scene). |
| 0:45 | **1 Generate** | *Generating...*, a step counter (Reading the start, Reading the goal, Generating the motion, Baking the clip), then *Done* (about 6 s warm, about 9 s on the very first run) | "A neural motion model writes the in-between on the CPU. The same input always gives the same bytes." | Over 30 s, or a red message: press **Use the backup** at the bottom of the window (one second); continue at 1:30. Every step stops by itself after 2 minutes with a plain message. |
| 1:30 | **2 Preview**, drag in the view to orbit | The character leaves the top of the jump and lands, looping, inside the window; a faded green ghost shows the start pose, a faded orange one the goal | "No Play mode, nothing added to the scene. Green is where it starts, orange where it must land." | The very first frame after Unity starts can be dark for an instant; it lights up on the next frame. |
| 2:15 | **3 Save** | The window lists `Jump_MidAir_to_Landing.anim`, `_Humanoid.anim`, `.fbx`, `Inbetween Demo.controller` and the scene (under 3 s with the FBX) | "Standard Unity files. The FBX goes to Maya, Blender or any other tool." | *FBX not written*: Unity was started without the library link. The `.anim` files are fine; say the FBX is the same clip, shown later from the backup. |
| 2:45 | **Play** (top of the Editor), then Play again to stop | The Game view: the character jumps from mid-air and lands on the ground, in a loop (Unity's Animator, Humanoid clip) | "This is plain Unity playing the clip we just made." | Character floats or does not move: press **Use the backup** and Play again. |
| 3:30 | **4 Play in SA runtime** | Unity enters Play mode; a second character appears on the right, driven by SA's ECS animation runtime from the `.saclip`, next to the Animator one on the left (setup about 1 s the first time, then SubScene baking a few seconds) | "Same motion, now in our ECS runtime, the one games ship with." | Error about a SubScene: press **Make demo scene again**, then 4 again. Still stuck: skip; this step is optional. |
| 4:30 | Stop Play. In the Project window open `Assets/SA Inbetween Demo`, unfold `Jump_MidAir_to_Landing.fbx` | The `.saclip`, the two `.anim` files and the FBX with its animation take (0.83 s) | "Every format a pipeline needs, from one click." | Nothing to fix here. |
| 5:00 | **SA > Inbetween > Create Generation Demo** | The Timeline window with three rows: **Steps** (the jump, a Generate clip, the landing), **Bake** (Retarget / Bake and Export), **Generated** (empty) (about 2 s); a dialog explains the next click | "Designers can lay the same thing out on a timeline, with clips and Inspector values only." | Dialog about saving the scene: **Save**. |
| 6:00 | Select the **Generate** clip; in the Inspector press **Bake Timeline** | The Inspector says *Writes 'A_MOD_BL_Jump_Walking_RMV_Masc_Full_to_A_MOD_BL_Land_IdleHard_Masc' ... 0.80 s, on SidekickSyntyCharacter*; the bake finishes in about 7 s (1.5 s when it was baked before) and the **Generated** row fills: the jump, the generated landing, then the landing clip | "The bake writes the same files as the window, and puts the result back on the timeline." | Over 30 s: **SA > Inbetween > Demo Backup > Restore** (the baked timeline is in the backup), then reopen `Assets/SA Inbetween Generation Demo/Generation Demo.unity`. |
| 7:00 | Press Play in the Timeline window (or scrub the playhead) | The character crouches, jumps, reaches the top, the generated motion carries it down, and it lands and stands; the hips line up at each seam | "Only the middle part was generated; it joins two clips that were never made for each other." | The character holds one pose: select the **Generation** object in the Hierarchy so the Timeline window plays it with its director. |
| 8:00 | Open `Jump to Landing, Three Takes.playable` (same folder), select its Generate clip: Variations is 3, Keep is As Set | Bake makes three versions (as set, shorter, longer) in about 20 s; switch **Keep** to Shorter and bake again: under 2 s, nothing generated | "Variations are free to try: switching the kept take does not generate again." | Skip if time is short; it is in the backup. |
| 9:00 | Close: show `docs/known-limitations.md` or the list below | | "What it does not do yet, honestly:" | |

Total: about 10 minutes with talking; about 2 minutes of waiting on the machine.

## Reset between rehearsals

**SA > Inbetween > Demo Backup > Restore** puts the demo folders back exactly as saved (files and their GUIDs) and reopens
the demo scene; about one second. Files made since the backup in those folders are removed. Your other assets are not
touched.

## Known limits (say them before anyone asks)

- **Humanoid characters only.** Mixamo, Synty and other humanoids; no animals or props.
- **Generation runs on the CPU** in about 4.6 s for 50 frames (cold); the first run per recipe also reads the clips.
  It does not use the GPU yet.
- **Transitions are 0.6 to 1.93 s per move**, in steps of about 0.13 s; longer ones use more goals.
- **Seams are not perfect in height.** The generated hips height is within about 7 cm of real motion. On the Timeline
  the clips line up sideways and in facing and keep their own height, so the feet stay on the ground; a step of a few
  centimetres can show (3.6 cm at the top of this jump, 6.1 cm at touchdown).
- **Arm twist** on the Humanoid `.anim` can differ from the Generic clip by up to 35 degrees on the upper arm and forearm
  (Unity's avatar spreads twist its own way).
- **Windows has not been run by a person**; the tools start on GitHub's Windows runner. macOS is not built.
- **The motion model is NVIDIA's** (MotionBricks, NVIDIA Open Model Agreement); each user downloads it. Our code is
  Apache-2.0.
- **Nothing regenerates by itself**: a changed recipe or timeline says *out of date* until Generate or Bake is pressed.

## If Unity itself misbehaves

- A modal Save dialog appears when a scene is replaced: **Save** keeps the work, **Cancel** keeps everything as it is.
- Unity seems frozen during Generate: it is not; the generator runs in its own process. **Cancel** in the window stops
  it at once.
- Unity crashed: start it again (step 1 of the evening before), then **Demo Backup > Restore**.
