# SA Inbetween for SA Animation

Imports `.saclip` files (baked by `sa-soma-bake`) and plays them on SA shared-family characters. Also makes new clips by bridging existing ones.

## Bridge two clips

Select two clips, then `Assets > Create > SA > Inbetween Bridge`. In the new recipe's Inspector press **Generate**. The result is a normal clip next to the recipe.

The Inspector previews the result in Edit mode: play, pause and scrub it on the recipe's Character (or on the SA skeleton, with the start and goal poses as faded ghosts, when no character is set), in a small 3D view and in the Scene view. No Play mode needed, and nothing is added to the open scene.

**Make variations** generates up to four versions (shorter, longer, model-picked length, other landing) shown as cards; **Keep this one** makes a version the result. Keep can be undone, including the clip file.

Set the tools folder once in `Edit > Project Settings > SA Inbetween`, or put it in `<project>/SaInbetween`. See `docs/designer-guide.md` in the repository.

## Start from a Unity clip

A start or goal can be a Unity clip instead of a `.saclip`: choose **Unity clip**, pick the clip (an FBX take or `.anim`, Humanoid or Generic) and the character it plays on. Generate converts it to a cached `.saclip` first. Frame numbers count at the clip's own frame rate. `Assets > SA > Convert to .saclip` writes a visible `.saclip` for a selected clip and character.

## Poses

**Capture** next to a source reads the character selected in the scene and writes a two-frame `<recipe>_<role>_pose.saclip` used as that source (also `Tools > SA > Inbetween > Capture Pose From Selection`). `.pose` files import as `SaPoseAsset` and can be a source of kind **Pose**.

## Export

**Also save a Unity clip (.anim)** (on by default) writes `<recipe>.anim` after every Generate and Keep: a Generic clip for the Character, starting at the origin and facing +Z. It plays on a Generic setup of that model; a Humanoid Animator ignores transform curves on human bones. **Export > Humanoid clip** writes `<recipe>_Humanoid.anim` with muscle curves for the Character's avatar. **Export > FBX** writes `<recipe>.fbx` when the FBX Exporter package is installed (`SA_FBX`). A `.saclip` can be exported to a Generic clip from its context menu, or with `Assets > SA > Export Animation Clip` while the clip and a character are selected.

## Play a clip

Add `SaClipPlayer` to a GameObject, assign a clip, and enter Play mode with one SA shared-family character in the scene.

Requires `com.sa.unity` 0.3.2 (`FamilyClips`).
