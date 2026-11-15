using System;
using System.IO;
using UnityEngine.Networking;

namespace SA.Inbetween.Install
{
    public sealed class Download
    {
        public readonly string Url;
        public readonly string File;
        public readonly string Sha256;
        public readonly long Bytes;
        UnityWebRequest request;

        public string Error { get; private set; } = "";
        public bool Done { get; private set; }

        public float Progress => Done ? 1f : request != null ? request.downloadProgress : 0f;

        public long Received => request != null ? (long)request.downloadedBytes : 0;

        public bool Ok => Done && Error == "";

        public Download(string url, string file, string sha256 = "", long bytes = 0)
        {
            Url = url;
            File = file;
            Sha256 = sha256 ?? "";
            Bytes = bytes;
        }

        public void Start()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File) ?? ".");
            var partial = File + ".partial";
            if (System.IO.File.Exists(partial)) System.IO.File.Delete(partial);
            request = new UnityWebRequest(Url, UnityWebRequest.kHttpVerbGET) { downloadHandler = new DownloadHandlerFile(partial) { removeFileOnAbort = true } };
            request.SetRequestHeader("User-Agent", "sa-inbetween-installer");
            request.SendWebRequest();
        }

        public bool Poll()
        {
            if (Done) return true;
            if (request == null || !request.isDone) return false;
            var partial = File + ".partial";
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                    Error = "downloading " + Url + " failed: " + request.error + (request.responseCode > 0 ? " (HTTP " + request.responseCode + ")" : "");
                else if (!System.IO.File.Exists(partial))
                    Error = "downloading " + Url + " wrote no file.";
                else
                {
                    var length = new FileInfo(partial).Length;
                    if (Bytes > 0 && length != Bytes)
                        Error = Path.GetFileName(File) + " is " + length + " bytes, expected " + Bytes + ". Try again.";
                    else if (Sha256 != "")
                    {
                        var hash = ToolArchive.Sha256(partial);
                        if (!string.Equals(hash, Sha256, StringComparison.OrdinalIgnoreCase))
                            Error = Path.GetFileName(File) + " has SHA-256 " + hash + ", expected " + Sha256 + ". The download is damaged or the file changed upstream; nothing was installed.";
                    }

                    if (Error == "")
                    {
                        if (System.IO.File.Exists(File)) System.IO.File.Delete(File);
                        System.IO.File.Move(partial, File);
                    }
                }
            }
            finally
            {
                if (Error != "" && System.IO.File.Exists(partial)) System.IO.File.Delete(partial);
                request.Dispose();
                request = null;
                Done = true;
            }

            return true;
        }

        public void Cancel()
        {
            if (Done) return;
            request?.Abort();
            request?.Dispose();
            request = null;
            var partial = File + ".partial";
            if (System.IO.File.Exists(partial)) System.IO.File.Delete(partial);
            Error = "Cancelled.";
            Done = true;
        }
    }
}
