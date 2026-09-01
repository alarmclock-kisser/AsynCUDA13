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

    public sealed class LogBroadcaster : IDisposable
    {
        private readonly IHubContext<LogHub> _hubContext;
        private readonly IRollingFileMemoryLogger _logger;

        public LogBroadcaster(IHubContext<LogHub> hubContext, IRollingFileMemoryLogger logger)
        {
            this._hubContext = hubContext;
            this._logger = logger;
            this._logger.LogWritten += this.OnLogWritten;
        }

        public void Dispose()
        {
            this._logger.LogWritten -= this.OnLogWritten;
        }

        private void OnLogWritten(DateTime timestamp, string line)
        {
            try
            {
                _ = this._hubContext.Clients.All.SendAsync("LogWritten", timestamp, line);
            }
            catch
            {
                // Fehler beim Senden - nichts mehr tun, da wir in einem Event-Handler sind
            }
        }
    }
}