﻿using PlayerIOClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rabbit;

namespace EESpender2
{
    class Program
    {
        static Client Client { get; set; }
        static Connection Lobby { get; set; }
        static List<Message> Messages { get; set; } = new List<Message>();

        static void Main(string[] args)
        {
            var required = new List<string>() { "getMySimplePlayerObject", "getLobbyProperties", "getShop" };

            EasyTimer.SetTimeout(() => {
                if (required.All(x => Messages.Select(m => m.Type).Contains(x))) {
                    var shop = new Shop(Messages.First(x => x.Type == "getShop"));

                    var priority = shop.ShopItems.Where(x => x.OwnedAmount == 0 && x.Price > 0).OrderByDescending(x => x.Price - x.EnergySpent)
                                   .OrderByDescending(x => x.IsNew).OrderByDescending(x => x.Name.Contains("world")).Reverse().ToList();

                    if (priority.Count() == 0)
                        priority = new List<Shop.ShopItem>() { shop.ShopItems.OrderByDescending(x => x.IsNew).First(x => x.OwnedAmount > 0 && x.Price > 0) };

                    Log(Severity.Info, "Username: " + Messages.FirstOrDefault(x => x.Type == "getMySimplePlayerObject")[0]);
                    Log(Severity.Info, "Priority Items: " + string.Join(", ", priority.Take(5).Select(x => x.Name)));
                    Log(Severity.Info, "Current Energy: " + shop.CurrentEnergy);
                    Log(Severity.Info, "Maximum Energy: " + shop.MaximumEnergy);

                    foreach (var message in Messages.Where(x => x.Type == "getLobbyProperties")) {
                        var FirstDailyLogin = (bool)message[0];
                        var LoginStreak = (int)message[1];

                        if (FirstDailyLogin && LoginStreak >= 0)
                            for (uint i = 2; i < message.Count; i += 2)
                                Log(Severity.Info, $"Login Streak (#{LoginStreak}). (reward: {message[i + 1]} {message[i]})");
                    }

                    if (shop.CurrentEnergy >= priority[0].EnergyPerClick) {
                        Log(Severity.Info, string.Format("Spending {0} energy on {1}",
                                            priority[0].EnergyPerClick * (Math.Floor((double)shop.CurrentEnergy / priority[0].EnergyPerClick)),
                                            priority[0].SafeName));

                        Lobby.Send("useAllEnergy", priority[0].Name);
                    }
                } else {
                    Log(Severity.Error, "Application took too long and was terminated.");
                }

                Environment.Exit(-1);
            }, 1000 * 10);

            System.Net.ServicePointManager.ServerCertificateValidationCallback += (o, certificate, chain, errors) => true;
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;

            if (args == null || args.ToList().Count() == 0) {
                Log(Severity.Error, "Unspecified account arguments.");
                throw new ArgumentException();
            }

            Client = new RabbitAuth().LogOn("everybody-edits-su9rn58o40itdbnw69plyw", args[0], args[1]);
            Lobby = Client.Multiplayer.CreateJoinRoom(Client.ConnectUserId, $"Lobby{Client.BigDB.Load("config", "config")["version"]}", true, null, null);
            Log(Severity.Info, "EESpender Started.");

            Lobby.OnMessage += (s, message) => {
                Messages.Add(message);

                if (message.Type == "connectioncomplete") {
                    EasyTimer.SetInterval(new Action(() => {
                        foreach (var m in required) {
                            if (!Messages.Any(x => x.Type == m))
                                Lobby.Send(m);
                        }
                    }), 100);
                }
            };

            System.Threading.Thread.Sleep(-1);
        }

        public enum Severity { Info = 0, Warning = 1, Error = 2 }
        public static void Log(Severity severity, string message)
        {
            var output = string.Format("[{0}] [{1:G}] {2}", severity.ToString().ToUpper(), DateTime.Now, message);

            Directory.CreateDirectory("logs");
            File.AppendAllText("logs" + Path.AltDirectorySeparatorChar + $"eespender_{ DateTime.Now.ToString("MM_dd_yy") }.txt", output + "\n");

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

            try {
                format = JsonConvert.DeserializeObject(client.DownloadString("https://raw.githubusercontent.com/atillabyte/EESpender/master/Assets/shop.json"));

                if (!File.Exists("shop.json"))
                    File.WriteAllText("shop.json", format.ToString());
                else {
                    dynamic temp = JsonConvert.DeserializeObject(File.ReadAllText("shop.json"));

                    if (format.version > temp.version) {
                        Program.Log(Program.Severity.Info, $"Updated Shop Format. (v.{format.version})");
                        File.WriteAllText("shop.json", format.ToString());
                    }
                }
            }
            catch (Exception ex) {
                Program.Log(Program.Severity.Warning, $"Unable to update Shop Format. (ex: {ex.Message})");
            }

            if (!File.Exists("shop.json")) {
                Program.Log(Program.Severity.Error, "Shop Format not found.");
                return;
            }

            format = JsonConvert.DeserializeObject(File.ReadAllText("shop.json"));

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