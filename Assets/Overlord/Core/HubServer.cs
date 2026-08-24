using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Overlord
{
    public class HubServer : IDisposable
    {
        public const int DefaultPort = 7790;
        public const int DefaultMaxConnections = 64;
        public const int DefaultMaxConcurrentQueries = 4;
        public const int DefaultIdleSeconds = 120;
        public const int DefaultBucketCapacity = 80;
        public const double DefaultRefillPerSecond = 4d;
        public const int CostWalk = 20;
        public const int CostExpensive = 10;
        public const int CostModerate = 3;
        public const int CostCheap = 1;

        public int MaxConnections = DefaultMaxConnections;
        public int MaxConcurrentQueries = DefaultMaxConcurrentQueries;
        public int IdleSeconds = DefaultIdleSeconds;
        public int BucketCapacity = DefaultBucketCapacity;
        public double RefillPerSecond = DefaultRefillPerSecond;

        private readonly HubDispatcher dispatcher;
        private readonly IPAddress address;
        private readonly int requestedPort;
        private readonly List<TcpClient> live = new List<TcpClient>();
        private readonly object liveLock = new object();

        private TcpListener listener;
        private CancellationTokenSource life;
        private SemaphoreSlim queries;
        private Task accepting;
        private bool disposed;

        private int connections;
        private long served;
        private long refused;
        private long limited;

        public string Notice { get; private set; }

        public HubServer(HubDispatcher dispatcher)
            : this(dispatcher, DefaultPort, IPAddress.Loopback)
        {
        }

        public HubServer(HubDispatcher dispatcher, int port)
            : this(dispatcher, port, IPAddress.Loopback)
        {
        }

        public HubServer(HubDispatcher dispatcher, int port, IPAddress address)
        {
            if (dispatcher == null)
            {
                throw new ArgumentNullException("dispatcher");
            }

            this.dispatcher = dispatcher;
            this.requestedPort = port < 0 ? DefaultPort : port;
            this.address = address ?? IPAddress.Loopback;
        }

        public bool Listening
        {
            get { return listener != null && life != null && !life.IsCancellationRequested; }
        }

        public int Port
        {
            get
            {
                if (listener == null)
                {
                    return requestedPort;
                }

                IPEndPoint bound = listener.LocalEndpoint as IPEndPoint;
                return bound == null ? requestedPort : bound.Port;
            }
        }

        public int Connections
        {
            get { return connections; }
        }

        public long Served
        {
            get { return Interlocked.Read(ref served); }
        }

        public long Refused
        {
            get { return Interlocked.Read(ref refused); }
        }

        public long Limited
        {
            get { return Interlocked.Read(ref limited); }
        }

        public async Task<string> StartAsync(Func<Task<string>> selfCheck)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("HubServer");
            }

            if (Listening)
            {
                return "the hub is already listening on port " + Port;
            }

            if (selfCheck != null)
            {
                string trouble;
                try
                {
                    trouble = await selfCheck().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    trouble = "the self check threw: " + ex.Message;
                }

                if (trouble != null)
                {
                    return trouble;
                }
            }

            string missing = dispatcher.Unregistered();
            Notice = missing == null
                ? null
                : "these allowlisted queries have no handler and will answer not available: " + missing;

            var fresh = new TcpListener(address, requestedPort);
            try
            {
                fresh.Start();
            }
            catch (SocketException ex)
            {
                return "could not listen on " + address + ":" + requestedPort + " (" + ex.SocketErrorCode + ")";
            }

            listener = fresh;
            life = new CancellationTokenSource();
            queries = new SemaphoreSlim(MaxConcurrentQueries < 1 ? 1 : MaxConcurrentQueries);
            accepting = AcceptAsync(life.Token);
            return null;
        }

        public void Stop()
        {
            if (life != null)
            {
                try
                {
                    life.Cancel();
                }
                catch (Exception)
                {
                }
            }

            if (listener != null)
            {
                try
                {
                    listener.Stop();
                }
                catch (Exception)
                {
                }
            }

            TcpClient[] open;
            lock (liveLock)
            {
                open = live.ToArray();
                live.Clear();
            }

            foreach (TcpClient client in open)
            {
                Close(client);
            }

            listener = null;
        }

        public void Dispose()
        {
            disposed = true;
            Stop();

            if (life != null)
            {
                try
                {
                    life.Dispose();
                }
                catch (Exception)
                {
                }
                life = null;
            }

            if (queries != null)
            {
                try
                {
                    queries.Dispose();
                }
                catch (Exception)
                {
                }
                queries = null;
            }
        }

        public static int CostOf(string query)
        {
            switch (query)
            {
                case HubQueries.Volume:
                    return CostWalk;
                case HubQueries.GetRichList:
                case HubQueries.Series:
                case HubQueries.Registry:
                    return CostExpensive;
                case HubQueries.GetAddress:
                case HubQueries.ChainStats:
                case HubQueries.Holders:
                    return CostModerate;
                default:
                    return CostCheap;
            }
        }

        private async Task AcceptAsync(CancellationToken token)
        {
            TcpListener current = listener;

            while (!token.IsCancellationRequested && current != null)
            {
                TcpClient client;
                try
                {
                    client = await current.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    continue;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                if (Interlocked.Increment(ref connections) > MaxConnections)
                {
                    Interlocked.Decrement(ref connections);
                    Interlocked.Increment(ref refused);
                    await RefuseAsync(client).ConfigureAwait(false);
                    continue;
                }

                lock (liveLock)
                {
                    live.Add(client);
                }

                Task ignored = ServeAsync(client, token);
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                var reader = new LineReader(stream);
                var bucket = new Bucket(BucketCapacity, RefillPerSecond);

                while (!token.IsCancellationRequested)
                {
                    string line = await Deadline(
                        reader.NextAsync(HubProtocol.MaxRequestBytes, token), IdleSeconds * 1000)
                        .ConfigureAwait(false);

                    if (reader.Overflow)
                    {
                        await WriteAsync(stream, HubProtocol.Fail(null, "request too large"), token)
                            .ConfigureAwait(false);
                        return;
                    }

                    if (line == null)
                    {
                        return;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    string answer = await AnswerAsync(line, bucket).ConfigureAwait(false);
                    await WriteAsync(stream, answer, token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                Interlocked.Decrement(ref connections);
                lock (liveLock)
                {
                    live.Remove(client);
                }
                Close(client);
            }
        }

        private async Task<string> AnswerAsync(string line, Bucket bucket)
        {
            HubRequest peek;
            string parseError;
            string callerId;
            bool parsed = HubProtocol.TryParseRequest(line, out peek, out parseError, out callerId);
            int cost = parsed ? CostOf(peek.Query) : CostCheap;

            if (!bucket.Take(cost))
            {
                Interlocked.Increment(ref limited);
                return HubProtocol.Fail(callerId, "rate limited");
            }

            SemaphoreSlim gate = queries;
            if (gate != null)
            {
                await gate.WaitAsync().ConfigureAwait(false);
            }

            try
            {
                string answer = await dispatcher.HandleLineAsync(line).ConfigureAwait(false);
                Interlocked.Increment(ref served);
                return answer;
            }
            finally
            {
                if (gate != null)
                {
                    try
                    {
                        gate.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
        }

        private async Task RefuseAsync(TcpClient client)
        {
            try
            {
                await WriteAsync(client.GetStream(), HubProtocol.Fail(null, "busy"), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            finally
            {
                Close(client);
            }
        }

        private static async Task WriteAsync(Stream stream, string line, CancellationToken token)
        {
            byte[] payload = new UTF8Encoding(false).GetBytes(line + "\n");
            await stream.WriteAsync(payload, 0, payload.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }

        private static async Task<T> Deadline<T>(Task<T> work, int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                return await work.ConfigureAwait(false);
            }

            Task delay = Task.Delay(timeoutMs);
            Task first = await Task.WhenAny(work, delay).ConfigureAwait(false);
            if (first != work)
            {
                throw new TimeoutException("the caller went quiet for " + timeoutMs + " ms");
            }

            return await work.ConfigureAwait(false);
        }

        private static void Close(TcpClient client)
        {
            if (client == null)
            {
                return;
            }

            try
            {
                client.Close();
            }
            catch (Exception)
            {
            }
        }

        private class LineReader
        {
            private readonly Stream stream;
            private readonly byte[] buffer = new byte[1024];
            private readonly MemoryStream pending = new MemoryStream();

            private int length;
            private int offset;

            public bool Overflow;

            public LineReader(Stream stream)
            {
                this.stream = stream;
            }

            public async Task<string> NextAsync(int cap, CancellationToken token)
            {
                pending.SetLength(0);

                while (true)
                {
                    if (offset >= length)
                    {
                        length = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        offset = 0;

                        if (length <= 0)
                        {
                            return null;
                        }
                    }

                    while (offset < length)
                    {
                        byte value = buffer[offset++];

                        if (value == 10)
                        {
                            string line = new UTF8Encoding(false)
                                .GetString(pending.GetBuffer(), 0, (int)pending.Length);
                            return line.TrimEnd('\r');
                        }

                        if (pending.Length >= cap)
                        {
                            Overflow = true;
                            return null;
                        }

                        pending.WriteByte(value);
                    }
                }
            }
        }

        private class Bucket
        {
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private readonly double capacity;
            private readonly double refill;

            private double tokens;
            private double last;

            public Bucket(double capacity, double refill)
            {
                this.capacity = capacity < 1d ? 1d : capacity;
                this.refill = refill < 0d ? 0d : refill;
                this.tokens = this.capacity;
                this.last = 0d;
            }

            public bool Take(int cost)
            {
                double now = clock.Elapsed.TotalSeconds;
                tokens += (now - last) * refill;
                last = now;

                if (tokens > capacity)
                {
                    tokens = capacity;
                }

                if (tokens < cost)
                {
                    return false;
                }

                tokens -= cost;
                return true;
            }
        }
    }
}
