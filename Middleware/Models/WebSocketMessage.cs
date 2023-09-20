using System.Net.WebSockets;

namespace Middleware.Models
{
    public class WebSocketMessage
    {
        public byte[] Data { get; set; }
        public WebSocketMessageType MessageType { get; set; }
    }
}
