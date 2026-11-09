# SA Inbetween for SA Animation

Imports `.saclip` files (baked by `sa-soma-bake`) and plays them on SA shared-family characters. Also makes new clips by bridging existing ones.

## Bridge two clips

Select two clips, then `Assets > Create > SA > Inbetween Bridge`. In the new recipe's Inspector press **Generate**. The result is a normal clip next to the recipe.

The Inspector previews the result in Edit mode: play, pause and scrub it on the SA skeleton, with the start and goal poses as faded ghosts, in a small 3D view and in the Scene view. No character or Play mode needed.

**Make variations** generates up to four versions (shorter, longer, model-picked length, other landing) shown as cards; **Keep this one** makes a version the result. Keep can be undone, including the clip file.

Set the tools folder once in `Edit > Project Settings > SA Inbetween`, or put it in `<project>/SaInbetween`. See `docs/designer-guide.md` in the repository.

## Play a clip

Add `SaClipPlayer` to a GameObject, assign a clip, and enter Play mode with one SA shared-family character in the scene.

Requires `com.sa.unity` 0.3.0.
