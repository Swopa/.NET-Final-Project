using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

const int Port = 8080;
const string WebRootPath = "webroot";

Socket listenerSocket = null;

try 
{
    IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, Port);
    listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    listenerSocket.Bind(localEndPoint);
    listenerSocket.Listen(100);

    Console.WriteLine($"Server started on port {Port}");

    string fullWebRootPath = Path.GetFullPath(WebRootPath);


    if (!Directory.Exists(fullWebRootPath))
    {
        Directory.CreateDirectory(fullWebRootPath);
        Console.WriteLine($"Created webroot directory at: {fullWebRootPath}");
    }
    else
    {
        Console.WriteLine($"Serving files from: {fullWebRootPath}");
    }
    Console.WriteLine("Waiting for connections...");


    while (true) 
    {
        Socket clientSocket = await listenerSocket.AcceptAsync();

        string clientIdentifier = "Unknown Client";

        if (clientSocket.RemoteEndPoint is IPEndPoint clientEndPoint)
        {
            clientIdentifier = $"{clientEndPoint.Address}:{clientEndPoint.Port}";
        }
        Console.WriteLine($"Client connected: {clientIdentifier}");

        ClientHandler handlerInstance = new ClientHandler(clientSocket, WebRootPath, clientIdentifier);
        Thread clientThread = new Thread(handlerInstance.Process);

        clientThread.Start();
    }
}
catch (SocketException se)
{
    Console.WriteLine($"SocketException: {se.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"Server error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
finally
{
    listenerSocket?.Close();
    Console.WriteLine("Server stopped.");
}


public class ClientHandler
{
    private Socket _clientSocket;
    private string _webRootPath;
    private string _clientIdentifier;

    public ClientHandler(Socket clientSocket, string webRootPath, string clientIdentifier) 
    { 
        _clientSocket = clientSocket;
        _webRootPath = webRootPath;
        _clientIdentifier = clientIdentifier;
    }

    public void Process()
    {
        Console.WriteLine($"Handling client: {_clientIdentifier} on NEW thread ID {Thread.CurrentThread.ManagedThreadId}");
        try
        {
            Thread.Sleep(100);
        }
        catch (IOException ex) 
        {
            Console.WriteLine($"IO Exception with client {_clientIdentifier}: {ex.Message}");
        }
        catch (SocketException se)
        {
            Console.WriteLine($"SocketException with client {_clientIdentifier}: {se.Message} (Code: {se.SocketErrorCode})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client {_clientIdentifier}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Console.WriteLine($"Closing connection for: {_clientIdentifier}");
            try
            {
                _clientSocket.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException) { /* Ignore if already closed or error */ }
            catch (ObjectDisposedException) { /* Ignore if already disposed */ }
            _clientSocket.Close();
        }
    }
}
