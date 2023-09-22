# EasyWebSocketProxy
Type based websocket communication between clients through a Middleware server (proxy-forwarder).

The EasyWebSocketProxy library allows you to create client to client websocket communication (like chat or gaming applications) through a Middleware server. It's similiar to a hub client-server communication but without the limitation of message sizes. With EasyWebSocketProxy you can achive sending large binary data as well.

## Usage:
```C#
//send message [CLIENT 1]
Uri uri = new($"wss://localhost:7298/ws?id={Guid.NewGuid()}&groupName=group_1");  //always include an ID and a group name in the URL
WsProxyClient wsProxyClient = new(uri);
await wsProxyClient.TryConnectAsync();
//send some typed message to the other client with the same group connected
await wsProxyClient.SendAsync<SocketMessage>(new() { Message = "Hello from Client 1" });
//send binary data
byte[] data = Encoding.UTF8.GetBytes("Hello from Client 1! This is going to be a byte array message!");
await wsProxyClient.SendAsync(data);
//...
await wsProxyClient.TryDisconnectAsync();

//receive message [CLIENT 2]
Uri uri = new($"wss://localhost:7298/ws?id={Guid.NewGuid()}&groupName=group_1");  //always include an ID and a group name in the URL
WsProxyClient wsProxyClient = new(uri);
await wsProxyClient.TryConnectAsync();
wsProxyClient.On<SocketMessage>((socketMessage) => Console.WriteLine($"{socketMessage.Message}"));
wsProxyClient.On<byte[]>((data) => Console.WriteLine($"Bytes received. Length: {data.Length}"));
//...
await wsProxyClient.TryDisconnectAsync();
```

## Using the demo:

![](https://github.com/meehi/EasyWebSocketProxy/blob/main/client-to-client.gif)

1) Start Middleware
2) Start Client 2
3) Start Client 1 and follow the instructions

In the sample apps you can find my own Middleware implementation in ASP.NET Core Razor project. It automatically handles client sessions and manages groups, basicly it's a forwarder:

```C#
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
```

If you are having trouble using the websocket server then please enable the Websocket Protocoll: https://learn.microsoft.com/en-us/iis/configuration/system.webserver/websocket
