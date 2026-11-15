using System;
using System.Collections.Generic;
using System.Globalization;

namespace SA.Inbetween.Install
{
    public static class SomaRestPose
    {
        public const int Joints = 77;
        public const int FloatsPerJoint = 7;
        public const int Bytes = Joints * FloatsPerJoint * sizeof(float);
        const double Centimetres = 100.0;

        sealed class Joint
        {
            public string Name = "";
            public double[] Offset = new double[3];
            public string[] Channels = Array.Empty<string>();
        }

        public static bool TryConvert(string bvh, int frame, out byte[] pose, out string error)
        {
            pose = Array.Empty<byte>();
            error = "";
            var joints = new List<Joint>();
            var lines = bvh.Replace("\r", "").Split('\n');
            var i = 0;
            Joint current = null;
            var inEnd = false;
            var depth = 0;
            var endDepth = -1;
            for (; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("MOTION", StringComparison.Ordinal)) break;
                var parts = t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                switch (parts[0])
                {
                    case "ROOT":
                    case "JOINT":
                        current = new Joint { Name = parts.Length > 1 ? parts[1] : "" };
                        joints.Add(current);
                        break;
                    case "End":
                        inEnd = true;
                        endDepth = depth;
                        break;
                    case "{":
                        depth++;
                        break;
                    case "}":
                        depth--;
                        if (inEnd && depth == endDepth) inEnd = false;
                        break;
                    case "OFFSET":
                        if (!inEnd && current != null && parts.Length >= 4)
                            for (var k = 0; k < 3; k++) current.Offset[k] = double.Parse(parts[k + 1], CultureInfo.InvariantCulture);
                        break;
                    case "CHANNELS":
                        if (current != null && parts.Length >= 2)
                        {
                            var count = int.Parse(parts[1], CultureInfo.InvariantCulture);
                            current.Channels = new string[count];
                            Array.Copy(parts, 2, current.Channels, 0, Math.Min(count, parts.Length - 2));
                        }

                        break;
                }
            }

            if (joints.Count != Joints + 1 || joints[0].Name != "Root" || joints[1].Name != "Hips")
            {
                error = "the motion file is not a SOMA skeleton (" + joints.Count + " joints, expected a Root and " + Joints + " more starting at Hips).";
                return false;
            }

            var motion = i + 3 + frame;
            if (motion >= lines.Length)
            {
                error = "the motion file has no frame " + frame + ".";
                return false;
            }

            var values = lines[motion].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var floats = new float[Joints * FloatsPerJoint];
            var at = 0;
            for (var j = 0; j < joints.Count; j++)
            {
                var joint = joints[j];
                var position = (double[])joint.Offset.Clone();
                double x = 0, y = 0, z = 0, w = 1;
                foreach (var channel in joint.Channels)
                {
                    if (at >= values.Length)
                    {
                        error = "frame " + frame + " has too few values.";
                        return false;
                    }

                    var value = double.Parse(values[at++], CultureInfo.InvariantCulture);
                    if (channel.EndsWith("position", StringComparison.Ordinal))
                    {
                        position["XYZ".IndexOf(channel[0])] = value;
                        continue;
                    }

                    var half = value * Math.PI / 360.0;
                    var s = Math.Sin(half);
                    var c = Math.Cos(half);
                    double ax = channel[0] == 'X' ? s : 0, ay = channel[0] == 'Y' ? s : 0, az = channel[0] == 'Z' ? s : 0;
                    var nx = w * ax + x * c + y * az - z * ay;
                    var ny = w * ay - x * az + y * c + z * ax;
                    var nz = w * az + x * ay - y * ax + z * c;
                    var nw = w * c - x * ax - y * ay - z * az;
                    x = nx;
                    y = ny;
                    z = nz;
                    w = nw;
                }

                if (j == 0) continue;
                var o = (j - 1) * FloatsPerJoint;
                floats[o] = (float)(position[0] / Centimetres);
                floats[o + 1] = (float)(position[1] / Centimetres);
                floats[o + 2] = (float)(position[2] / Centimetres);
                floats[o + 3] = (float)x;
                floats[o + 4] = (float)y;
                floats[o + 5] = (float)z;
                floats[o + 6] = (float)w;
            }

            pose = new byte[Bytes];
            Buffer.BlockCopy(floats, 0, pose, 0, pose.Length);
            return true;
        }
    }
}
