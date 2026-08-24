using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Overlord.Tor
{
    public static class TorLauncher
    {
        public enum State
        {
            Idle,
            Starting,
            Ready,
            Failed
        }

        private const int FirstPort = 9250;
        private const int LastPort = 9299;
        private const string Folder = "tor";

        private static readonly object gate = new object();

        private static Process process;
        private static State state = State.Idle;
        private static int percent;
        private static string message = "";
        private static bool usingExisting;
        private static int socksPort;
        private static int hiddenPort;
        private static string hiddenDir;
        private static string onion;

        public static State Status
        {
            get { lock (gate) return state; }
        }

        public static int Percent
        {
            get { lock (gate) return percent; }
        }

        public static string Message
        {
            get { lock (gate) return message; }
        }

        public static bool UsingExisting
        {
            get { lock (gate) return usingExisting; }
        }

        public static bool Ready
        {
            get { return Status == State.Ready; }
        }

        public static int HiddenServicePort
        {
            get { lock (gate) return hiddenPort; }
        }

        public static string Onion
        {
            get
            {
                lock (gate)
                {
                    if (!string.IsNullOrEmpty(onion))
                    {
                        return onion;
                    }

                    if (string.IsNullOrEmpty(hiddenDir))
                    {
                        return null;
                    }

                    string file = Path.Combine(hiddenDir, "hostname");
                    try
                    {
                        if (!File.Exists(file))
                        {
                            return null;
                        }

                        string read = File.ReadAllText(file).Trim();
                        if (read.Length > 0)
                        {
                            onion = read;
                        }
                    }
                    catch (Exception)
                    {
                    }

                    return onion;
                }
            }
        }

        public static void PublishHiddenService(int localPort)
        {
            lock (gate)
            {
                if (hiddenPort == localPort && state == State.Ready && !usingExisting)
                {
                    return;
                }
            }

            Stop();

            lock (gate)
            {
                hiddenPort = localPort;
                onion = null;
            }

            Ensure();
        }

        public static string Describe()
        {
            lock (gate)
            {
                switch (state)
                {
                    case State.Idle: return "Tor has not been started yet.";
                    case State.Starting: return percent > 0
                        ? "Starting Tor... " + percent + "%"
                        : "Starting Tor...";
                    case State.Ready: return usingExisting
                        ? "Using the Tor already running on this machine."
                        : "Tor is ready.";
                    default: return string.IsNullOrEmpty(message) ? "Tor could not start." : message;
                }
            }
        }

        public static void Ensure()
        {
            lock (gate)
            {
                if (state == State.Starting || state == State.Ready) return;
                state = State.Starting;
                percent = 0;
                message = "";
            }

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Settle(State.Ready, "A headless hub uses the system Tor.", true, TorConfig.DefaultSocksPort);
                return;
            }

            bool wantsHiddenService;
            lock (gate)
            {
                wantsHiddenService = hiddenPort > 0;
            }

            if (!wantsHiddenService)
            {
                if (Bound(TorConfig.DefaultSocksPort))
                {
                    Debug.Log("[Tor] found an existing Tor on " + TorConfig.DefaultSocksPort + ", using it.");
                    Settle(State.Ready, "", true, TorConfig.DefaultSocksPort);
                    return;
                }

                int adopted = Adoptable();
                if (adopted > 0)
                {
                    Debug.Log("[Tor] adopting the Tor already listening on " + adopted + ".");
                    Settle(State.Ready, "", true, adopted);
                    return;
                }
            }

            string binary = BundledBinary();
            if (string.IsNullOrEmpty(binary))
            {
                Settle(State.Failed,
                    "Tor is not running and no bundled copy was found. Start Tor yourself, then press CONNECT again.",
                    false, 0);
                return;
            }

            try
            {
                Launch(binary);
            }
            catch (Exception e)
            {
                Settle(State.Failed, "Tor could not start (" + e.GetType().Name + "). Start Tor yourself and try again.",
                    false, 0);
            }
        }

        public static void Stop()
        {
            Process running;

            lock (gate)
            {
                running = process;
                process = null;
                if (state != State.Failed) state = State.Idle;
                percent = 0;
            }

            if (running == null) return;

            try
            {
                if (!running.HasExited)
                {
                    running.Kill();
                    running.WaitForExit(3000);
                }
            }
            catch (Exception)
            {
            }

            try
            {
                running.Dispose();
            }
            catch (Exception)
            {
            }

            Debug.Log("[Tor] stopped the bundled Tor.");
        }

        private static void Launch(string binary)
        {
            int port = FreePort();
            if (port == 0)
            {
                Settle(State.Failed, "No free port for Tor between " + FirstPort + " and " + LastPort + ".", false, 0);
                return;
            }

            string root = Path.Combine(Application.persistentDataPath, Folder);
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);

            string service = null;
            lock (gate)
            {
                if (hiddenPort > 0)
                {
                    service = Path.Combine(root, "hs");
                    hiddenDir = service;
                }
            }

            if (service != null)
            {
                Directory.CreateDirectory(service);
            }

            string torrc = Path.Combine(root, "torrc");
            File.WriteAllText(torrc, Torrc(port, data, service), new UTF8Encoding(false));

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = "-f \"" + torrc + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(binary)
            };

            Process started = new Process { StartInfo = info, EnableRaisingEvents = true };

            started.OutputDataReceived += (sender, args) => ReadLine(args.Data, port);
            started.ErrorDataReceived += (sender, args) => ReadLine(args.Data, port);
            started.Exited += (sender, args) => Exited();

            started.Start();
            started.BeginOutputReadLine();
            started.BeginErrorReadLine();

            lock (gate) process = started;

            Debug.Log("[Tor] started the bundled Tor on SOCKS port " + port + ".");
        }

        private static string Torrc(int port, string data, string service)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("SocksPort ").Append(port).Append('\n');
            sb.Append("DataDirectory \"").Append(TorrcPath(data)).Append("\"\n");
            sb.Append("ClientOnly 1\n");
            sb.Append("Log notice stdout\n");
            sb.Append("AvoidDiskWrites 1\n");

            if (!string.IsNullOrEmpty(service))
            {
                int forward;
                lock (gate)
                {
                    forward = hiddenPort;
                }

                sb.Append("HiddenServiceDir \"").Append(TorrcPath(service)).Append("\"\n");
                sb.Append("HiddenServiceVersion 3\n");
                sb.Append("HiddenServicePort ").Append(forward)
                  .Append(" 127.0.0.1:").Append(forward).Append('\n');
            }

            return sb.ToString();
        }

        private static string TorrcPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            string native = Path.DirectorySeparatorChar == '\\'
                ? path.Replace('/', '\\')
                : path.Replace('\\', '/');

            return native.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void ReadLine(string line, int port)
        {
            if (string.IsNullOrEmpty(line)) return;

            int at = line.IndexOf("Bootstrapped ", StringComparison.Ordinal);
            if (at < 0) return;

            int start = at + "Bootstrapped ".Length;
            int end = start;
            while (end < line.Length && char.IsDigit(line[end])) end++;

            int found;
            if (end == start || !int.TryParse(line.Substring(start, end - start), out found)) return;

            lock (gate)
            {
                if (found > percent) percent = found;
            }

            if (found >= 100) Settle(State.Ready, "", false, port);
        }

        private static void Exited()
        {
            lock (gate)
            {
                if (state == State.Ready) return;
                state = State.Failed;
                if (string.IsNullOrEmpty(message))
                    message = "Tor stopped before it finished starting.";
            }
        }

        private static void Settle(State next, string text, bool existing, int port)
        {
            lock (gate)
            {
                state = next;
                message = text;
                usingExisting = existing;
                socksPort = port;
                if (next == State.Ready) percent = 100;
            }

            if (next == State.Ready && port > 0)
                TorConfig.SetSocksProxy(TorConfig.DefaultSocksHost, port);
        }

        private static string BundledBinary()
        {
            string name = Application.platform == RuntimePlatform.WindowsPlayer ||
                          Application.platform == RuntimePlatform.WindowsEditor
                ? "tor.exe"
                : "tor";

            try
            {
                string direct = Path.Combine(Application.streamingAssetsPath, Path.Combine("Tor", name));
                if (File.Exists(direct)) return direct;

                string root = Path.Combine(Application.streamingAssetsPath, "Tor");
                if (!Directory.Exists(root)) return null;

                string[] found = Directory.GetFiles(root, name, SearchOption.AllDirectories);
                return found.Length > 0 ? found[0] : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int Adoptable()
        {
            HashSet<int> busy = BoundPorts();

            for (int port = FirstPort; port <= LastPort; port++)
            {
                if (busy.Contains(port))
                {
                    return port;
                }
            }

            return 0;
        }

        private static int FreePort()
        {
            HashSet<int> busy = BoundPorts();

            for (int port = FirstPort; port <= LastPort; port++)
                if (!busy.Contains(port)) return port;

            return 0;
        }

        private static bool Bound(int port)
        {
            return BoundPorts().Contains(port);
        }

        private static HashSet<int> BoundPorts()
        {
            var ports = new HashSet<int>();

            try
            {
                IPEndPoint[] active = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                foreach (IPEndPoint endpoint in active)
                {
                    ports.Add(endpoint.Port);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Tor] could not read the listening ports (" + e.GetType().Name +
                    "), assuming none are in use.");
            }

            return ports;
        }
    }
}
