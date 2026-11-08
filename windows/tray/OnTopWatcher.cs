namespace UxPlayTray
{
    /// <summary>
    /// "Keep on top of other windows": the mirror window opens a moment after the iPhone
    /// connects, so this keeps trying, once per tick, until the window is there and set,
    /// for at most <see cref="MaxTries"/> ticks. The caller supplies the ticks (a timer).
    /// </summary>
    sealed class OnTopWatcher
    {
        /// <summary>At one tick every half second, 30 seconds.</summary>
        public const int MaxTries = 60;

        readonly IMirrorWindow window;
        int tries;

        public OnTopWatcher(IMirrorWindow window)
        {
            this.window = window;
        }

        public bool IsWatching { get; private set; }

        public void Start()
        {
            tries = 0;
            IsWatching = true;
        }

        public void Stop() => IsWatching = false;

        /// <summary>One try; returns whether to keep ticking.</summary>
        /// <param name="wanted">The setting is still on.</param>
        /// <param name="receiverRunning">uxplay.exe is still running, as <paramref name="processId"/>.</param>
        public bool Tick(bool wanted, bool receiverRunning, int processId)
        {
            if (!IsWatching)
                return false;
            if (!wanted || !receiverRunning || window.SetTopMost(processId, true) || ++tries >= MaxTries)
                IsWatching = false;
            return IsWatching;
        }
    }
}
