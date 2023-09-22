using Microsoft.AspNetCore.Mvc;
using Middleware.Models;
using System.Net.WebSockets;

namespace Middleware.Controllers
{
    public class WebSocketController : ControllerBase
    {
        [Route("/ws")]
        public async Task Get()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using WebSocket webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                await ProxyForward(webSocket);
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }

        private async Task ProxyForward(WebSocket webSocket)
        {
            string? idString = HttpContext.Request.Query["id"];
            string? groupName = HttpContext.Request.Query["groupName"];
            if (string.IsNullOrEmpty(idString) || string.IsNullOrEmpty(groupName))
                return;

            Guid id = new(idString);
            if (!Globals.WebSocketGroups.ContainsKey(groupName))
                Globals.WebSocketGroups.TryAdd(groupName, new() { new() { Id = id } });
            else
                if (!Globals.WebSocketGroups[groupName].Any(a => a.Id == id))
                Globals.WebSocketGroups[groupName].Add(new() { Id = id });

            WebSocketSession currentSession = Globals.WebSocketGroups[groupName].First(a => a.Id == id);
            CancellationTokenSource cts = new();
            new Task(async () =>
            {
                while (!cts.IsCancellationRequested)
                    if (currentSession.Message.TryDequeue(out WebSocketMessage? message))
                        if (webSocket.State == WebSocketState.Open && message.Data != null)
                            await webSocket.SendAsync(new ArraySegment<byte>(message.Data), message.MessageType, true, CancellationToken.None);
            }).Start();

            var buffer = new byte[ushort.MaxValue];
            var offset = 0;
            do
            {
                try
                {
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, ushort.MaxValue), CancellationToken.None);

                    offset += result.Count;
                    if (!result.EndOfMessage)
                        Array.Resize(ref buffer, buffer.Length + ushort.MaxValue);
                    else
                    {
                        if (webSocket.State == WebSocketState.Open && Globals.WebSocketGroups[groupName].Any(a => a.Id != id))
                            foreach (WebSocketSession session in Globals.WebSocketGroups[groupName].Where(a => a.Id != id))
                                session.Message.Enqueue(new() { Data = buffer[0..offset], MessageType = result.MessageType });

                        buffer = new byte[ushort.MaxValue];
                        offset = 0;
                    }
                }
                catch
                {
                }
            } while (webSocket.State == WebSocketState.Open);

            cts.Cancel();
            Globals.WebSocketGroups[groupName].Remove(currentSession);
            if (Globals.WebSocketGroups[groupName].Count == 0)
                Globals.WebSocketGroups.TryRemove(groupName, out List<WebSocketSession>? sessions);
        }
    }

}
