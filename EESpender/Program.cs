using PlayerIOClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using System.Threading;
using Newtonsoft.Json;
using Rabbit;
using Rabbit.EE;

namespace EESpender2
{
    class Program
    {
        static Client Client { get; set; }
        static Connection Lobby { get; set; }

        static bool Ready = false;
        static bool Connected => Ready && Lobby.Connected && Lobby != null && Messages.Any(x => x.Type == "connectioncomplete");
        enum Status { Incomplete = 0, Complete = 1 };
        static System.Timers.Timer Timer;
        static List<Message> Messages { get; set; } = new List<Message>();
        static List<Function> Series = new List<Function>();
        class Function
        {
            public string Tag { get; set; }
            public Status Status { get; set; } = Status.Incomplete;
            public Func<Status> Func { get; set; }
        }
        class Profile
        {
            public string UsernameOrEmail { get; set; }
            public string PasswordOrAuth { get; set; }

            public int LoginStreak { get; set; }
            public bool FirstDailyLogin { get; set; }
            public Shop CurrentShop { get; set; }
        }

        static void Main(string[] args)
        {
            var required = new List<string>() { "getMySimplePlayerObject", "getLobbyProperties", "getShop" };
            foreach (var message in required)
                Series.Add(new Function()
                {
                    Tag = message,
                    Func = new Func<Status>(() =>
                    {
                        while (!Messages.Any(x => x.Type == message))
                        {
                            if (!Connected)
                                return Status.Incomplete;

                            Lobby.Send(message);
                            Thread.Sleep(1000);
                        }

                        return Status.Complete;
                    })
                });

            Client = new RabbitAuth().LogOn("everybody-edits-su9rn58o40itdbnw69plyw", args[0], args[1]);
            Lobby = Client.Multiplayer.CreateJoinRoom(Client.ConnectUserId, $"Lobby{Client.BigDB.Load("config", "config")["version"]}", true, null, null);
            Log(Severity.Info, "Started.");

            Lobby.OnMessage += (s, message) =>
            {
                Messages.Add(message);

                if (message.Type == "connectioncomplete")
                {
                    Ready = true;

                    Timer = new System.Timers.Timer(1500) { AutoReset = true, Enabled = false };
                    Timer.Elapsed += (s2, e) =>
                    {
                        if (Ready && Connected)
                        {
                            var functions = Series.Where(x => x.Status == Status.Incomplete);

                            if (functions.Count() > 0)
                            {
                                foreach (var function in functions)
                                {
                                    if (!Connected)
                                        break;

                                    var status = function.Func.Invoke();
                                    if (status == Status.Incomplete)
                                        Log(Severity.Warning, $"Function has terminated unsuccessfully. ({function.Tag})");

                                    function.Status = status;
                                    Thread.Sleep(1000);
                                }
                            }
                            else
                            {
                                Timer.Stop();

                                var shop = new Shop(Messages.First(x => x.Type == "getShop"));

                                var priority = shop.ShopItems.Where(x => x.OwnedAmount == 0).Where(x => x.Price > 0)
                                    .OrderByDescending(x => x.Price).OrderByDescending(x => x.EnergySpent).OrderByDescending(x => x.IsNew).OrderByDescending(x => x.Price).Reverse().ToList();

                                if (priority.Count() == 0)
                                    priority = new List<Shop.ShopItem>() { shop.ShopItems.OrderByDescending(x => x.IsNew).First(x => x.OwnedAmount > 0 && x.Price > 0) };

                                Log(Severity.Info, "Username: " + Messages.FirstOrDefault(x => x.Type == "getMySimplePlayerObject")[0]);
                                Log(Severity.Info, "Priority Items: " + string.Join(", ", priority.Take(5).Select(x => x.Name)));
                                Log(Severity.Info, "Current Energy: " + shop.CurrentEnergy);
                                Log(Severity.Info, "Maximum Energy: " + shop.MaximumEnergy);

                                if (shop.CurrentEnergy >= priority[0].EnergyPerClick)
                                {
                                    Log(Severity.Info, string.Format("Spending {0} energy on {1}",
                                                        priority[0].EnergyPerClick * (Math.Floor((double)shop.CurrentEnergy / priority[0].EnergyPerClick)),
                                                        priority[0].SafeName));

                                    Lobby.Send("useAllEnergy", priority[0].Name);
                                }
                                else
                                {
                                    Log(Severity.Info, "Nothing to spend energy on.");
                                    Environment.Exit(0);
                                }
                            }
                        }
                        else {
                            Reconnect();
                        }
                    };
                    Timer.Start();
                }
                if (message.Type == "useEnergy" || message.Type == "useAllEnergy")
                {
                    Lobby.Send("getShop");
                }
                if (message.Type == "getLobbyProperties")
                {
                    var FirstDailyLogin = (bool)message[0];
                    var LoginStreak = (int)message[1];

                    if (FirstDailyLogin && LoginStreak >= 0)
                        for (uint i = 2; i < message.Count; i += 2)
                            Log(Severity.Info, $"Login Streak. (reward: {message[i + 1]} {message[i]})");
                }
            };

            Lobby.OnDisconnect += (s, message) =>
            {
                Log(Severity.Error, "Disconnected. " + message);
            };

            Console.ReadLine();
        }

        private static void Reconnect()
        {
            Main(new string[] { });
        }

        public enum Severity { Info = 0, Warning = 1, Error = 2 }
        public static void Log(Severity severity, string message)
        {
            var output = string.Format("[{0}] [{1:G}] {2}", severity.ToString().ToUpper(), DateTime.Now, message);

            Directory.CreateDirectory("logs");
            using (StreamWriter w = File.AppendText("logs" + Path.AltDirectorySeparatorChar + $"eespender_{ DateTime.Now.ToShortDateString() }.txt"))
            {
                w.WriteLine(output + "\n");
                w.Flush();
                w.Close();
            }

            Console.WriteLine(output);
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
            dynamic format;

            try
            {
                format = JsonConvert.DeserializeObject(client.DownloadString("https://raw.githubusercontent.com/atillabyte/EESpender/master/Assets/shop.json"));

                if (!File.Exists("shop.json"))
                    File.WriteAllText("shop.json", format.ToString());
                else
                {
                    dynamic temp = JsonConvert.DeserializeObject(File.ReadAllText("shop.json"));

                    if (format.version > temp.version)
                    {
                        Program.Log(Program.Severity.Info, $"Updated Shop Format. (v.{format.version})");
                        File.WriteAllText("shop.json", format.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log(Program.Severity.Warning, $"Unable to update Shop Format. (ex: {ex.Message})");
            }

            if (!File.Exists("shop.json"))
            {
                Program.Log(Program.Severity.Error, "Shop Format not found.");
                return;
            }

            format = JsonConvert.DeserializeObject(File.ReadAllText("shop.json"));

            CurrentEnergy = e[Convert.ToUInt32(format.energy)];
            MaximumEnergy = e[Convert.ToUInt32(format.maximum)];

            for (uint i = (uint)format.index.Value; i < e.Count; i += (uint)format.iterator.Value)
            {
                ShopItems.Add(new ShopItem()
                {
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
}