using System;
using SA.Editor;
using UnityEditor;
using UnityEngine;
using Pose = SA.Motion.Numerics.Pose;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class CharacterPreview : IDisposable
    {
        PreviewRenderUtility utility;
        GameObject character;
        byte[] bytes;
        GameObject instance;
        FamilyRigPlan plan;
        Pose[] locals = Array.Empty<Pose>();
        SkinnedMeshRenderer[] skins = Array.Empty<SkinnedMeshRenderer>();
        Mesh[] baked = Array.Empty<Mesh>();
        MeshRenderer[] props = Array.Empty<MeshRenderer>();
        int frames;
        float height = 1.8f;

        public float Yaw = 150f;
        public float Pitch = 10f;
        public float Zoom = 1f;
        public string Error { get; private set; } = "";
        public bool Ready => plan != null && frames > 0;
        public float Duration => frames > 1 ? (frames - 1) / SaClipExport.Rate : 0f;
        public GameObject Character => character;

        public void Dispose()
        {
            Release();
            utility?.Cleanup();
            utility = null;
        }

        void Release()
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            foreach (var mesh in baked)
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            baked = Array.Empty<Mesh>();
            skins = Array.Empty<SkinnedMeshRenderer>();
            props = Array.Empty<MeshRenderer>();
            plan = null;
            frames = 0;
            locals = Array.Empty<Pose>();
        }

        public bool Bind(GameObject target, byte[] clip)
        {
            if (target == character && ReferenceEquals(clip, bytes))
                return Ready;
            character = target;
            bytes = clip;
            Release();
            Error = "";
            if (target == null || clip == null || clip.Length == 0)
                return false;
            utility ??= new PreviewRenderUtility();
            instance = UnityEngine.Object.Instantiate(target);
            instance.name = target.name;
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(false);
            baked = new Mesh[skins.Length];
            for (var i = 0; i < skins.Length; i++)
            {
                baked[i] = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skins[i].enabled = false;
            }
            props = instance.GetComponentsInChildren<MeshRenderer>(false);
            foreach (var prop in props) prop.enabled = false;
            if (!FamilyClips.TryPlan(instance, out var planned, out var error) || !SaClipExport.TrySampleLocals(planned, clip, out var sampled, out var count, out error))
            {
                Error = error;
                Release();
                return false;
            }

            plan = planned;
            locals = sampled;
            frames = count;
            height = Height(instance);
            return true;
        }

        static float Height(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 1.8f;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return Mathf.Clamp(bounds.max.y, 0.5f, 4f);
        }

        public void Pose(float seconds)
        {
            if (!Ready) return;
            var frame = Mathf.Clamp(Mathf.RoundToInt(seconds * SaClipExport.Rate), 0, frames - 1);
            var count = plan.BoneCount;
            for (var b = 1; b < count; b++)
            {
                var bone = plan.Bones[b];
                var pose = plan.CanonicalOfBone[b] > 0 ? locals[frame * count + b] : plan.Rest[b];
                bone.localRotation = new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
                if (b == plan.Hips || plan.CanonicalOfBone[b] <= 0)
                    bone.localPosition = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            }
        }

        public bool Orbit(Rect rect)
        {
            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive, rect);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition) && e.button == 0:
                    GUIUtility.hotControl = id;
                    e.Use();
                    return false;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    Yaw += e.delta.x * 0.6f;
                    Pitch = Mathf.Clamp(Pitch + e.delta.y * 0.4f, -10f, 80f);
                    e.Use();
                    return true;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    return false;
                case EventType.ScrollWheel when rect.Contains(e.mousePosition):
                    Zoom = Mathf.Clamp(Zoom * (1f + e.delta.y * 0.04f), 0.3f, 4f);
                    e.Use();
                    return true;
            }

            return false;
        }

        public void Draw(Rect rect, float seconds)
        {
            if (Event.current.type != EventType.Repaint || rect.width < 8f || rect.height < 8f || !Ready)
                return;
            utility.BeginPreview(rect, GUIStyle.none);
            Render(seconds);
            utility.EndAndDrawPreview(rect);
        }

        public Texture2D Snapshot(int width, int height, float seconds)
        {
            if (!Ready) return null;
            utility.BeginStaticPreview(new Rect(0, 0, width, height));
            Render(seconds);
            return utility.EndStaticPreview();
        }

        void Render(float seconds)
        {
            Pose(seconds);
            var hips = plan.Bones[plan.Hips].position;
            var focus = new Vector3(hips.x, Mathf.Max(height * 0.5f, hips.y), hips.z);
            var camera = utility.camera;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SkeletonRenderer.BackgroundColor;
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * (height * 2.6f * Zoom), rotation);
            utility.lights[0].intensity = 1.2f;
            utility.lights[0].transform.rotation = Quaternion.Euler(40f, Yaw - 30f, 0f);
            utility.lights[1].intensity = 0.7f;
            utility.ambientColor = new Color(0.35f, 0.35f, 0.38f, 1f);
            SkeletonRenderer.Grid(utility, focus);
            for (var i = 0; i < skins.Length; i++)
            {
                var skin = skins[i];
                skin.BakeMesh(baked[i], true);
                var matrix = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                var materials = skin.sharedMaterials;
                for (var m = 0; m < baked[i].subMeshCount && m < materials.Length; m++)
                    utility.DrawMesh(baked[i], matrix, materials[m], m);
            }
            foreach (var prop in props)
            {
                var filter = prop.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var materials = prop.sharedMaterials;
                for (var m = 0; m < filter.sharedMesh.subMeshCount && m < materials.Length; m++)
                    utility.DrawMesh(filter.sharedMesh, prop.transform.localToWorldMatrix, materials[m], m);
            }
            utility.Render(true);
        }
    }
}
