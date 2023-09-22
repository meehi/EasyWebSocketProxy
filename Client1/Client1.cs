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

        Uri uri = new($"{Common.HOST}?id={Guid.NewGuid()}&groupName={Common.GROUP_NAME}");

        WsProxyClient wsProxyClient = new(uri);
        Console.WriteLine($"Connecting to {Common.HOST} with the following group name: {Common.GROUP_NAME}");
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
        await wsProxyClient.SendAsync<SocketMessage>(new() { Message = "Hello from Client 1" });
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