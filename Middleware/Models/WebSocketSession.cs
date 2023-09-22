using System.Collections.Concurrent;

namespace Middleware.Models
{
    public class WebSocketSession
    {
        public Guid Id { get; set; }
        public ConcurrentQueue<WebSocketMessage> Message { get; set; } = new();
    }
}