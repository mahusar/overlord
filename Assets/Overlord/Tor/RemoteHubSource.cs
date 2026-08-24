using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Overlord.Explorer;

namespace Overlord.Tor
{
    public class RemoteHubSource : IHubSource, IDisposable
    {
        public const int DefaultTimeoutMs = 45000;

        private readonly string onion;
        private readonly int port;
        private readonly string name;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        private TcpClient socket;
        private StreamReader reader;
        private StreamWriter writer;
        private bool disposed;

        public int TimeoutMs = DefaultTimeoutMs;

        public RemoteHubSource(string onion, int port)
        {
            if (string.IsNullOrEmpty(onion))
            {
                throw new ArgumentException("onion must be set", "onion");
            }

            this.onion = onion;
            this.port = port <= 0 ? TorConfig.DefaultHubPort : port;
            name = Shorten(onion);
        }

        public string Name
        {
            get { return name; }
        }

        public string Onion
        {
            get { return onion; }
        }

        public bool Connected
        {
            get { return socket != null && socket.Connected; }
        }

        public async Task<string> AskAsync(string line)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("RemoteHubSource");
            }

            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        await EnsureAsync().ConfigureAwait(false);
                        await Deadline(writer.WriteLineAsync(line)).ConfigureAwait(false);
                        string answer = await Deadline(reader.ReadLineAsync()).ConfigureAwait(false);

                        if (answer == null)
                        {
                            throw new IOException("the hub closed the connection");
                        }

                        return answer;
                    }
                    catch (Exception) when (attempt == 0)
                    {
                        Drop();
                    }
                }

                throw new IOException("the hub did not answer");
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<string> ConnectAsync()
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await EnsureAsync().ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                Drop();
                return Describe(ex);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task EnsureAsync()
        {
            if (socket != null && socket.Connected)
            {
                return;
            }

            Drop();

            var fresh = new TcpClient();
            fresh.NoDelay = true;

            await fresh.ConnectThroughProxyAsync(TorConfig.SocksHost, TorConfig.SocksPort,
                onion, port).ConfigureAwait(false);

            NetworkStream stream = fresh.GetStream();
            stream.ReadTimeout = TimeoutMs;
            stream.WriteTimeout = TimeoutMs;

            socket = fresh;
            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.NewLine = "\n";
            writer.AutoFlush = true;
        }

        private async Task Deadline(Task work)
        {
            Task delay = Task.Delay(TimeoutMs);
            Task first = await Task.WhenAny(work, delay).ConfigureAwait(false);
            if (first != work)
            {
                throw new TimeoutException("the hub took longer than " + TimeoutMs + " ms");
            }

            await work.ConfigureAwait(false);
        }

        private async Task<T> Deadline<T>(Task<T> work)
        {
            Task delay = Task.Delay(TimeoutMs);
            Task first = await Task.WhenAny(work, delay).ConfigureAwait(false);
            if (first != work)
            {
                throw new TimeoutException("the hub took longer than " + TimeoutMs + " ms");
            }

            return await work.ConfigureAwait(false);
        }

        private void Drop()
        {
            try
            {
                if (writer != null) writer.Dispose();
            }
            catch (Exception)
            {
            }

            try
            {
                if (reader != null) reader.Dispose();
            }
            catch (Exception)
            {
            }

            try
            {
                if (socket != null) socket.Close();
            }
            catch (Exception)
            {
            }

            writer = null;
            reader = null;
            socket = null;
        }

        public void Dispose()
        {
            disposed = true;
            Drop();
        }

        public static string Describe(Exception ex)
        {
            if (ex == null)
            {
                return "unknown error";
            }

            if (ex is TimeoutException)
            {
                return "the hub did not answer in time";
            }

            IOException io = ex as IOException;
            if (io != null)
            {
                return io.Message;
            }

            SocketException socketError = ex as SocketException;
            if (socketError != null)
            {
                return "Tor refused the connection (" + socketError.SocketErrorCode + "). Is Tor running?";
            }

            return ex.Message;
        }

        private static string Shorten(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 20)
            {
                return value;
            }
            return value.Substring(0, 12) + "..." + value.Substring(value.Length - 6);
        }
    }
}
