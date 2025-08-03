using System.Net.WebSockets;

namespace ParkingHelp.WebSockets
{
    public class WebSocketUser
    {
        public int UserId { get; set; }
        public WebSocket Socket { get; set; }
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    }
}
