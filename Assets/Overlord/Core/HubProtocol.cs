using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Overlord
{
    public class HubRequest
    {
        public string Id;
        public string Query;
        public JObject Parameters;
    }

    public static class HubProtocol
    {
        public const int MaxRequestBytes = 4096;
        public const int MaxResponseBytes = 1048576;
        public const int MaxIdLength = 64;

        public static bool TryParseRequest(string line, out HubRequest request, out string error)
        {
            string id;
            return TryParseRequest(line, out request, out error, out id);
        }

        public static bool TryParseRequest(string line, out HubRequest request, out string error,
            out string id)
        {
            request = null;
            error = null;
            id = null;

            if (string.IsNullOrEmpty(line))
            {
                error = "empty request";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(line) > MaxRequestBytes)
            {
                error = "request too large";
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(line);
            }
            catch (JsonException)
            {
                error = "malformed json";
                return false;
            }

            string caller = root.Value<string>("id");
            if (string.IsNullOrEmpty(caller) || caller.Length > MaxIdLength)
            {
                error = "bad id";
                return false;
            }
            id = caller;

            string query = root.Value<string>("q");
            if (string.IsNullOrEmpty(query))
            {
                error = "missing q";
                return false;
            }

            if (!HubQueries.IsAllowed(query))
            {
                error = "not allowed";
                return false;
            }

            request = new HubRequest
            {
                Id = caller,
                Query = query,
                Parameters = root["p"] as JObject
            };
            return true;
        }

        public static string Ok(string id, object result)
        {
            var response = new JObject
            {
                ["id"] = id ?? string.Empty,
                ["ok"] = true,
                ["r"] = result == null ? new JObject() : JToken.FromObject(result)
            };
            return Render(response, id);
        }

        public static string Fail(string id, string error)
        {
            var response = new JObject
            {
                ["id"] = id ?? string.Empty,
                ["ok"] = false,
                ["e"] = Sanitize(error)
            };
            return Render(response, id);
        }

        private static string Render(JObject response, string id)
        {
            string line = response.ToString(Formatting.None);

            if (Encoding.UTF8.GetByteCount(line) > MaxResponseBytes)
            {
                var trimmed = new JObject
                {
                    ["id"] = id ?? string.Empty,
                    ["ok"] = false,
                    ["e"] = "response too large"
                };
                return trimmed.ToString(Formatting.None);
            }

            return line;
        }

        private static string Sanitize(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return "error";
            }

            string flat = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (flat.Length > 200)
            {
                flat = flat.Substring(0, 200);
            }
            return flat.Length == 0 ? "error" : flat;
        }
    }
}
