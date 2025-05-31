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

    private static readonly string HtmlError404 = """
        <!DOCTYPE html><html><head><title>404 Not Found</title></head>
        <body><h1>Error 404: Page Not Found</h1></body></html>
        """;

    private static readonly string HtmlError405 = """
        <!DOCTYPE html><html><head><title>405 Method Not Allowed</title></head>
        <body><h1>Error 405: Method Not Allowed</h1></body></html>
        """;

    private static readonly string HtmlError403 = """
        <!DOCTYPE html><html><head><title>403 Forbidden</title></head>
        <body><h1>Error 403: Forbidden</h1></body></html>
        """;

    public ClientHandler(Socket clientSocket, string webRootPath, string clientIdentifier)
    {
        _clientSocket = clientSocket;
        _webRootPath = webRootPath;
        _clientIdentifier = clientIdentifier;
    }

    public void SendHttpResponse(StreamWriter writer, NetworkStream stream, string statusCode, string statusMessage, string contentType,
        byte[] contentBytes, bool closeConnection = true)
    {
        try
        {
            writer.WriteLine($"HTTP/1.1 {statusCode} {statusMessage}");
            writer.WriteLine($"Content-Type: {contentType}");
            writer.WriteLine($"Content-Length: {contentBytes.Length}");
            if (closeConnection)
            {
                writer.WriteLine("Connection: close");
            }

            writer.WriteLine();
            writer.Flush();

            if (contentBytes.Length > 0)
            {
                stream.Write(contentBytes, 0, contentBytes.Length);
                stream.Flush();
            }
            Console.WriteLine($"Client {_clientIdentifier}: Sent {statusCode} {statusMessage}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client {_clientIdentifier}: Error sending HTTP response ({statusCode}): {ex.Message}");
        }
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
                if (string.IsNullOrEmpty(requestLine))
                {
                    Console.WriteLine($"Client {_clientIdentifier}: Empty request line.");
                    return;
                }
                //Console.WriteLine($"Client {_clientIdentifier} Request: {requestLine}");



                string headerLine;
                while (!string.IsNullOrEmpty(headerLine = reader.ReadLine())) { /* Consume headers */ }



                string[] requestParts = requestLine.Split(' ');

                if (requestParts.Length < 3)
                {
                    Console.WriteLine($"Client {_clientIdentifier}: Invalid request line format.");
                    byte[] errorBody = Encoding.UTF8.GetBytes(HtmlError403);
                    SendHttpResponse(writer, stream, "400", "Bad Request", "text/html; charset=UTF-8", errorBody);
                    return;
                }


                


                string httpMethod = requestParts[0].ToUpper();     // Will be "GET"
                string requestedUrl = requestParts[1];   // Will be "/" or "/index.html" or something else

                if (httpMethod != "GET")
                {
                    Console.WriteLine($"Client {_clientIdentifier}: Method '{httpMethod}' not allowed.");
                    byte[] errorBody = Encoding.UTF8.GetBytes(HtmlError405);
                    SendHttpResponse(writer, stream, "405", "Method Not Allowed", "text/html; charset=UTF-8", errorBody);
                    return;
                }


                string relativeFilePath = requestedUrl.TrimStart('/');
                if (string.IsNullOrEmpty(relativeFilePath)) // Request for "/"
                {
                    relativeFilePath = "index.html"; // Default document
                }


                string safeRelativePath = Path.GetFullPath(Path.Combine(_webRootPath, relativeFilePath))
                                              .Substring(Path.GetFullPath(_webRootPath).Length)
                                              .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!relativeFilePath.Equals(safeRelativePath, StringComparison.OrdinalIgnoreCase) || safeRelativePath.Contains(".."))
                {
                    Console.WriteLine($"Client {_clientIdentifier}: Directory traversal attempt or invalid path: '{requestedUrl}' -> '{safeRelativePath}'");
                    byte[] errorBody = Encoding.UTF8.GetBytes(HtmlError403); // Or 400 Bad Request
                    SendHttpResponse(writer, stream, "403", "Forbidden", "text/html; charset=UTF-8", errorBody);
                    return;
                }

                relativeFilePath = safeRelativePath;


                string fileExtension = Path.GetExtension(relativeFilePath).ToLowerInvariant();
                var allowedExtensions = new[] { ".html", ".css", ".js" };

                if (!allowedExtensions.Contains(fileExtension) && !string.IsNullOrEmpty(fileExtension))
                {
                    Console.WriteLine($"Client {_clientIdentifier}: Invalid file extension '{fileExtension}' for URL '{requestedUrl}'.");
                    byte[] errorBody = Encoding.UTF8.GetBytes(HtmlError403);
                    SendHttpResponse(writer, stream, "403", "Forbidden", "text/html; charset=UTF-8", errorBody);
                    return;
                }


                string baseDirectory = AppContext.BaseDirectory;
                string fullWebRoot = Path.GetFullPath(Path.Combine(baseDirectory, _webRootPath));
                string absoluteFilePath = Path.Combine(fullWebRoot, relativeFilePath);

                if (File.Exists(absoluteFilePath))
                {

                    string contentType = "application/octet-stream";
                    if (fileExtension == ".html") contentType = "text/html; charset=UTF-8";
                    else if (fileExtension == ".css") contentType = "text/css; charset=UTF-8";
                    else if (fileExtension == ".js") contentType = "application/javascript; charset=UTF-8";


                    Console.WriteLine($"Client {_clientIdentifier}: Serving file: {absoluteFilePath} as {contentType}");
                    byte[] fileContentBytes = File.ReadAllBytes(absoluteFilePath);
                    SendHttpResponse(writer, stream, "200", "OK", contentType, fileContentBytes);
                }
                else
                {
                    Console.WriteLine($"Client {_clientIdentifier}: File not found at path: {absoluteFilePath}");
                    byte[] errorBody = Encoding.UTF8.GetBytes(HtmlError404);
                    SendHttpResponse(writer, stream, "404", "Not Found", "text/html; charset=UTF-8", errorBody);
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

    
}
