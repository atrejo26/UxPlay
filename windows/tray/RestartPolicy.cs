using System;
using System.Collections.Generic;

namespace UxPlayTray
{
    /// <summary>What to do when uxplay.exe stops by itself.</summary>
    enum RestartDecision
    {
        /// <summary>It had been running a while: start it again quietly.</summary>
        Restart,
        /// <summary>It stopped right after starting: the setup is wrong, so tell the user.</summary>
        StoppedAtStart,
        /// <summary>It keeps stopping: restarting again would only loop, so tell the user.</summary>
        StopsTooOften,
    }

    /// <summary>
    /// Decides whether to restart uxplay.exe after it stops unexpectedly. A crash after
    /// it ran a while is restarted, but no more than <see cref="MaxRestarts"/> times in
    /// <see cref="Period"/>, so something that crashes it every time (a driver, an iPhone
    /// update) is reported instead of hidden behind endless restarts.
    /// </summary>
    sealed class RestartPolicy
    {
        public const int MaxRestarts = 3;
        public static readonly TimeSpan Period = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan MinUptime = TimeSpan.FromSeconds(20);

        readonly Queue<DateTime> restarts = new Queue<DateTime>();

        public RestartDecision Decide(TimeSpan uptime, DateTime now)
        {
            if (uptime <= MinUptime)
                return RestartDecision.StoppedAtStart;
            while (restarts.Count > 0 && now - restarts.Peek() >= Period)
                restarts.Dequeue();
            if (restarts.Count >= MaxRestarts)
                return RestartDecision.StopsTooOften;
            restarts.Enqueue(now);
            return RestartDecision.Restart;
        }

        /// <summary>The user started it again: give it a fresh allowance.</summary>
        public void Reset() => restarts.Clear();
    }
}
