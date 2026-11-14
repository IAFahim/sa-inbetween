using System;
using System.Collections.Generic;
using SA.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
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
        static Material faded;
        MaterialPropertyBlock tint;
        byte[] startBytes;
        byte[] goalBytes;
        string ghostKey = "";
        float startSeconds = -1f;
        float goalSeconds = -1f;
        bool ghostsBaked;
        readonly List<(Mesh Mesh, Matrix4x4 Matrix, Color Color)> ghosts = new List<(Mesh Mesh, Matrix4x4 Matrix, Color Color)>();

        public bool ShowGhosts = true;
        public int GhostMeshes => ghosts.Count;

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
            ClearGhosts();
        }

        void ClearGhosts()
        {
            foreach (var ghost in ghosts)
                if (ghost.Mesh != null) UnityEngine.Object.DestroyImmediate(ghost.Mesh);
            ghosts.Clear();
            ghostsBaked = false;
        }

        public void GhostsFor(InbetweenRecipe recipe)
        {
            if (recipe == null) return;
            var start = RecipeRunner.PreviewBytes(recipe, recipe.Start);
            var last = recipe.Goals.Count == 0 ? default : recipe.Goals[recipe.Goals.Count - 1];
            var goal = recipe.Goals.Count == 0 ? null : RecipeRunner.PreviewBytes(recipe, last);
            var key = recipe.Start.Where + "|" + recipe.Start.AnchorSeconds + "|" + last.Where + "|" + last.AnchorSeconds;
            if (ReferenceEquals(start, startBytes) && ReferenceEquals(goal, goalBytes) && key == ghostKey) return;
            startBytes = start;
            goalBytes = goal;
            ghostKey = key;
            startSeconds = SourceSeconds(start, recipe.Start);
            goalSeconds = SourceSeconds(goal, last);
            ClearGhosts();
        }

        static float SourceSeconds(byte[] bytes, ClipFrame frame) =>
            bytes != null && bytes.Length > 0 && SaClipPose.TryLoad(bytes, out var pose, out _) ? PreviewMath.FrameSeconds(frame, pose.Duration) : -1f;

        void BakeGhosts()
        {
            ghostsBaked = true;
            BakeGhost(startBytes, startSeconds, 0, SkeletonPreview.StartColor);
            BakeGhost(goalBytes, goalSeconds, frames - 1, SkeletonPreview.GoalColor);
        }

        void BakeGhost(byte[] source, float seconds, int resultFrame, Color color)
        {
            if (source == null || source.Length == 0 || seconds < 0f) return;
            if (!SaClipExport.TrySampleLocals(plan, source, out var sourcePoses, out var sourceFrames, out _) || sourceFrames == 0) return;
            SaClipExport.ApplyFrame(plan, locals, resultFrame);
            var resultHips = plan.Bones[plan.Hips].position;
            SaClipExport.ApplyFrame(plan, sourcePoses, Mathf.Clamp(Mathf.RoundToInt(seconds * SaClipExport.Rate), 0, sourceFrames - 1));
            var sourceHips = plan.Bones[plan.Hips].position;
            var shift = new Vector3(resultHips.x - sourceHips.x, 0f, resultHips.z - sourceHips.z);
            foreach (var skin in skins)
            {
                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skin.BakeMesh(mesh, true);
                Shade(mesh, skin.transform.rotation);
                ghosts.Add((mesh, Matrix4x4.TRS(skin.transform.position + shift, skin.transform.rotation, Vector3.one), color));
            }
        }

        static void Shade(Mesh mesh, Quaternion rotation)
        {
            var normals = mesh.normals;
            var colors = new Color32[mesh.vertexCount];
            var light = new Vector3(0.3f, 0.8f, 0.5f).normalized;
            for (var i = 0; i < colors.Length; i++)
            {
                var lit = i < normals.Length ? 0.55f + 0.45f * Mathf.Max(0f, Vector3.Dot(rotation * normals[i], light)) : 1f;
                var level = (byte)Mathf.RoundToInt(255f * lit);
                colors[i] = new Color32(level, level, level, 255);
            }

            mesh.colors32 = colors;
        }

        static Material Faded()
        {
            if (faded != null) return faded;
            faded = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave, renderQueue = (int)RenderQueue.Transparent };
            faded.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            faded.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            faded.SetInt("_Cull", (int)CullMode.Back);
            faded.SetInt("_ZWrite", 0);
            return faded;
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
            utility.AddSingleGO(instance);
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
            SaClipExport.ApplyFrame(plan, locals, Mathf.Clamp(Mathf.RoundToInt(seconds * SaClipExport.Rate), 0, frames - 1));
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
            if (ShowGhosts && !ghostsBaked) BakeGhosts();
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
            if (ShowGhosts && ghosts.Count > 0)
            {
                tint ??= new MaterialPropertyBlock();
                var material = Faded();
                foreach (var ghost in ghosts)
                {
                    tint.SetColor("_Color", ghost.Color);
                    for (var m = 0; m < ghost.Mesh.subMeshCount; m++)
                        utility.DrawMesh(ghost.Mesh, ghost.Matrix, material, m, tint);
                }
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
