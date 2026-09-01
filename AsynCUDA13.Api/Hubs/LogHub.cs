using Microsoft.AspNetCore.SignalR;
using AsynCUDA13.Shared.Interfaces;

namespace AsynCUDA13.Api.Hubs
{
    public class LogHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }

    public static class LogBroadcaster
    {
        private static IHubContext<LogHub>? _hubContext;
        private static IRollingFileMemoryLogger? _logger;
        private static readonly object _lock = new();
        private static bool _isSubscribed = false;

        public static void SetHubContext(IHubContext<LogHub> hubContext)
        {
            lock (_lock)
            {
                _hubContext = hubContext;
            }
        }

        public static void SubscribeToLogger(IRollingFileMemoryLogger logger)
        {
            lock (_lock)
            {
                if (!_isSubscribed)
                {
                    _logger = logger;
                    logger.LogWritten += OnLogWritten;
                    _isSubscribed = true;
                }
            }
        }

        public static void UnsubscribeFromLogger()
        {
            lock (_lock)
            {
                if (_isSubscribed)
                {
                    _logger?.LogWritten -= OnLogWritten;
                    _isSubscribed = false;
                }
            }
        }

        private static void OnLogWritten(DateTime timestamp, string line)
        {
            try
            {
                var hubContext = _hubContext;
                if (hubContext != null)
                {
                    // Fire-and-forget, aber wir können nicht awaitten da wir in einem Event-Handler sind
                    _ = hubContext.Clients.All.SendAsync("LogWritten", timestamp, line);
                }
            }
            catch
            {
                // Fehler beim Senden - nichts mehr tun, da wir in einem Event-Handler sind
            }
        }
    }
}