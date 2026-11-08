# Using sa-inbetween with the SA ECS runtime

Generation is authoring-time and asynchronous. The ECS hot path only ever plays baked clips.

```text
request -> PortedGenerator (worker thread) -> .saib -> sa-soma-bake -> SA shared clip -> clip blob
                                                                         ECS hot path: samples the blob
```

## Rules

1. Never call `GenerateAsync`, `Encode` or `Decode` from a system `OnUpdate`, a Burst job or any
   per-frame code. A generation costs tens of milliseconds to seconds and allocates (public
   `Encode`/`Decode` build `Block` and `Pose` values). Run it on a worker thread or in a process.
2. The only thing that crosses into the ECS is a finished clip. Swap it in at a sync point as an
   ordinary blob asset, the same way any other clip is added. No managed object stays in a
   component.
3. Inference is single-threaded per network instance (`OnnxNetwork` is not thread-safe, the
   TensorRT engine neither). Use one generator per worker and queue requests.
4. A request is pure data and `Recipes.Hash` identifies it. Cache baked clips by recipe hash;
   identical requests give bit-identical motion, so repeats never need to run again.
5. Generated clips are 18 to 58 frames at 30 fps. Chain with several `--target` options, or
   crossfade between baked clips in the runtime, instead of generating per frame.
6. The retarget and bake run in sa-motion (`tools/SA.Soma.Bake`, `SA.Soma`), not in the ECS
   runtime. Lanes join only through `.saib` and the SA clip file.

## Where each step runs

| Step | Where | Allocates | On the hot path |
| --- | --- | --- | --- |
| Generate | worker thread or CLI | yes | no |
| Bake to SA clip | worker thread or CLI | yes | no |
| Load clip blob | sync point | once per clip | no |
| Sample and blend | ECS systems (existing SA runtime) | no | yes |

The engine kernels themselves (encode, decode, correction, IK) are span-based and allocate zero
bytes per call after warmup, so a future in-process real-time use can call them from a dedicated
thread without GC pressure. The public wrappers are not zero-allocation by design.
