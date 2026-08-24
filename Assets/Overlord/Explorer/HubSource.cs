using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xst.Rpc;

namespace Overlord.Explorer
{
    public interface IHubSource
    {
        string Name { get; }
        Task<string> AskAsync(string line);
    }

    public class LocalHubSource : IHubSource
    {
        private readonly HubDispatcher dispatcher;
        private readonly HubHandlers handlers;
        private readonly string name;

        public LocalHubSource(XstClient client, PeerSet peers, string name)
        {
            dispatcher = new HubDispatcher();
            handlers = new HubHandlers(client, peers);
            handlers.RegisterAll(dispatcher);
            this.name = string.IsNullOrEmpty(name) ? "local daemon" : name;
        }

        public string Name
        {
            get { return name; }
        }

        public Task<string> SelfCheckAsync()
        {
            return handlers.SelfCheckAsync();
        }

        public Task<string> AskAsync(string line)
        {
            return dispatcher.HandleLineAsync(line);
        }
    }

    public class HubAnswer
    {
        public string Source;
        public bool Ok;
        public JToken Result;
        public string Error;

        public static HubAnswer Parse(string source, string line)
        {
            var answer = new HubAnswer { Source = source };

            if (string.IsNullOrEmpty(line))
            {
                answer.Error = "no answer";
                return answer;
            }

            JObject root;
            try
            {
                root = JObject.Parse(line);
            }
            catch (Exception)
            {
                answer.Error = "unreadable answer";
                return answer;
            }

            answer.Ok = root.Value<bool>("ok");
            if (answer.Ok)
            {
                answer.Result = root["r"];
            }
            else
            {
                answer.Error = root.Value<string>("e");
                if (string.IsNullOrEmpty(answer.Error))
                {
                    answer.Error = "refused";
                }
            }

            return answer;
        }
    }
}
