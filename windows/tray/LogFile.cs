using System;
using System.IO;
using System.Text;

namespace UxPlayTray
{
    /// <summary>
    /// uxplay.exe's output, saved to %LOCALAPPDATA%\UxPlay\uxplay.log. Written from two
    /// threads (stdout and stderr), so every write is locked, and kept below a size limit
    /// so a receiver left running for weeks can't fill the disk.
    /// </summary>
    sealed class LogFile : IDisposable
    {
        const long MaxChars = 5 * 1024 * 1024;

        readonly object gate = new object();
        readonly string path;
        readonly string header;
        StreamWriter writer;
        long written;

        public LogFile(string path, string header, bool append)
        {
            this.path = path;
            this.header = header;
            try
            {
                if (append && File.Exists(path) && new FileInfo(path).Length > MaxChars)
                    append = false;
                Open(append);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                writer = null;  // no log, but UxPlay itself still works
            }
        }

        void Open(bool append)
        {
            writer = new StreamWriter(path, append, new UTF8Encoding(false)) { AutoFlush = true };
            written = 0;
            Write(header);
        }

        void Write(string line)
        {
            writer.WriteLine(line);
            written += line.Length + 2;
        }

        public void WriteLine(string line)
        {
            lock (gate)
            {
                if (writer == null)
                    return;
                try
                {
                    if (written > MaxChars)
                    {
                        writer.Dispose();
                        Open(false);
                        Write("(older messages were removed to keep this log small)");
                    }
                    Write(line);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                {
                    writer = null;
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                try { writer?.Dispose(); } catch (IOException) { }
                writer = null;
            }
        }
    }
}
