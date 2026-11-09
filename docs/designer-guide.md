# Designer guide: make a motion in Unity

You do not need a terminal. You pick where a character starts and where it ends, and Inbetween Studio makes the motion between them, bakes it and plays it on your character.

## One-time setup

1. Put the packages `com.sa.unity` and `com.sa.inbetween.sa` in your Unity project.
2. Get a **tools folder** from your technical artist. It is one folder that holds:
   - `sa-inbetween` (the generator),
   - `sa-soma-bake` (turns a motion into an SA clip),
   - `mmm_v6_soma_fp32.onnx` (NVIDIA's model),
   - `humanoid-v1.safamily` (the SA character family),
   - `library/` (the pose library, with a `library.json`).
3. Open `Window > SA > Inbetween Studio`, open **Setup** and press **Browse** to choose that folder. Every line should say *found*. Studio remembers the folder.

## Make a motion

1. Choose a **Start pose** and a **Goal pose** from the lists, for example Walk 1 to Dance vogue 2.
2. Press **Generate motion**. It takes a few seconds. The clip is saved to `Assets/Generated/` and imported.
3. Press **Preview on character**. Studio adds a player to the scene and enters Play mode. The scene needs your SA character in it.
4. Press **Add another goal after this** to chain moves: the character goes to goal 1, then goal 2, and so on.

## Make it feel different

Open **Advanced**:
- **Shortest / Longest** control how long the move takes (more is slower).
- **Land exactly on the goal** keeps the final pose on target. Turn it off for a looser, more natural landing.

## If something goes wrong

- A red *missing* in Setup: the tools folder lacks that file. The line names it.
- *Generating failed* or *Baking failed*: the log under the button says why. The most common cause is a wrong model file.
- The character does not move: the preview needs exactly one SA shared-family character in the scene. The player's Status field says what it is waiting for.

## Build your own pose library

The library is just poses cut from motion clips. `tools/make_pose_library.py` builds one from the BVH clips that ship with MotionBricks:

```bash
~/GitHub/motionbricks/.venv/bin/python tools/make_pose_library.py --out ~/sa-inbetween-library
```

The poses are NVIDIA sample data. Keep the library on your machines and do not commit it.
