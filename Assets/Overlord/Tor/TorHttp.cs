using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Overlord.Tor
{
    public class TorResponse
    {
        public int Status;
        public string Body;
        public string Error;

        public bool Ok
        {
            get { return Error == null && Status >= 200 && Status < 300; }
        }
    }

    public static class TorHttp
    {
        public const int DefaultTimeoutMs = 45000;
        public const int MaxBodyBytes = 1048576;
        public const int MaxRedirects = 3;
        public const string UserAgent = "Overlord/0.1 (+https://github.com/mahusar/overlord)";

        public static async Task<TorResponse> GetAsync(string url, string accept, int timeoutMs)
        {
            var response = new TorResponse();
            string target = url;

            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                string host;
                string path;
                int port;
                if (!Split(target, out host, out path, out port))
                {
                    response.Error = "that url cannot be parsed";
                    return response;
                }

                try
                {
                    TorResponse hopResult = await FetchAsync(host, port, path, accept, timeoutMs)
                        .ConfigureAwait(false);

                    if (hopResult.Status == 301 || hopResult.Status == 302 ||
                        hopResult.Status == 307 || hopResult.Status == 308)
                    {
                        if (string.IsNullOrEmpty(hopResult.Body))
                        {
                            hopResult.Error = "redirected with no location";
                            return hopResult;
                        }

                        target = hopResult.Body;
                        continue;
                    }

                    return hopResult;
                }
                catch (Exception ex)
                {
                    response.Error = Describe(ex);
                    return response;
                }
            }

            response.Error = "too many redirects";
            return response;
        }

        private static async Task<TorResponse> FetchAsync(string host, int port, string path,
                                                          string accept, int timeoutMs)
        {
            var result = new TorResponse();

            using (var socket = new TcpClient())
            {
                socket.NoDelay = true;

                Task connect = socket.ConnectThroughProxyAsync(
                    TorConfig.SocksHost, TorConfig.SocksPort, host, port);

                if (!await Within(connect, timeoutMs).ConfigureAwait(false))
                {
                    result.Error = "Tor did not open a circuit in time";
                    return result;
                }

                using (NetworkStream raw = socket.GetStream())
                {
                    Stream stream = raw;
                    SslStream tls = null;

                    try
                    {
                        if (port == 443)
                        {
                            tls = new SslStream(raw, false);
                            Task handshake = tls.AuthenticateAsClientAsync(host);
                            if (!await Within(handshake, timeoutMs).ConfigureAwait(false))
                            {
                                result.Error = "the TLS handshake did not finish in time";
                                return result;
                            }
                            stream = tls;
                        }

                        var request = new StringBuilder();
                        request.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
                        request.Append("Host: ").Append(host).Append("\r\n");
                        request.Append("User-Agent: ").Append(UserAgent).Append("\r\n");
                        request.Append("Accept: ").Append(
                            string.IsNullOrEmpty(accept) ? "*/*" : accept).Append("\r\n");
                        request.Append("Accept-Encoding: identity\r\n");
                        request.Append("Connection: close\r\n\r\n");

                        byte[] bytes = Encoding.ASCII.GetBytes(request.ToString());
                        await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);

                        byte[] all = await ReadAllAsync(stream, timeoutMs).ConfigureAwait(false);
                        Parse(all, result);
                        return result;
                    }
                    finally
                    {
                        if (tls != null)
                        {
                            try
                            {
                                tls.Dispose();
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
        }

        private static async Task<byte[]> ReadAllAsync(Stream stream, int timeoutMs)
        {
            var buffer = new byte[16384];
            using (var sink = new MemoryStream())
            {
                while (sink.Length < MaxBodyBytes)
                {
                    Task<int> read = stream.ReadAsync(buffer, 0, buffer.Length);
                    if (!await Within(read, timeoutMs).ConfigureAwait(false))
                    {
                        break;
                    }

                    int count = read.Result;
                    if (count <= 0)
                    {
                        break;
                    }

                    sink.Write(buffer, 0, count);
                }

                return sink.ToArray();
            }
        }

        private static void Parse(byte[] raw, TorResponse result)
        {
            if (raw == null || raw.Length == 0)
            {
                result.Error = "the server sent nothing";
                return;
            }

            int split = Find(raw, Encoding.ASCII.GetBytes("\r\n\r\n"));
            if (split < 0)
            {
                result.Error = "the reply had no header break";
                return;
            }

            string head = Encoding.ASCII.GetString(raw, 0, split);
            string[] lines = head.Split('\n');

            if (lines.Length == 0)
            {
                result.Error = "the reply had no status line";
                return;
            }

            string[] status = lines[0].Trim().Split(' ');
            int code;
            if (status.Length < 2 ||
                !int.TryParse(status[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
            {
                result.Error = "the status line could not be read";
                return;
            }

            result.Status = code;

            bool chunked = false;
            string location = null;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                string name = line.Substring(0, colon).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();

                if (name == "transfer-encoding" && value.ToLowerInvariant().Contains("chunked"))
                {
                    chunked = true;
                }
                else if (name == "location")
                {
                    location = value;
                }
            }

            if (code == 301 || code == 302 || code == 307 || code == 308)
            {
                result.Body = location;
                return;
            }

            int start = split + 4;
            int length = raw.Length - start;
            if (length < 0)
            {
                length = 0;
            }

            string body = Encoding.UTF8.GetString(raw, start, length);
            result.Body = chunked ? Dechunk(body) : body;
        }

        private static string Dechunk(string body)
        {
            var sink = new StringBuilder();
            int at = 0;

            while (at < body.Length)
            {
                int lineEnd = body.IndexOf("\r\n", at, StringComparison.Ordinal);
                if (lineEnd < 0)
                {
                    break;
                }

                string header = body.Substring(at, lineEnd - at).Trim();
                int semicolon = header.IndexOf(';');
                if (semicolon >= 0)
                {
                    header = header.Substring(0, semicolon);
                }

                int size;
                if (!int.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out size))
                {
                    break;
                }

                if (size == 0)
                {
                    break;
                }

                int from = lineEnd + 2;
                if (from + size > body.Length)
                {
                    size = body.Length - from;
                    if (size <= 0)
                    {
                        break;
                    }
                }

                sink.Append(body, from, size);
                at = from + size + 2;
            }

            return sink.ToString();
        }

        private static async Task<bool> Within(Task work, int timeoutMs)
        {
            Task delay = Task.Delay(timeoutMs);
            Task first = await Task.WhenAny(work, delay).ConfigureAwait(false);
            if (first != work)
            {
                return false;
            }

            await work.ConfigureAwait(false);
            return true;
        }

        private static bool Split(string url, out string host, out string path, out int port)
        {
            host = null;
            path = "/";
            port = 443;

            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            string rest;
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                rest = url.Substring(8);
                port = 443;
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                rest = url.Substring(7);
                port = 80;
            }
            else
            {
                return false;
            }

            int slash = rest.IndexOf('/');
            string authority = slash < 0 ? rest : rest.Substring(0, slash);
            path = slash < 0 ? "/" : rest.Substring(slash);

            int colon = authority.IndexOf(':');
            if (colon > 0)
            {
                int parsed;
                if (int.TryParse(authority.Substring(colon + 1),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    port = parsed;
                }
                authority = authority.Substring(0, colon);
            }

            host = authority;
            return host.Length > 0;
        }

        private static int Find(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool hit = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        hit = false;
                        break;
                    }
                }
                if (hit)
                {
                    return i;
                }
            }
            return -1;
        }

        private static string Describe(Exception ex)
        {
            if (ex is AuthenticationException)
            {
                return "the certificate could not be verified through Tor";
            }

            if (ex is IOException)
            {
                return ex.Message;
            }

            SocketException socketError = ex as SocketException;
            if (socketError != null)
            {
                return "Tor refused the connection (" + socketError.SocketErrorCode + ")";
            }

            return ex.Message;
        }
    }
}
