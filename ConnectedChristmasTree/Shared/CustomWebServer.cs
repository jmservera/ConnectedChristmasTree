using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Shared
{
    public sealed class CustomWebServer : IDisposable
    {
        private const uint BufferSize = 8192;
        private string port;
        private readonly StreamSocketListener listener;
        string htmlPath;

        public event EventHandler<string> CommandReceived;
        public string Port { get { return port; } }

        public CustomWebServer(string serverPort)
        {
            listener = new StreamSocketListener();
            listener.ConnectionReceived += (s, e) => ProcessRequestAsync(e.Socket);
            port = serverPort;
        }
        public CustomWebServer(int serverPort) : this(serverPort.ToString())
        {
        }

        public IAsyncAction StartServerAsync(string htmlFilePath="content/Controller.html")
        {
            htmlPath = htmlFilePath;
            return listener.BindServiceNameAsync(port);
        }

        public void Dispose()
        {
            listener.Dispose();
        }

        private async void ProcessRequestAsync(StreamSocket socket)
        {
            StringBuilder request = new StringBuilder();
            using (IInputStream input = socket.InputStream)
            {
                byte[] data = new byte[BufferSize];
                IBuffer buffer = data.AsBuffer();
                uint dataRead = BufferSize;
                while (dataRead == BufferSize)
                {
                    await input.ReadAsync(buffer, BufferSize, InputStreamOptions.Partial);
                    request.Append(Encoding.UTF8.GetString(data, 0, data.Length));
                    dataRead = buffer.Length;
                }
            }

            string requestMethod = request.ToString().Split('\n')[0];
            string[] requestParts = requestMethod.Split(' ');

            if (requestParts[0] == "GET")
            {
                var requestQuery = requestParts[1].Split('?').LastOrDefault();
                if (requestQuery != null)
                {
                    var button = requestQuery.Split('=').FirstOrDefault();
                    if (button != null)
                    {
                        if (CommandReceived != null)
                        {
                            CommandReceived(this, button);
                        }
                    }
                }

                using (IOutputStream output = socket.OutputStream)
                {
                    await WriteResponseAsync(requestParts[1], output);
                }
            }
            else
                throw new InvalidDataException("HTTP method not supported: "
                                               + requestParts[0]);
        }

        private async Task WriteResponseAsync(string request, IOutputStream os)
        {
            // Show the html 
            using (Stream resp = os.AsStreamForWrite())
            {
                using (var file = File.OpenRead(htmlPath))
                {
                    string header = $"HTTP/1.1 200 OK\r\nContent-Length: {file.Length}\r\n" +
                                     "Content-Type:text/html\r\nConnection: close\r\n\r\n";
                    byte[] headerArray = Encoding.UTF8.GetBytes(header);
                    await resp.WriteAsync(headerArray, 0, headerArray.Length);
                    await file.CopyToAsync(resp);
                }
            }
        }
    }
}
