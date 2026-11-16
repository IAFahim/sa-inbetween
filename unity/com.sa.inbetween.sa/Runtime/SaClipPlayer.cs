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
        [Tooltip("Start the clip on its own first frame instead of blending in from whatever the character was doing.")]
        public bool StartExactly = true;
        [Tooltip("Name of the character this player drives: the object that has Shared Family Authoring. Leave empty when the scene has one SA character.")]
        public string Character = "";
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
            if (!SaCharacterPick.TryFind(manager, Character, out var found, out var waiting))
            {
                Status = waiting;
                return;
            }

            if (!SaClipRuntime.TryAttach(manager, found, Clip.Bytes, Loop, StartExactly, out clipIndex, out var error))
            {
                Status = error;
                wanted = false;
                return;
            }

            world = active;
            entity = found;
            Status = "Playing " + Clip.name;
            wanted = false;
        }
    }
}
