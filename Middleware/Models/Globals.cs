using System.Collections.Concurrent;

namespace Middleware.Models
{
    public static class Globals
    {
        public static ConcurrentDictionary<string, List<WebSocketSession>> WebSocketGroups = new();
    }

}
