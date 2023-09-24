﻿using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<Guid, Delegate> _replyHandlers = new();
        private readonly ConcurrentDictionary<object, Guid> _replyToObjects = new();

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

        public async Task<object?> SendAndWaitForAnswerAsync<T>(T obj, Type answerTypeHint, TimeSpan cancelAfter)
        {
            if (_clientWebSocket == null || _clientWebSocket.State != WebSocketState.Open)
                return null;

            object? result = null;

            MessageWrapper<T> message = new() { Id = Guid.NewGuid(), MessageType = typeof(T).FullName, Message = obj };

            CancellationTokenSource cts = new(cancelAfter);
            _replyHandlers.TryAdd(message.Id.Value, new Action<object?>(
                (param) =>
                {
                    if (param != null)
                    {
                        string? jsonObject = param.ToString();
                        if (!string.IsNullOrEmpty(jsonObject))
                            result = Deserialize(jsonObject, answerTypeHint);
                    }
                    cts.Cancel();
                }));

            await _clientWebSocket.SendAsync(new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message))), WebSocketMessageType.Text, true, new CancellationTokenSource().Token);

            var task = Task.Run(() =>
            {
                cts.Token.WaitHandle.WaitOne(cancelAfter);
            }, cts.Token);
            await task.ConfigureAwait(false);
            task.Wait();

            _replyHandlers.TryRemove(message.Id.Value, out Delegate? replyCallback);

            return result;
        }

        public async Task ReplyToAsync<T>(object replyToObject, T answer)
        {
            if (_clientWebSocket == null || _clientWebSocket.State != WebSocketState.Open)
                return;

            _replyToObjects.TryRemove(replyToObject, out Guid replyID);

            MessageWrapper<T> message = new() { MessageType = typeof(T).FullName, Message = answer, ReplyId = replyID };

            await _clientWebSocket.SendAsync(new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message))), WebSocketMessageType.Text, true, new CancellationTokenSource().Token);
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
                                string? message = responseMessage?.Message?.ToString();
                                if (responseMessage != null && !string.IsNullOrEmpty(message))
                                {
                                    if (responseMessage.ReplyId != null && _replyHandlers.Any(a => a.Key == responseMessage.ReplyId.Value))
                                    {
                                        _replyHandlers.TryRemove(responseMessage.ReplyId.Value, out Delegate? replyCallback);
                                        if (responseMessage.Message  != null)
                                            replyCallback?.DynamicInvoke(new object[] { responseMessage.Message });
                                    }
                                    else if (_handlers.Any(a => a.Key.FullName == responseMessage.MessageType))
                                    {
                                        KeyValuePair<Type, Delegate> handler = _handlers.First(a => a.Key.FullName == responseMessage.MessageType);
                                        object? receivedMessage = Deserialize(message, handler.Key);
                                        if (receivedMessage != null)
                                        {
                                            if (responseMessage.Id != null)
                                                _replyToObjects.TryAdd(receivedMessage, responseMessage.Id.Value);

                                            handler.Value.DynamicInvoke(new object[] { receivedMessage });
                                        }
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

        private static object? Deserialize(string message, Type type)
        {
            if (type == typeof(bool))
                return Convert.ToBoolean(message);
            if (type == typeof(byte))
                return Convert.ToByte(message);
            if (type == typeof(char))
                return Convert.ToChar(message);
            if (type == typeof(DateTime))
                return Convert.ToDateTime(message);
            if (type == typeof(decimal))
                return Convert.ToDecimal(message);
            if (type == typeof(double))
                return Convert.ToDouble(message);
            if (type == typeof(float))
                return Convert.ToSingle(message);
            if (type == typeof(int) || type == typeof(Int32))
                return Convert.ToInt32(message);
            if (type == typeof(Int16))
                return Convert.ToInt16(message);
            if (type == typeof(Int64))
                return Convert.ToInt64(message);
            if (type == typeof(uint) || type == typeof(UInt32))
                return Convert.ToUInt32(message);
            if (type == typeof(UInt16))
                return Convert.ToUInt16(message);
            if (type == typeof(UInt64))
                return Convert.ToUInt64(message);
            if (type == typeof(string))
                return Convert.ToString(message);

            return JsonSerializer.Deserialize(message, type);
        }

        #endregion
    }
}