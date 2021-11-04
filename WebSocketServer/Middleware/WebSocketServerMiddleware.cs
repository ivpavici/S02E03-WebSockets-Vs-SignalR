using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;

namespace WebSocketServer.Middleware;
public class WebSocketServerMiddleware
{
    private readonly RequestDelegate _next;

    private readonly WebSocketServerConnectionManager _manager;

    public WebSocketServerMiddleware(RequestDelegate next, WebSocketServerConnectionManager manager)
    {
        _next = next;
        _manager = manager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
            Console.WriteLine("Websocket connected");

            string ConnId = _manager.AddSocket(webSocket);
            await SendConnIdAsync(webSocket, ConnId);

            await ReceiveMessage(webSocket, async (result, buffer) =>
            {
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    Console.WriteLine("Message received");
                    Console.WriteLine($"Message {Encoding.UTF8.GetString(buffer, 0, result.Count)}");
                    await RouteJSONMessageAsync(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    return;
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    string id = _manager.GetAllSockets().FirstOrDefault(s => s.Value == webSocket).Key;
                    Console.WriteLine($"Receive->Close on: " + id);

                    WebSocket sock;
                    _manager.GetAllSockets().TryRemove(id, out sock);
                    Console.WriteLine("Managed Connections: " + _manager.GetAllSockets().Count.ToString());

                    await sock.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);

                    return;
                }
            });
        }
        else
        {
            Console.WriteLine("Hello from the 2rd request delegate");
            await _next(context);
        }
    }

    public async Task RouteJSONMessageAsync(string message)
    {
        var routeObj = JsonConvert.DeserializeObject<dynamic>(message);

        if (Guid.TryParse(routeObj.To.ToString(), out Guid guidOutput))
        {
            Console.WriteLine("Targeted");
            var sock = _manager.GetAllSockets().FirstOrDefault(s => s.Key == routeObj.To.ToString());
            if (sock.Value != null)
            {
                if (sock.Value.State == WebSocketState.Open)
                    await sock.Value.SendAsync(Encoding.UTF8.GetBytes(routeObj.Message.ToString()), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            else
            {
                Console.WriteLine("Invalid Recipient");
            }
        }
        else
        {
            Console.WriteLine("Broadcast");
            foreach (var socket in _manager.GetAllSockets())
            {
                if (socket.Value.State == WebSocketState.Open)
                {
                    await socket.Value.SendAsync(Encoding.UTF8.GetBytes(routeObj.Message.ToString()),
                        WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
    }

    private async Task SendConnIdAsync(WebSocket socket, string connId)
    {
        var buffer = Encoding.UTF8.GetBytes("ConnID: " + connId);
        await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task ReceiveMessage(WebSocket socket, Action<WebSocketReceiveResult, byte[]> handleMessage)
    {
        var buffer = new byte[1024 * 4];

        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(
                buffer: new ArraySegment<byte>(buffer),
                cancellationToken: CancellationToken.None);

            handleMessage(result, buffer);
        }
    }
}
