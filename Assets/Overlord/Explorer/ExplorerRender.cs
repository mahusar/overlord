using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Overlord.Explorer
{
    public static class ExplorerRender
    {
        private static readonly float[] HistoryWidths = { 110f, 560f, 160f, 0f };
        private static readonly float[] RichWidths = { 60f, 380f, 180f, 0f };
        private static readonly float[] PeerWidths = { 480f, 80f, 120f, 0f };
        private static readonly float[] TxWidths = { 60f, 150f, 0f };

        public static void Show(ExplorerUI ui, string query, Verdict verdict)
        {
            ui.Clear();
            ui.SetBadge(verdict);

            if (verdict == null || !verdict.HasResult)
            {
                ui.AddHeading("nothing to show");
                ui.AddNote(verdict == null ? "no answer" : verdict.Error, ExplorerUI.Bad);
                ui.ScrollToTop();
                return;
            }

            if (!verdict.Unanimous)
            {
                ui.AddNote("The sources do not agree. Showing what " + verdict.Agreed +
                           " of " + verdict.Answered + " returned. Differing: " +
                           string.Join(", ", verdict.DissentingSources.ToArray()),
                           ExplorerUI.Warn);
                ui.AddSpace(6f);
            }

            JObject result = verdict.Result as JObject;

            switch (query)
            {
                case HubQueries.Ping:
                    ShowPing(ui, result);
                    break;
                case HubQueries.GetInfo:
                    ShowInfo(ui, result);
                    break;
                case HubQueries.GetBlock:
                    ShowBlock(ui, result);
                    break;
                case HubQueries.GetTransaction:
                    ShowTransaction(ui, result);
                    break;
                case HubQueries.GetAddress:
                    ShowAddress(ui, result);
                    break;
                case HubQueries.GetRichList:
                    ShowRichList(ui, result);
                    break;
                case HubQueries.Peers:
                    ShowPeers(ui, result);
                    break;
                default:
                    ShowRaw(ui, query, verdict.Result);
                    break;
            }

            ui.ScrollToTop();
        }

        private static void ShowPing(ExplorerUI ui, JObject result)
        {
            ui.AddHeading("hub");
            ui.AddRow("hub version", Text(result, "version"));
            ui.AddRow("height", Number(result, "blocks"));
        }

        private static void ShowInfo(ExplorerUI ui, JObject result)
        {
            ui.AddHeading("chain");
            ui.AddRow("height", Number(result, "blocks"));
            ui.AddRow("best block", Text(result, "blockhash"));
            ui.AddRow("money supply", Amount(result, "moneysupply"));
            ui.AddRow("connections", Number(result, "connections"));
            ui.AddRow("network", Flag(result, "testnet") ? "testnet" : "mainnet");
            ui.AddSpace(10f);
            ui.AddHeading("daemon");
            ui.AddRow("version", Text(result, "version"));
            ui.AddRow("build", Text(result, "buildversion"));
            ui.AddRow("protocol", Number(result, "protocolversion"));
            ui.AddRow("hub version", Text(result, "hub"));

            string errors = Text(result, "errors");
            if (!string.IsNullOrEmpty(errors) && errors != "-")
            {
                ui.AddSpace(8f);
                ui.AddNote("daemon reports: " + errors, ExplorerUI.Warn);
            }
        }

        private static void ShowBlock(ExplorerUI ui, JObject result)
        {
            ui.AddHeading("block " + Number(result, "height"));
            ui.AddRow("hash", Text(result, "hash"));
            ui.AddRow("time", When(result, "time"));
            ui.AddRow("confirmations", Number(result, "confirmations"));
            ui.AddRow("size", Number(result, "size") + " bytes");
            ui.AddRow("staker", Text(result, "staker_alias"));
            ui.AddRow("reward", Amount(result, "block_reward"));
            ui.AddRow("mint", Amount(result, "mint"));
            ui.AddRow("money supply", Amount(result, "moneysupply"));
            ui.AddRow("merkle root", Text(result, "merkleroot"));
            ui.AddRow("previous", Text(result, "previousblockhash"));
            ui.AddRow("next", Text(result, "nextblockhash"));

            JArray transactions = result == null ? null : result["tx"] as JArray;
            int count = transactions == null ? 0 : transactions.Count;

            ui.AddSpace(12f);
            ui.AddHeading("transactions (" + count.ToString(CultureInfo.InvariantCulture) + ")");

            if (count == 0)
            {
                ui.AddNote("None. A qPoS block carries no coinstake, so empty blocks are normal.",
                    ExplorerUI.Muted);
                return;
            }

            ui.AddSeparator();
            for (int i = 0; i < count; i++)
            {
                JToken entry = transactions[i];
                string txid = entry.Type == JTokenType.String
                    ? entry.Value<string>()
                    : entry.Value<string>("txid");
                ui.AddColumns(new[] { (i + 1).ToString(CultureInfo.InvariantCulture), txid ?? "-" },
                    TxWidths, ExplorerUI.Ink);
            }
        }

        private static void ShowTransaction(ExplorerUI ui, JObject result)
        {
            ui.AddHeading("transaction");
            ui.AddRow("txid", Text(result, "txid"));
            ui.AddRow("block", Text(result, "blockhash"));
            ui.AddRow("time", When(result, "blocktime"));
            ui.AddRow("confirmations", Number(result, "confirmations"));
            ui.AddRow("version", Number(result, "version"));
            ui.AddRow("locktime", Number(result, "locktime"));

            JArray inputs = result == null ? null : result["vin"] as JArray;
            JArray outputs = result == null ? null : result["vout"] as JArray;

            ui.AddSpace(12f);
            ui.AddHeading("inputs (" + Count(inputs) + ")");
            ui.AddSeparator();
            if (inputs != null)
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    JObject input = inputs[i] as JObject;
                    if (input == null)
                    {
                        continue;
                    }

                    string previous = input.Value<string>("txid");
                    string label = previous == null
                        ? (input["coinbase"] != null ? "coinbase" : "generated")
                        : Short(previous) + " : " + Number(input, "vout");

                    ui.AddColumns(new[] { (i + 1).ToString(CultureInfo.InvariantCulture), label, "" },
                        TxWidths, ExplorerUI.Ink);
                }
            }

            ui.AddSpace(12f);
            ui.AddHeading("outputs (" + Count(outputs) + ")");
            ui.AddSeparator();
            if (outputs != null)
            {
                for (int i = 0; i < outputs.Count; i++)
                {
                    JObject output = outputs[i] as JObject;
                    if (output == null)
                    {
                        continue;
                    }

                    string address = "-";
                    JObject script = output["scriptPubKey"] as JObject;
                    if (script != null)
                    {
                        JArray addresses = script["addresses"] as JArray;
                        if (addresses != null && addresses.Count > 0)
                        {
                            address = addresses[0].Value<string>();
                            if (addresses.Count > 1)
                            {
                                address += " (+" + (addresses.Count - 1) + ")";
                            }
                        }
                        else
                        {
                            address = script.Value<string>("type") ?? "-";
                        }
                    }

                    ui.AddColumns(new[]
                    {
                        (i + 1).ToString(CultureInfo.InvariantCulture),
                        Amount(output, "value"),
                        address
                    }, TxWidths, ExplorerUI.Ink);
                }
            }
        }

        private static void ShowAddress(ExplorerUI ui, JObject result)
        {
            ui.AddHeading("address");
            ui.AddRow("address", Text(result, "address"));
            ui.AddRow("balance", Amount(result, "balance"));
            ui.AddRow("rank", Number(result, "rank"));
            ui.AddRow("received", Amount(result, "received"));
            ui.AddRow("sent", Amount(result, "sent"));
            ui.AddRow("transactions", Number(result, "transactions"));
            ui.AddRow("unspent outputs", Number(result, "unspent"));
            ui.AddRow("inputs / outputs", Number(result, "inputs") + " / " + Number(result, "outputs"));

            JObject history = result == null ? null : result["history"] as JObject;
            JArray rows = history == null ? null : history["data"] as JArray;

            ui.AddSpace(12f);
            string total = history == null ? "0" : Number(history, "total");
            string page = history == null ? "1" : Number(history, "page");
            string last = history == null ? "1" : Number(history, "last_page");
            ui.AddHeading("history, page " + page + " of " + last + ", " + total + " total");

            if (rows == null || rows.Count == 0)
            {
                ui.AddNote("No transactions for this address.", ExplorerUI.Muted);
                return;
            }

            ui.AddColumns(new[] { "height", "txid", "balance after", "" }, HistoryWidths, ExplorerUI.Muted);
            ui.AddSeparator();

            for (int i = 0; i < rows.Count; i++)
            {
                JObject row = rows[i] as JObject;
                if (row == null)
                {
                    continue;
                }

                JObject info = row["txinfo"] as JObject;
                ui.AddColumns(new[]
                {
                    info == null ? "-" : Number(info, "height"),
                    row.Value<string>("txid") ?? "-",
                    Amount(row, "balance"),
                    info == null ? "" : When(info, "blocktime")
                }, HistoryWidths, ExplorerUI.Ink);
            }
        }

        private static void ShowRichList(ExplorerUI ui, JObject result)
        {
            JArray rows = result == null ? null : result["rows"] as JArray;

            ui.AddHeading("rich list, from rank " + Number(result, "start"));

            if (rows == null || rows.Count == 0)
            {
                ui.AddNote("The rich list came back empty. The explore API may be off.",
                    ExplorerUI.Warn);
                return;
            }

            ui.AddColumns(new[] { "rank", "address", "balance", "" }, RichWidths, ExplorerUI.Muted);
            ui.AddSeparator();

            int start = result["start"] == null ? 1 : result.Value<int>("start");
            for (int i = 0; i < rows.Count; i++)
            {
                JObject row = rows[i] as JObject;
                if (row == null)
                {
                    continue;
                }

                ui.AddColumns(new[]
                {
                    (start + i).ToString(CultureInfo.InvariantCulture),
                    row.Value<string>("address") ?? "-",
                    Amount(row, "balance"),
                    ""
                }, RichWidths, ExplorerUI.Ink);
            }
        }

        private static void ShowPeers(ExplorerUI ui, JObject result)
        {
            JArray rows = result == null ? null : result["peers"] as JArray;

            ui.AddHeading("peers (" + Count(rows) + ")");

            if (rows == null || rows.Count == 0)
            {
                ui.AddNote("No peers known yet. Bootstrap comes from the on-chain registry, " +
                           "which is not wired up yet.", ExplorerUI.Muted);
                return;
            }

            ui.AddColumns(new[] { "onion", "port", "height", "seen" }, PeerWidths, ExplorerUI.Muted);
            ui.AddSeparator();

            for (int i = 0; i < rows.Count; i++)
            {
                JObject row = rows[i] as JObject;
                if (row == null)
                {
                    continue;
                }

                ui.AddColumns(new[]
                {
                    row.Value<string>("onion") ?? "-",
                    Number(row, "port"),
                    Number(row, "height"),
                    When(row, "seen")
                }, PeerWidths, ExplorerUI.Ink);
            }
        }

        private static void ShowRaw(ExplorerUI ui, string query, JToken result)
        {
            ui.AddHeading(query);
            JObject asObject = result as JObject;
            if (asObject == null)
            {
                ui.AddNote(result == null ? "-" : result.ToString(), ExplorerUI.Ink);
                return;
            }

            foreach (var property in asObject)
            {
                JToken value = property.Value;
                string shown = value == null || value.Type == JTokenType.Null
                    ? "-"
                    : (value.Type == JTokenType.Object || value.Type == JTokenType.Array
                        ? value.Type.ToString().ToLowerInvariant()
                        : value.ToString());
                ui.AddRow(property.Key, shown);
            }
        }

        private static string Count(JArray array)
        {
            return (array == null ? 0 : array.Count).ToString(CultureInfo.InvariantCulture);
        }

        private static string Short(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 20)
            {
                return value ?? "-";
            }
            return value.Substring(0, 10) + "..." + value.Substring(value.Length - 6);
        }

        private static string Text(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return "-";
            }

            string value = source[field].Type == JTokenType.String
                ? source.Value<string>(field)
                : source[field].ToString();

            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        private static bool Flag(JObject source, string field)
        {
            return source != null && source[field] != null &&
                   source[field].Type == JTokenType.Boolean && source.Value<bool>(field);
        }

        private static string Number(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return "-";
            }

            JToken token = source[field];
            if (token.Type == JTokenType.Integer)
            {
                return token.Value<long>().ToString("N0", CultureInfo.InvariantCulture);
            }

            if (token.Type == JTokenType.Float)
            {
                return token.Value<decimal>().ToString("0.########", CultureInfo.InvariantCulture);
            }

            return token.ToString();
        }

        private static string Amount(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type == JTokenType.Null)
            {
                return "-";
            }

            decimal value;
            try
            {
                value = source[field].Value<decimal>();
            }
            catch (Exception)
            {
                return Text(source, field);
            }

            return value.ToString("N6", CultureInfo.InvariantCulture) + " XST";
        }

        private static string When(JObject source, string field)
        {
            if (source == null || source[field] == null || source[field].Type != JTokenType.Integer)
            {
                return "-";
            }

            long seconds = source.Value<long>(field);
            if (seconds <= 0)
            {
                return "-";
            }

            DateTime moment = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
            return moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
        }
    }
}
