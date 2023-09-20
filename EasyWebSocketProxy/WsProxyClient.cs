using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using EasyWebSocketProxy.Models;

namespace EasyWebSocketProxy
{
    public class WsProxyClient
    {
        #region Private variables

        private Uri _host;
        private ClientWebSocket? _clientWebSocket;
        private bool _disconnectRequested;
        private readonly ConcurrentDictionary<Type, Delegate> _handlers = new();

        #endregion

        #region Constructors

        public WsProxyClient(Uri host)
        {
            _host = host;
        }

        #endregion

        #region Public methods

        public async Task<bool> TryConnectAsync()
        {
            _disconnectRequested = false;
            _clientWebSocket = new();
            bool connected = false;
            try
            {
                await _clientWebSocket.ConnectAsync(_host, CancellationToken.None);
                connected = true;
            }
            catch
            {
            }
            if (connected)
                StartReceiveMessages();

            return connected;
        }

        public async Task TryDisconnectAsync()
        {
            _disconnectRequested = true;
            if (_clientWebSocket == null || _clientWebSocket.State != WebSocketState.Open)
                return;

            try
            {
                await _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
            catch
            {
            }
        }

        public bool IsConnected()
        {
            return _clientWebSocket?.State == WebSocketState.Open;
        }

        public void On<T>(Action<T> callback)
        {
            if (!_handlers.ContainsKey(typeof(T)))
                _handlers.TryAdd(typeof(T), callback);
        }

        public async Task SendAsync<T>(T obj)
        {
            if (_clientWebSocket == null || _clientWebSocket.State != WebSocketState.Open)
                return;

            MessageWrapper<T> message = new() { MessageType = typeof(T).FullName, Message = obj };
            await _clientWebSocket.SendAsync(new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message))), WebSocketMessageType.Text, true, new CancellationTokenSource().Token);
        }

        public async Task SendAsync(byte[] bytes)
        {
            if (_clientWebSocket == null || _clientWebSocket.State != WebSocketState.Open)
                return;

            await _clientWebSocket.SendAsync(new ArraySegment<byte>(bytes, 0, bytes.Length), WebSocketMessageType.Binary, true, new CancellationTokenSource().Token);
        }

        #endregion

        #region Private methods

        private void StartReceiveMessages()
        {
            if (_clientWebSocket == null)
                return;

            new Task(async () =>
            {
                CancellationTokenSource cts = new();
                var buffer = new byte[ushort.MaxValue];
                var offset = 0;
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        WebSocketReceiveResult result = await _clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer, offset, ushort.MaxValue), cts.Token);

                        offset += result.Count;
                        if (!result.EndOfMessage)
                            Array.Resize(ref buffer, buffer.Length + ushort.MaxValue);
                        else
                        {
                            if (result.MessageType == WebSocketMessageType.Binary)
                            {
                                if (_handlers.Any(a => a.Key.FullName == typeof(byte[]).FullName))
                                {
                                    KeyValuePair<Type, Delegate> handler = _handlers.First(a => a.Key.FullName == typeof(byte[]).FullName);
                                    handler.Value.DynamicInvoke(new object[] { buffer[0..offset] });
                                }
                            }
                            else
                            {
                                MessageWrapper? responseMessage = JsonSerializer.Deserialize<MessageWrapper>(Encoding.UTF8.GetString(buffer[0..offset]));
                                if (responseMessage != null)
                                    if (_handlers.Any(a => a.Key.FullName == responseMessage.MessageType))
                                    {
                                        KeyValuePair<Type, Delegate> handler = _handlers.First(a => a.Key.FullName == responseMessage.MessageType);
                                        string? message = responseMessage.Message?.ToString();
                                        if (message != null)
                                        {
                                            var receivedMessage = JsonSerializer.Deserialize(message, handler.Key);
                                            if (receivedMessage != null)
                                                handler.Value.DynamicInvoke(new object[] { receivedMessage });
                                        }
                                    }
                            }

                            buffer = new byte[ushort.MaxValue];
                            offset = 0;
                        }
                    }
                    catch
                    {
                        cts.Cancel();
                    }
                }

                if (!_disconnectRequested)
                    await TryConnectAsync();
            }).Start();
        }

        #endregion
    }

}