using System.Collections.Concurrent;
using Serilog;

namespace DeadCellsMultiplayerMod.UI
{
    internal static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _queue = new();
        private static bool _mainMenuReady;

        public static void Enqueue(Action? action)
        {
            if (action == null) return;
            _queue.Enqueue(action);
        }

        public static void Process(ILogger? log)
        {
            if (!_mainMenuReady)
                return;
            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    log?.Warning("[NetMod] Main thread task failed: {Message}", ex.Message);
                }
            }
        }

        public static void SetMainMenuReady()
        {
            _mainMenuReady = true;
        }
    }
}
