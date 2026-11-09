using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SA.Inbetween.Clips
{
    [DisallowMultipleComponent]
    public sealed class SaClipPlayer : MonoBehaviour
    {
        public SaClipAsset Clip;
        public bool Loop = true;
        public bool PlayOnStart = true;
        public string Status = "Waiting for the character...";

        World world;
        Entity entity;
        int clipIndex = -1;
        bool wanted;

        public bool IsPlaying => clipIndex >= 0;

        bool previousRunInBackground;

        void OnEnable()
        {
            wanted = PlayOnStart;
            previousRunInBackground = Application.runInBackground;
#if UNITY_EDITOR
            Application.runInBackground = true;
#endif
        }

        void OnDisable()
        {
            Stop();
#if UNITY_EDITOR
            Application.runInBackground = previousRunInBackground;
#endif
        }

#if UNITY_EDITOR
        void LateUpdate()
        {
            if (clipIndex < 0)
                return;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
#endif

        public void Play()
        {
            wanted = true;
        }

        void Hold()
        {
            if (world == null || !world.IsCreated || !world.EntityManager.Exists(entity) || !world.EntityManager.HasBuffer<SharedContribution>(entity))
                return;
            var manager = world.EntityManager;
            manager.CompleteAllTrackedJobs();
            var contributions = manager.GetBuffer<SharedContribution>(entity);
            for (var i = 0; i < contributions.Length; i++)
                if (contributions[i].Clip == clipIndex && contributions[i].Weight >= 1f)
                    return;
            SharedFamilyControl.PlaySingle(ref contributions, clipIndex);
        }

        public void Stop()
        {
            if (clipIndex >= 0 && world != null && world.IsCreated)
                SaClipRuntime.Detach(world.EntityManager, entity, clipIndex);
            clipIndex = -1;
            world = null;
            wanted = false;
            Status = "Stopped";
        }

        void Update()
        {
            if (clipIndex >= 0)
            {
                Hold();
                return;
            }

            if (!wanted)
                return;
            if (Clip == null || Clip.Bytes.Length == 0)
            {
                Status = "No clip assigned";
                return;
            }

            var active = World.DefaultGameObjectInjectionWorld;
            if (active == null || !active.IsCreated)
            {
                Status = "Waiting for the animation world...";
                return;
            }

            var manager = active.EntityManager;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<SharedClipAsset>(), ComponentType.ReadOnly<SharedContribution>());
            var count = query.CalculateEntityCount();
            if (count != 1)
            {
                Status = count == 0 ? "Waiting for the character..." : "Found " + count + " shared-family characters; this player needs exactly one";
                return;
            }

            using var entities = query.ToEntityArray(Allocator.Temp);
            if (!SaClipRuntime.TryAttach(manager, entities[0], Clip.Bytes, Loop, out clipIndex, out var error))
            {
                Status = error;
                wanted = false;
                return;
            }

            world = active;
            entity = entities[0];
            Status = "Playing " + Clip.name;
            wanted = false;
        }
    }
}
