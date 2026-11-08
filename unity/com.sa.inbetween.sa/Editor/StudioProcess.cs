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
                error = "Could not start " + command.File + ": " + e.Message;
                return false;
            }
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
