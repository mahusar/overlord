using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xst.Rpc;

namespace Overlord
{
    public delegate Task<object> HubQueryHandler(HubRequest request);

    public class HubDispatcher
    {
        public static Action<string, Exception> OnInternalError;

        private readonly Dictionary<string, HubQueryHandler> handlers =
            new Dictionary<string, HubQueryHandler>(StringComparer.Ordinal);

        public void Register(string query, HubQueryHandler handler)
        {
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentNullException("query");
            }

            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            if (!HubQueries.IsAllowed(query))
            {
                throw new InvalidOperationException(
                    "refusing to register a handler for a query that is not on the allowlist: " + query);
            }

            handlers[query] = handler;
        }

        public IEnumerable<string> Registered
        {
            get { return handlers.Keys; }
        }

        public string Unregistered()
        {
            var missing = new List<string>();
            foreach (string q in HubQueries.All)
            {
                if (!handlers.ContainsKey(q))
                {
                    missing.Add(q);
                }
            }
            return missing.Count == 0 ? null : string.Join(", ", missing.ToArray());
        }

        public async Task<string> HandleLineAsync(string line)
        {
            HubRequest request;
            string parseError;
            string callerId;
            if (!HubProtocol.TryParseRequest(line, out request, out parseError, out callerId))
            {
                return HubProtocol.Fail(callerId, parseError);
            }

            HubQueryHandler handler;
            if (!handlers.TryGetValue(request.Query, out handler))
            {
                return HubProtocol.Fail(request.Id, "not available");
            }

            try
            {
                object result = await handler(request);
                return HubProtocol.Ok(request.Id, result);
            }
            catch (XstRpcException ex)
            {
                return HubProtocol.Fail(request.Id,
                    ex.Code.HasValue ? ex.Code.Value + " " + ex.Message : ex.Message);
            }
            catch (ArgumentException ex)
            {
                return HubProtocol.Fail(request.Id, "bad parameters: " + ex.Message);
            }
            catch (Exception ex)
            {
                Action<string, Exception> report = OnInternalError;
                if (report != null)
                {
                    try
                    {
                        report(request.Query, ex);
                    }
                    catch (Exception)
                    {
                    }
                }

                return HubProtocol.Fail(request.Id, "internal error");
            }
        }
    }
}
