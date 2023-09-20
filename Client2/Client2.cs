// See https://aka.ms/new-console-template for more information
using EasyWebSocketProxy;
using SharedData.Models;

public class Client2
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, I'm client 2! Please make sure that Middleware and the other client is running!");

        Uri uri = new($"{Common.HOST}?id={Guid.NewGuid()}&groupName={Common.GROUP_NAME}");

        WsProxyClient wsProxyClient = new(uri);
        Console.WriteLine($"Connecting to {Common.HOST} with the following group name: {Common.GROUP_NAME}");
        if (await wsProxyClient.TryConnectAsync())
        {
            Console.WriteLine("Connected!");
            Console.WriteLine("Waiting for receiving messages from Client 1...");
        }
        else
        {
            Console.WriteLine("Something went wrong. Good bye!");
            Console.ReadLine();
            return;
        }

        wsProxyClient.On<SocketMessage>(ProcessSocketMessage);
        wsProxyClient.On<byte[]>(ProcessBinaryMessage);

        Console.ReadLine();
    }

    private static void ProcessSocketMessage(SocketMessage socketMessage)
    {
        Console.WriteLine($"Type message received. Message: {socketMessage.Message}");
    }

    private static void ProcessBinaryMessage(byte[] data)
    {
        Console.WriteLine($"Bytes received. Length: {data.Length}");
    }
}