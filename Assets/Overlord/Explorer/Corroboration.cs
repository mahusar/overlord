using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Overlord.Explorer
{
    public class Verdict
    {
        public JToken Result;
        public int Asked;
        public int Answered;
        public int Agreed;
        public string Error;
        public List<string> AgreeingSources = new List<string>();
        public List<string> DissentingSources = new List<string>();

        public bool HasResult
        {
            get { return Result != null; }
        }

        public bool Unanimous
        {
            get { return Answered > 0 && Agreed == Answered; }
        }

        public string Badge
        {
            get
            {
                if (!HasResult)
                {
                    return "no source answered";
                }

                if (Answered == 1)
                {
                    return "1 source";
                }

                return Unanimous
                    ? Agreed + " of " + Answered + " sources agree"
                    : Agreed + " of " + Answered + " agree, " + DissentingSources.Count + " differ";
            }
        }
    }

    public static class Corroboration
    {
        private static readonly HashSet<string> volatileFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "confirmations",
            "depth",
            "nextblockhash",
            "hub",
            "connections",
            "errors",
            "blocks",
            "difficulty",
            "moneysupply",
            "seen"
        };

        public static Verdict Judge(List<HubAnswer> answers)
        {
            var verdict = new Verdict();
            if (answers == null)
            {
                verdict.Error = "no source answered";
                return verdict;
            }

            verdict.Asked = answers.Count;

            var groups = new Dictionary<string, List<HubAnswer>>(StringComparer.Ordinal);
            var order = new List<string>();
            string firstError = null;

            foreach (HubAnswer answer in answers)
            {
                if (answer == null)
                {
                    continue;
                }

                if (!answer.Ok || answer.Result == null)
                {
                    if (firstError == null && !string.IsNullOrEmpty(answer.Error))
                    {
                        firstError = answer.Source + ": " + answer.Error;
                    }
                    continue;
                }

                verdict.Answered++;
                string key = Canonical(answer.Result);

                List<HubAnswer> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<HubAnswer>();
                    groups[key] = group;
                    order.Add(key);
                }
                group.Add(answer);
            }

            if (verdict.Answered == 0)
            {
                verdict.Error = firstError ?? "no source answered";
                return verdict;
            }

            string winner = null;
            int best = 0;
            foreach (string key in order)
            {
                if (groups[key].Count > best)
                {
                    best = groups[key].Count;
                    winner = key;
                }
            }

            verdict.Result = groups[winner][0].Result;
            verdict.Agreed = best;

            foreach (string key in order)
            {
                foreach (HubAnswer answer in groups[key])
                {
                    if (key == winner)
                    {
                        verdict.AgreeingSources.Add(answer.Source);
                    }
                    else
                    {
                        verdict.DissentingSources.Add(answer.Source);
                    }
                }
            }

            return verdict;
        }

        public static string Canonical(JToken token)
        {
            JToken stripped = Strip(token);
            return stripped == null ? string.Empty : stripped.ToString(Formatting.None);
        }

        private static JToken Strip(JToken token)
        {
            JObject asObject = token as JObject;
            if (asObject != null)
            {
                var keys = new List<string>();
                foreach (KeyValuePair<string, JToken> property in asObject)
                {
                    if (!volatileFields.Contains(property.Key))
                    {
                        keys.Add(property.Key);
                    }
                }
                keys.Sort(StringComparer.Ordinal);

                var clean = new JObject();
                foreach (string key in keys)
                {
                    clean[key] = Strip(asObject[key]);
                }
                return clean;
            }

            JArray asArray = token as JArray;
            if (asArray != null)
            {
                var clean = new JArray();
                foreach (JToken item in asArray)
                {
                    clean.Add(Strip(item));
                }
                return clean;
            }

            return token == null ? JValue.CreateNull() : token.DeepClone();
        }
    }
}
