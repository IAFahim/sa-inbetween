using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace SA.Inbetween.Clips.Editor
{
    public sealed class StudioProcess
    {
        readonly StringBuilder log = new StringBuilder();
        readonly object gate = new object();
        Process process;
        int exitCode = int.MinValue;

        public bool Running => exitCode == int.MinValue && process != null;
        public int ExitCode => exitCode;
        public bool Handled { get; private set; }

        public void MarkHandled() => Handled = true;

        public string Log
        {
            get { lock (gate) return log.ToString(); }
        }

        public bool Start(StudioCommand command, out string error)
        {
            error = "";
            var info = new ProcessStartInfo(command.File)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in command.Args)
                info.ArgumentList.Add(a);
            try
            {
                process = new Process { StartInfo = info, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => Append(e.Data);
                process.ErrorDataReceived += (_, e) => Append(e.Data);
                process.Exited += (_, _) => Interlocked.Exchange(ref exitCode, process.ExitCode);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return true;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                process = null;
                error = "Could not start " + command.File + ": " + e.Message + StartHint(command.File);
                return false;
            }
        }

        public static string StartHint(string file)
        {
            if (string.Equals(file, "dotnet", StringComparison.Ordinal))
                return ". The tools folder has no self-contained sa-inbetween launcher next to sa-inbetween.dll; download the tools for this platform from the release page.";
            if (Environment.OSVersion.Platform == PlatformID.Unix && System.IO.File.Exists(file))
                return ". Make it executable: chmod +x \"" + file + "\"";
            return "";
        }

        public static string ExitHint(int exitCode)
        {
            const int WindowsDllNotFound = -1073741515;
            const int WindowsBadImage = -1073741701;
            if (exitCode == WindowsDllNotFound || exitCode == WindowsBadImage)
                return " Windows could not load a DLL the tool needs: install the Microsoft Visual C++ Redistributable 2015-2022 (x64) from https://aka.ms/vs/17/release/vc_redist.x64.exe and try again.";
            return "";
        }

        public void Kill()
        {
            try
            {
                if (Running) process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
        }

        void Append(string line)
        {
            if (line == null) return;
            lock (gate) log.AppendLine(line);
        }
    }
}
