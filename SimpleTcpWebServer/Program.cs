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
            using (NetworkStream stream = new NetworkStream(_clientSocket, true))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 8192, leaveOpen: true))
            { 
                string requestLine = reader.ReadLine();
                if(string.IsNullOrEmpty(requestLine)) 
                {
                    Console.WriteLine($"Client {_clientIdentifier}: Empty request line.");
                    return;
                }
                Console.WriteLine($"Client {_clientIdentifier} Request: {requestLine}");



                string headerLine;
                while (!string.IsNullOrEmpty(headerLine = reader.ReadLine())) 
                {
                    Console.WriteLine(headerLine);
                }



                string[] requestParts = requestLine.Split(' ');

                if (requestParts.Length < 3) 
                {
                    Console.WriteLine($"Client {_clientIdentifier}: Invalid request line format.");
                    SendMinimalError(writer, "400 Bad Request", "Bad Request"); 
                    return;
                }



                string httpMethod = requestParts[0];     // Will be "GET"
                string requestedUrl = requestParts[1];   // Will be "/" or "/index.html" or something else

                if (httpMethod.ToUpper() == "GET")
                {
                    string targetFilePath = "index.html";

                    if (requestedUrl == "/" || requestedUrl.ToLower() == "/index.html")
                    {
                        string baseDirectory = AppContext.BaseDirectory;
                        string fullWebRoot = Path.GetFullPath(Path.Combine(baseDirectory, _webRootPath));
                        string filePath = Path.Combine(fullWebRoot, targetFilePath);

                        if (File.Exists(filePath))
                        {
                            byte[] fileContentBytes = File.ReadAllBytes(filePath);

                            writer.WriteLine("HTTP/1.1 200 OK");
                            writer.WriteLine("Content-Type: text/html; charset=UTF-8");
                            writer.WriteLine($"Content-Length: {fileContentBytes.Length}");
                            writer.WriteLine("Connection: close");
                            writer.WriteLine();
                            writer.Flush();

                            stream.Write(fileContentBytes, 0, fileContentBytes.Length);
                            stream.Flush();

                            Console.WriteLine($"Client {_clientIdentifier}: Successfully sent {filePath}.");
                        }
                        else
                        {
                            Console.WriteLine($"Client {_clientIdentifier}: File not found at path: {filePath}");
                            SendMinimalError(writer, "404 Not Found", $"File {targetFilePath} not found on server.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Client {_clientIdentifier}: Requested URL '{requestedUrl}' is not '/index.html'.");
                        SendMinimalError(writer, "404 Not Found", $"File for URL {requestedUrl} not found.");
                    }
                } 
                else
                {
                    Console.WriteLine($"Client {_clientIdentifier}: Method '{httpMethod}' not allowed.");
                    SendMinimalError(writer, "405 Method Not Allowed", "Method Not Allowed");
                } 
            }
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

    private void SendMinimalError(StreamWriter writer, string statusCodeAndReason, string bodyMessage)
    {
        try
        {
            writer.WriteLine($"HTTP/1.1 {statusCodeAndReason}");
            writer.WriteLine("Content-Type: text/plain; charset=UTF-8");
            writer.WriteLine("Connection: close");
            writer.WriteLine(); // End of headers
            writer.WriteLine(bodyMessage);
            writer.Flush(); // Send response
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending minimal error response to {_clientIdentifier}: {ex.Message}");
        }
    }
}
