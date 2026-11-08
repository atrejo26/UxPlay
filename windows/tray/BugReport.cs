using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UxPlayTray
{
    /// <summary>
    /// Troubleshooting → Copy details for a bug report: what someone helping needs to
    /// know, as text to paste into a GitHub issue or an email. The user's folder is
    /// replaced with %USERPROFILE% so their Windows account name isn't shared.
    /// </summary>
    static class BugReport
    {
        public const int LogLines = 60;

        public static string Build(string appVersion, string windowsVersion, string uxplayOptions,
                                   IEnumerable<string> logTail, string userProfile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("UxPlay for Windows " + (appVersion ?? "(not a release build)"));
            sb.AppendLine("Windows: " + windowsVersion);
            sb.AppendLine("uxplay options: " + uxplayOptions);
            sb.AppendLine();
            sb.AppendLine("Last lines of the log:");
            bool any = false;
            foreach (string line in logTail)
            {
                sb.AppendLine(line);
                any = true;
            }
            if (!any)
                sb.AppendLine("(the log is empty)");
            string text = sb.ToString();
            if (!string.IsNullOrEmpty(userProfile) && userProfile.Length > 3)  // never replace "C:\"
                text = Replace(text, userProfile.TrimEnd('\\', '/'), "%USERPROFILE%");
            return text;
        }

        /// <summary>The last <paramref name="count"/> lines, read once without keeping the whole file.</summary>
        public static List<string> Tail(TextReader reader, int count)
        {
            var lines = new Queue<string>(count + 1);
            for (string line; (line = reader.ReadLine()) != null;)
            {
                lines.Enqueue(line);
                if (lines.Count > count)
                    lines.Dequeue();
            }
            return new List<string>(lines);
        }

        /// <summary>The end of a log another process may still be writing; empty when there is none.</summary>
        public static List<string> TailOfFile(string path, int count)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    // a 5 MB log: only its end matters
                    const long tailBytes = 64 * 1024;
                    if (stream.Length > tailBytes)
                        stream.Seek(-tailBytes, SeekOrigin.End);
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    {
                        if (stream.Position > 0)
                            reader.ReadLine();  // the first line is cut in the middle
                        return Tail(reader, count);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new List<string>();
            }
        }

        static string Replace(string text, string what, string with)
        {
            var sb = new StringBuilder();
            int at = 0;
            for (int i; (i = text.IndexOf(what, at, StringComparison.OrdinalIgnoreCase)) >= 0; at = i + what.Length)
                sb.Append(text, at, i - at).Append(with);
            return sb.Append(text, at, text.Length - at).ToString();
        }
    }
}
