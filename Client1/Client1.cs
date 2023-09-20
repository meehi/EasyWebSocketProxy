// See https://aka.ms/new-console-template for more information
using EasyWebSocketProxy;
using SharedData.Models;
using System.Text;

public class Client1
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, I'm client 1! Please make sure that Middleware and the other client is running!");
        Console.WriteLine("Press enter to continue...");
        Console.ReadLine();

        string host = "wss://localhost:7298/ws";
        string groupName = "group_1";
        Uri uri = new($"{host}?id={Guid.NewGuid()}&groupName={groupName}");

        WsProxyClient wsProxyClient = new(uri);
        Console.WriteLine($"Connecting to {host} with the following group name: {groupName}");
        if (await wsProxyClient.TryConnectAsync())
            Console.WriteLine("Connected!");
        else
        {
            Console.WriteLine("Something went wrong. Good bye!");
            Console.ReadLine();
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Press enter to send Type message to Client 2...");
        Console.ReadLine();
        await wsProxyClient.SendAsync(new SocketMessage() { Message = "Hello from Client 1" });
        Console.WriteLine("Type message sent!");

        Console.WriteLine();
        Console.WriteLine("Press enter to send byte array to Client 2...");
        Console.ReadLine();
        byte[] data = Encoding.UTF8.GetBytes("Hello from Client 1! This is going to be a byte array message!");
        await wsProxyClient.SendAsync(data);
        Console.WriteLine("Byte array sent! Good bye!");
        Console.ReadLine();
    }
}