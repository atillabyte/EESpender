using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using PlayerIOClient;
using Newtonsoft.Json;
using Rabbit;
using static EESpender2.Helpers;

namespace EESpender2
{
    class Program
    {
        static List<UserIntance> UserInstances = new List<UserIntance>();
        static void Main(string[] args)
        {
            EasyTimer.SetTimeout(new Action(() => {
                foreach (var instance in UserInstances) {
                    instance.LogOutput();
                }

                LogLineBreak();
                Log(Severity.Error, "Application took too long and was terminated.", true);
            }), 1000 * 30);

            ServicePointManager.ServerCertificateValidationCallback += (o, certificate, chain, errors) => true;
            Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;

            if (File.Exists("accounts.dat")) {
                if (args.Length > 0)
                    Log(Severity.Warning, "You must only either provide accounts as arguments or within 'accounts.dat', defaulting to 'accounts.dat'.");

                args = File.ReadAllText(@"accounts.dat").Split(' ');
            }

            if (args.IsNullOrEmpty())
                Log(Severity.Error, "No accounts specified.", true);

            if (args.Length % 2 == 1)
                Log(Severity.Error, "Missing a required account argument.", true);

            for (int i = 0; i < args.Length; i += 2)
                UserInstances.Add(new UserIntance(args[i], args[i + 1]));

            while (UserInstances.All(x => !x.Completed))
                Thread.Sleep(100);

            foreach (var instance in UserInstances)
                instance.LogOutput();
            LogLineBreak();

            Environment.Exit(0);
            Thread.Sleep(Timeout.Infinite);
        }
    }

    class UserIntance
    {
        public Client Client { get; set; }
        public Connection Lobby { get; set; }
        public List<string> RequiredMessageTypes = new List<string>() { "getMySimplePlayerObject", "getLobbyProperties", "getShop" };
        public List<Message> ReceivedMessages = new List<Message>();
        public List<string> Output = new List<string>();

        public bool Completed = false;
        public string Username = "Unspecified";

        public UserIntance(string email, string auth)
        {
            try {
                this.Client = new RabbitAuth().LogOn("everybody-edits-su9rn58o40itdbnw69plyw", email, auth);
            }
            catch (Exception ex) {
                Log(Severity.Error, $"Authentication failed. ({ex.Message})");
                Completed = true;

                return;
            }

            this.Lobby = Client.Multiplayer.CreateJoinRoom(Client.ConnectUserId, $"Lobby{Client.BigDB.Load("config", "config")["version"]}", true, null, null);
            this.Lobby.OnMessage += (s, e) => ReceivedMessages.Add(e);

            Helpers.Log(Severity.Info, "EESpender Started.");
            Thread.Sleep(1000);

            this.Start();
        }

        public void Start()
        {
            foreach (var type in RequiredMessageTypes) {
                this.Lobby.Send(Message.Create(type));
                Thread.Sleep(100);
            }
            
            EasyTimer.SetTimeout(new Action(() => {
                while (!RequiredMessageTypes.All(y => ReceivedMessages.Select(x => x.Type).Contains(y))) {
                    Thread.Sleep(200);
                }

                this.Username = (string)ReceivedMessages.FirstOrDefault(x => x.Type == "getMySimplePlayerObject")[0];

                var shop = new Shop(ReceivedMessages.First(x => x.Type == "getShop"));
                var priority = shop.ShopItems.Where(x => x.OwnedAmount == 0 && x.Price > 0).OrderByDescending(x => x.Price - x.EnergySpent)
                               .OrderByDescending(x => x.IsNew).OrderByDescending(x => x.Name.Contains("world")).Reverse().ToList();

                if (priority.Count() == 0)
                    priority = shop.ShopItems.OrderByDescending(x => x.IsNew).OrderByDescending(x => x.Name.StartsWith("world")).Where(x => x.Price - x.EnergySpent > 0).ToList();

                Output.Add("Username: " +  this.Username);
                Output.Add("Priority Items: " + string.Join(", ", priority.Take(5).Select(x => x.Name)));
                Output.Add("Current Energy: " + $"{shop.CurrentEnergy}/{shop.MaximumEnergy}");

                foreach (var message in ReceivedMessages.Where(x => x.Type == "getLobbyProperties")) {
                    var FirstDailyLogin = (bool)message[0];
                    var LoginStreak = (int)message[1];

                    if (FirstDailyLogin && LoginStreak >= 0)
                        for (uint i = 2; i < message.Count; i += 2)
                            Output.Add($"Login Streak (#{LoginStreak}). (reward: {message[i + 1]} {message[i]})");
                }

                if (shop.CurrentEnergy >= priority[0].EnergyPerClick) {
                    Output.Add(string.Format("Spending {0} energy on {1}",
                                        priority[0].EnergyPerClick * (Math.Floor((double)shop.CurrentEnergy / priority[0].EnergyPerClick)),
                                        priority[0].SafeName));

                    Lobby.Send("useAllEnergy", priority[0].Name);
                }
                Completed = true;
            }), 5000);
        }

        public void LogOutput()
        {
            if (!this.Output.ToArray().IsNullOrEmpty()) {
                LogLineBreak();
                foreach (var message in this.Output)
                    Log(Severity.Info, message);
            }
        }
    }

    class Shop
    {
        public int MaximumEnergy { get; set; }
        public int CurrentEnergy { get; set; }

        public List<ShopItem> ShopItems { get; set; } = new List<ShopItem>();

        public Shop(Message e)
        {
            var client = new System.Net.WebClient() { Proxy = null };
            dynamic format = null;

            try {
                format = JsonConvert.DeserializeObject(client.DownloadString("https://raw.githubusercontent.com/atillabyte/EESpender/master/Assets/shop.json"));

                if (!File.Exists("shop.json"))
                    File.WriteAllText("shop.json", format.ToString());
                else {
                    dynamic temp = JsonConvert.DeserializeObject(File.ReadAllText("shop.json"));

                    if (format.version > temp.version) {
                        Log(Severity.Info, $"Updated Shop Format. (v.{format.version})");
                        File.WriteAllText("shop.json", format.ToString());
                    }
                }
            }
            catch (Exception ex) {
                Log(Severity.Warning, $"Unable to update Shop Format. (ex: {ex.Message})");
            }

            if (!File.Exists("shop.json")) {
                Log(Severity.Error, "Shop Format not found.");
                return;
            }

            CurrentEnergy = e[Convert.ToUInt32(format.energy)];
            MaximumEnergy = e[Convert.ToUInt32(format.maximum)];

            for (uint i = (uint)format.index.Value; i < e.Count; i += (uint)format.iterator.Value) {
                ShopItems.Add(new ShopItem() {
                    Name = e[(uint)i + Convert.ToUInt32(format.properties.id.Value)],
                    Price = e[(uint)i + Convert.ToUInt32(format.properties.price.Value)],
                    EnergyPerClick = e[(uint)i + Convert.ToUInt32(format.properties.increment.Value)],
                    EnergySpent = e[(uint)i + Convert.ToUInt32(format.properties.spent.Value)],
                    PriceGems = e[(uint)i + Convert.ToUInt32(format.properties.gems.Value)],
                    OwnedAmount = e[(uint)i + Convert.ToUInt32(format.properties.owned.Value)],
                    SafeName = e[(uint)i + Convert.ToUInt32(format.properties.text.Value)],
                    IsNew = e[(uint)i + Convert.ToUInt32(format.properties.isnew.Value)]
                });
            }
        }

        public class ShopItem
        {
            public string Name { get; set; }
            public int Price { get; set; }
            public int EnergyPerClick { get; set; }
            public int OwnedAmount { get; set; }
            public int PriceGems { get; set; }
            public int EnergySpent { get; set; }
            public string SafeName { get; set; }
            public bool IsNew { get; set; }
        }
    }

    static class Helpers
    {
        public enum Severity { Info = 0, Warning = 1, Error = 2 }
        public static void Log(Severity severity, string message, bool terminate = false)
        {
            var output = string.Format("[{0}] [{1:G}] {2}", severity.ToString().ToUpper(), DateTime.Now, message);

            Directory.CreateDirectory("logs");
            File.AppendAllText("logs" + Path.AltDirectorySeparatorChar + $"eespender_{ DateTime.Now.ToString("MM_dd_yy") }.txt", output + "\n");

            Console.WriteLine(output);

            if (terminate)
                Environment.Exit(-1);
        }

        public static void LogLineBreak() => Log(Severity.Info, "--------------------------------------------------------");
        public static bool IsNullOrEmpty<T>(this T[] array) => array == null || array.Length == 0;
    }

    static class EasyTimer
    {
        public static IDisposable SetInterval(Action method, int delayInMilliseconds)
        {
            System.Timers.Timer timer = new System.Timers.Timer(delayInMilliseconds);
            timer.Elapsed += (source, e) => method();

            timer.Enabled = true;
            timer.Start();

            return timer as IDisposable;
        }

        public static IDisposable SetTimeout(Action method, int delayInMilliseconds)
        {
            System.Timers.Timer timer = new System.Timers.Timer(delayInMilliseconds);
            timer.Elapsed += (source, e) => method();

            timer.AutoReset = false;
            timer.Enabled = true;
            timer.Start();

            return timer as IDisposable;
        }
    }
}