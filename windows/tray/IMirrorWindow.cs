namespace UxPlayTray
{
    /// <summary>The receiver's video window, found by the receiver's process id.</summary>
    interface IMirrorWindow
    {
        bool Exists(int processId);

        /// <summary>Whether the window is in full screen; null when no window is open.</summary>
        bool? IsFullScreen(int processId);

        /// <summary>Switches the open window in or out of full screen; false when there is none.</summary>
        bool SetFullScreen(int processId, bool fullScreen);

        /// <summary>Keeps the open window above other windows, or not; false when there is none.</summary>
        bool SetTopMost(int processId, bool topMost);

        /// <summary>Saves what the open window shows as a PNG; false when there is no window or it can't be read.</summary>
        bool SaveScreenshot(int processId, string path);
    }
}
