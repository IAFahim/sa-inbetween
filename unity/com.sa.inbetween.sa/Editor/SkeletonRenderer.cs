using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class SkeletonRenderer : IDisposable
    {
        static readonly Color Background = new Color(0.16f, 0.17f, 0.19f, 1f);
        static readonly Color GridColor = new Color(1f, 1f, 1f, 0.12f);
        const float GridStep = 0.5f;
        const int GridLines = 12;

        static Mesh joint;
        static Mesh bone;
        static Mesh grid;
        static Material solid;
        static Material faded;

        MaterialPropertyBlock block;
        PreviewRenderUtility utility;

        public float Yaw = 150f;
        public float Pitch = 12f;
        public float Distance = 4.8f;

        public void Dispose()
        {
            utility?.Cleanup();
            utility = null;
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
                    Distance = Mathf.Clamp(Distance * (1f + e.delta.y * 0.04f), 1.2f, 12f);
                    e.Use();
                    return true;
            }

            return false;
        }

        public void Draw(Rect rect, IReadOnlyList<SkeletonLayer> layers)
        {
            if (Event.current.type != EventType.Repaint || rect.width < 8f || rect.height < 8f)
                return;
            Prepare();
            utility.BeginPreview(rect, GUIStyle.none);
            Render(layers);
            utility.EndAndDrawPreview(rect);
        }

        public Texture2D RenderStatic(int width, int height, IReadOnlyList<SkeletonLayer> layers)
        {
            Prepare();
            utility.BeginStaticPreview(new Rect(0, 0, width, height));
            Render(layers);
            return utility.EndStaticPreview();
        }

        void Prepare()
        {
            utility ??= new PreviewRenderUtility();
            block ??= new MaterialPropertyBlock();
            if (solid != null)
                return;
            var shader = Shader.Find("Hidden/Internal-Colored");
            solid = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            solid.SetInt("_Cull", (int)CullMode.Off);
            solid.SetInt("_ZWrite", 1);
            faded = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = (int)RenderQueue.Transparent };
            faded.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            faded.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            faded.SetInt("_Cull", (int)CullMode.Off);
            faded.SetInt("_ZWrite", 0);
            joint = Sphere();
            bone = Bone();
            grid = Grid();
        }

        void Render(IReadOnlyList<SkeletonLayer> layers)
        {
            var focus = new Vector3(0f, 0.85f, 0f);
            if (layers.Count > 0)
            {
                var hips = layers[0].World[SaClipPose.Hips];
                focus = new Vector3(hips.x, 0.85f, hips.z);
            }

            var camera = utility.camera;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * Distance, rotation);

            var origin = new Vector3(Mathf.Round(focus.x / GridStep) * GridStep, 0f, Mathf.Round(focus.z / GridStep) * GridStep);
            Mesh(grid, Matrix4x4.Translate(origin), faded, GridColor);
            foreach (var layer in layers)
                if (!layer.Ghost)
                    Skeleton(layer, solid);
            foreach (var layer in layers)
                if (layer.Ghost)
                    Skeleton(layer, faded);
            utility.Render(true);
        }

        void Skeleton(SkeletonLayer layer, Material material)
        {
            var world = layer.World;
            for (var i = 1; i < world.Length; i++)
            {
                var parent = SaClipPose.Parent(i);
                if (parent <= 0)
                    continue;
                var from = world[parent];
                var delta = world[i] - from;
                var length = delta.magnitude;
                if (length < 1e-4f)
                    continue;
                var width = PreviewMath.IsFinger(i) ? 0.012f : Mathf.Min(0.07f, length * 0.35f);
                Mesh(bone, Matrix4x4.TRS(from, Quaternion.FromToRotation(Vector3.forward, delta / length), new Vector3(width, width, length)), material, layer.Color);
            }

            for (var i = 1; i < world.Length; i++)
            {
                var radius = PreviewMath.JointRadius(i);
                if (radius > 0f)
                    Mesh(joint, Matrix4x4.TRS(world[i], Quaternion.identity, Vector3.one * radius), material, layer.Color);
            }
        }

        void Mesh(Mesh mesh, Matrix4x4 matrix, Material material, Color color)
        {
            block.SetColor("_Color", color);
            utility.DrawMesh(mesh, matrix, material, 0, block);
        }

        static Color Shade(Vector3 normal)
        {
            var light = 0.55f + 0.45f * Mathf.Max(0f, Vector3.Dot(normal, new Vector3(0.3f, 0.8f, -0.5f).normalized));
            return new Color(light, light, light, 1f);
        }

        static Mesh Flat(List<Vector3> corners, List<int> triangles, Vector3 center)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            for (var t = 0; t < triangles.Count; t += 3)
            {
                var a = corners[triangles[t]];
                var b = corners[triangles[t + 1]];
                var c = corners[triangles[t + 2]];
                var normal = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(normal, (a + b + c) / 3f - center) < 0f)
                {
                    normal = -normal;
                    (b, c) = (c, b);
                }

                var shade = Shade(normal);
                foreach (var v in new[] { a, b, c })
                {
                    indices.Add(vertices.Count);
                    vertices.Add(v);
                    colors.Add(shade);
                }
            }

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Sphere()
        {
            const int rings = 6;
            const int segments = 10;
            var corners = new List<Vector3>();
            var triangles = new List<int>();
            for (var r = 0; r <= rings; r++)
            {
                var phi = Mathf.PI * r / rings;
                for (var s = 0; s <= segments; s++)
                {
                    var theta = 2f * Mathf.PI * s / segments;
                    corners.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)));
                }
            }

            for (var r = 0; r < rings; r++)
            for (var s = 0; s < segments; s++)
            {
                var a = r * (segments + 1) + s;
                var b = a + segments + 1;
                triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }

            return Flat(corners, triangles, Vector3.zero);
        }

        static Mesh Bone()
        {
            var corners = new List<Vector3>
            {
                Vector3.zero,
                new Vector3(1f, 0f, 0.15f), new Vector3(0f, 1f, 0.15f), new Vector3(-1f, 0f, 0.15f), new Vector3(0f, -1f, 0.15f),
                new Vector3(0f, 0f, 1f),
            };
            var triangles = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                var a = 1 + i;
                var b = 1 + (i + 1) % 4;
                triangles.AddRange(new[] { 0, b, a, a, b, 5 });
            }

            return Flat(corners, triangles, new Vector3(0f, 0f, 0.2f));
        }

        static Mesh Grid()
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var extent = GridLines * GridStep;
            for (var i = -GridLines; i <= GridLines; i++)
            {
                var o = i * GridStep;
                indices.Add(vertices.Count); vertices.Add(new Vector3(o, 0f, -extent));
                indices.Add(vertices.Count); vertices.Add(new Vector3(o, 0f, extent));
                indices.Add(vertices.Count); vertices.Add(new Vector3(-extent, 0f, o));
                indices.Add(vertices.Count); vertices.Add(new Vector3(extent, 0f, o));
            }

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            var colors = new Color[vertices.Count];
            for (var i = 0; i < colors.Length; i++) colors[i] = Color.white;
            mesh.colors = colors;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
