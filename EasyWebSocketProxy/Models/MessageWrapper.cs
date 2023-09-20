namespace EasyWebSocketProxy.Models
{
    internal class MessageWrapper<T>
    {
        public string? MessageType { get; set; }
        public T? Message { get; set; }
    }

    internal class MessageWrapper
    {
        public string? MessageType { get; set; }
        public object? Message { get; set; }
    }
}
