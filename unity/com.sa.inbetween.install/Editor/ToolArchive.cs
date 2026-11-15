using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SA.Inbetween.Install
{
    public static class ToolArchive
    {
        public static string Sha256(string file)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(file);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static bool Inside(string root, string path)
        {
            var full = Path.GetFullPath(path);
            var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(top, StringComparison.Ordinal);
        }

        public static List<string> Extract(string archive, string destination)
        {
            Directory.CreateDirectory(destination);
            return archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? Zip(archive, destination) : TarGz(archive, destination);
        }

        static List<string> Zip(string archive, string destination)
        {
            var written = new List<string>();
            using var zip = new ZipArchive(File.OpenRead(archive), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                var target = Path.Combine(destination, entry.FullName.Replace('\\', '/'));
                if (!Inside(destination, target)) throw new IOException("the archive has an entry outside its folder: " + entry.FullName);
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                using var input = entry.Open();
                using var output = File.Create(target);
                input.CopyTo(output);
                written.Add(target);
            }

            return written;
        }

        static List<string> TarGz(string archive, string destination)
        {
            var written = new List<string>();
            var executable = new List<string>();
            using var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
            var header = new byte[512];
            string longName = null;
            while (true)
            {
                if (!Fill(gzip, header)) break;
                if (Array.TrueForAll(header, b => b == 0)) break;
                var name = longName ?? Text(header, 0, 100);
                longName = null;
                var prefix = Text(header, 345, 155);
                if (prefix != "") name = prefix + "/" + name;
                var size = Convert.ToInt64(Text(header, 124, 12).Trim() == "" ? "0" : Text(header, 124, 12).Trim(), 8);
                var mode = Text(header, 100, 8).Trim();
                var type = (char)header[156];
                var data = new byte[size];
                if (size > 0 && !Fill(gzip, data)) throw new IOException("the archive ends early.");
                var pad = (int)((512 - size % 512) % 512);
                if (pad > 0) Fill(gzip, new byte[pad]);
                if (type == 'L')
                {
                    longName = Encoding.UTF8.GetString(data).TrimEnd('\0');
                    continue;
                }

                if (type == 'x' || type == 'g') continue;
                var relative = name.StartsWith("./", StringComparison.Ordinal) ? name.Substring(2) : name;
                if (relative == "" || relative == ".") continue;
                var target = Path.Combine(destination, relative);
                if (!Inside(destination, target)) throw new IOException("the archive has an entry outside its folder: " + name);
                if (type == '5')
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                if (type != '0' && type != '\0') continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.WriteAllBytes(target, data);
                written.Add(target);
                if (mode != "" && (Convert.ToInt32(mode, 8) & 0x49) != 0) executable.Add(target);
            }

            MakeExecutable(executable);
            return written;
        }

        public static void MakeExecutable(List<string> files)
        {
            if (files.Count == 0 || Environment.OSVersion.Platform != PlatformID.Unix) return;
            for (var i = 0; i < files.Count; i += 200)
            {
                var info = new ProcessStartInfo("chmod") { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("+x");
                for (var k = i; k < Math.Min(files.Count, i + 200); k++) info.ArgumentList.Add(files[k]);
                using var process = Process.Start(info);
                process?.WaitForExit(30000);
            }
        }

        static string Text(byte[] header, int offset, int length)
        {
            var end = offset;
            while (end < offset + length && header[end] != 0) end++;
            return Encoding.ASCII.GetString(header, offset, end - offset);
        }

        static bool Fill(Stream stream, byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) return read == buffer.Length;
                read += n;
            }

            return true;
        }
    }
}
