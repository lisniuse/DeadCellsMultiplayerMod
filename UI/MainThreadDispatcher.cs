using Serilog;

namespace DeadCellsMultiplayerMod.UI
{
    internal static class MainThreadDispatcher
    {
        public static void Enqueue(Action? action)
        {
            if (action == null)
                return;

            GameMenu.EnqueueMainThread(action);
        }

        public static void Process(ILogger? log)
        {
            _ = log;
            GameMenu.ProcessMainThreadQueue();
        }

        public static void SetMainMenuReady()
        {
        }
    }
}
