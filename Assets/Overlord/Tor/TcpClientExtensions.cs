using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Overlord.Tor
{
    public static class TcpClientExtensions
    {
        private const byte SOCKS_5 = 0x05;
        private const byte AUTH_ANONYMOUS = 0x00;
        private const byte AUTH_USERNAME = 0x02;
        private const byte AUTH_VERSION = 0x01;

        private const byte CONNECT = 0x01;

        private const byte IPV4 = 0x01;
        private const byte DOMAIN = 0x03;
        private const byte IPV6 = 0x04;

        private const byte EMPTY = 0x00;
        private const byte ERROR = 0xFF;

        public static async Task<(string BoundAddress, int BoundPort)> ConnectThroughProxyAsync(
                    this TcpClient client,
                    string proxyAddress,
                    int proxyPort,
                    string destinationAddress,
                    int destinationPort,
                    string username = null,
                    string password = null,
                    CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(proxyAddress)) throw new ArgumentNullException(nameof(proxyAddress));
            if (proxyPort < IPEndPoint.MinPort || proxyPort > IPEndPoint.MaxPort) throw new ArgumentOutOfRangeException(nameof(proxyPort));
            if (string.IsNullOrEmpty(destinationAddress)) throw new ArgumentNullException(nameof(destinationAddress));
            if (destinationPort < IPEndPoint.MinPort || destinationPort > IPEndPoint.MaxPort) throw new ArgumentOutOfRangeException(nameof(destinationPort));
            if ((username == null) != (password == null)) throw new ArgumentException("Username and password must both be supplied or neither");
            if (username != null && username.Length > 255) throw new ArgumentOutOfRangeException(nameof(username));
            if (password != null && password.Length > 255) throw new ArgumentOutOfRangeException(nameof(password));

            cancellationToken = cancellationToken == default ? CancellationToken.None : cancellationToken;

            async Task<byte[]> ReadAsync(NetworkStream stream, int length)
            {
                var buffer = new byte[length];
                var bytesRead = await stream.ReadAsync(buffer, 0, length, cancellationToken).ConfigureAwait(false);
                if (bytesRead < length) throw new IOException("Incomplete read from SOCKS server");
                return buffer.AsSpan(0, bytesRead).ToArray();
            }
            await client.ConnectAsync(proxyAddress, proxyPort).ConfigureAwait(false);
            var stream = client.GetStream();
              
            // Auth method selection
            var authMethods = new List<byte> { SOCKS_5, 0x01, AUTH_ANONYMOUS };
            if (username != null) authMethods.Add(AUTH_USERNAME);

            await stream.WriteAsync(authMethods.ToArray(), cancellationToken).ConfigureAwait(false);

            var authResponse = await ReadAsync(stream, 2);
            if (authResponse[0] != SOCKS_5) throw new IOException($"Invalid SOCKS version: {authResponse[0]}");

            switch (authResponse[1])
            {
                case AUTH_ANONYMOUS:
                    break;
                case AUTH_USERNAME:
                    var creds = new List<byte> { AUTH_VERSION, (byte)username.Length };
                    creds.AddRange(Encoding.ASCII.GetBytes(username));
                    creds.Add((byte)password.Length);
                    creds.AddRange(Encoding.ASCII.GetBytes(password));

                    await stream.WriteAsync(creds.ToArray(), cancellationToken).ConfigureAwait(false);

                    var credsResponse = await ReadAsync(stream, 2);
                    if (credsResponse[0] != AUTH_VERSION || credsResponse[1] != EMPTY)
                        throw new IOException($"Auth failed: status {credsResponse[1]}");
                    break;
                default:
                    throw new IOException($"No acceptable auth method (got {authResponse[1]})");
            }

            // Request
            var request = new List<byte> { SOCKS_5, CONNECT, EMPTY, DOMAIN };
            request.Add((byte)destinationAddress.Length);
            request.AddRange(Encoding.ASCII.GetBytes(destinationAddress));
            var portBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)destinationPort));
            request.AddRange(portBytes);

            await stream.WriteAsync(request.ToArray(), cancellationToken).ConfigureAwait(false);

            // Response
            var response = await ReadAsync(stream, 4);
            if (response[0] != SOCKS_5 || response[1] != EMPTY)
            {
                string msg = response[1] switch
                {
                    0x01 => "General failure",
                    0x05 => "Connection refused",
                    // ... add others if needed
                    _ => $"Unknown SOCKS error {response[1]}"
                };
                throw new IOException($"SOCKS reply error: {msg}");
            }

            string boundAddr;
            switch (response[3])
            {
                case IPV4:
                    var ipBytes = await ReadAsync(stream, 4);
                    boundAddr = new IPAddress(ipBytes).ToString();
                    break;
                case DOMAIN:
                    var lenByte = await ReadAsync(stream, 1);
                    var domainBytes = await ReadAsync(stream, lenByte[0]);
                    boundAddr = Encoding.ASCII.GetString(domainBytes);
                    break;
                case IPV6:
                    var ipv6Bytes = await ReadAsync(stream, 16);
                    boundAddr = new IPAddress(ipv6Bytes).ToString();
                    break;
                default:
                    throw new IOException($"Unknown ATYP {response[3]}");
            }

            var portBuffer = await ReadAsync(stream, 2);
            var boundPort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(portBuffer, 0));

            return (boundAddr, boundPort);
        }
    }
}