using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace UxPlayTray
{
    /// <summary>The AirPlay receiver the tray app controls.</summary>
    interface IReceiver : IDisposable
    {
        bool IsRunning { get; }

        /// <summary>Process id of the running receiver, for finding its window.</summary>
        int ProcessId { get; }

        /// <summary>How long the current (or last) receiver has been running.</summary>
        TimeSpan Uptime { get; }

        /// <summary>Starts it with these options. Throws when it can't be started.</summary>
        /// <param name="keepLog">Add to the log instead of starting a new one.</param>
        void Start(string arguments, bool keepLog);

        void Stop();

        /// <summary>A line of the receiver's output (raised on a background thread).</summary>
        event Action<string> OutputReceived;

        /// <summary>The receiver stopped by itself: its exit code (raised on a background thread).</summary>
        event Action<int> ExitedUnexpectedly;
    }

    /// <summary>
    /// uxplay.exe from the bundle, run hidden with the bundled GStreamer. Its output goes
    /// to a log file; it is tied to this app with a job object so it never outlives it.
    /// </summary>
    sealed class ReceiverProcess : IReceiver
    {
        readonly string exePath;
        readonly string serverDir;
        readonly string dataDir;
        readonly string logPath;
        readonly JobObject job = new JobObject();

        Process process;
        LogFile log;
        DateTime startedAt;
        DateTime stoppedAt;

        public ReceiverProcess(string exePath, string serverDir, string dataDir, string logPath)
        {
            this.exePath = exePath;
            this.serverDir = serverDir;
            this.dataDir = dataDir;
            this.logPath = logPath;
        }

        public event Action<string> OutputReceived;
        public event Action<int> ExitedUnexpectedly;

        public bool IsRunning => process != null;
        public int ProcessId => process?.Id ?? 0;
        public TimeSpan Uptime => (process != null ? DateTime.Now : stoppedAt) - startedAt;

        public void Start(string arguments, bool keepLog)
        {
            if (process != null)
                return;
            Directory.CreateDirectory(dataDir);
            var psi = new ProcessStartInfo(exePath, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = dataDir,
            };
            GStreamerEnvironment.Apply(psi, serverDir, dataDir);

            var processLog = new LogFile(logPath, "> uxplay.exe " + arguments, keepLog);
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            // Each copy of uxplay.exe writes to its own log, even after a restart.
            p.OutputDataReceived += (s, e) => OnOutput(processLog, e.Data);
            p.ErrorDataReceived += (s, e) => OnOutput(processLog, e.Data);
            p.Exited += (s, e) => OnExited(p, processLog);
            try
            {
                p.Start();
            }
            catch
            {
                processLog.Dispose();
                p.Dispose();
                throw;
            }
            job.Add(p);  // uxplay.exe is closed automatically if this app goes away
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            process = p;
            log = processLog;
            startedAt = DateTime.Now;
        }

        void OnOutput(LogFile processLog, string line)
        {
            if (line == null)
                return;
            processLog.WriteLine(line);
            OutputReceived?.Invoke(line);
        }

        void OnExited(Process p, LogFile processLog)
        {
            // Exited can arrive before the last lines of output have been read, and those
            // usually say why it stopped: wait for them before closing the log.
            try { p.WaitForExit(); } catch (InvalidOperationException) { }
            int code;
            try { code = p.ExitCode; } catch (InvalidOperationException) { code = -1; }
            processLog.Dispose();
            lock (job)
            {
                if (p != process)
                    return;  // stopped on purpose
                process = null;
                log = null;
                stoppedAt = DateTime.Now;
            }
            p.Dispose();
            ExitedUnexpectedly?.Invoke(code);
        }

        public void Stop()
        {
            Process p;
            lock (job)
            {
                p = process;
                process = null;
                stoppedAt = DateTime.Now;
            }
            if (p == null)
                return;
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            p.Dispose();
            log?.Dispose();
            log = null;
        }

        /// <summary>Ends copies of the bundled uxplay.exe left behind by a crash.</summary>
        public void KillLeftovers()
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
            {
                try
                {
                    if (string.Equals(p.MainModule.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch (Exception)
                {
                    // not ours, or no access
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Stop();
            job.Dispose();
        }
    }
}
