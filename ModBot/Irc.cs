﻿using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ModBot
{
    public delegate void OnConnect();

    public delegate void Connected(string channel, string nick, bool partnered);

    public delegate void OnMessageReceived(string user, string message);

    public delegate void OnDisconnect();

    public delegate void Disconnected();

    static class Irc
    {
        private static iniUtil ini = Program.ini;
        public static TcpClient irc;
        public static StreamReader read;
        public static StreamWriter write;
        private static MainWindow MainForm = Program.MainForm;
        public static string nick, password, channel, currency, currencyName, admin, donation_clientid, donation_token, channeltoken;
        public static int interval, payout, subpayout, StreamStartTime;
        public static bool partnered, IsStreaming, ResourceKeeper, IsModerator, Reconnecting, DetailsConfirmed;
        public static bool greetingOn;
        public static string greeting;
        public static int LastCurrencyDisabledAnnounce, LastUsedCurrencyTop5, LastAnnouncedTickets;
        public static Dictionary<string, int> ActiveUsers = new Dictionary<string, int>(), Warnings = new Dictionary<string, int>(), giveawayFalseEntries = new Dictionary<string, int>();
        public static List<string> IgnoredUsers = new List<string>(), Moderators = new List<string>(), usersToLookup = new List<string>(), Subscribers = new List<string>(), Users = new List<string>();
        public static Dictionary<string, string> UserColors = new Dictionary<string, string>();
        public static Timer currencyQueue, auctionLoop, giveawayQueue, warningsRemoval, newViewers;
        public static List<Thread> Threads = new List<Thread>();
        //private static StreamWriter log = new StreamWriter(@"Data\Logs\Messages.txt", true);

        public static event OnConnect OnConnect = (() => { });

        public static event Connected Connected = ((string channel, string nick, bool partnered) => { });

        public static event OnMessageReceived OnMessageReceived = ((string user, string message) => { });

        public static event OnDisconnect OnDisconnect = (() => { });

        public static event Disconnected Disconnected = (() => { });

        public static void Initialize()
        {
            Api.MainForm = MainForm = Program.MainForm;

            Reconnecting = false;

            ActiveUsers.Clear();
            Warnings.Clear();
            usersToLookup.Clear();
            Moderators.Clear();
            DetailsConfirmed = false;
            IsModerator = false;

            Program.FocusConsole();

            Program.Invoke(() =>
            {
                MainForm.SettingsErrorLabel.Text = "";

                if (donation_clientid == "" || donation_token == "")
                {
                    MainForm.Windows.FromControl(MainForm.DonationsWindow).Button.Enabled = false;
                    MainForm.Windows.FromControl(MainForm.DonationsWindow).Button.Text = "Donations\r\n(Disabled)";
                    Irc.donation_clientid = "";
                    Irc.donation_token = "";
                }

                foreach (System.Windows.Forms.Control ctrl in MainForm.SettingsWindow.Controls)
                {
                    if (ctrl.GetType() != typeof(System.Windows.Forms.Label) && ctrl != MainForm.Misc_ShowConsole)
                    {
                        ctrl.Enabled = false;
                    }
                }
            });

            Console.WriteLine("Validating bot's access token...");

            bool bAbort = true;
            for (int attempts = 0; attempts < 5; attempts++)
            {
                Console.WriteLine("Bot's access token validation attempt : " + (attempts + 1) + "/5");
                using (WebClient w = new WebClient())
                {
                    w.Proxy = null;
                    try
                    {
                        string json_data = w.DownloadString("https://api.twitch.tv/kraken?oauth_token=" + password.Replace("oauth:", ""));
                        JObject json = JObject.Parse(json_data);
                        if (json["token"]["valid"].ToString() == "True" && json["token"]["user_name"].ToString() == nick)
                        {
                            foreach (JToken x in json["token"]["authorization"]["scopes"])
                            {
                                if (x.ToString() == "chat_login")
                                {
                                    Console.WriteLine("Bot's access token has been validated.\r\n");
                                    bAbort = false;
                                    break;
                                }
                            }
                            if (!bAbort) break;
                        }
                        else
                        {
                            Program.Invoke(() =>
                            {
                                Console.WriteLine(MainForm.SettingsErrorLabel.Text += "Twitch reported that bot's auth token is invalid.\r\n");
                            });
                            Thread.Sleep(10);
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        Api.LogError("*************Error Message (via validateBotToken()): " + DateTime.Now + "*********************************\r\n" + e + "\r\n");
                    }
                }

                if (attempts == 4)
                {
                    Console.WriteLine("Failed to validate the bot's access token after 5 attempts.");
                }
                Thread.Sleep(100);
            }

            if (!bAbort)
            {
                Console.WriteLine("Confirming channel's existence in Twitch...");

                for (int attempts = 0; attempts < 5; attempts++)
                {
                    Console.WriteLine("Channel's existence confirming attempt : " + (attempts + 1) + "/5");
                    using (WebClient w = new WebClient())
                    {
                        w.Proxy = null;
                        try
                        {
                            w.DownloadString("https://api.twitch.tv/kraken/channels/" + channel.Substring(1));
                            Console.WriteLine("Channel existence confirmed.\r\n");
                            bAbort = false;
                            break;
                        }
                        catch (Exception e)
                        {
                            if (e.Message.Contains("(404) Not Found."))
                            {
                                Program.Invoke(() =>
                                {
                                    Console.WriteLine(MainForm.SettingsErrorLabel.Text += "Twitch reported channel not found.\r\n");
                                });
                                bAbort = true;
                                System.Threading.Thread.Sleep(10);
                                break;
                            }
                            else
                            {
                                Api.LogError("*************Error Message (via confirmStream()): " + DateTime.Now + "*********************************\r\n" + e + "\r\n");
                            }
                        }
                    }

                    if (attempts == 4)
                    {
                        Console.WriteLine("Failed to confirm the channel's existence after 5 attempts.");
                    }
                    Thread.Sleep(100);
                }

                if (!bAbort)
                {
                    Console.WriteLine("Validating channel's access token...");

                    for (int attempts = 0; attempts < 5; attempts++)
                    {
                        Console.WriteLine("Channel's access token validation attempt : " + (attempts + 1) + "/5");
                        using (WebClient w = new WebClient())
                        {
                            w.Proxy = null;
                            try
                            {
                                JObject json = JObject.Parse(w.DownloadString("https://api.twitch.tv/kraken?oauth_token=" + channeltoken));
                                if (json["token"]["valid"].ToString() == "True" && json["token"]["user_name"].ToString() == channel.Substring(1))
                                {
                                    int scopes = 0;
                                    foreach (JToken x in json["token"]["authorization"]["scopes"])
                                    {
                                        if (x.ToString() == "user_read" || x.ToString() == "channel_editor" || x.ToString() == "channel_commercial" || x.ToString() == "channel_check_subscription" || x.ToString() == "channel_subscriptions" || x.ToString() == "chat_login")
                                        {
                                            scopes++;
                                        }
                                    }

                                    if (scopes == 6)
                                    {
                                        Console.WriteLine("Channel's access token has been validated.\r\n\r\nChecking partnership status...");

                                        json = JObject.Parse(w.DownloadString("https://api.twitch.tv/kraken/user?oauth_token=" + channeltoken));
                                        Console.WriteLine((partnered = json["partnered"].ToString() == "True") ? "Partnered.\r\n" : "Not partnered.\r\n");

                                        Program.Invoke(() =>
                                        {
                                            ini.SetValue("Settings", "Channel_UseSteam", (MainForm.Channel_UseSteam.Checked = (ini.GetValue("Settings", "Channel_UseSteam", "0") == "1")) ? "1" : "0");

                                            MainForm.Giveaway_MustSubscribe.Enabled = partnered;
                                            MainForm.Giveaway_SubscribersWinMultiplier.Enabled = partnered;
                                            MainForm.Channel_SubscriptionRewards.Enabled = partnered;
                                            MainForm.Channel_WelcomeSub.Enabled = partnered;
                                            if (!partnered)
                                            {
                                                MainForm.Giveaway_MustSubscribe.Checked = false;
                                                MainForm.Giveaway_SubscribersWinMultiplier.Checked = false;
                                                MainForm.Channel_SubscriptionRewards.Checked = false;
                                                MainForm.Channel_WelcomeSub.Checked = false;
                                            }

                                            foreach(System.Windows.Forms.CheckBox btn in MainForm.Windows.PartnershipOnly.Buttons)
                                            {
                                                btn.Enabled = partnered;
                                                if (btn.Checked) MainForm.Windows.FromControl(MainForm.SettingsWindow).Button.Checked = true;
                                            }
                                        });
                                        break;
                                    }
                                    else
                                    {
                                        Program.Invoke(() =>
                                        {
                                            Console.WriteLine(MainForm.SettingsErrorLabel.Text += "The channel's access token is missing access. It must be generated through here to have all the access required.\r\n");
                                        });
                                        bAbort = true;
                                        Thread.Sleep(10);
                                        break;
                                    }
                                }
                                else
                                {
                                    Program.Invoke(() =>
                                    {
                                        Console.WriteLine(MainForm.SettingsErrorLabel.Text += "Twitch reported that the channel's auth token is invalid.\r\n");
                                    });
                                    bAbort = true;
                                    Thread.Sleep(10);
                                    break;
                                }
                            }
                            catch (Exception e)
                            {
                                Api.LogError("*************Error Message (via validateBotToken()): " + DateTime.Now + "*********************************\r\n" + e + "\r\n");
                            }
                        }

                        if (attempts == 4)
                        {
                            Console.WriteLine("Failed to validate the channel's access token after 5 attempts.");
                        }
                        Thread.Sleep(100);
                    }
                }
            }

            DetailsConfirmed = true;

            if (!bAbort)
            {
                Console.WriteLine("Configuring settings...");

                Program.Invoke(() =>
                {
                    string name = "";
                    foreach (string word in currencyName.Split(' '))
                    {
                        if (word != "")
                        {
                            int length = (name + word).Length;
                            string suffix = (length < currencyName.Length) ? (currencyName.Substring(length).Split(' ')).Length > 0 ? currencyName.Substring(length).Split(' ')[0] : currencyName.Substring(length) : "";
                            name += word + ((MainForm.CreateGraphics().MeasureString(word, MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font).Width > MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Width - 16 || MainForm.CreateGraphics().MeasureString(word + " " + suffix, MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font).Width > MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Width - 16) ? "\r\n" : " ");
                        }
                    }
                    MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Text = name;
                    while (MainForm.CreateGraphics().MeasureString(name, MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font).Width > MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Width - 16 || MainForm.CreateGraphics().MeasureString(name, MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font).Height > MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Height - 16)
                    {
                        MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font = new Font(MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font.Name, MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font.Size - 1, FontStyle.Bold);
                    }
                    if (MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font.Size < 6)
                    {
                        MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Text = "Currency";
                        MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font = new Font(MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font.Name, 10F, FontStyle.Bold);
                    }
                    MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Text = admin;

                    MainForm.Currency_HandoutLabel.Text = "Handout " + currencyName + " to :";

                    MainForm.Giveaway_MinCurrency.Text = "Must have at least                       " + currencyName;
                });

                ini.SetValue("Settings", "ResourceKeeper", (ResourceKeeper = (ini.GetValue("Settings", "ResourceKeeper", "1") == "1")) ? "1" : "0");

                ini.SetValue("Settings", "Channel_Greeting", greeting = ini.GetValue("Settings", "Channel_Greeting", "Hello @user! Welcome to the stream!"));

                IgnoredUsers.Add("jtv");
                IgnoredUsers.Add("twitchnotify");
                IgnoredUsers.Add("moobot");
                IgnoredUsers.Add("nightbot");
                IgnoredUsers.Add(nick.ToLower());
                IgnoredUsers.Add(admin.ToLower());

                Console.WriteLine("Settings configured.\r\n");

                Database.Initialize();
            }

            if (bAbort || Database.DB == null && Database.MySqlDB == null)
            {
                Console.WriteLine("Aborting connection...");

                Disconnect(bAbort ? !bAbort : MainForm.SettingsErrorLabel.Text == "" || MainForm.SettingsErrorLabel.Text == "Unable to connect to MySQL server.\r\n", false);

                Console.WriteLine("Connection aborted.\r\n");
                return;
            }

            Database.setUserLevel(admin, 4);

            Connect();

            //YouTube.PlaySong();
        }

        private static void RegisterCommands()
        {
            Console.WriteLine("Registering commands...");

            Commands.Add("!raffle", Command_Giveaway, 0, 0);
            Commands.Add("!giveaway", Command_Giveaway, 0, 0);
            Commands.Add("!ticket", Command_Ticket, 0, 0);
            Commands.Add("!tickets", Command_Ticket, 0, 0);

            Commands.Add("!" + currency, Command_Currency, 0, 0);
            if (currency != "currency") Commands.Add("!currency", Command_Currency, 0, 0);

            Commands.Add("!gamble", Command_Gamble, 2, 0);
            Commands.Add("!bet", Command_Bet, 0, 0);

            Commands.Add("!auction", Command_Auction, 2, 0);
            Commands.Add("!bid", Command_Bid, 0, 0);

            Commands.Add("!btag", Command_BTag, 0, 0);
            Commands.Add("!battletag", Command_BTag, 0);

            Commands.Add("!modbot", Command_ModBot, 0, 300);

            Commands.Add("!warn", Command_Warn, 1, 0);
            Commands.Add("!warnings", Command_Warnings, 1, 0);

            Commands.Add("!uptime", Command_Uptime, 0, 300);

            /*Commands.Add("!songrequest", Command_SongRequest, 0, 0);
            Commands.Add("!testsong", Command_TestSong, 0, 0);
            Commands.Add("!skipsong", Command_SkipSong, 0, 0);
            Commands.Add("!stopsong", Command_StopSong, 0, 0);*/

            Commands.Add("!botinfo", Command_BotInfo, 0, 300);
            Commands.Add("!bot", Command_BotInfo, 0, 300);

            Console.WriteLine("Commands registered.\r\n");
        }

        private static void Connect()
        {
            Console.WriteLine("Initializing connection...");

            if(!Reconnecting) OnConnect();

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                if (irc != null)
                {
                    //Console.WriteLine("Irc connection already exists. Closing it and opening a new one.");
                    irc.Close();
                }

                irc = new TcpClient();

                Console.WriteLine("Connection attempt number : " + attempt + "/5");

                try
                {
                    irc.Connect("199.9.250.229", 443);

                    Console.WriteLine("Connection successful.\r\n\r\nConfiguring input/output...");

                    read = new StreamReader(irc.GetStream());
                    write = new StreamWriter(irc.GetStream());

                    write.AutoFlush = true;

                    Console.WriteLine("Input/output configured.\r\n\r\nJoining the channel...");

                    sendRaw("TWITCHCLIENT 3");
                    sendRaw("PASS " + password);
                    sendRaw("NICK " + nick);
                    sendRaw("USER " + nick + " 8 * :" + nick);
                    sendRaw("JOIN " + channel);

                    if (!read.ReadLine().Contains("Login unsuccessful"))
                    {
                        nick = Api.GetDisplayName(nick);
                        admin = Api.GetDisplayName(admin);

                        Console.WriteLine("Joined the channel.\r\n");
                        /*Console.WriteLine("Joined the channel.\r\n\r\nSending lame entrance line...\r\n");

                        List<string> lLines = new List<string>();
                        lLines.Add("ModBot has entered the building.");
                        lLines.Add("No fear, ModBot is here!");
                        lLines.Add("ModBot with style.");
                        lLines.Add("Fear not, here's the (Mod)Bot.");
                        lLines.Add("ModBot's in the HOUSE!");
                        sendMessage(lLines[new Random().Next(0, lLines.Count)], "", false);*/

                        //sendMessage("/twitchclient 3", "", false, false);

                        if (Reconnecting) return;

                        RegisterCommands();

                        StartThreads();

                        Program.Invoke(() =>
                        {
                            foreach (System.Windows.Forms.CheckBox btn in MainForm.Windows.ConnectionOnly.Buttons)
                            {
                                btn.Enabled = true;
                            }

                            MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Text = admin;
                            while (MainForm.CreateGraphics().MeasureString(admin, MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Font).Width > MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Width - 16) MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Font = new Font(MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Font.Name, MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Font.Size - 1, FontStyle.Bold);

                            MainForm.DisconnectButton.Enabled = true;

                            MainForm.GetSettings();
                        });

                        Connected(channel, nick, partnered);
                    }
                    else
                    {
                        Program.Invoke(() =>
                        {
                            Console.WriteLine(MainForm.SettingsErrorLabel.Text += "Username and/or password (oauth token) are incorrect!\r\n");
                            MainForm.ConnectButton.Enabled = false;
                            MainForm.DisconnectButton.Enabled = false;
                        });
                    }

                    return;
                }
                catch (SocketException e)
                {
                    Api.LogError("*************Error Message (via Connect()): " + DateTime.Now + "*********************************\r\n" + e + "\r\n");
                }
                catch (Exception e)
                {
                    Api.LogError("*************Error Message (via Connect()): " + DateTime.Now + "*********************************\r\n" + e + "\r\n");
                }

                Program.Invoke(() =>
                {
                    MainForm.ConnectButton.Enabled = true;
                    MainForm.DisconnectButton.Enabled = false;
                });

                if (attempt <= 5)
                {
                    Console.WriteLine("Failed connect or configure post-connection settings.\r\nRetrying in 5 seconds.\r\n");
                    Thread.Sleep(5000);
                }
                else
                {
                    Console.WriteLine("Failed to connect to Twitch.TV chat servers...");

                    Disconnect(true, false);
                }
            }
        }

        public static void Disconnect(bool allowreconnecting = true, bool log = true)
        {
            Program.FocusConsole();

            if (log) Console.WriteLine("\r\nDisconnecting...\r\n"); OnDisconnect();

            Pool.cancel();
            if (Giveaway.Started) Giveaway.cancelGiveaway(false);

            DetailsConfirmed = false;
            IsModerator = false;
            IsStreaming = false;

            Program.Invoke(() =>
            {
                MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Text = "Currency";
                MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font = new Font(MainForm.Windows.FromControl(MainForm.CurrencyWindow).Button.Font.Name, 10F, FontStyle.Bold);
                MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Text = "Channel";
                MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Font = new Font(MainForm.Windows.FromControl(MainForm.ChannelWindow).Button.Font.Name, 10F, FontStyle.Bold);
                MainForm.ChannelStatusLabel.Text = "DISCONNECTED";
                MainForm.ChannelStatusLabel.ForeColor = Color.Red;
                MainForm.Windows.FromControl(MainForm.DonationsWindow).Button.Text = "Donations";

                foreach (Window window in MainForm.Windows) if ((window.RequiresConnection || window.RequiresMod || window.RequiresPartnership) && !window.ControlManually) window.Button.Enabled = false;
                MainForm.Windows.FromControl(MainForm.DonationsWindow).Button.Enabled = false;

                foreach (System.Windows.Forms.CheckBox btn in MainForm.Windows.Buttons) if (!btn.Enabled && btn.Checked) MainForm.Windows.FromControl(MainForm.SettingsWindow).Button.Checked = true;

                foreach (System.Windows.Forms.Control ctrl in MainForm.SettingsWindow.Controls)
                {
                    ctrl.Enabled = true;
                }
                MainForm.DisconnectButton.Enabled = false;
                MainForm.ConnectButton.Enabled = false;
            });

            MainForm.ChannelTitle = "";
            MainForm.ChannelGame = "";

            if (Threads.Count > 0)
            {
                if (log) Console.WriteLine("Stopping threads...");
                List<Thread> Ts = new List<Thread>();
                foreach (Thread t in Threads)
                {
                    t.Abort();
                    Ts.Add(t);
                }
                Threads.Clear();
                foreach (Thread t in Api.dCheckingDisplayName.Values)
                {
                    t.Abort();
                    Ts.Add(t);
                }
                Api.dCheckingDisplayName.Clear();
                /*foreach (Thread t in Ts)
                {
                    Console.Write(t.Name + " thread... ");
                    while (t.IsAlive) Thread.Sleep(10);
                    Console.Write("DONE\r\n");
                }*/
                if (log) Console.WriteLine("Threads stopped.\r\n");
                //Console.WriteLine("Threads stopped.\r\nClearing threads list...");
                //Threads.Clear();
                //Console.WriteLine("Threads list clear.\r\n");
            }

            if (log) Console.WriteLine("Unregistering commands...");
            lock (Commands.lCommands) foreach (Commands.Command command in Commands.lCommands.ToList()) Commands.Remove(command.Cmd);
            if (log) Console.WriteLine("Unregistered commands.\r\n");

            if (irc != null && irc.Connected)
            {
                if (log) Console.WriteLine("Closing connection...");
                irc.Close();
                if (log) Console.WriteLine("Connection closed.\r\n");
            }

            if (Database.DB != null)
            {
                if (log) Console.WriteLine("Closing database...");
                Database.DB.Close();
                Database.DB = null;
                if (log) Console.WriteLine("Database closed.\r\n");
            }

            if (Database.MySqlDB != null)
            {
                if (log) Console.WriteLine("Closing MySQL connection...");
                //Database.MySqlDB.Close();
                Database.MySqlDB = null;
                if (log) Console.WriteLine("MySQL connection closed.\r\n");
            }

            Program.Invoke(() =>
            {
                MainForm.ConnectButton.Enabled = allowreconnecting;
            });

            if (log) Console.WriteLine("Disconnected.\r\n"); Disconnected();
        }

        private static void StartThreads()
        {
            if (Threads.Count > 0)
            {
                Console.WriteLine("Stopping previously created threads...");
                List<Thread> Ts = new List<Thread>();
                foreach (Thread t in Threads)
                {
                    t.Abort();
                    Ts.Add(t);
                }
                Threads.Clear();
                foreach (Thread t in Api.dCheckingDisplayName.Values)
                {
                    t.Abort();
                    Ts.Add(t);
                }
                Api.dCheckingDisplayName.Clear();
                /*foreach (Thread t in Ts)
                {
                    Console.Write(t.Name + " thread... ");
                    while (t.IsAlive) Thread.Sleep(10);
                    Console.Write("DONE\r\n");
                }*/
                Console.WriteLine("Previously created threads stopped.\r\n");
                //Console.WriteLine("Previously created threads stopped.\r\nClearing threads list...");
                //Threads.Clear();
                //Console.WriteLine("Threads list clear.");
            }

            Console.Write("Creating and starting timers and threads...\r\nCurrency check queue timer... ");

            if (currencyQueue == null) currencyQueue = new Timer(handleCurrencyQueue, null, Timeout.Infinite, Timeout.Infinite);
            currencyQueue.Change(Timeout.Infinite, Timeout.Infinite);

            Console.Write("DONE\r\nAuction highest bidder timer... ");

            if (auctionLoop == null) auctionLoop = new Timer(auctionLoopHandler, null, Timeout.Infinite, Timeout.Infinite);
            auctionLoop.Change(Timeout.Infinite, Timeout.Infinite);

            Console.Write("DONE\r\nGiveaway joining report timer... ");

            if (giveawayQueue == null) giveawayQueue = new Timer(giveawayQueueHandler, null, Timeout.Infinite, Timeout.Infinite);
            giveawayQueue.Change(Timeout.Infinite, Timeout.Infinite);

            Console.Write("DONE\r\nWarnings removal timer... ");

            if (warningsRemoval == null) warningsRemoval = new Timer(warningsRemovalHandler, null, 900000, 900000);
            warningsRemoval.Change(900000, 900000);

            Console.Write("DONE\r\nNew viewers timer... ");
            if (newViewers == null) newViewers = new Timer(newViewersHandler, null, (int)MainForm.Channel_ViewersChangeInterval.Value * 60000, (int)MainForm.Channel_ViewersChangeInterval.Value * 60000);
            newViewers.Change((int)MainForm.Channel_ViewersChangeInterval.Value * 60000, (int)MainForm.Channel_ViewersChangeInterval.Value * 60000);

            Console.Write("DONE\r\nTime watched and currency handout thread... ");

            bool Running = false;

            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(60000);
                    if (irc.Connected && Running && IsStreaming)
                    {
                        new Thread(() =>
                        {
                            List<string> spreadsheetSubs = Api.checkSpreadsheetSubs();
                            //buildUserList();
                            lock (ActiveUsers)
                            {
                                foreach (string user in ActiveUsers.Keys)
                                {
                                    Database.addTimeWatched(user, 1);
                                    TimeSpan t = Database.getTimeWatched(user);
                                    if (t.TotalMinutes % interval == 0 && (MainForm.Currency_HandoutEveryone.Checked || MainForm.Currency_HandoutActiveStream.Checked && ActiveUsers[user] >= StreamStartTime || MainForm.Currency_HandoutActiveTime.Checked && ActiveUsers[user] >= StreamStartTime && Api.GetUnixTimeNow() - ActiveUsers[user] <= Convert.ToInt32(MainForm.Currency_HandoutLastActive.Value) * 60))
                                    {
                                        int old = Database.checkCurrency(user);
                                        while (old == Database.checkCurrency(user))
                                        {
                                            //old = Database.checkCurrency(user);
                                            if (Database.isSubscriber(user) || spreadsheetSubs.Contains(user) || Api.IsSubscriber(user))
                                            {
                                                Database.addCurrency(user, subpayout);
                                            }
                                            else
                                            {
                                                Database.addCurrency(user, payout);
                                            }
                                        }
                                    }
                                }
                            }
                        }).Start();
                    }
                }
            });
            Threads.Add(thread);
            thread.Name = "Time watched and currency handout";
            thread.Start();

            Console.Write("DONE\r\nUser list thread... ");

            thread = new Thread(() =>
            {
                buildUserList(false); // Preload the list of users so the currency handout options will work correctly.

                Users = ActiveUsers.Keys.ToList();

                while (true)
                {
                    buildUserList();

                    if (partnered)
                    {
                        new Thread(() =>
                        {
                            List<string> subs = Api.GetAllSubscribers();
                            lock (Subscribers)
                            {
                                Subscribers = subs;
                            }
                        }).Start();
                    }

                    Thread.Sleep(60000);
                }
            });
            Threads.Add(thread);
            thread.Name = "User list";
            thread.Start();

            if (partnered)
            {
                Console.Write("DONE\r\nSubscriber rewards thread... ");

                thread = new Thread(() =>
                {
                    Dictionary<string, DateTime> LastSubscription = Api.GetLastSubscribers(new DateTime(), 1);
                    if (LastSubscription.Count > 0)
                    {
                        Program.Invoke(() =>
                        {
                            MainForm.Channel_SubscriptionsDate.Value = LastSubscription.ElementAt(0).Value;
                        });
                    }

                    while (true)
                    {
                        Dictionary<string, DateTime> Subscriptions = Api.GetLastSubscribers(MainForm.Channel_SubscriptionsDate.Value);
                        if (Subscriptions.Count > 0)
                        {
                            Program.Invoke(() =>
                            {
                                MainForm.Channel_SubscriptionsDate.Value = Subscriptions.ElementAt(0).Value.AddSeconds(1);
                            });
                            lock (MainForm.SubscriptionRewards)
                            {
                                if (MainForm.Channel_SubscriptionRewards.Checked && MainForm.SubscriptionRewards.Count > 0)
                                {
                                    for (int i = Subscriptions.Count; i > 0; i--)
                                    {
                                        string subscriber = Subscriptions.ElementAt(i).Key;
                                        string reward = MainForm.SubscriptionRewards.ElementAt(new Random().Next(0, MainForm.SubscriptionRewards.Count)).Key;
                                        string text = "[" + DateTime.Now + " via subscription rewards] " + subscriber + " has won " + reward;
                                        for (int attempts = 0; attempts < 10; attempts++)
                                        {
                                            try
                                            {
                                                using (StreamWriter log = new StreamWriter(@"Data\Logs\Rewards.txt", true))
                                                {
                                                    log.WriteLine(text);
                                                }
                                                break;
                                            }
                                            catch
                                            {
                                                System.Threading.Thread.Sleep(250);
                                            }
                                        }
                                        sendMessage(subscriber + " has subscribed and won " + reward + "!");
                                        if (MainForm.SubscriptionRewards[reward] != "")
                                        {
                                            Thread.Sleep(1000);
                                            sendMessage(subscriber + ", please " + MainForm.SubscriptionRewards[reward] + ".");
                                        }
                                        Thread.Sleep(1000);
                                    }
                                }
                            }
                        }

                        if (Subscriptions.Count < 10) Thread.Sleep(5000);
                    }
                });
                Threads.Add(thread);
                thread.Name = "Subscriber rewards";
                thread.Start();
            }

            Console.Write("DONE\r\nStream status thread... ");

            thread = new Thread(() =>
            {
                while (true)
                {
                    bool bIsStreaming = false;
                    using (WebClient w = new WebClient())
                    {
                        w.Proxy = null;
                        try
                        {
                            JObject stream = JObject.Parse(w.DownloadString("https://api.twitch.tv/kraken/streams/" + channel.Substring(1)));
                            if (stream["stream"].HasValues)
                            {
                                StreamStartTime = Api.GetUnixFromTime(DateTime.Parse(stream["stream"]["created_at"].ToString()));
                                bIsStreaming = true;
                            }
                        }
                        catch
                        {
                        }
                    }

                    IsStreaming = bIsStreaming;

                    Thread.Sleep(1000);
                    if (ResourceKeeper) Thread.Sleep(29000);
                }
            });
            Threads.Add(thread);
            thread.Name = "Stream status";
            thread.Start();

            Console.Write("DONE\r\nConnection ping thread... ");

            //KeepAlive();
            thread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(30000);
                    sendRaw("PING 1245");
                }
            });
            Threads.Add(thread);
            thread.Name = "Connection ping";
            thread.Start();

            Console.Write("DONE\r\nChannel data updating thread... ");

            thread = new Thread(() =>
            {
                MainForm.UpdateChannelData();
            });
            Threads.Add(thread);
            thread.Name = "Channel data updating";
            thread.Start();

            if (Irc.donation_clientid != "" && Irc.donation_token != "")
            {
                Console.Write("DONE\r\nDonations updating thread... ");

                thread = new Thread(() =>
                {
                    MainForm.UpdateDonations();
                });
                Threads.Add(thread);
                thread.Name = "Donations updating";
                thread.Start();
            }

            Console.Write("DONE\r\nInput listening thread... ");

            //Listen();
            thread = new Thread(() =>
            {
                Thread.Sleep(50);
                Console.WriteLine("Attempting to listen to input...");
                int attempt = 0;
                while (attempt < 5)
                {
                    attempt++;
                    Console.WriteLine((Running ? "Fix " : "Listening to input ") + "attempt number : " + attempt + "/5");
                    try
                    {
                        while (irc.Connected && Threads.Count > 0)
                        {
                            parseMessage(read.ReadLine());
                            if (attempt > 0)
                            {
                                if (!Running)
                                {
                                    Console.WriteLine("Listening to input.\r\n");
                                }
                                else
                                {
                                    Console.WriteLine("The attempt was successful, everything should keep running the way it should.");
                                }
                                attempt = 0;
                                Running = true;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if ((attempt == 0 || attempt == 5 && !Running) && Threads.Count > 0 && !e.Message.Contains("System.Threading.ThreadAbortException"))
                        {
                            if (attempt == 0)
                            {
                                Console.WriteLine("Uh oh, there was an error! Attempts to keep everything running are being executed, if the attempts fail or if you keep seeing this message, email your error log (Data/Logs/Errors.txt) file to CoMaNdO.ModBot@gmail.com with the title \"ModBot - Error\" (Other titles will most likely be ignored).");
                            }
                            else
                            {
                                Console.WriteLine("Failed to listen to input... Please try reconnecting... If this issue keeps occouring, please email your error log (Data/Logs/Errors.txt) file to CoMaNdO.ModBot@gmail.com with the title \"ModBot - Error\" (Other titles will most likely be ignored).");
                            }
                            Api.LogError("*************Error Message (via Listen()): " + DateTime.Now + "*********************************\r\n" + e + "\r\n");
                        }
                    }
                    Thread.Sleep(500);
                }
                Running = false;
                //MainForm.Hide();
                Console.WriteLine("The attempts were unsuccessful... Try reconnecting again later...");
                System.Windows.Forms.MessageBox.Show("ModBot has encountered an error, more information available in the console...", "ModBot", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            });
            Threads.Add(thread);
            thread.Name = "Input listening";
            thread.Start();

            Console.Write("DONE\r\nSuccessfully created and started all timers and threads!\r\n\r\n");
        }

        private static void newViewersHandler(object state)
        {
            if (!DetailsConfirmed || !irc.Connected || !IsStreaming || !MainForm.Channel_ViewersChange.Checked) return;

            lock (Users)
            {
                lock (ActiveUsers)
                {
                    int difference = 0;
                    List<string> TotalUsers = new List<string>();
                    foreach (string user in ActiveUsers.Keys)
                    {
                        if (!Users.Contains(user)) difference++;
                        if (!TotalUsers.Contains(user)) TotalUsers.Add(user);
                    }
                    foreach (string user in Users)
                    {
                        if (!ActiveUsers.Keys.Contains(user)) difference++;
                        if (!TotalUsers.Contains(user)) TotalUsers.Add(user);
                    }

                    if ((float)TotalUsers.Count / difference * 100 >= (float)MainForm.Channel_ViewersChangeRate.Value)
                    {
                        sendMessage(MainForm.Channel_ViewersChangeMessage.Text);
                    }

                    Users = ActiveUsers.Keys.ToList();
                }
            }
        }

        private static void warningsRemovalHandler(object state)
        {
            lock (Warnings)
            {
                Dictionary<string, int> warns = new Dictionary<string, int>();
                foreach (string user in Warnings.Keys)
                {
                    if (Warnings[user] > 1)
                    {
                        warns.Add(user, Warnings[user] - 1);
                    }
                }
                if (Warnings.Count > 0)
                {
                    Warnings = warns;
                    sendMessage("Users with warnings has just lost one, hurray! Now play nice Jebaited");
                }
            }
        }

        private static void giveawayQueueHandler(object state)
        {
            if (Giveaway.Started && Giveaway.Users.Count > 0)
            {
                sendMessage("Total of " + Giveaway.Users.Count + " people joined the giveaway.");
                Thread.Sleep(1000);

                string finalmessage = "";
                lock (giveawayFalseEntries)
                {
                    foreach (string user in giveawayFalseEntries.Keys)
                    {
                        string name = Api.GetDisplayName(user);
                        string msg = "you have insufficient " + currencyName + ", you don't answer the requirements or the tickets amount you put is invalid";
                        if (giveawayFalseEntries[user] < 1 || giveawayFalseEntries[user] > Giveaway.MaxTickets)
                        {
                            msg = "the tickets amount you put is invalid";
                        }
                        else if (Database.checkCurrency(user) < Giveaway.Cost * giveawayFalseEntries[user])
                        {
                            msg = "you have insufficient " + currencyName;
                        }
                        else if (MainForm.Giveaway_MustFollow.Checked && !Api.IsFollower(user))
                        {
                            msg = "you don't follow the channel";
                        }
                        else if (MainForm.Giveaway_MustSubscribe.Checked && !Api.IsSubscriber(user))
                        {
                            msg = "you are not subscribed to the channel";
                        }
                        else if (MainForm.Giveaway_MustWatch.Checked && Api.CompareTimeWatched(user) == -1)
                        {
                            msg = "you haven't watched the stream for long enough";
                        }

                        if (MainForm.Giveaway_WarnFalseEntries.Checked && Moderators.Contains(nick.ToLower()))
                        {
                            if (warnUser(user, 1, 10, "Attempting to buy tickets without meeting the requirements or with insufficient funds or invalid parameters", 3, true, true, MainForm.Giveaway_AnnounceWarnedEntries.Checked, 6) == 1)
                            {
                                if (finalmessage.Length + msg.Length > 996)
                                {
                                    sendMessage(finalmessage);
                                    Thread.Sleep(1000);
                                    finalmessage = "";
                                }
                                finalmessage += name + ", " + msg + " (Warning: " + Warnings[user] + "/3). ";
                            }
                        }
                        else
                        {
                            if (finalmessage.Length + msg.Length > 996)
                            {
                                sendMessage(finalmessage);
                                Thread.Sleep(1000);
                                finalmessage = "";
                            }
                            finalmessage += name + ", " + msg + ". ";
                        }
                    }
                    if (finalmessage != "") sendMessage(finalmessage); // ToDo: Add an option to disable as it is spamming.
                    if (MainForm.Giveaway_TypeTickets.Checked && Api.GetUnixTimeNow() - LastAnnouncedTickets > 60)
                    {
                        LastAnnouncedTickets = Api.GetUnixTimeNow();
                        sendMessage("Ticket cost: " + Giveaway.Cost + ", max. tickets: " + Giveaway.MaxTickets + ".");
                    }
                    giveawayFalseEntries.Clear();
                }
            }
        }

        private static void parseMessage(string message)
        {
            if (message == null || message == "") return;

            new Thread(() =>
            {
                //Console.WriteLine(message);

                string[] msg = message.Split(' ');
                string user;

                if (msg[0].Equals("PING"))
                {
                    sendRaw("PONG " + msg[1]);
                    //Console.WriteLine("PONG " + msg[1]);
                }
                else if (msg[1].Equals("PRIVMSG"))
                {
                    user = getUser(message);
                    addUserToList(user, -1, true);
                    
                    string text = message.Substring(message.IndexOf(":", 1) + 1);
                    if (text.Substring(1).StartsWith("ACTION")) text = "/me " + text.Substring(8).Replace(text[text.Length - 1].ToString(), "");

                    if (user == "jtv")
                    {
                        if (text.StartsWith("HISTORYEND"))
                        {
                            Console.WriteLine("Everything should be set and ready!\r\nModBot is good to go!\r\n");
                        }
                        else if (text.StartsWith("USERCOLOR"))
                        {
                            msg = text.Split(' ');
                            user = msg[1].ToLower();
                            if (!UserColors.ContainsKey(user))
                            {
                                UserColors.Add(user, msg[2]);
                            }
                            else
                            {
                                UserColors[user] = msg[2];
                            }
                        }
                        else if (text.StartsWith("The moderators of this room are: "))
                        {
                            lock (Moderators)
                            {
                                Moderators.Clear();
                                foreach (string mod in text.Substring(33).Replace(" ", "").Split(','))
                                {
                                    if (mod != "" && !Moderators.Contains(mod))
                                    {
                                        Moderators.Add(mod);
                                    }
                                }
                                Moderators.Add(channel.Substring(1));
                            }

                            Program.Invoke(() =>
                            {
                                foreach (System.Windows.Forms.CheckBox btn in MainForm.Windows.ModOnly.Buttons)
                                {
                                    btn.Enabled = Moderators.Contains(nick.ToLower());
                                    if (!btn.Enabled && btn.Checked) MainForm.Windows.FromControl(MainForm.SettingsWindow).Button.Checked = true;
                                }

                                MainForm.Giveaway_WarnFalseEntries.Enabled = (!MainForm.Giveaway_TypeActive.Checked && Moderators.Contains(nick.ToLower()));
                                if (MainForm.Giveaway_TypeActive.Checked || !Moderators.Contains(nick.ToLower())) MainForm.Giveaway_WarnFalseEntries.Checked = false;
                            });
                        }

                        return;
                    }
                    else if (user == "twitchnotify")
                    {
                        Console.WriteLine(text);
                        msg = text.Split(' ');
                        user = Api.GetDisplayName(msg[0]);
                        sendMessage(MainForm.Channel_WelcomeSubMessage.Text.Replace("@user", user));

                        return;
                    }

                    string name = Api.GetDisplayName(user);
                    Console.WriteLine(name + ": " + text);

                    OnMessageReceived(name, text);

                    if (user == "dorcomando")
                    {
                        if (text.ToLower() == "peekaboo")
                        {
                            sendMessage("I see you...", "", true, false);

                            return;
                        }
                    }

                    if (IsModerator && MainForm.Spam_CWL.Checked)
                    {
                        foreach (char character in text)
                        {
                            if (!"`~!@#$%^&*()-_=+'\"\\/.,?[]{}<>|:; ".Contains(character) && !MainForm.Spam_CWLBox.Text.ToLower().Contains(character.ToString().ToLower()))
                            {
                                warnUser(user, 1, 30, "Using a restricted character", 0, false, true, MainForm.Spam_CWLAnnounceTimeouts.Checked, 6);
                                return;
                            }
                        }
                    }

                    if (MainForm.Giveaway_TypeKeyword.Checked && text.ToLower() == MainForm.Giveaway_CustomKeyword.Text.ToLower())
                    {
                        Command_Ticket(name, "Custom", new string[0]);
                        return;
                    }

                    Commands.CheckCommand(name, text, true, false);

                    if (user.Equals(MainForm.Giveaway_WinnerLabel.Text.ToLower()))
                    {
                        Program.Invoke(() =>
                        {
                            MainForm.Giveaway_WinnerChat.SelectionColor = Color.Blue;
                            if (UserColors.ContainsKey(user)) MainForm.Giveaway_WinnerChat.SelectionColor = ColorTranslator.FromHtml(UserColors[user]);
                            MainForm.Giveaway_WinnerChat.SelectionFont = new Font("Segoe Print", 7, FontStyle.Bold);
                            MainForm.Giveaway_WinnerChat.SelectedText = name;
                            MainForm.Giveaway_WinnerChat.SelectionColor = Color.Black;
                            MainForm.Giveaway_WinnerChat.SelectionFont = new Font("Microsoft Sans Serif", 8);
                            MainForm.Giveaway_WinnerChat.SelectedText = ": " + text + "\r\n";
                            MainForm.Giveaway_WinnerChat.Select(MainForm.Giveaway_WinnerChat.Text.Length, MainForm.Giveaway_WinnerChat.Text.Length);
                            MainForm.Giveaway_WinnerChat.ScrollToCaret();
                            MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.FromArgb(0, 200, 0);
                        });
                    }
                }
                /*else if (msg[1].Equals("JOIN"))
                {
                    user = getUser(message);

                    if (!ActiveUsers.ContainsKey(user)) addUserToList(user);

                    if (!Database.userExists(user))
                    {
                        Database.newUser(user);
                        //db.addCurrency(user, payout);
                    }

                    if (user == Api.capName(nick) || user == "Jtv") return;

                    string name = Api.GetDisplayName(user);

                    Console.WriteLine(name + " joined");

                    if (greetingOn && greeting != "")
                    {
                        sendMessage(greeting.Replace("@user", name));
                    }

                    if (user.Equals(Api.capName(MainForm.Giveaway_WinnerLabel.Text)))
                    {
                        Program.Invoke(() =>
                        {
                            MainForm.Giveaway_WinnerChat.SelectionColor = Color.Green;
                            MainForm.Giveaway_WinnerChat.SelectionFont = new Font("Segoe Print", 7, FontStyle.Bold);
                            MainForm.Giveaway_WinnerChat.SelectedText = name + " has joined the channel.\r\n";
                        });
                    }
                }
                else if (msg[1].Equals("PART"))
                {
                    user = getUser(message);
                    removeUserFromList(user);

                    string name = Api.GetDisplayName(user);

                    Console.WriteLine(name + " left");

                    if (user.Equals(Api.capName(MainForm.Giveaway_WinnerLabel.Text)))
                    {
                        Program.Invoke(() =>
                        {
                            MainForm.Giveaway_WinnerTimerLabel.Text = "The winner left!";
                            MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.FromArgb(255, 0, 0);

                            MainForm.Giveaway_WinnerChat.SelectionColor = Color.Red;
                            MainForm.Giveaway_WinnerChat.SelectionFont = new Font("Segoe Print", 7, FontStyle.Bold);
                            MainForm.Giveaway_WinnerChat.SelectedText = name + " has left the channel.\r\n";
                        });
                    }
                }*/
                else if (msg[1].Equals("MODE"))
                {
                    user = msg[4].ToLower();
                    if (msg[3] == "+o")
                    {
                        /*if (!Moderators.Contains(user))
                        {
                            Moderators.Add(user);
                        }*/
                        if (user == nick.ToLower())
                        {
                            IsModerator = true;
                        }
                    }
                    else if (msg[3] == "-o")
                    {
                        /*if (Moderators.Contains(user))
                        {
                            Moderators.Remove(user);
                        }*/
                        if (user == nick.ToLower())
                        {
                            IsModerator = false;
                        }
                    }

                    //buildUserList();
                    //sendMessage("/mods", "", false, false);
                }
                else if (msg[1].Equals("352"))
                {
                    //Console.WriteLine(message);
                    addUserToList(msg[4]);
                }
                /*else
                {
                    //Console.WriteLine(message);
                }*/
            }).Start();
        }

        private static void Command_Giveaway(string user, string cmd, string[] args)
        {
            if (args.Length > 0)
            {
                //ADMIN GIVEAWAY COMMANDS: !giveaway open <TicketCost> <MaxTickets>, !giveaway close, !giveaway draw, !giveaway cancel//
                if (Database.getUserLevel(user) >= 1)
                {
                    if (args[0].Equals("announce"))
                    {
                        /*string sMessage = "Get the party started! Viewers active in chat within the last " + Convert.ToInt32(MainForm.Giveaway_ActiveUserTime.Value) + " minutes ";
                        if (MainForm.Giveaway_MustFollowCheckBox.Checked)
                        {
                            if (!MainForm.Giveaway_MinCurrencyCheckBox.Checked)
                            {
                                sMessage = sMessage + "and follow the stream ";
                            }
                            else
                            {
                                sMessage = sMessage + "follow the stream, and have " + MainForm.Giveaway_MinCurrency.Value + " " + currencyName + " ";
                            }
                        }
                        else
                        {
                            sMessage = sMessage + "and have " + MainForm.Giveaway_MinCurrency.Value + " " + currencyName + " ";
                        }
                        sMessage = sMessage + "will qualify for the giveaway!";
                        sendMessage(sMessage);*/
                        sendMessage("Get the party started! Viewers active in chat within the last " + MainForm.Giveaway_ActiveUserTime.Value + " minutes" + (MainForm.Giveaway_MustFollow.Checked ? MainForm.Giveaway_MinCurrency.Checked ? " follow the stream, and have " + MainForm.Giveaway_MinCurrencyBox.Value + " " + currencyName : " and follow the stream" : "") + " will qualify for the giveaway!");
                    }

                    if (Database.getUserLevel(user) >= 2)
                    {
                        if (args[0].Equals("roll"))
                        {
                            if (Giveaway.Started)
                            {
                                if (!Giveaway.Open)
                                {
                                    string winner = Giveaway.getWinner();
                                    if (winner != "")
                                    {
                                        TimeSpan t = Database.getTimeWatched(winner);
                                        //sendMessage(winner + " has won the giveaway! (" + (Api.IsSubscriber(winner) ? "Subscribes to the channel | " : "") + (Api.IsFollower(winner) ? "Follows the channel | " : "") + "Has " + Database.checkCurrency(winner) + " " + currencyName + " | Has watched the stream for " + t.Days + " days, " + t.Hours + " hours and " + t.Minutes + " minutes | Chance : " + Giveaway.Chance.ToString("0.00") + "%)");
                                        sendMessage(winner + " has won the giveaway! (" + (Api.IsSubscriber(winner) ? "Subscribes to the channel | " : "") + (Api.IsFollower(winner) ? "Follows the channel | " : "") + "Has " + Database.checkCurrency(winner) + " " + currencyName + " | Has watched the stream for " + t.Days + " days, " + t.Hours + " hours and " + t.Minutes + " minutes)");
                                    }
                                    else
                                    {
                                        sendMessage("No valid winner found, please try again!");
                                    }
                                }
                                else
                                {
                                    sendMessage("The giveaway has to be closed first!");
                                }
                            }
                            else
                            {
                                sendMessage("No giveaway running!");
                            }
                        }
                        else if (args[0].Equals("type") && args.Length > 1)
                        {
                            if (!Giveaway.Started)
                            {
                                int type;
                                if (int.TryParse(args[1], out type) && type >= 1 && type <= 3)
                                {
                                    Program.Invoke(() =>
                                    {
                                        MainForm.Giveaway_TypeActive.Checked = (type == 1);
                                        MainForm.Giveaway_TypeKeyword.Checked = (type == 2);
                                        MainForm.Giveaway_TypeTickets.Checked = (type == 3);
                                    });
                                    sendMessage("Giveaway type changed!");
                                }
                            }
                            else
                            {
                                sendMessage("Can't change giveaway type while a giveaway is running!");
                            }
                        }
                        else if (args[0].Equals("start") || args[0].Equals("create") || args[0].Equals("run"))
                        {
                            if (!Giveaway.Started)
                            {
                                if (MainForm.Giveaway_TypeTickets.Checked)
                                {
                                    int ticketcost = 0, maxtickets = 1;
                                    if (args.Length > 1)
                                    {
                                        int.TryParse(args[1], out ticketcost);
                                        if (args.Length > 2)
                                        {
                                            int.TryParse(args[2], out maxtickets);
                                        }
                                    }
                                    if (ticketcost >= 0 && maxtickets > 0)
                                    {
                                        Giveaway.startGiveaway(ticketcost, maxtickets); ;
                                    }
                                    else
                                    {
                                        sendMessage("Ticket cost cannot be lower than 0 and max tickets cannot be lower than 1.");
                                    }
                                }
                                else
                                {
                                    Giveaway.startGiveaway();
                                }
                            }
                            else
                            {
                                sendMessage("A giveaway is already running.");
                            }
                        }
                        else if (args[0].Equals("close") || args[0].Equals("lock"))
                        {
                            if (Giveaway.Started)
                            {
                                if (Giveaway.Open)
                                {
                                    Giveaway.closeGiveaway();
                                }
                                else
                                {
                                    sendMessage("Entries to the giveaway has been closed already.");
                                }
                            }
                            else
                            {
                                sendMessage("A giveaway is not running.");
                            }
                        }
                        else if (args[0].Equals("open") || args[0].Equals("unlock"))
                        {
                            if (Giveaway.Started)
                            {
                                if (!Giveaway.Open)
                                {
                                    Giveaway.openGiveaway();
                                }
                                else
                                {
                                    sendMessage("Entries to the giveaway are already open.");
                                }
                            }
                            else
                            {
                                sendMessage("A giveaway is not running.");
                            }
                        }
                        else if (args[0].Equals("stop") || args[0].Equals("end"))
                        {
                            if (Giveaway.Started)
                            {
                                Giveaway.endGiveaway();
                            }
                            else
                            {
                                sendMessage("A giveaway is not running.");
                            }
                        }
                        else if (args[0].Equals("cancel") || args[0].Equals("abort"))
                        {
                            if (Giveaway.Started)
                            {
                                Giveaway.cancelGiveaway();
                            }
                        }
                    }
                }

                //REGULAR USER COMMANDS: !giveaway help
                /*if (args[0].Equals("help"))
                {
                    string sMessage = "In order to join the giveaway, you have to be active in chat ";
                    if (MainForm.Giveaway_MustFollowCheckBox.Checked)
                    {
                        if (!MainForm.Giveaway_MinCurrencyCheckBox.Checked)
                        {
                            sMessage = sMessage + "and follow the stream, ";
                        }
                        else
                        {
                            sMessage = sMessage + ", follow the stream and have " + MainForm.Giveaway_MinCurrency.Value + " " + currencyName + ", ";
                        }
                    }
                    else
                    {
                        sMessage = sMessage + "and have " + MainForm.Giveaway_MinCurrency.Value + " " + currencyName + ", ";
                    }
                    sMessage = sMessage + "the winner is selected from a list of viewers that were active within the last " + MainForm.Giveaway_ActiveUserTime.Value + " minutes";
                    if (MainForm.Giveaway_MustFollowCheckBox.Checked || MainForm.Giveaway_MinCurrencyCheckBox.Checked) sMessage = sMessage + " and comply the terms";
                    sMessage = sMessage + ".";
                    sendMessage(sMessage);
                }*/

                if ((args[0].Equals("buy") || args[0].Equals("join") || args[0].Equals("purchase") || args[0].Equals("ticket") || args[0].Equals("tickets")) && args.Length > 1)
                {
                    Command_Ticket(user, "!ticket", new string[] { args[1] });
                }
            }
            else
            {
                Command_Ticket(user, "!ticket", args);
            }
        }

        private static void Command_Ticket(string user, string cmd, string[] args)
        {
            if (Giveaway.Started && (MainForm.Giveaway_TypeKeyword.Checked && (MainForm.Giveaway_CustomKeyword.Text == "" || cmd == "Custom") || MainForm.Giveaway_TypeTickets.Checked))
            {
                if (Giveaway.Open)
                {
                    if (MainForm.Giveaway_TypeKeyword.Checked)
                    {
                        if (!Giveaway.HasBoughtTickets(user))
                        {
                            user = user.ToLower();
                            lock (giveawayFalseEntries)
                            {
                                if (Giveaway.BuyTickets(user))
                                {
                                    if (giveawayFalseEntries.ContainsKey(user)) giveawayFalseEntries.Remove(user);
                                    giveawayQueue.Change(10000, Timeout.Infinite);
                                }
                                else
                                {
                                    if (!giveawayFalseEntries.ContainsKey(user)) giveawayFalseEntries.Add(user, 1);
                                    if (giveawayFalseEntries.Count == 1) giveawayQueue.Change(10000, Timeout.Infinite);
                                }
                            }
                        }
                        else
                        {
                            if (MainForm.Giveaway_WarnFalseEntries.Checked) warnUser(user, 1, 5, "Giveaway entries closed and/or is in the giveaway already", 0, false, true, MainForm.Giveaway_AnnounceWarnedEntries.Checked, 3);
                        }
                    }
                    else
                    {
                        if (args.Length > 0)
                        {
                            user = user.ToLower();
                            lock (giveawayFalseEntries)
                            {
                                int tickets;
                                if (int.TryParse(args[0], out tickets) && tickets > 0 && Giveaway.BuyTickets(user, tickets))
                                {
                                    if (giveawayFalseEntries.ContainsKey(user)) giveawayFalseEntries.Remove(user);
                                    giveawayQueue.Change(10000, Timeout.Infinite);
                                }
                                else
                                {
                                    if (!giveawayFalseEntries.ContainsKey(user)) giveawayFalseEntries.Add(user, tickets);
                                    if (giveawayFalseEntries.Count == 1) giveawayQueue.Change(10000, Timeout.Infinite);
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (MainForm.Giveaway_WarnFalseEntries.Checked) warnUser(user, 1, 5, "Giveaway entries closed and/or is in the giveaway already", 0, false, true, MainForm.Giveaway_AnnounceWarnedEntries.Checked, 3);
                }
            }
        }

        private static void Command_Currency(string user, string cmd, string[] args)
        {
            if (args.Length > 0)
            {
                if (args.Length == 1)
                {
                    if (args[0].Equals("top5"))
                    {
                        if (!MainForm.Currency_DisableCommand.Checked && Api.GetUnixTimeNow() - LastUsedCurrencyTop5 > 600 || Database.getUserLevel(user) >= 1)
                        {
                            LastUsedCurrencyTop5 = Api.GetUnixTimeNow();
                            int max = 5;
                            Dictionary<string, int> TopPoints = new Dictionary<string, int>();
                            //"SELECT * FROM table ORDER BY amount DESC LIMIT 5;"
                            List<string> users = new List<string>();
                            if (Database.DB != null)
                            {
                                using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + Database.table + " ORDER BY currency DESC LIMIT " + (max + IgnoredUsers.Count) + ";", Database.DB))
                                {
                                    using (SQLiteDataReader r = query.ExecuteReader())
                                    {
                                        while (r.Read())
                                        {
                                            string usr = r["user"].ToString().ToLower();
                                            if (!IgnoredUsers.Any(c => c.Equals(usr.ToLower())) && !TopPoints.ContainsKey(usr))
                                            {
                                                TopPoints.Add(usr, int.Parse(r["currency"].ToString()));
                                            }
                                        }
                                    }
                                }
                            }
                            else if (Database.MySqlDB != null)
                            {
                                using (MySqlConnection con = Database.MySqlDB.Clone())
                                {
                                    con.Open();
                                    using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + Database.table + " ORDER BY currency DESC LIMIT " + (max + IgnoredUsers.Count) + ";", con))
                                    {
                                        using (MySqlDataReader r = query.ExecuteReader())
                                        {
                                            while (r.Read())
                                            {
                                                string usr = r["user"].ToString().ToLower();
                                                if (!IgnoredUsers.Any(c => c.Equals(usr.ToLower())) && !TopPoints.ContainsKey(usr))
                                                {
                                                    TopPoints.Add(usr, int.Parse(r["currency"].ToString()));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            //TopPoints = TopPoints.OrderByDescending(key => key.Value).ToDictionary(item => item.Key, item => item.Value);
                            if (TopPoints.Count > 0)
                            {
                                string output = "";
                                if (TopPoints.Count < max)
                                {
                                    max = TopPoints.Count;
                                }
                                for (int i = 0; i < max; i++)
                                {
                                    output += Api.GetDisplayName(TopPoints.ElementAt(i).Key) + " (" + Database.getTimeWatched(TopPoints.ElementAt(i).Key).ToString(@"d\d\ hh\h\ mm\m") + ") - " + TopPoints.ElementAt(i).Value + ", ";
                                }
                                sendMessage("The " + max + " users with the most " + currencyName + " are: " + output.Substring(0, output.Length - 2) + ".");
                            }
                            else
                            {
                                sendMessage("An error has occoured while looking for the 5 users with the most " + currencyName + "! Try again later.");
                            }
                        }
                    }
                    else if ((args[0].Equals("lock") || args[0].Equals("disable")) && Database.getUserLevel(user) >= 2)
                    {
                        MainForm.Currency_DisableCommand.Checked = true;
                        sendMessage("The !" + currency + " command is now disabled.", user + " disabled the currency command.");
                    }
                    else if ((args[0].Equals("unlock") || args[0].Equals("enable")) && Database.getUserLevel(user) >= 2)
                    {
                        MainForm.Currency_DisableCommand.Checked = false;
                        sendMessage("The !" + currency + " command is now available to use.", user + " enabled the currency command.");
                    }
                    else if (args[0].Equals("clear") && Database.getUserLevel(user) >= 3)
                    {
                        foreach (string usr in Database.GetAllUsers())
                        {
                            Database.setCurrency(usr, 0);
                        }
                        sendMessage("Cleared all the " + currencyName + "!", user + " cleared all the " + currencyName + "!");
                    }
                    else
                    {
                        if (Database.getUserLevel(user) >= 1)
                        {
                            if (!args[0].Contains(","))
                            {
                                if (Database.userExists(args[0]))
                                {
                                    sendMessage("Mod check: " + Api.GetDisplayName(args[0], true) + " (" + Database.getTimeWatched(args[0]).ToString(@"d\d\ hh\h\ mm\m") + ")" + " has " + Database.checkCurrency(args[0]) + " " + currencyName);
                                }
                                else
                                {
                                    sendMessage("Mod check: " + Api.GetDisplayName(args[0]) + " is not a valid user.");
                                }
                            }
                            else
                            {
                                foreach (string usr in args[0].Split(','))
                                {
                                    addToLookups(usr);
                                }
                            }
                        }
                    }
                }
                else if (args.Length >= 2 && Database.getUserLevel(user) >= 3)
                {
                    /////////////MOD ADD CURRENCY//////////////
                    if (args[0].Equals("add"))
                    {
                        int amount;
                        if (int.TryParse(args[1], out amount) && args.Length >= 3)
                        {
                            if (args[2].Equals("all"))
                            {
                                foreach (string usr in Database.GetAllUsers())
                                {
                                    Database.addCurrency(usr, amount);
                                }
                                sendMessage("Added " + amount + " " + currencyName + " to everyone.", user + " added " + amount + " " + currencyName + " to everyone.");
                            }
                            else if (args[2].Equals("online"))
                            {
                                foreach (string usr in ActiveUsers.Keys)
                                {
                                    Database.addCurrency(usr, amount);
                                }
                                sendMessage("Added " + amount + " " + currencyName + " to online users.", user + " added " + amount + " " + currencyName + " to online users.");
                            }
                            else
                            {
                                Database.addCurrency(args[2], amount);
                                sendMessage("Added " + amount + " " + currencyName + " to " + Api.GetDisplayName(args[2]), user + " added " + amount + " " + currencyName + " to " + Api.GetDisplayName(args[2]));
                            }
                        }
                    }
                    else if (args[0].Equals("set"))
                    {
                        int amount;
                        if (int.TryParse(args[1], out amount) && args.Length >= 3)
                        {
                            if (args[2].Equals("all"))
                            {
                                foreach (string usr in Database.GetAllUsers())
                                {
                                    Database.setCurrency(usr, amount);
                                }
                                sendMessage("Set everyone's " + currencyName + " to " + amount + ".", user + " set everyone's " + currencyName + " to " + amount + ".");
                            }
                            else if (args[2].Equals("online"))
                            {
                                foreach (string usr in ActiveUsers.Keys)
                                {
                                    Database.setCurrency(usr, amount);
                                }
                                sendMessage("Set online users's " + currencyName + " to " + amount + ".", user + " set online users's " + currencyName + " to " + amount + ".");
                            }
                            else
                            {
                                Database.setCurrency(args[2], amount);
                                sendMessage("Set " + args[2].ToLower() + "'s " + currencyName + " to " + amount + ".", user + " set " + args[2].ToLower() + "'s " + currencyName + " to " + amount + ".");
                            }
                        }
                    }

                    ////////////MOD REMOVE CURRENCY////////////////
                    else if (args[0].Equals("remove"))
                    {
                        int amount;
                        if (args[1] != null && int.TryParse(args[1], out amount) && args.Length >= 3)
                        {

                            if (args[2].Equals("all"))
                            {
                                foreach (string usr in Database.GetAllUsers())
                                {
                                    Database.removeCurrency(usr, amount);
                                }
                                sendMessage("Removed " + amount + " " + currencyName + " from everyone.", user + " removed " + amount + " " + currencyName + " from everyone.");
                            }
                            else if (args[2].Equals("online"))
                            {
                                foreach (string usr in ActiveUsers.Keys)
                                {
                                    Database.removeCurrency(usr, amount);
                                }
                                sendMessage("Removed " + amount + " " + currencyName + " from online users.", user + " removed " + amount + " " + currencyName + " from online users.");
                            }
                            else
                            {
                                Database.removeCurrency(args[2], amount);
                                sendMessage("Removed " + amount + " " + currencyName + " from " + args[2].ToLower(), user + " removed " + amount + " " + currencyName + " from " + args[2].ToLower());
                            }

                        }
                    }
                }
            }
            else
            {
                if (MainForm.Currency_DisableCommand.Checked && Database.getUserLevel(user) == 0 && Api.GetUnixTimeNow() - LastCurrencyDisabledAnnounce > 600)
                {
                    LastCurrencyDisabledAnnounce = Api.GetUnixTimeNow();
                    sendMessage("The !" + currency + " command is disabled, you may politely ask a mod to check your " + currencyName + " for you.");
                }
                if (!MainForm.Currency_DisableCommand.Checked || Database.getUserLevel(user) >= 1)
                {
                    addToLookups(user);
                }
            }
        }

        private static void Command_Gamble(string user, string cmd, string[] args)
        {
            if (args[0].Equals("open") && args.Length >= 4)
            {
                if (!Pool.Running)
                {
                    int maxBet;
                    if (int.TryParse(args[1], out maxBet))
                    {
                        if (maxBet > 0)
                        {
                            List<string> Options = Pool.buildBetOptions(args);
                            if (Options.Count > 1)
                            {
                                Pool.CreatePool(maxBet, Options);
                                sendMessage("New Betting Pool opened!  Max bet = " + maxBet + " " + currencyName);
                                string temp = "Betting open for: ";
                                for (int i = 0; i < Pool.options.Count; i++)
                                {
                                    temp += "(" + (i + 1).ToString() + ") " + Pool.options[i];
                                    if (i + 1 < Pool.options.Count)
                                    {
                                        temp += ", ";
                                    }
                                }
                                sendMessage(temp + ".");
                                sendMessage("Bet by typing \"!bet 50 option1name\" to bet 50 " + currencyName + " on option 1, \"!bet 25 option2name\" to bet 25 " + currencyName + " on option 2, etc. You can also bet with \"!bet 10 #OptionNumber\"");
                            }
                            else
                            {
                                sendMessage("You need at least two betting options in order to start a betting pool!");
                            }
                        }
                        else
                        {
                            sendMessage("Max bet can not be lower than 1!");
                        }
                    }
                    else
                    {
                        sendMessage("Invalid syntax. Open a betting pool with: !gamble open <maxBet> <option1>, <option2>, .... <optionN> (comma delimited options)");
                    }
                }
                else
                {
                    sendMessage("Betting Pool already opened. Close or cancel the current one before starting a new one.");
                }
            }
            else if (args[0].Equals("close"))
            {
                if (Pool.Running)
                {
                    if (!Pool.Locked)
                    {
                        Pool.Locked = true;
                        sendMessage("Bets locked in. Good luck everyone!");
                        string temp = "The following options were open for betting: ";
                        for (int i = 0; i < Pool.options.Count; i++)
                        {
                            temp += "(" + (i + 1).ToString() + ") " + Pool.options[i] + " - " + Pool.getNumberOfBets(Pool.options[i]) + " bets (" + Pool.getTotalBetsOn(Pool.options[i]) + " " + currencyName + " bet)";
                            if (i + 1 < Pool.options.Count)
                            {
                                temp += ", ";
                            }
                        }
                        sendMessage(temp + ".");
                    }
                    else
                    {
                        sendMessage("Pool is already locked.");
                    }
                }
                else
                {
                    sendMessage("The betting pool is not running.");
                }
            }
            else if (args[0].Equals("winner") && args.Length >= 2)
            {
                if (Pool.Running && Pool.Locked)
                {
                    bool inQuote = false;
                    string option = "";
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i].StartsWith("\""))
                        {
                            inQuote = true;
                        }
                        if (!inQuote)
                        {
                            option = args[i];
                        }
                        if (inQuote)
                        {
                            option += args[i] + " ";
                        }
                        if (args[i].EndsWith("\""))
                        {
                            option = option.Substring(1, option.Length - 3);
                            inQuote = false;
                        }
                    }
                    if (option == args[1])
                    {
                        if (option.StartsWith("#"))
                        {
                            int optionnumber = 0;
                            if (int.TryParse(option.Substring(1), out optionnumber))
                            {
                                option = Pool.GetOptionFromNumber(optionnumber);
                            }
                        }
                    }
                    if (Pool.options.Contains(option))
                    {
                        Pool.closePool(option);
                        sendMessage("Betting Pool closed! A total of " + Pool.getTotalBets() + " " + currencyName + " were bet.");
                        string output = "Bets for:";
                        for (int i = 0; i < Pool.options.Count; i++)
                        {
                            double x = ((double)Pool.getTotalBetsOn(Pool.options[i]) / Pool.getTotalBets()) * 100;
                            output += " " + Pool.options[i] + " - " + Pool.getNumberOfBets(Pool.options[i]) + " (" + Math.Round(x) + "%);";
                            //Console.WriteLine("TESTING: getTotalBetsOn(" + i + ") = " + pool.getTotalBetsOn(i) + " --- getTotalBets() = " + pool.getTotalBets() + " ---  (double)betsOn(i)/totalBets() = " + (double)(pool.getTotalBetsOn(i) / pool.getTotalBets()) + " --- *100 = " + (double)(pool.getTotalBetsOn(i) / pool.getTotalBets()) * 100 + " --- Converted to a double = " + (double)((pool.getTotalBetsOn(i) / pool.getTotalBets()) * 100) + " --- Rounded double = " + Math.Round((double)((pool.getTotalBetsOn(i) / pool.getTotalBets()) * 100)));
                        }
                        sendMessage(output);
                        Dictionary<string, int> winners = Pool.getWinners();
                        output = "Winners:";
                        if (winners.Count == 0)
                        {
                            sendMessage(output + " No One!");
                        }
                        for (int i = 0; i < winners.Count; i++)
                        {
                            output += " " + winners.ElementAt(i).Key + " - " + winners.ElementAt(i).Value + " (Bet " + Pool.getBetAmount(winners.ElementAt(i).Key) + ")";
                            if (i == 0 && i == winners.Count - 1)
                            {
                                sendMessage(output);
                                output = "";
                            }
                            else if ((i != 0 && i % 10 == 0) || i == winners.Count - 1)
                            {
                                sendMessage(output);
                                output = "";
                            }
                        }

                        Pool.Clear();
                    }
                    else
                    {
                        sendMessage("The option you specified is not available in the current pool!");
                    }
                }
                else
                {
                    sendMessage("Betting pool must be open and bets must be locked before you can specify a winner, lock the bets by using \"!gamble close\".");
                    sendMessage("Close the betting pool by typing \"!gamble winner option1name\" if option 1 won, \"!gamble winner option2name\" for option 2, etc.");
                    sendMessage("You can type \"!bet help\" to get a list of the options as a reminder.");
                }
            }
            else if (args[0].Equals("cancel"))
            {
                if (Pool.Running)
                {
                    Pool.cancel();
                    sendMessage("Betting Pool canceled. All bets refunded");
                }
                else
                {
                    sendMessage("The betting pool is not running.");
                }
            }
        }

        private static void Command_Bet(string user, string cmd, string[] args)
        {
            if (Pool.Running)
            {
                if (args.Length > 0)
                {
                    int betAmount;
                    if (args[0].Equals("help"))
                    {
                        if (!Pool.Locked)
                        {
                            string temp = "Betting open for: ";
                            for (int i = 0; i < Pool.options.Count; i++)
                            {
                                temp += "(" + (i + 1).ToString() + ") " + Pool.options[i] + " - " + Pool.getNumberOfBets(Pool.options[i]) + " bets (" + Pool.getTotalBetsOn(Pool.options[i]) + " " + currencyName + " bet)";
                                if (i + 1 < Pool.options.Count)
                                {
                                    temp += ", ";
                                }
                            }
                            sendMessage(temp + ".");
                            sendMessage("Bet by typing \"!bet 50 option1name\" to bet 50 " + currencyName + " on option 1, \"bet 25 option2name\" to bet 25 " + currencyName + " on option 2, etc. You can also bet with \"!bet 10 #OptionNumber\".");
                        }
                        else
                        {
                            string temp = "The pool is now closed, the following options were open for betting: ";
                            for (int i = 0; i < Pool.options.Count; i++)
                            {
                                temp += "(" + (i + 1).ToString() + ") " + Pool.options[i] + " - " + Pool.getNumberOfBets(Pool.options[i]) + " bets (" + Pool.getTotalBetsOn(Pool.options[i]) + " " + currencyName + " bet)";
                                if (i + 1 < Pool.options.Count)
                                {
                                    temp += ", ";
                                }
                            }
                            sendMessage(temp + ".");
                        }
                    }
                    else if (!Pool.Locked && int.TryParse(args[0], out betAmount) && args.Length >= 2)
                    {
                        bool inQuote = false;
                        string option = "";
                        for (int i = 1; i < args.Length; i++)
                        {
                            if (args[i].StartsWith("\""))
                            {
                                inQuote = true;
                            }
                            if (!inQuote)
                            {
                                option = args[i];
                            }
                            if (inQuote)
                            {
                                option += args[i] + " ";
                            }
                            if (args[i].EndsWith("\""))
                            {
                                option = option.Substring(1, option.Length - 3);
                                inQuote = false;
                            }
                        }
                        if (option == args[1])
                        {
                            if (option.StartsWith("#"))
                            {
                                int optionnumber = 0;
                                if (int.TryParse(option.Substring(1), out optionnumber))
                                {
                                    option = Pool.GetOptionFromNumber(optionnumber);
                                    if (option == "")
                                    {
                                        sendMessage(user + " the option number does not exist");
                                        return;
                                    }
                                }
                            }
                        }
                        if (Pool.placeBet(user, option, betAmount))
                        {
                            sendMessage(user + " has placed a " + betAmount + " " + currencyName + " bet on \"" + option + "\"");
                        }
                    }
                }
                else
                {
                    if (Pool.isInPool(user))
                    {
                        sendMessage(user + ": " + Pool.getBetOn(user) + " (" + Pool.getBetAmount(user) + ")");
                    }
                }
            }
        }

        private static void Command_Auction(string user, string cmd, string[] args)
        {
            if (args.Length > 0)
            {
                if (args[0].Equals("open"))
                {
                    if (!Auction.Open)
                    {
                        Auction.Start();
                        sendMessage("Auction open!  Bid by typing \"!bid 50\", etc.");
                    }
                    else sendMessage("Auction already open. Close or cancel the previous one first.");
                }
                else if (args[0].Equals("close"))
                {
                    if (Auction.Open)
                    {
                        Tuple<string, int> winner = Auction.Close();
                        sendMessage("Auction closed!  Winner is: " + checkBtag(winner.Item1) + " (" + winner.Item2 + ")");
                    }
                    else sendMessage("No auction open.");
                }
                else if (args[0].Equals("cancel"))
                {
                    if (Auction.Open)
                    {
                        Auction.Cancel();
                        sendMessage("Auction cancelled. Bids refunded.");
                    }
                    else sendMessage("No auction open.");
                }
            }
        }

        private static void Command_Bid(string user, string cmd, string[] args)
        {
            if (args.Length > 0)
            {
                int amount;
                if (int.TryParse(args[0], out amount))
                {
                    if (Auction.Open)
                    {
                        if (Auction.placeBid(user, amount))
                        {
                            auctionLoop.Change(0, 30000);
                        }
                    }
                }
            }
        }

        private static void Command_BTag(string user, string cmd, string[] args)
        {
            if (args.Length > 0 && args[0].Contains("#"))
            {
                Database.setBtag(user, args[0]);
            }
        }

        private static void Command_ModBot(string user, string cmd, string[] args)
        {
            if (args.Length > 0)
            {
                if (Database.getUserLevel(user) >= 4)
                {
                    if (args.Length >= 2)
                    {
                        if (args[0].Equals("payout"))
                        {
                            int amount = 0;
                            if (int.TryParse(args[1], out amount) && amount >= MainForm.Currency_HandoutAmount.Minimum && amount <= MainForm.Currency_HandoutAmount.Maximum)
                            {
                                payout = amount;
                                sendMessage("New payout amount: " + amount);
                            }
                            else
                            {
                                sendMessage("Can't change payout amount. Must be a valid integer greater than " + (MainForm.Currency_HandoutAmount.Minimum - 1) + " and less than " + (MainForm.Currency_HandoutAmount.Maximum + 1));
                            }
                        }
                        else if (args[0].Equals("subpayout"))
                        {
                            int amount = 0;
                            if (int.TryParse(args[1], out amount) && amount >= MainForm.Currency_SubHandoutAmount.Minimum && amount <= MainForm.Currency_SubHandoutAmount.Maximum)
                            {
                                payout = amount;
                                sendMessage("New subscribers' payout amount: " + amount);
                            }
                            else
                            {
                                sendMessage("Can't change subscribers' payout amount. Must be a valid integer greater than " + (MainForm.Currency_SubHandoutAmount.Minimum - 1) + " and less than " + (MainForm.Currency_SubHandoutAmount.Maximum + 1));
                            }
                        }
                        else if (args[0].Equals("interval"))
                        {
                            int tempInterval = -1;
                            if (int.TryParse(args[1], out tempInterval) && tempInterval >= MainForm.Currency_HandoutInterval.Minimum && tempInterval <= MainForm.Currency_HandoutInterval.Maximum)
                            {
                                interval = tempInterval;
                                sendMessage("New currency payout interval: " + tempInterval);
                            }
                            else
                            {
                                sendMessage("Payout interval could not be changed. A valid interval must be greater than " + (MainForm.Currency_HandoutInterval.Minimum - 1) + " and less than " + (MainForm.Currency_HandoutInterval.Maximum + 1) + " minutes.");
                            }
                        }
                        else if (args[0].Equals("greeting") || args[0].Equals("greetings"))
                        {
                            if (args[1].Equals("on"))
                            {
                                greetingOn = true;
                                sendMessage("Greetings turned on.");
                            }
                            if (args[1].Equals("off"))
                            {
                                greetingOn = false;
                                sendMessage("Greetings turned off.");
                            }
                            if (args[1].Equals("set") && args.Length >= 3)
                            {
                                string sGreeting = "";
                                for (int i = 2; i < args.Length; i++)
                                {
                                    sGreeting += args[i] + " ";
                                }
                                greeting = sGreeting.Substring(0, sGreeting.Length - 1);
                                ini.SetValue("Settings", "Channel_Greeting", greeting);
                                sendMessage("Your new greeting is: " + greeting);
                            }
                        }
                        else if (args[0].Equals("addsub"))
                        {
                            if (Database.addSub(args[1]))
                            {
                                sendMessage(Api.GetDisplayName(args[1]) + " has been added as a subscriber.");
                            }
                            else
                            {
                                sendMessage(Api.GetDisplayName(args[1]) + " does not exist in the database. Have them type !<currency> then try again.");
                            }
                        }
                        else if (args[0].Equals("removesub") || args[0].Equals("delsub") || args[0].Equals("deletesub"))
                        {
                            if (Database.removeSub(args[1]))
                            {
                                sendMessage(Api.GetDisplayName(args[1]) + " has been removed from the subscribers list.");
                            }
                            else
                            {
                                sendMessage(Api.GetDisplayName(args[1]) + " does not exist in the database.");
                            }
                        }
                    }
                }
                if (Database.getUserLevel(user) >= 3)
                {
                    if (args.Length >= 2)
                    {
                        if (args[0].Equals("addmod"))
                        {
                            string tNick = Api.GetDisplayName(args[1]);
                            if (Database.userExists(tNick))
                            {
                                if (!tNick.Equals(admin, StringComparison.OrdinalIgnoreCase) && (Database.getUserLevel(tNick) < 3 && Database.getUserLevel(user) == 3 || Database.getUserLevel(user) >= 4))
                                {
                                    Database.setUserLevel(tNick, 1);
                                    sendMessage(tNick + " has been added as a bot moderator.", user + " added " + tNick + "as a bot moderator.");
                                }
                                else
                                {
                                    sendMessage("Cannot change broadcaster access level.");
                                }
                            }
                            else
                            {
                                sendMessage(tNick + " does not exist in the database. Have them type !<currency>, then try to add them again.");
                            }
                        }
                        if (args[0].Equals("addsuper"))
                        {
                            string tNick = Api.GetDisplayName(args[1]);
                            if (Database.userExists(tNick))
                            {
                                if (!tNick.Equals(admin, StringComparison.OrdinalIgnoreCase) && (Database.getUserLevel(tNick) < 3 && Database.getUserLevel(user) == 3 || Database.getUserLevel(user) >= 4))
                                {
                                    Database.setUserLevel(tNick, 2);
                                    sendMessage(tNick + " has been added as a bot Super Mod.", user + " added " + tNick + "as a super bot moderator.");
                                }
                                else
                                {
                                    sendMessage("Cannot change Broadcaster access level.");
                                }
                            }
                            else
                            {
                                sendMessage(tNick + " does not exist in the database. Have them type !<currency>, then try to add them again.");
                            }
                        }
                        if (args[0].Equals("demote"))
                        {
                            string tNick = Api.GetDisplayName(args[1]);
                            if (Database.userExists(tNick))
                            {
                                if (Database.getUserLevel(tNick) > 0)
                                {
                                    if (!tNick.Equals(admin, StringComparison.OrdinalIgnoreCase) && (Database.getUserLevel(tNick) < 3 && Database.getUserLevel(user) == 3 || Database.getUserLevel(user) >= 4))
                                    {
                                        Database.setUserLevel(tNick, Database.getUserLevel(tNick) - 1);
                                        sendMessage(tNick + " has been demoted.", user + "demoted " + tNick);
                                    }
                                    else
                                    {
                                        sendMessage("Cannot change Broadcaster access level.");
                                    }
                                }
                                else
                                {
                                    sendMessage("User is already Access Level 0. Cannot demote further.");
                                }
                            }
                            else
                            {
                                sendMessage(tNick + " does not exist in the database. Have them type !<currency>, then try again.");
                            }
                        }
                        if (args[0].Equals("setlevel") && args.Length >= 3)
                        {
                            string tNick = Api.GetDisplayName(args[1]);
                            if (Database.userExists(tNick))
                            {
                                if (!tNick.Equals(admin, StringComparison.OrdinalIgnoreCase) && (Database.getUserLevel(tNick) < 3 && Database.getUserLevel(user) == 3 || Database.getUserLevel(user) >= 4))
                                {
                                    int level;
                                    if (int.TryParse(args[2], out level) && level >= 0 && (level < 4 && Database.getUserLevel(user) >= 4 || level < 3))
                                    {
                                        Database.setUserLevel(tNick, level);
                                        sendMessage(tNick + " set to Access Level " + level, user + "set " + tNick + "'s Access Level to " + level);
                                    }
                                    else sendMessage("Level must be greater than or equal to 0, and less than 3 (0>=Level<3)");
                                }
                                else sendMessage("Cannot change that mod's access level.");
                            }
                            else
                            {
                                sendMessage(tNick + " does not exist in the database. Have them type !currency, then try again.");
                            }
                        }
                    }
                }
                if (Database.getUserLevel(user) >= 2)
                {
                    if ((args[0].Equals("addcommand") || args[0].Equals("addcmd") || args[0].Equals("addcom") || args[0].Equals("createcommand") || args[0].Equals("createcmd") || args[0].Equals("createcom")) && args.Length >= 4)
                    {
                        int level;
                        if (int.TryParse(args[1], out level) && level >= 0 && level <= 4)
                        {
                            string command = args[2];
                            if (!Commands.CheckCommand("", command))
                            {
                                string output = "";
                                for (int i = 3; i < args.Length; i++)
                                {
                                    output += args[i] + " ";
                                }
                                Database.Commands.addCommand(command, level, output.Substring(0, output.Length - 1));
                                sendMessage(command + " command added.", user + " added the command " + command);
                            }
                            else
                            {
                                sendMessage(command + " is already a command.");
                            }
                        }
                        else
                        {
                            sendMessage("Invalid syntax. Correct syntax is \"!modbot addcommand <access level> <command> <text you want to output>");
                        }
                    }
                    else if ((args[0].Equals("removecommand") || args[0].Equals("removecmd") || args[0].Equals("removecom") || args[0].Equals("delcmd") || args[0].Equals("delcom") || args[0].Equals("deletecmd") || args[0].Equals("deletecom") || args[0].Equals("deletecommand")) && args.Length >= 2)
                    {
                        string command = args[1];
                        if (Database.Commands.cmdExists(command))
                        {
                            Database.Commands.removeCommand(command);
                            sendMessage(command + " command removed.", user + " removed the command " + command);
                        }
                        else
                        {
                            sendMessage(command + " command does not exist.");
                        }
                    }
                }
                if (Database.getUserLevel(user) >= 1)
                {
                    if (args[0].Equals("commmandlist") || args[0].Equals("cmdlist") || args[0].Equals("commmandslist") || args[0].Equals("cmdslist") || args[0].Equals("cmds") || args[0].Equals("commands"))
                    {
                        string temp = Database.Commands.getList();
                        if (temp != "")
                        {
                            sendMessage("Current commands: " + temp);
                        }
                        else
                        {
                            sendMessage("No custom commands were added.");
                        }
                    }
                }
            }
            else
            {
                Command_BotInfo(user, cmd, args);
            }
        }

        private static void Command_Warn(string user, string command, string[] args)
        {
            if (args.Length > 0)
            {
                int max = 3, arg = 1, interval = 10;
                string name = args[0].ToLower(), reason = "";
                if (args.Length > 1 && int.TryParse(args[0], out max))
                {
                    arg = 2;
                    if (args.Length > 2 && int.TryParse(args[1], out interval))
                    {
                        name = args[2].ToLower();
                        arg = 3;
                    }
                    else
                    {
                        name = args[1].ToLower();
                        interval = 10;
                    }
                }
                else
                {
                    max = 3;
                    arg = 1;
                }

                for (int i = arg; i < args.Length; i++)
                {
                    reason += args[i] + " ";
                }

                if (warnUser(name, 1, interval, reason, max) == 1)
                {
                    sendMessage(Api.GetDisplayName(name) + ", you have been warned by " + user + (reason != "" ? " for " + reason : "") + " (Warning: " + Warnings[name] + "/" + max + ")", user + " has warned " + name + (reason != "" ? " for " + reason : ""));
                }
            }
        }

        private static void Command_Warnings(string user, string command, string[] args)
        {
            string name = user;
            user = user.ToLower();
            if (args.Length > 0)
            {
                name = Api.GetDisplayName(user = args[0].ToLower());
            }

            if (Warnings.ContainsKey(user))
            {
                sendMessage(name + " has " + Warnings[user] + " warnings.");
            }
            else
            {
                sendMessage(name + " has no warnings.");
            }
        }

        private static void Command_Uptime(string user, string cmd, string[] args)
        {
            if(IsStreaming)
            {
                TimeSpan t = TimeSpan.FromSeconds(Api.GetUnixTimeNow() - StreamStartTime);
                sendMessage("The stream is up for " + t.Days + " days, " + t.Hours + " hours, " + t.Minutes + " minutes and " + t.Seconds + " seconds.");
            }
            else
            {
                sendMessage("The stream is offline.");
            }
        }

        private static void Command_SongRequest(string user, string cmd, string[] args)
        {
            if (args.Length > 0)
            {
                YouTube.Song song = YouTube.GetSong(args[0]);
                song.requester = user;
                int response = YouTube.AddSong(song);
                if (response == 1)
                {
                    sendMessage(user + ", the song \"" + song.title + "\" (Duration: " + song.duration + ") has been added to the queue.");
                }
                else if (response == 0)
                {
                    sendMessage(user + ", the song you requested is too long.");
                }
                else if (response == -1)
                {
                    sendMessage(user + ", the song you requested is invalid.");
                }
            }
        }

        private static void Command_TestSong(string user, string cmd, string[] args)
        {
            YouTube.PlaySong();
        }

        private static void Command_SkipSong(string user, string cmd, string[] args)
        {
            YouTube.VoteSkip(user);
        }

        private static void Command_StopSong(string user, string cmd, string[] args)
        {
            YouTube.StopSong();
        }

        private static void Command_BotInfo(string user, string cmd, string[] args)
        {
            sendMessage("This channel is using CoMaNdO's modified version of ModBot (v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + "), downloadable from http://modbot.wordpress.com :)");
        }

        public static void addUserToList(string user, int time = -1, bool welcome = false)
        {
            if (time < 0) time = Api.GetUnixTimeNow();
            user = user.ToLower();
            lock (ActiveUsers)
            {
                if (!ActiveUsers.ContainsKey(user))
                {
                    ActiveUsers.Add(user, time);

                    if (user == nick.ToLower() || user == "jtv") return;

                    string name = Api.GetDisplayName(user);

                    if (time == 0 || !welcome) return;

                    Console.WriteLine(name + " joined.");

                    if (greetingOn && greeting != "")
                    {
                        sendMessage(greeting.Replace("@user", name));
                    }

                    if (user.Equals(MainForm.Giveaway_WinnerLabel.Text.ToLower()))
                    {
                        Program.Invoke(() =>
                        {
                            MainForm.Giveaway_WinnerChat.SelectionColor = Color.Green;
                            MainForm.Giveaway_WinnerChat.SelectionFont = new Font("Segoe Print", 7, FontStyle.Bold);
                            MainForm.Giveaway_WinnerChat.SelectedText = name + " has joined the channel.\r\n";
                        });
                    }
                }
                else
                {
                    ActiveUsers[user] = time;
                }
            }
        }

        public static bool IsUserInList(string user)
        {
            user = user.ToLower();
            lock (ActiveUsers)
            {
                if (ActiveUsers.ContainsKey(user))
                {
                    return true;
                }
            }
            return false;
        }

        public static void removeUserFromList(string user)
        {
            user = user.ToLower();
            lock (ActiveUsers)
            {
                if (ActiveUsers.ContainsKey(user))
                {
                    ActiveUsers.Remove(user);
                }
            }
        }

        public static void buildUserList(bool justjoined = true)
        {
            //sendRaw("WHO " + channel);
            Thread thread = new Thread(() =>
            {
                sendMessage("/mods", "", false, false);

                using (WebClient w = new WebClient())
                {
                    string json_data = "";
                    try
                    {
                        json_data = w.DownloadString("http://tmi.twitch.tv/group/user/" + channel.Substring(1) + "/chatters");
                        if (json_data.Replace("\"", "") != "")
                        {
                            JObject stream = JObject.Parse(JObject.Parse(json_data)["chatters"].ToString());
                            List<string> lUsers = (stream["moderators"].ToString().Replace(" ", "").Replace("\"", "").Replace("\r\n", "").Replace("[", "").Replace("]", "") + "," + stream["staff"].ToString().Replace(" ", "").Replace("\"", "").Replace("\r\n", "").Replace("[", "").Replace("]", "") + "," + stream["admins"].ToString().Replace(" ", "").Replace("\"", "").Replace("\r\n", "").Replace("[", "").Replace("]", "") + "," + stream["viewers"].ToString().Replace(" ", "").Replace("\"", "").Replace("\r\n", "").Replace("[", "").Replace("]", "")).Split(',').ToList();
                            lock (ActiveUsers)
                            {
                                List<string> Delete = new List<string>();
                                string Leaves = "", Joins = "";

                                foreach (string sUser in ActiveUsers.Keys)
                                {
                                    string user = sUser.ToLower();

                                    if (!IgnoredUsers.Contains(user) && !lUsers.Contains(user)) Delete.Add(user);
                                }

                                foreach (string user in Delete)
                                {
                                    removeUserFromList(user);

                                    string name = Api.GetDisplayName(user);

                                    Leaves += (Leaves != "" ? ", " : "") + name;

                                    if (user.Equals(MainForm.Giveaway_WinnerLabel.Text.ToLower()))
                                    {
                                        Program.Invoke(() =>
                                        {
                                            MainForm.Giveaway_WinnerTimerLabel.Text = "The winner left!";
                                            MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.FromArgb(255, 0, 0);

                                            MainForm.Giveaway_WinnerChat.SelectionColor = Color.Red;
                                            MainForm.Giveaway_WinnerChat.SelectionFont = new Font("Segoe Print", 7, FontStyle.Bold);
                                            MainForm.Giveaway_WinnerChat.SelectedText = name + " has left the channel.\r\n";
                                        });
                                    }
                                }

                                if (Leaves != "") Console.WriteLine(Leaves + " left.");

                                foreach (string sUser in lUsers)
                                {
                                    string user = sUser.ToLower();

                                    if (user != "" && !ActiveUsers.ContainsKey(user))
                                    {
                                        addUserToList(user, justjoined ? -1 : 0);

                                        if (user == nick.ToLower() || user == "jtv") return;

                                        string name = Api.GetDisplayName(user);

                                        if (justjoined)
                                        {
                                            Joins += (Joins != "" ? ", " : "") + name;

                                            if (user.Equals(MainForm.Giveaway_WinnerLabel.Text.ToLower()))
                                            {
                                                Program.Invoke(() =>
                                                {
                                                    MainForm.Giveaway_WinnerChat.SelectionColor = Color.Green;
                                                    MainForm.Giveaway_WinnerChat.SelectionFont = new Font("Segoe Print", 7, FontStyle.Bold);
                                                    MainForm.Giveaway_WinnerChat.SelectedText = name + " has joined the channel.\r\n";
                                                });
                                            }
                                        }
                                    }
                                }

                                if (Joins != "")
                                {
                                    Console.WriteLine(Joins + " joined.");

                                    if (greetingOn && greeting != "")
                                    {
                                        sendMessage(greeting.Replace("@user", Joins));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Api.LogError("*************Error Message (via buildUserList()): " + DateTime.Now + "*********************************\r\nUnable to connect to Twitch API to build the user list.\r\n" + e + "\r\n");
                    }
                }
            });
            Threads.Add(thread);
            thread.Name = "User updating";
            thread.Start();
            thread.Join();
            if (Threads.Contains(thread)) Threads.Remove(thread);
        }

        private static string getUser(string message)
        {
            return message.Split('!')[0].Substring(1).ToLower();
        }

        public static bool sendRaw(string message)
        {
            if (irc == null || write == null) return false;

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    write.WriteLine(message);
                    return true;
                }
                catch (Exception)
                {
                    if (attempt == 5)
                    {
                        Console.Clear();
                        //Console.WriteLine("Can't send data. Attempt: " + attempt);
                        Console.WriteLine("Disconnected. Attempting to reconnect.");
                        irc.Close();
                        //Flush();
                        Reconnecting = true;
                        Connect();
                    }
                }
            }
            return false;
        }

        public static bool sendMessage(string message, string log = "", bool logtoconsole = true, bool useaction = true)
        {
            if (irc == null) return false;

            if (log != "")
            {
                Commands.Log(log);
            }
            if (logtoconsole)
            {
                Console.WriteLine(nick + ": " + message);
            }
            return sendRaw("PRIVMSG " + channel + " :" + (useaction ? "/me " : "") + message);
        }

        public static int warnUser(string user, int add = 1, int lengthrate = 5, string reason = "", int max = 3, bool announcewarns = true, bool console = true, bool chat = true, int limit = 0)
        {
            string name = Api.GetDisplayName(user = user.ToLower());
            if (ActiveUsers.ContainsKey(user) && !Moderators.Contains(user) && !IgnoredUsers.Contains(user) && Database.getUserLevel(user) == 0)
            {
                lock (Warnings)
                {
                    if (Warnings.Count == 0)
                    {
                        warningsRemoval.Change(900000, 900000);
                    }

                    if (!Warnings.ContainsKey(user))
                    {
                        Warnings.Add(user, add);
                    }
                    else
                    {
                        Warnings[user] += add;
                    }
                    if (Warnings[user] > max)
                    {
                        int multiplier = Warnings[user];
                        if (limit > 0 && multiplier > limit) multiplier = limit;
                        timeoutUser(name, multiplier * lengthrate, (reason != "" ? reason + " " : "") + (announcewarns ? "after " + max + " warnings." : ""), console, chat);

                        return 2;
                    }
                    return 1;
                }
            }
            return 0;
        }

        public static bool timeoutUser(string user, int interval = 10, string reason = "", bool console = true, bool chat = true)
        {
            user = user.ToLower();
            if (!ActiveUsers.ContainsKey(user.ToLower()) || Moderators.Contains(user.ToLower()) || IgnoredUsers.Contains(user) || Database.getUserLevel(user) > 0 || !IsModerator) return false;
            //sendRaw("PRIVMSG " + channel + " :/timeout " + user + " " + interval);
            sendMessage("/timeout " + user + " " + interval, "", false, false);
            user = Api.GetDisplayName(user);
            if (chat)
            {
                sendMessage(user + " has been timed out for " + interval + " seconds." + (reason != "" ? " Reason: " + reason : ""), "", false);
            }
            if (console)
            {
                Console.WriteLine(user + " has been timed out for " + interval + " seconds." + (reason != "" ? " Reason: " + reason : ""));
            }
            TimeOutLog(user + " has been timed out for " + interval + " seconds." + (reason != "" ? " Reason: " + reason : ""));
            return true;
        }

        public static string checkBtag(string person)
        {
            //DB Lookup person to see if they have a battletag set
            string btag = Database.getBtag(person);
            //print(btag);
            return person + (btag != "" ? " [" + btag + "] " : "");
        }

        public static void addToLookups(string user)
        {
            lock (usersToLookup)
            {
                if (!usersToLookup.Contains(user))
                {
                    currencyQueue.Change(5000, Timeout.Infinite);
                    usersToLookup.Add(user);
                }
            }
        }

        private static void handleCurrencyQueue(Object state)
        {
            lock (usersToLookup)
            {
                if (usersToLookup.Count > 0)
                {
                    string output = currencyName + ":";
                    bool addComma = false;
                    foreach (string person in usersToLookup)
                    {
                        if (Database.userExists(person))
                        {
                            if (addComma)
                            {
                                output += ", ";
                            }

                            output += " " + Api.GetDisplayName(person) + " (" + Database.getTimeWatched(person).ToString(@"d\d\ hh\h\ mm\m") + ")" + " - " + Database.checkCurrency(person);
                            if (Pool.Running && Pool.isInPool(person))
                            {
                                output += " [" + Pool.getBetAmount(person) + "]";
                            }
                            if (Auction.Open && Auction.highBidder.Equals(person))
                            {
                                output += " {" + Auction.highBid + "}";
                            }
                            addComma = true;
                        }
                    }
                    usersToLookup.Clear();
                    sendMessage(output);
                }
            }
        }

        private static void auctionLoopHandler(Object state)
        {
            if (Auction.Open)
            {
                sendMessage(Api.GetDisplayName(Auction.highBidder) + " is currently winning, with a bid of " + Auction.highBid + "!");
            }
        }

        /*private static void Log(string output)
        {
            output = "[" + DateTime.Now + "] " + output;
            for (int attempts = 0; attempts < 10; attempts++)
            {
                try
                {
                    log.WriteLine(output);
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(250);
                }
            }
        }*/

        public static void TimeOutLog(string output)
        {
            output = "[" + DateTime.Now + "] " + output;
            for (int attempts = 0; attempts < 10; attempts++)
            {
                try
                {
                    using (StreamWriter log = new StreamWriter(@"Data\Logs\Timeouts.txt", true))
                    {
                        log.WriteLine(output);
                    }
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(250);
                }
            }
        }
    }
}