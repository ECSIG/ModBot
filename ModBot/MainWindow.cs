﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ModBot
{
    partial class MainWindow : CustomForm
    {
        private iniUtil ini = Program.ini;
        public Dictionary<string, Dictionary<string, string>> dSettings = new Dictionary<string, Dictionary<string, string>>();
        private bool bIgnoreUpdates, MetadataModified;
        public int iSettingsPresent = -2;
        public WindowsList Windows = new WindowsList();
        public Dictionary<Control, Dictionary<Control, Control>> TabConfigs = new Dictionary<Control, Dictionary<Control, Control>>();
        public Window CurrentWindow = null;
        public VScrollBar WindowsScroll;
        private List<Thread> Threads = new List<Thread>();
        private string AuthenticationScopes;
        public string ChannelTitle, ChannelGame;
        public Dictionary<string, string> SubscriptionRewards = new Dictionary<string, string>();

        public MainWindow()
        {
            InitializeComponent();

            Program.MainForm = this;

            /*foreach (Control ctrl in Controls)
            {
                ctrl.TabStop = false;
            }*/

            Text = "ModBot v" + (VersionLabel.Text = Assembly.GetExecutingAssembly().GetName().Version.ToString());
            VersionLabel.Text = "Version: " + VersionLabel.Text + "\r\nAPI Version: " + Program.ApiVersion;

            Thread thread = new Thread(() =>
            {
                string Hash;
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                {
                    using (FileStream stream = File.OpenRead(AppDomain.CurrentDomain.FriendlyName))
                    {
                        Hash = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                    }
                }

                while (true)
                {
                    using (WebClient w = new WebClient())
                    {
                        w.Proxy = null;
                        try
                        {
                            // Thanks to Illarvan for giving me some space on his server!
                            List<string> channels;
                            string url = "http://ddoguild.co.uk/modbot/streams/";
                            if (Irc.DetailsConfirmed)
                            {
                                int iViewers = Irc.ActiveUsers.Count;
                                foreach (string user in Irc.ActiveUsers.Keys)
                                {
                                    if (Irc.IgnoredUsers.Contains(user.ToLower()))
                                    {
                                        iViewers--;
                                    }
                                }
                                url = "http://ddoguild.co.uk/modbot/streams/?channel=" + Irc.channel.Substring(1) + "&bot=" + Irc.nick + "&hash=" + Hash + "&version=" + Assembly.GetExecutingAssembly().GetName().Version + "&viewers=" + iViewers + "&date=" + new DateTime(2000, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddDays(Assembly.GetExecutingAssembly().GetName().Version.Build).AddSeconds(Assembly.GetExecutingAssembly().GetName().Version.Revision * 2).ToString("M/dd/yyyy hh:mm:ss tt") + "&status=" + (Irc.IsStreaming ? "2" : "1") + "&title=" + ChannelTitle + "&game=" + ChannelGame;
                            }

                            channels = w.DownloadString(url).Replace("<pre>", "").Replace("</pre>", "").Split(Environment.NewLine.ToCharArray()).ToList();
                            Dictionary<Tuple<string, string, string, string, string>, Tuple<string, string, int>> Channels = new Dictionary<Tuple<string, string, string, string, string>, Tuple<string, string, int>>();
                            foreach (string channel in channels)
                            {
                                if (channel != "")
                                {
                                    JObject json = JObject.Parse(channel);
                                    int status = (int)json["Status"], updated = (int)json["Time"];
                                    string sStatus = "Disconnected";
                                    if (Api.GetUnixTimeNow() - updated < 300)
                                    {
                                        if (status == 2)
                                        {
                                            sStatus = "On air";
                                        }
                                        else
                                        {
                                            sStatus = "Off air";
                                        }
                                    }
                                    //Channels.Add(new Tuple<string, string, string, int, string>(JObject.Parse(w.DownloadString("https://api.twitch.tv/kraken/users/" + json["Channel"].ToString()))["display_name"].ToString(), sStatus, json["Version"].ToString(), int.Parse(json["Viewers"].ToString()), new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(updated).ToString()));
                                    Channels.Add(new Tuple<string, string, string, string, string>(json["Channel"].ToString(), json["Bot"].ToString(), sStatus, json["Version"].ToString(), new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(updated).ToLocalTime().ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + " " + CultureInfo.CurrentCulture.DateTimeFormat.LongTimePattern)), new Tuple<string, string, int>(json["Title"].ToString(), json["Game"].ToString(), int.Parse(json["Viewers"].ToString())));
                                }
                            }

                            Program.Invoke(() =>
                            {
                                foreach (Tuple<string, string, string, string, string> channel in Channels.Keys)
                                {
                                    bool Found = false;
                                    foreach (DataGridViewRow row in About_Users.Rows)
                                    {
                                        if (row.Cells["Channel"].Value.ToString() == channel.Item1 && row.Cells["Bot"].Value.ToString() == channel.Item2)
                                        {
                                            row.Cells["Status"].Value = channel.Item3;
                                            row.Cells["Version"].Value = channel.Item4;
                                            row.Cells["Updated"].Value = channel.Item5;
                                            row.Cells["Title"].Value = Channels[channel].Item1;
                                            row.Cells["Game"].Value = Channels[channel].Item2;
                                            row.Cells["Viewers"].Value = Channels[channel].Item3;
                                            Found = true;
                                            break;
                                        }
                                    }
                                    if (!Found)
                                    {
                                        About_Users.Rows.Add(channel.Item1, channel.Item2, Channels[channel].Item1, Channels[channel].Item2, channel.Item3, Channels[channel].Item3, channel.Item4, channel.Item5);
                                    }
                                }

                                int lastmonth = 0, onair = 0;
                                foreach (DataGridViewRow row in About_Users.Rows)
                                {
                                    DateTime time = DateTime.Parse(row.Cells["Updated"].Value.ToString());
                                    if (time.CompareTo(DateTime.Now.Subtract(TimeSpan.FromDays(30))) > -1) lastmonth++;
                                    //if (time.Year == DateTime.Now.Year && time.Month == DateTime.Now.Month) lastmonth++;
                                    if (row.Cells["Status"].Value.ToString() == "On air") onair++;
                                }
                                //About_UsersLabel.Text = "Other users (" + About_Users.Rows.Count + " total, " + lastmonth + " in the last 30 days, " + onair + " currently on air):";
                                About_UsersLabel.Text = "Other users (" + About_Users.Rows.Count + " total, " + lastmonth + " in the last month, " + onair + " currently on air):";

                                About_Users.Sort(About_Users.SortedColumn, About_Users.SortOrder == SortOrder.Ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending);
                            });
                        }
                        catch
                        {
                        }
                    }
                    Thread.Sleep(60000);
                }
            });
            thread.Name = "Status reporting";
            thread.Start();
            Threads.Add(thread);

            About_Users.Sort(About_Users.Columns["Status"], System.ComponentModel.ListSortDirection.Ascending);
            //About_Users.Columns["Version"].Visible = false;
            //About_Users.Columns["Updated"].Visible = false;
            //About_Users.Columns["Title"].Width += 120;
            //About_Users.Columns["Game"].Width += 80;

            /*new Thread(() =>
            {
                while(true)
                {
                    Console.WriteLine(YouTubePlayer.IsPlaying() + " " + YouTubePlayer.Playing);
                    Thread.Sleep(1000);
                }
            }).Start();*/

            /*thread = new Thread(() =>
            {
                AutoCompleteStringCollection Games = new AutoCompleteStringCollection();
                int count = 0, max = 100;
                List<string> games = new List<string>();
                using (WebClient w = new WebClient())
                {
                    w.Proxy = null;
                    try
                    {
                        while (count < max)
                        {
                            System.Xml.Linq.XElement xml = System.Xml.Linq.XElement.Parse(w.DownloadString("http://www.giantbomb.com/api/games/?api_key=1b38477055d29fcf3e5d5ca264ea20e25d457c31&limit=100&offset=" + count + "&sort=date_added:asc"));
                            max = int.Parse(xml.Element("number_of_total_results").Value);
                            foreach (System.Xml.Linq.XElement game in xml.Element("results").Elements())
                            {
                                if (!games.Contains(game.Element("name").Value))
                                {
                                    games.Add(game.Element("name").Value);
                                }
                            }
                            count += 100;
                            Console.WriteLine(count + "/" + max);
                        }
                    }
                    catch
                    {
                    }
                }
                BeginInvoke(() =>
                {
                    File.WriteAllLines(@"Data\Games.txt", games.ToArray());
                    Games.AddRange(games.ToArray());
                    ChannelGameBox.AutoCompleteCustomSource = Games;
                });
            });
            thread.Name = "Download games list";
            thread.Start();
            Threads.Add(thread);*/
            if (File.Exists(@"Data\Games.txt"))
            {
                AutoCompleteStringCollection Games = new AutoCompleteStringCollection();
                Games.AddRange(File.ReadAllLines(@"Data\Games.txt"));
                Channel_Game.AutoCompleteCustomSource = Games;
            }

            CenterSpacer(ConnectionLabel, ConnectionSpacer);
            CenterSpacer(CurrencyLabel, CurrencySpacer);
            CenterSpacer(SubscribersLabel, SubscribersSpacer);
            CenterSpacer(DonationsLabel, DonationsSpacer);
            CenterSpacer(HandoutLabel, HandoutSpacer);
            CenterSpacer(GiveawayTypeLabel, GiveawayTypeSpacer);
            CenterSpacer(GiveawayRulesLabel, GiveawayRulesSpacer, false, true);
            CenterSpacer(GiveawayBansLabel, GiveawayBansSpacer);
            CenterSpacer(GiveawayUsersLabel, GiveawayUsersSpacer);
            CenterSpacer(Spam_CWLLabel, Spam_CWLSpacer, false, true);
            CenterSpacer(MySQLLabel, MySQLSpacer, true, false);
            CenterSpacer(SubscribersLabel, SubscribersSpacer);
            CenterSpacer(SubscriptionsLabel, SubscriptionsSpacer);
            CenterSpacer(MessageTimersLabel, MessageTimersSpacer);

            Panel panel = new Panel();
            panel.Size = new Size(1, 1);
            panel.Location = new Point(GiveawayTypeSpacer.Location.X + GiveawayTypeSpacer.Size.Width - 1, GiveawayTypeSpacer.Location.Y + 9);
            GiveawayWindow.Controls.Add(panel);
            panel.BringToFront();
            panel = new Panel();
            panel.Size = new Size(1, 1);
            panel.Location = new Point(GiveawayBansSpacer.Location.X + GiveawayBansSpacer.Size.Width - 1, GiveawayBansSpacer.Location.Y + 9);
            GiveawayWindow.Controls.Add(panel);
            panel.BringToFront();

            // Todo : Create a method
            panel = new Panel();
            panel.Size = new Size(Bot_TokenButton.Size.Width, 1);
            panel.Location = new Point(Bot_TokenButton.Location.X, Bot_TokenButton.Location.Y);
            SettingsWindow.Controls.Add(panel);
            panel.BringToFront();
            panel = new Panel();
            panel.Size = new Size(Bot_TokenButton.Size.Width, 1);
            panel.Location = new Point(Bot_TokenButton.Location.X, Bot_TokenButton.Location.Y + Bot_TokenButton.Size.Height - 1);
            SettingsWindow.Controls.Add(panel);
            panel.BringToFront();
            panel = new Panel();
            panel.BackColor = Color.Black;
            panel.Size = new Size(Bot_TokenButton.Size.Width, 1);
            panel.Location = new Point(Bot_TokenButton.Location.X, Bot_TokenButton.Location.Y + 1);
            SettingsWindow.Controls.Add(panel);
            panel.BringToFront();
            panel = new Panel();
            panel.BackColor = Color.Black;
            panel.Size = new Size(Bot_TokenButton.Size.Width, 1);
            panel.Location = new Point(Bot_TokenButton.Location.X, Bot_TokenButton.Location.Y + Bot_TokenButton.Size.Height - 2);
            SettingsWindow.Controls.Add(panel);
            panel.BringToFront();

            panel = new Panel();
            panel.Size = new Size(Channel_TokenButton.Size.Width, 1);
            panel.Location = new Point(Channel_TokenButton.Location.X, Channel_TokenButton.Location.Y);
            SettingsWindow.Controls.Add(panel);
            panel.BringToFront();
            panel = new Panel();
            panel.Size = new Size(Channel_TokenButton.Size.Width, 1);
            panel.Location = new Point(Channel_TokenButton.Location.X, Channel_TokenButton.Location.Y + Channel_TokenButton.Size.Height - 1);
            SettingsWindow.Controls.Add(panel);
            panel.BringToFront();
            panel = new Panel();
            panel.BackColor = Color.Black;
            panel.Size = new Size(Channel_TokenButton.Size.Width, 1);
            panel.Location = new Point(Channel_TokenButton.Location.X, Channel_TokenButton.Location.Y + 1);
            SettingsWindow.Controls.Add(panel);
            panel.BringToFront();
            panel = new Panel();
            panel.BackColor = Color.Black;
            panel.Size = new Size(Channel_TokenButton.Size.Width, 1);
            panel.Location = new Point(Channel_TokenButton.Location.X, Channel_TokenButton.Location.Y + Channel_TokenButton.Size.Height - 2);
            SettingsWindow.Controls.Add(panel);
            panel.BringToFront();

            panel = new Panel();
            panel.Size = new Size(About_Users.Size.Width, 1);
            panel.Location = new Point(About_Users.Location.X, About_Users.Location.Y);
            AboutWindow.Controls.Add(panel);
            panel.BringToFront();
            panel = new Panel();
            panel.Size = new Size(1, About_Users.Size.Height);
            panel.Location = new Point(About_Users.Location.X + About_Users.Size.Width - 18, About_Users.Location.Y);
            AboutWindow.Controls.Add(panel);
            panel.BringToFront();
            About_UsersLabel.BringToFront();

            Windows.Add(new Window("Settings", SettingsWindow, false));
            Windows.Add(new Window("Channel", ChannelWindow));
            Windows.Add(new Window("Currency", CurrencyWindow));
            Windows.Add(new Window("Giveaway", GiveawayWindow));
            Windows.Add(new Window("Donations", DonationsWindow, true, false, false, true));
            Windows.Add(new Window("Spam Filter", SpamFilterWindow, true, true, false, false, "Spam\r\nFilter"));
            Windows.Add(new Window("About", AboutWindow, false));

            Dictionary<Control, Control> TabConfig = new Dictionary<Control, Control>();
            TabConfig.Add(this, Bot_Name);
            TabConfig.Add(Bot_Name, Bot_TokenButton);
            TabConfig.Add(Bot_TokenButton, Channel_Name);
            TabConfig.Add(Channel_Name, Channel_TokenButton);
            TabConfig.Add(Channel_TokenButton, Currency_HandoutInterval);
            TabConfig.Add(Currency_HandoutInterval, Currency_HandoutAmount);
            TabConfig.Add(Currency_HandoutAmount, Currency_Name);
            TabConfig.Add(Currency_Name, Currency_Command);
            TabConfig.Add(Currency_Command, Subscribers_Spreadsheet);
            TabConfig.Add(Subscribers_Spreadsheet, Currency_SubHandoutAmount);
            TabConfig.Add(Currency_SubHandoutAmount, Donations_ST_ClientId);
            TabConfig.Add(Donations_ST_ClientId, Donations_ST_Token);
            TabConfig.Add(Donations_ST_Token, ConnectButton);
            TabConfig.Add(ConnectButton, DisconnectButton);
            TabConfigs.Add(SettingsWindow, TabConfig);

            TabConfig = new Dictionary<Control, Control>();
            TabConfig.Add(this, Channel_Title);
            TabConfig.Add(Channel_Title, Channel_Game);
            TabConfig.Add(Channel_Game, Channel_UpdateTitleGame);
            TabConfig.Add(Channel_UpdateTitleGame, Channel_UseSteam);
            TabConfig.Add(Channel_UseSteam, Channel_ViewersChange);
            TabConfig.Add(Channel_ViewersChange, Channel_WelcomeSub);
            TabConfig.Add(Channel_WelcomeSub, Channel_SubscriptionRewards);
            TabConfigs.Add(ChannelWindow, TabConfig);

            // finish the rest

            int count = Windows.Count; // Amount of buttons that will be in view.
            int h = Height - 38; // Height that can be used.
            int minsize = 84; // Min size of each button.
            while (h / count < minsize) count--;
            if (count < Windows.Count)
            {
                h -= 24;
                while (h / count < minsize) count--;
            }

            foreach (CheckBox btn in Windows.Buttons)
            {
                btn.Size = new Size(100, h / count);
            }

            int y = -(h / count * count - Height + 38 + count < Windows.Count ? 24 : 0);
            while (y > 0)
            {
                int c = 0;
                foreach (CheckBox btn in Windows.Buttons)
                {
                    c++;
                    if (c > count || y == 0) break;
                    btn.Size = new Size(btn.Size.Width, btn.Size.Height + 1);
                    y--;
                }
            }

            y = 0;
            int btnc = 0;
            foreach (CheckBox btn in Windows.Buttons)
            {
                btnc++;
                if (btnc > count) break;
                y += btn.Size.Height;
            }
            while (y < h)
            {
                btnc = 0;
                foreach (CheckBox btn in Windows.Buttons)
                {
                    btnc++;
                    if (btnc > count || y >= h) break;
                    btn.Size = new Size(btn.Size.Width, btn.Size.Height + 1);
                    y++;
                }
            }

            int currenty = 30;
            foreach (CheckBox btn in Windows.Buttons)
            {
                btn.Location = new Point(8, currenty);
                currenty += btn.Size.Height;
            }

            WindowsScroll = new VScrollBar();
            WindowsScroll.Size = new Size(102, 24);
            WindowsScroll.Location = new Point(7, Height - 32);
            WindowsScroll.Visible = (count < Windows.Count);
            WindowsScroll.Maximum = Windows.Count - count + 9;
            WindowsScroll.Scroll += (object sender, ScrollEventArgs e) =>
            {
                /*currenty = 30 - h / count * e.OldValue; // ToDo: fix animation cutting
                foreach (CheckBox btn in Windows.Keys)
                {
                    btn.Location = new Point(8, currenty);
                    currenty += btn.Size.Height;
                }*/
                VScrollBar s = (VScrollBar)sender;
                s.Enabled = false;

                foreach (CheckBox btn in Windows.Buttons)
                {
                    btn.Size = new Size(100, h / count);
                }

                y = -(h / count * count - Height + 38 + count < Windows.Count ? 24 : 0);
                while (y > 0)
                {
                    int c = 0;
                    foreach (CheckBox btn in Windows.Buttons)
                    {
                        c++;
                        if (c - 1 < e.NewValue) continue;
                        if (c > count || y == 0) break;
                        btn.Size = new Size(btn.Size.Width, btn.Size.Height + 1);
                        y--;
                    }
                }

                y = 0;
                btnc = 0;
                foreach (CheckBox btn in Windows.Buttons)
                {
                    btnc++;
                    if (btnc - 1 < e.NewValue) continue;
                    if (btnc > count + e.NewValue) break;
                    y += btn.Size.Height;
                }
                while(y < h)
                {
                    btnc = 0;
                    foreach (CheckBox btn in Windows.Buttons)
                    {
                        btnc++;
                        if (btnc - 1 < e.NewValue) continue;
                        if (btnc > count + e.NewValue || y >= h) break;
                        btn.Size = new Size(btn.Size.Width, btn.Size.Height + 1);
                        y++;
                    }
                }

                new Thread(() =>
                {
                    while (30 - h / count * e.NewValue != Windows.Buttons[0].Location.Y)
                    {
                        if (e.NewValue != WindowsScroll.Value) break;
                        foreach (CheckBox btn in Windows.Buttons)
                        {
                            if (e.NewValue != WindowsScroll.Value) break;
                            Program.Invoke(() =>
                            {
                                btn.Location = new Point(8, btn.Location.Y + (30 - h / count * e.NewValue > Windows.Buttons[0].Location.Y ? 1 : -1));
                            });
                        }
                        Thread.Sleep(1);
                    }

                    Program.Invoke(() =>
                    {
                        if (e.NewValue == WindowsScroll.Value) // Make sure nothing is messed up.
                        {
                            currenty = 30 - h / count * e.NewValue;
                            foreach (CheckBox btn in Windows.Buttons)
                            {
                                btn.Location = new Point(8, currenty);
                                currenty += btn.Size.Height;
                            }

                            foreach (CheckBox btn in Windows.Buttons)
                            {
                                btn.Size = new Size(100, h / count);
                            }

                            y = -(h / count * count - Height + 38 + count < Windows.Count ? 24 : 0);
                            while (y > 0)
                            {
                                int c = 0;
                                foreach (CheckBox btn in Windows.Buttons)
                                {
                                    c++;
                                    if (c - 1 < e.NewValue) continue;
                                    if (c > count || y == 0) break;
                                    btn.Size = new Size(btn.Size.Width, btn.Size.Height + 1);
                                    y--;
                                }
                            }

                            y = 0;
                            btnc = 0;
                            foreach (CheckBox btn in Windows.Buttons)
                            {
                                btnc++;
                                if (btnc - 1 < e.NewValue) continue;
                                if (btnc > count + e.NewValue) break;
                                y += btn.Size.Height;
                            }
                            while (y < h)
                            {
                                btnc = 0;
                                foreach (CheckBox btn in Windows.Buttons)
                                {
                                    btnc++;
                                    if (btnc - 1 < e.NewValue) continue;
                                    if (btnc > count + e.NewValue || y >= h) break;
                                    btn.Size = new Size(btn.Size.Width, btn.Size.Height + 1);
                                    y++;
                                }
                            }
                        }

                        s.Enabled = true;
                    });
                }).Start();
            };
            Controls.Add(WindowsScroll);
            WindowsScroll.BringToFront();

            CurrentWindow = Windows.FromControl(SettingsWindow);
            SettingsWindow.BringToFront();

            SettingsErrorLabel.Text = "";

            bIgnoreUpdates = true;

            ini.SetValue("Settings", "BOT_Name", Bot_Name.Text = ini.GetValue("Settings", "BOT_Name", "ModBot"));
            ini.SetValue("Settings", "BOT_Password", Bot_Token.Text = ini.GetValue("Settings", "BOT_Password", ""));

            ini.SetValue("Settings", "Channel_Name", Channel_Name.Text = ini.GetValue("Settings", "Channel_Name", "ModChannel"));
            ini.SetValue("Settings", "Channel_Token", Channel_Token.Text = ini.GetValue("Settings", "Channel_Token", ""));
            ini.SetValue("Settings", "Channel_SteamID64", Channel_SteamID64.Text = ini.GetValue("Settings", "Channel_SteamID64", "SteamID64"));
            ini.SetValue("Settings", "Channel_ViewersChange", (Channel_ViewersChange.Checked = (ini.GetValue("Settings", "Channel_ViewersChange", "0") == "1")) ? "1" : "0");
            int variable = Convert.ToInt32(ini.GetValue("Settings", "Channel_ViewersChangeInterval", "5"));
            if (variable > Channel_ViewersChangeInterval.Maximum || variable < Channel_ViewersChangeInterval.Minimum)
            {
                variable = 5;
            }
            ini.SetValue("Settings", "Channel_ViewersChangeInterval", (Channel_ViewersChangeInterval.Value = variable).ToString());
            variable = Convert.ToInt32(ini.GetValue("Settings", "Channel_ViewersChangeRate", "10"));
            if (variable > Channel_ViewersChangeRate.Maximum || variable < Channel_ViewersChangeRate.Minimum)
            {
                variable = 10;
            }
            ini.SetValue("Settings", "Channel_ViewersChangeRate", (Channel_ViewersChangeRate.Value = variable).ToString());
            ini.SetValue("Settings", "Channel_ViewersChangeMessage", Channel_ViewersChangeMessage.Text = ini.GetValue("Settings", "Channel_ViewersChangeMessage", "New viewers remember to follow the channel!"));
            ini.SetValue("Settings", "Channel_WelcomeSub", (Channel_WelcomeSub.Checked = (ini.GetValue("Settings", "Channel_WelcomeSub", "0") == "1")) ? "1" : "0");
            ini.SetValue("Settings", "Channel_WelcomeSubMessage", Channel_WelcomeSubMessage.Text = ini.GetValue("Settings", "Channel_WelcomeSubMessage", "Welcome to the team @user!"));
            ini.SetValue("Settings", "Channel_SubscriptionRewards", (Channel_SubscriptionRewards.Checked = (ini.GetValue("Settings", "Channel_SubscriptionRewards", "0") == "1")) ? "1" : "0");
            if (!Directory.Exists(@"Data\Subscriptions")) Directory.CreateDirectory(@"Data\Subscriptions");
            if (File.Exists(@"Data\Subscriptions\Rewards.txt"))
            {
                foreach (string line in File.ReadAllLines(@"Data\Subscriptions\Rewards.txt"))
                {
                    string[] reward = line.Split(';');
                    if (reward.Length > 1) Channel_SubscriptionRewardsList.Rows.Add(reward[0], reward[1]);
                }
            }
            Channel_SubscriptionRewardsList.CellValueChanged += new DataGridViewCellEventHandler(Channel_SubscriptionRewardsList_Changed);
            Channel_SubscriptionRewardsList.RowsAdded += new DataGridViewRowsAddedEventHandler(Channel_SubscriptionRewardsList_Changed);
            Channel_SubscriptionRewardsList.RowsRemoved += new DataGridViewRowsRemovedEventHandler(Channel_SubscriptionRewardsList_Changed);

            ini.SetValue("Settings", "Currency_Name", Currency_Name.Text = ini.GetValue("Settings", "Currency_Name", "Mod Coins"));
            ini.SetValue("Settings", "Currency_Command", Currency_Command.Text = ini.GetValue("Settings", "Currency_Command", "ModCoins"));
            variable = Convert.ToInt32(ini.GetValue("Settings", "Currency_Interval", "5"));
            if (variable > Currency_HandoutInterval.Maximum || variable < Currency_HandoutInterval.Minimum)
            {
                variable = 5;
            }
            ini.SetValue("Settings", "Currency_Interval", (Currency_HandoutInterval.Value = variable).ToString());
            variable = Convert.ToInt32(ini.GetValue("Settings", "Currency_Payout", "1"));
            if (variable > Currency_HandoutAmount.Maximum || variable < Currency_HandoutAmount.Minimum)
            {
                variable = 1;
            }
            ini.SetValue("Settings", "Currency_Payout", (Currency_HandoutAmount.Value = variable).ToString());
            variable = Convert.ToInt32(ini.GetValue("Settings", "Currency_SubscriberPayout", "1"));
            if (variable > Currency_SubHandoutAmount.Maximum || variable < Currency_SubHandoutAmount.Minimum)
            {
                variable = 1;
            }
            ini.SetValue("Settings", "Currency_SubscriberPayout", (Currency_SubHandoutAmount.Value = variable).ToString());

            ini.SetValue("Settings", "Subsribers_URL", Subscribers_Spreadsheet.Text = ini.GetValue("Settings", "Subsribers_URL", ""));

            ini.SetValue("Settings", "Donations_ClientID", Donations_ST_ClientId.Text = ini.GetValue("Settings", "Donations_ClientID", ""));
            ini.SetValue("Settings", "Donations_Token", Donations_ST_Token.Text = ini.GetValue("Settings", "Donations_Token", ""));
            ini.SetValue("Settings", "Donations_UpdateTop", (UpdateTopDonorsCheckBox.Checked = (ini.GetValue("Settings", "Donations_UpdateTop", "0") == "1")) ? "1" : "0");
            variable = Convert.ToInt32(ini.GetValue("Settings", "Donations_Top_Limit", "20"));
            if (variable > TopDonorsLimit.Maximum || variable < TopDonorsLimit.Minimum)
            {
                variable = 20;
            }
            ini.SetValue("Settings", "Donations_Top_Limit", (TopDonorsLimit.Value = variable).ToString());
            ini.SetValue("Settings", "Donations_UpdateRecent", (UpdateRecentDonorsCheckBox.Checked = (ini.GetValue("Settings", "Donations_UpdateRecent", "0") == "1")) ? "1" : "0");
            variable = Convert.ToInt32(ini.GetValue("Settings", "Donations_Recent_Limit", "5"));
            if (variable > RecentDonorsLimit.Maximum || variable < RecentDonorsLimit.Minimum)
            {
                variable = 5;
            }
            ini.SetValue("Settings", "Donations_Recent_Limit", (RecentDonorsLimit.Value = variable).ToString());
            ini.SetValue("Settings", "Donations_UpdateLast", (UpdateLastDonorCheckBox.Checked = (ini.GetValue("Settings", "Donations_UpdateLast", "0") == "1")) ? "1" : "0");

            ini.SetValue("Settings", "Currency_DisableCommand", (Currency_DisableCommand.Checked = (ini.GetValue("Settings", "Currency_DisableCommand", "0") == "1")) ? "1" : "0");
            string sCurrencyHandout = ini.GetValue("Settings", "Currency_Handout", "0");
            ini.SetValue("Settings", "Currency_Handout", sCurrencyHandout);
            if (sCurrencyHandout.Equals("0"))
            {
                Currency_HandoutEveryone.Checked = true;
            }
            else if (sCurrencyHandout.Equals("1"))
            {
                Currency_HandoutActiveStream.Checked = true;
            }
            else if (sCurrencyHandout.Equals("2"))
            {
                Currency_HandoutActiveTime.Checked = true;
            }
            variable = Convert.ToInt32(ini.GetValue("Settings", "Currency_HandoutTime", "5"));
            if (variable > Currency_HandoutLastActive.Maximum || variable < Currency_HandoutLastActive.Minimum)
            {
                variable = 5;
            }
            ini.SetValue("Settings", "Currency_HandoutTime", (Currency_HandoutLastActive.Value = variable).ToString());

            ini.SetValue("Settings", "Spam_CWL", (Spam_CWL.Checked = (ini.GetValue("Settings", "Spam_CWL", "0") == "1")) ? "1" : "0");
            ini.SetValue("Settings", "Spam_CWLAnnounceTimeouts", (Spam_CWLAnnounceTimeouts.Checked = (ini.GetValue("Settings", "Spam_CWLAnnounceTimeouts", "0") == "1")) ? "1" : "0");
            ini.SetValue("Settings", "Spam_CWhiteList", Spam_CWLBox.Text = ini.GetValue("Settings", "Spam_CWhiteList", "abcdefghijklmnopqrstuvwxyz0123456789"));

            ini.SetValue("Settings", "Misc_ShowConsole", (Misc_ShowConsole.Checked = (ini.GetValue("Settings", "Misc_ShowConsole", "1") == "1")) ? "1" : "0");

            ini.SetValue("Settings", "MySQL_Host", MySQL_Host.Text = ini.GetValue("Settings", "MySQL_Host", ""));
            variable = Convert.ToInt32(ini.GetValue("Settings", "MySQL_Port", "3306"));
            if (variable > MySQL_Port.Maximum || variable < MySQL_Port.Minimum)
            {
                variable = 3306;
            }
            ini.SetValue("Settings", "MySQL_Port", (MySQL_Port.Value = variable).ToString());
            ini.SetValue("Settings", "MySQL_Database", MySQL_Database.Text = ini.GetValue("Settings", "MySQL_Database", ""));
            ini.SetValue("Settings", "MySQL_Username", MySQL_Username.Text = ini.GetValue("Settings", "MySQL_Username", ""));
            ini.SetValue("Settings", "MySQL_Password", MySQL_Password.Text = ini.GetValue("Settings", "MySQL_Password", ""));

            ini.SetValue("Settings", "Database_Table", Database_Table.Text = ini.GetValue("Settings", "Database_Table", ""));

            Channel_SubscriptionsDate.Value = DateTime.UtcNow;
            Channel_SubscriptionsDate.CustomFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + " " + CultureInfo.CurrentCulture.DateTimeFormat.LongTimePattern;
            //Channel_SubscriptionsDate.CustomFormat = "dddd, MMMM M, yyyy H:mm:ss";
            //Channel_SubscriptionsDate.CustomFormat = "d/MM/yy H:mm:ss";

            if (ConnectButton.Enabled) ConnectButton.Focus();

            bIgnoreUpdates = false;

            SongRequestPlayer.Navigated += YouTube.SongRequestPlayer_Navigated;
        }

        private void MainWindow_Load(object sender, EventArgs e)
        {
            Hide();
            Program.MainFormLoaded();

            //Update checking
            Thread thread = new Thread(() =>
            {
                try
                {
                    bool bUpdateNote = false;
                    while (!bUpdateNote)
                    {
                        Thread.Sleep(60000);
                        if (IsActivated)
                        {
                            bUpdateNote = true;
                            Program.Updates.CheckUpdate(false, true);
                        }
                        else
                        {
                            bUpdateNote = true;
                            bool bNote = false;
                            Activated += (object form, EventArgs eargs) =>
                            {
                                if (!bNote)
                                {
                                    bNote = true;
                                    Program.Updates.CheckUpdate(false, true);
                                }
                            };
                        }
                        Program.Updates.CheckUpdate(true, false);
                    }
                }
                catch
                {
                }
            });
            Threads.Add(thread);
            thread.Name = "Update checking";
            thread.Start();

            foreach (Control ctrl in Program.Windows.Keys) Program.AddToMainWindow(ctrl, Program.Windows[ctrl]);

            Program.LoadingScreen.Hide();
            Show();

            if (Program.args.Contains("-connect") && ConnectButton.Enabled)
            {
                ConnectButton.PerformClick();
            }
            else
            {
                Program.Updates.WelcomeMsg();
                Program.Updates.WhatsNew();
                if (!Program.args.Contains("-skipmotd")) Program.Updates.MsgOfTheDay();
            }

            /*Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            WindowState = FormWindowState.Maximized;
            //Scale(new SizeF(screen.Width / Size.Width, screen.Height / Size.Height));
            Scale(new SizeF(1.3F, 1.3F));
            FixBorders();*/

            //Program.AddToMainWindow("Test", new Form());
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            {
                if (keyData == Keys.Tab)
                {
                    if (TabConfigs.ContainsKey(CurrentWindow.Control))
                    {
                        if (!CurrentWindow.Control.ContainsFocus && TabConfigs[CurrentWindow.Control].ContainsKey(this))
                        {
                            TabConfigs[CurrentWindow.Control][this].Focus();
                        }
                        else if (CurrentWindow.Control.ContainsFocus)
                        {
                            foreach (Control ctrl in CurrentWindow.Control.Controls)
                            {
                                if(ctrl.Focused)
                                {
                                    if (TabConfigs[CurrentWindow.Control].ContainsKey(ctrl)) TabConfigs[CurrentWindow.Control][ctrl].Focus();
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (TabConfigs.ContainsKey(CurrentWindow.Control) && CurrentWindow.Control.ContainsFocus)
                    {
                        foreach (Control ctrl in CurrentWindow.Control.Controls)
                        {
                            if (ctrl.Focused && TabConfigs[CurrentWindow.Control].ContainsValue(ctrl))
                            {
                                foreach (Control ctrl2 in TabConfigs[CurrentWindow.Control].Keys)
                                {
                                    if (ctrl == TabConfigs[CurrentWindow.Control][ctrl2])
                                    {
                                        if(ctrl2 != this) ctrl2.Focus();
                                        break;
                                    }
                                }
                                break;
                            }
                        } 
                    }
                }
                return true;
            }

            if (keyData == (Keys.Alt | Keys.F4) || keyData == (Keys.Control | Keys.W))
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CenterSpacer(Label label, GroupBox spacer, bool hideleft = false, bool hideright = false)
        {
            label.Location = new Point(spacer.Location.X + spacer.Size.Width / 2 - label.Size.Width / 2, spacer.Location.Y);
            if (hideleft)
            {
                Panel panel = new Panel();
                panel.Size = new Size(1, 2);
                panel.Location = new Point(spacer.Location.X, spacer.Location.Y + 9);
                spacer.Parent.Controls.Add(panel);
                panel.BringToFront();
            }
            if (hideright)
            {
                Panel panel = new Panel();
                panel.Size = new Size(1, 2);
                panel.Location = new Point(spacer.Location.X + spacer.Size.Width - 1, spacer.Location.Y + 9);
                spacer.Parent.Controls.Add(panel);
                panel.BringToFront();
            }
        }

        public void GetSettings()
        {
            if (!bIgnoreUpdates)
            {
                bIgnoreUpdates = true;
                ////SettingsPresents.TabPages.Clear();
                //Console.WriteLine("Getting Settings");
                Dictionary<Control, bool> dState = new Dictionary<Control, bool>();
                /*foreach (Control ctrl in GiveawayWindow.Controls)
                {
                    if (!dState.ContainsKey(ctrl))
                    {
                        dState.Add(ctrl, ctrl.Enabled);
                        ctrl.Enabled = false;
                    }
                }*/
                bool bRecreateSections = false;
                foreach (string section in ini.GetSectionNames())
                {
                    if (section != "Settings" && !dSettings.ContainsKey(section))
                    {
                        bRecreateSections = true;
                        Giveaway_SettingsPresents.TabPages.Clear();
                        dSettings.Clear();
                        break;
                    }
                }
                foreach (string section in ini.GetSectionNames())
                {
                    if (section != "")
                    {
                        //Console.WriteLine(section);
                        if (!section.Equals("Settings"))
                        {
                            Dictionary<string, string> dSectionSettings = new Dictionary<string, string>();
                            string sVariable = ini.GetValue(section, "Giveaway_Type", "0");
                            ini.SetValue(section, "Giveaway_Type", sVariable);
                            dSectionSettings.Add("Giveaway_Type", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_TicketCost", "5");
                            ini.SetValue(section, "Giveaway_TicketCost", sVariable);
                            dSectionSettings.Add("Giveaway_TicketCost", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MaxTickets", "10");
                            ini.SetValue(section, "Giveaway_MaxTickets", sVariable);
                            dSectionSettings.Add("Giveaway_MaxTickets", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MinCurrencyChecked", "0");
                            ini.SetValue(section, "Giveaway_MinCurrencyChecked", sVariable);
                            dSectionSettings.Add("Giveaway_MinCurrencyChecked", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MustFollow", "0");
                            ini.SetValue(section, "Giveaway_MustFollow", sVariable);
                            dSectionSettings.Add("Giveaway_MustFollow", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MustSubscribe", "0");
                            ini.SetValue(section, "Giveaway_MustSubscribe", sVariable);
                            dSectionSettings.Add("Giveaway_MustSubscribe", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MustWatch", "0");
                            ini.SetValue(section, "Giveaway_MustWatch", sVariable);
                            dSectionSettings.Add("Giveaway_MustWatch", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MustWatchDays", "0");
                            ini.SetValue(section, "Giveaway_MustWatchDays", sVariable);
                            dSectionSettings.Add("Giveaway_MustWatchDays", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MustWatchHours", "0");
                            ini.SetValue(section, "Giveaway_MustWatchHours", sVariable);
                            dSectionSettings.Add("Giveaway_MustWatchHours", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MustWatchMinutes", "1");
                            ini.SetValue(section, "Giveaway_MustWatchMinutes", sVariable);
                            dSectionSettings.Add("Giveaway_MustWatchMinutes", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_MinCurrency", "1");
                            ini.SetValue(section, "Giveaway_MinCurrency", sVariable);
                            dSectionSettings.Add("Giveaway_MinCurrency", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_ActiveUserTime", "5");
                            ini.SetValue(section, "Giveaway_ActiveUserTime", sVariable);
                            dSectionSettings.Add("Giveaway_ActiveUserTime", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_AutoBanWinner", "0");
                            ini.SetValue(section, "Giveaway_AutoBanWinner", sVariable);
                            dSectionSettings.Add("Giveaway_AutoBanWinner", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_WarnFalseEntries", "0");
                            ini.SetValue(section, "Giveaway_WarnFalseEntries", sVariable);
                            dSectionSettings.Add("Giveaway_WarnFalseEntries", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_AnnounceWarnedEntries", "0");
                            ini.SetValue(section, "Giveaway_AnnounceWarnedEntries", sVariable);
                            dSectionSettings.Add("Giveaway_AnnounceWarnedEntries", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_SubscribersWinMultiplier", "0");
                            ini.SetValue(section, "Giveaway_SubscribersWinMultiplier", sVariable);
                            dSectionSettings.Add("Giveaway_SubscribersWinMultiplier", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_SubscribersWinMultiplierAmount", "1");
                            ini.SetValue(section, "Giveaway_SubscribersWinMultiplierAmount", sVariable);
                            dSectionSettings.Add("Giveaway_SubscribersWinMultiplierAmount", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_CustomKeyword", "");
                            ini.SetValue(section, "Giveaway_CustomKeyword", sVariable);
                            dSectionSettings.Add("Giveaway_CustomKeyword", sVariable);
                            sVariable = ini.GetValue(section, "Giveaway_BanList", "");
                            ini.SetValue(section, "Giveaway_BanList", sVariable);
                            dSectionSettings.Add("Giveaway_BanList", sVariable);

                            /*foreach (KeyValuePair<string, string> kv in dSectionSettings)
                            {
                                Console.WriteLine(kv.Key + " = " + kv.Value);
                            }*/

                            if (bRecreateSections)
                            {
                                dSettings.Add(section, dSectionSettings);

                                Giveaway_SettingsPresents.TabPages.Add(section);
                            }
                            else
                            {
                                if (dSettings.ContainsKey(section))
                                {
                                    dSettings[section] = dSectionSettings;
                                }
                                else
                                {
                                    dSettings.Add(section, dSectionSettings);
                                }
                            }
                        }
                    }
                }

                Program.Invoke(() =>
                {
                    foreach (Control ctrl in dState.Keys)
                    {
                        if (GiveawayWindow.Controls.Contains(ctrl))
                        {
                            ctrl.Enabled = dState[ctrl];
                        }
                    }

                    string sSelectedPresent = ini.GetValue("Settings", "SelectedPresent", "Default");
                    if (sSelectedPresent != "")
                    {
                        for (int i = 0; i < Giveaway_SettingsPresents.TabPages.Count; i++)
                        {
                            if (Giveaway_SettingsPresents.TabPages[i].Text.Equals(sSelectedPresent))
                            {
                                iSettingsPresent = Giveaway_SettingsPresents.SelectedIndex = i;
                                break;
                            }
                        }
                    }

                    if (Giveaway_BanListListBox.Items.Count > 0)
                    {
                        Giveaway_BanListListBox.Items.Clear();
                    }

                    if (Giveaway_SettingsPresents.SelectedIndex > -1)
                    {
                        if (dSettings.ContainsKey(Giveaway_SettingsPresents.TabPages[Giveaway_SettingsPresents.SelectedIndex].Text))
                        {
                            foreach (KeyValuePair<string, string> KeyValue in dSettings[Giveaway_SettingsPresents.TabPages[Giveaway_SettingsPresents.SelectedIndex].Text])
                            {
                                if (KeyValue.Key != "")
                                {
                                    if (KeyValue.Key.Equals("Giveaway_Type"))
                                    {
                                        Giveaway_TypeActive.Checked = (KeyValue.Value == "0");
                                        Giveaway_TypeKeyword.Checked = (KeyValue.Value == "1");
                                        Giveaway_TypeTickets.Checked = (KeyValue.Value == "2");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_TicketCost"))
                                    {
                                        Giveaway_TicketCost.Value = Convert.ToInt32(KeyValue.Value);
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MaxTickets"))
                                    {
                                        Giveaway_MaxTickets.Value = Convert.ToInt32(KeyValue.Value);
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MinCurrencyChecked"))
                                    {
                                        Giveaway_MinCurrency.Checked = (KeyValue.Value == "1");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MustFollow"))
                                    {
                                        Giveaway_MustFollow.Checked = (KeyValue.Value == "1");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MustSubscribe") && Irc.partnered)
                                    {
                                        Giveaway_MustSubscribe.Checked = (KeyValue.Value == "1");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MustWatch"))
                                    {
                                        Giveaway_MustWatch.Checked = (KeyValue.Value == "1");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MustWatchDays"))
                                    {
                                        Giveaway_MustWatchDays.Value = Convert.ToInt32(KeyValue.Value);
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MustWatchHours"))
                                    {
                                        Giveaway_MustWatchHours.Value = Convert.ToInt32(KeyValue.Value);
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MustWatchMinutes"))
                                    {
                                        Giveaway_MustWatchMinutes.Value = Convert.ToInt32(KeyValue.Value);
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_MinCurrency"))
                                    {
                                        Giveaway_MinCurrencyBox.Value = Convert.ToInt32(KeyValue.Value);
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_ActiveUserTime"))
                                    {
                                        Giveaway_ActiveUserTime.Value = Convert.ToInt32(KeyValue.Value);
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_AutoBanWinner"))
                                    {
                                        Giveaway_AutoBanWinner.Checked = (KeyValue.Value == "1");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_WarnFalseEntries"))
                                    {
                                        Giveaway_WarnFalseEntries.Checked = (KeyValue.Value == "1");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_AnnounceWarnedEntries"))
                                    {
                                        Giveaway_AnnounceWarnedEntries.Checked = (KeyValue.Value == "1");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_SubscribersWinMultiplier"))
                                    {
                                        Giveaway_SubscribersWinMultiplier.Checked = (KeyValue.Value == "1");
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_SubscribersWinMultiplierAmount"))
                                    {
                                        Giveaway_SubscribersWinMultiplierAmount.Value = Convert.ToInt32(KeyValue.Value);
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_CustomKeyword"))
                                    {
                                        Giveaway_CustomKeyword.Text = KeyValue.Value;
                                    }
                                    else if (KeyValue.Key.Equals("Giveaway_BanList"))
                                    {
                                        string[] bans = KeyValue.Value.Split(';');
                                        foreach (string ban in bans)
                                        {
                                            //Console.WriteLine(ban);
                                            if (!ban.Equals("") && !Giveaway_BanListListBox.Items.Contains(ban.ToLower()))
                                            {
                                                Giveaway_BanListListBox.Items.Add(ban.ToLower());
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                });

                if (Giveaway_SettingsPresents.TabPages.Count == 0)
                {
                    Giveaway_SettingsPresents.TabPages.Add("Default");
                    iSettingsPresent = 0;
                    SaveSettings();
                }
                bIgnoreUpdates = false;
            }
        }

        public void UpdateDonations()
        {
            while (Irc.donation_clientid != "" && Irc.donation_token != "")
            {
                List<Transaction> transactions = Api.UpdateTransactions().OrderByDescending(key => key.date).ToList();
                if (transactions.Count > 0)
                {
                    string sDonationsIgnoreRecent = ini.GetValue("Settings", "Donations_Ignore_Recent", "");
                    ini.SetValue("Settings", "Donations_Ignore_Recent", sDonationsIgnoreRecent);
                    string[] sRecentIgnores = sDonationsIgnoreRecent.Split(',');
                    string sDonationsIgnoreLatest = ini.GetValue("Settings", "Donations_Ignore_Latest", "");
                    ini.SetValue("Settings", "Donations_Ignore_Latest", sDonationsIgnoreLatest);
                    string[] sLatestIgnores = sDonationsIgnoreLatest.Split(',');
                    string sDonationsIgnoreTop = ini.GetValue("Settings", "Donations_Ignore_Top", "");
                    ini.SetValue("Settings", "Donations_Ignore_Top", sDonationsIgnoreTop);
                    string[] sTopIgnores = sDonationsIgnoreTop.Split(',');

                    Program.Invoke(() =>
                    {
                        foreach (Transaction transaction in transactions)
                        {
                            bool found = false;
                            foreach (DataGridViewRow row in Donations_List.Rows) if (row.Cells["ID"].Value.ToString() == transaction.id) found = true;
                            if (!found) Donations_List.Rows.Add(transaction.date.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + " " + CultureInfo.CurrentCulture.DateTimeFormat.LongTimePattern), transaction.donor, transaction.amount, transaction.id, transaction.notes, !sRecentIgnores.Contains(transaction.id), !sLatestIgnores.Contains(transaction.id), !sTopIgnores.Contains(transaction.id), true);
                        }
                    });

                    if (!Directory.Exists(@"Data\Donations")) Directory.CreateDirectory(@"Data\Donations");

                    int count = Convert.ToInt32(RecentDonorsLimit.Value), iCount = 0;
                    if (transactions.Count < count) count = transactions.Count;
                    string sTopDonors = "", sRecentDonors = "", sLatestDonor = "";
                    List<Transaction> Donors = new List<Transaction>();
                    foreach (Transaction transaction in transactions)
                    {
                        if (UpdateRecentDonorsCheckBox.Checked)
                        {
                            if (!sRecentIgnores.Contains(transaction.id) && iCount < count)
                            {
                                if (iCount > 0)
                                {
                                    sRecentDonors += ", ";
                                }
                                sRecentDonors += transaction.ToString("$AMOUNT - DONOR");
                                iCount++;
                            }
                        }
                        if (UpdateLastDonorCheckBox.Checked && !sLatestIgnores.Contains(transaction.id) && sLatestDonor == "")
                        {
                            File.WriteAllText(@"Data\Donations\LatestDonation.txt", (sLatestDonor = transaction.ToString("$AMOUNT - DONOR")));
                        }

                        if (UpdateTopDonorsCheckBox.Checked)
                        {
                            if (!sTopIgnores.Contains(transaction.id))
                            {
                                if (!Donors.Any(c => c.donor.ToLower() == transaction.donor.ToLower()))
                                {
                                    Donors.Add(transaction);
                                }
                                else
                                {
                                    foreach (Transaction trans in Donors)
                                    {
                                        if (transaction.donor.ToLower() == trans.donor.ToLower())
                                        {
                                            trans.amount = (float.Parse(trans.amount, CultureInfo.InvariantCulture.NumberFormat) + float.Parse(transaction.amount, CultureInfo.InvariantCulture.NumberFormat)).ToString("0.00");
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (UpdateRecentDonorsCheckBox.Checked)
                    {
                        File.WriteAllText(@"Data\Donations\RecentDonors.txt", sRecentDonors);
                    }

                    transactions = Donors.OrderByDescending(key => float.Parse(key.amount)).ToList();
                    if (UpdateTopDonorsCheckBox.Checked)
                    {
                        count = Convert.ToInt32(TopDonorsLimit.Value);
                        if (Donors.Count < count)
                        {
                            count = Donors.Count;
                        }
                        iCount = 0;
                        foreach (Transaction transaction in Donors)
                        {
                            if (iCount < count)
                            {
                                if (iCount > 0)
                                {
                                    sTopDonors += "\r\n";
                                }
                                sTopDonors += transactions[iCount].ToString("$AMOUNT - DONOR");
                                iCount++;
                            }
                        }
                        File.WriteAllText(@"Data\Donations\TopDonors.txt", sTopDonors);
                    }
                }

                Thread.Sleep(1000);
                if (Irc.ResourceKeeper) Thread.Sleep(29000);
            }
        }

        public void UpdateChannelData()
        {
            while (true)
            {
                int iStatus = 0;
                if (Irc.irc.Connected)
                {
                    if (Irc.IsStreaming)
                    {
                        iStatus = 2;
                    }
                    else
                    {
                        iStatus = 1;
                    }
                }

                if (!MetadataModified)
                {
                    using (WebClient w = new WebClient())
                    {
                        w.Proxy = null;
                        try
                        {
                            JObject stream = JObject.Parse(w.DownloadString("https://api.twitch.tv/kraken/channels/" + Irc.channel.Substring(1)));

                            ChannelGame = stream["game"].ToString();
                            ChannelTitle = stream["status"].ToString();

                            if (Channel_UseSteam.Checked)
                            {
                                string Game = Api.GetSteamCurrentGame();

                                if (!MetadataModified)
                                {
                                    ChannelGame = Game;

                                    if (stream["game"].ToString() != ChannelGame)
                                    {
                                        Api.UpdateMetadata(ChannelTitle, ChannelGame);
                                    }
                                }
                            }

                            Program.Invoke(() =>
                            {
                                if (!MetadataModified)
                                {
                                    Channel_Title.Text = ChannelTitle;
                                    Channel_Game.Text = ChannelGame;

                                    MetadataModified = false;
                                }
                            });
                        }
                        //catch (Exception e)
                        catch
                        {
                            //Console.WriteLine(e);
                            //Api.LogError("*************Error Message (via GrabData()): " + DateTime.Now + "*********************************\r\nUnable to connect to Twitch API to check stream data.\r\n" + e + "\r\n");
                        }
                    }
                }

                Program.Invoke(() =>
                {
                    ChannelStatusLabel.Text = "DISCONNECTED";
                    ChannelStatusLabel.ForeColor = Color.Red;
                    if (iStatus == 2)
                    {
                        ChannelStatusLabel.Text = "ON AIR";
                        ChannelStatusLabel.ForeColor = Color.Green;
                        int iViewers = Irc.ActiveUsers.Count;
                        foreach (string user in Irc.ActiveUsers.Keys)
                        {
                            if (Irc.IgnoredUsers.Contains(user.ToLower()))
                            {
                                iViewers--;
                            }
                        }
                        ChannelStatusLabel.Text += " (" + iViewers + ")";
                    }
                    else if (iStatus == 1)
                    {
                        ChannelStatusLabel.Text = "OFF AIR";
                        ChannelStatusLabel.ForeColor = Color.Blue;
                    }
                });

                Thread.Sleep(1000);
                if (Irc.ResourceKeeper) Thread.Sleep(29000);
            }
        }

        private void Giveaway_RerollButton_Click(object sender, EventArgs e)
        {
            Giveaway.getWinner();
        }

        private void Giveaway_StartButton_Click(object sender, EventArgs e)
        {
            if (Giveaway_TypeTickets.Checked)
            {
                Giveaway.startGiveaway(int.Parse(Giveaway_TicketCost.Value.ToString()), int.Parse(Giveaway_MaxTickets.Value.ToString()));
            }
            else
            {
                Giveaway.startGiveaway();
            }
        }

        private void Giveaway_OpenButton_Click(object sender, EventArgs e)
        {
            Giveaway.openGiveaway();
        }

        private void Giveaway_CloseButton_Click(object sender, EventArgs e)
        {
            Giveaway.closeGiveaway();
        }

        private void Giveaway_StopButton_Click(object sender, EventArgs e)
        {
            Giveaway.endGiveaway();
        }

        private void Giveaway_CancelButton_Click(object sender, EventArgs e)
        {
            Giveaway.cancelGiveaway();
        }

        private void Giveaway_AnnounceWinnerButton_Click(object sender, EventArgs e)
        {
            TimeSpan t = Database.getTimeWatched(Giveaway_WinnerLabel.Text);
            string winner = Giveaway_WinnerLabel.Text;
            //Irc.sendMessage(winner + " has won the giveaway! (" + (Api.IsSubscriber(winner) ? "Subscribes to the channel | " : "") + (Api.IsFollower(winner) ? "Follows the channel | " : "") + "Has " + Database.checkCurrency(winner) + " " + Irc.currencyName + " | Has watched the stream for " + t.Days + " days, " + t.Hours + " hours and " + t.Minutes + " minutes | Chance : " + Giveaway.Chance.ToString("0.00") + "%)");
            //Irc.sendMessage(winner + " has won the giveaway! (" + (Api.IsSubscriber(winner) ? "Subscribes to the channel | " : "") + (Api.IsFollower(winner) ? "Follows the channel | " : "") + "Has " + Database.checkCurrency(winner) + " " + Irc.currencyName + " | Has watched the stream for " + t.Days + " days, " + t.Hours + " hours and " + t.Minutes + " minutes)");
            Irc.sendMessage(winner + " has won the giveaway! (" + (Irc.Subscribers.Contains(winner.ToLower()) ? "Subscribes to the channel | " : "") + (Api.IsFollower(winner) ? "Follows the channel | " : "") + "Has " + Database.checkCurrency(winner) + " " + Irc.currencyName + " | Has watched the stream for " + t.Days + " days, " + t.Hours + " hours and " + t.Minutes + " minutes)");
        }

        private void Giveaway_AddBanTextBox_TextChanged(object sender, EventArgs e)
        {
            if (Giveaway_AddBanTextBox.Text == "" || Giveaway_AddBanTextBox.Text.Length < 5 || Giveaway_AddBanTextBox.Text.Contains(" ") || Giveaway_AddBanTextBox.Text.Contains(".") || Giveaway_AddBanTextBox.Text.Contains(",") || Giveaway_AddBanTextBox.Text.Contains("\"") || Giveaway_AddBanTextBox.Text.Contains("'") || Irc.IgnoredUsers.Any(user => user.ToLower() == Giveaway_AddBanTextBox.Text.ToLower()) || Giveaway_BanListListBox.Items.Contains(Giveaway_AddBanTextBox.Text))
            {
                Giveaway_BanButton.Enabled = false;
            }
            else
            {
                Giveaway_BanButton.Enabled = true;
            }
        }

        private void Giveaway_BanListListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Giveaway_BanListListBox.SelectedIndex >= 0) Giveaway_UnbanButton.Enabled = true;
        }

        private void Giveaway_CopyWinnerButton_Click(object sender, EventArgs e)
        {
            System.Windows.Forms.Clipboard.SetText(Giveaway_WinnerLabel.Text);
        }

        private void Giveaway_Settings_Changed(object sender, EventArgs e)
        {
            Control ctrl = (Control)sender;
            if (ctrl == Giveaway_UnbanButton)
            {
                int iOldIndex = Giveaway_BanListListBox.SelectedIndex;
                Giveaway_BanListListBox.Items.RemoveAt(iOldIndex);
                Giveaway_UnbanButton.Enabled = false;
                if (Giveaway_BanListListBox.Items.Count > 0)
                {
                    if (iOldIndex > Giveaway_BanListListBox.Items.Count - 1)
                    {
                        Giveaway_BanListListBox.SelectedIndex = Giveaway_BanListListBox.Items.Count - 1;
                    }
                    else
                    {
                        Giveaway_BanListListBox.SelectedIndex = iOldIndex;
                    }
                }
            }
            else if (ctrl == Giveaway_BanButton)
            {
                Giveaway_BanListListBox.Items.Add(Giveaway_AddBanTextBox.Text);
                Giveaway_AddBanTextBox.Text = "";
                Giveaway_BanButton.Enabled = false;
            }
            else if (ctrl == Giveaway_MinCurrency)
            {
                Giveaway_MinCurrencyBox.Enabled = Giveaway_MinCurrency.Checked;
            }
            else if (ctrl == Giveaway_TypeActive)
            {
                Giveaway_ActiveUserTime.Enabled = Giveaway_TypeActive.Checked;
                Giveaway_WarnFalseEntries.Enabled = (!Giveaway_TypeActive.Checked && Irc.Moderators.Contains(Irc.nick.ToLower()));
                if (Giveaway_TypeActive.Checked || !Irc.Moderators.Contains(Irc.nick.ToLower())) Giveaway_WarnFalseEntries.Checked = false;
            }
            else if (ctrl == Giveaway_TypeKeyword)
            {
                Giveaway_CustomKeyword.Enabled = Giveaway_TypeKeyword.Checked;
            }
            else if (ctrl == Giveaway_TypeTickets)
            {
                if (Giveaway_TypeTickets.Checked) Giveaway_MinCurrency.Checked = false;
                Giveaway_MinCurrency.Enabled = !Giveaway_TypeTickets.Checked;
                Giveaway_TicketCost.Enabled = Giveaway_MaxTickets.Enabled = Giveaway_TypeTickets.Checked;
            }
            else if (ctrl == Giveaway_MustWatch)
            {
                Giveaway_MustWatchDays.Enabled = Giveaway_MustWatchHours.Enabled = Giveaway_MustWatchMinutes.Enabled = Giveaway_MustWatch.Checked;
            }
            else if (ctrl == Giveaway_MustWatchHours)
            {
                if (Giveaway_MustWatchHours.Value == -1)
                {
                    if (Giveaway_MustWatchDays.Value > 0)
                    {
                        Giveaway_MustWatchHours.Value = 23;
                        Giveaway_MustWatchDays.Value--;
                    }
                }
                else if (Giveaway_MustWatchHours.Value == 24)
                {
                    Giveaway_MustWatchHours.Value = 0;
                    Giveaway_MustWatchDays.Value++;
                }
            }
            else if (ctrl == Giveaway_MustWatchMinutes)
            {
                if (Giveaway_MustWatchMinutes.Value == -1)
                {
                    if (Giveaway_MustWatchHours.Value > 0 || Giveaway_MustWatchDays.Value > 0)
                    {
                        Giveaway_MustWatchMinutes.Value = 59;
                        Giveaway_MustWatchHours.Value--;
                    }
                    else
                    {
                        Giveaway_MustWatchMinutes.Value = 0;
                    }
                }
                else if (Giveaway_MustWatchMinutes.Value == 60)
                {
                    Giveaway_MustWatchMinutes.Value = 0;
                    Giveaway_MustWatchHours.Value++;
                }
            }
            else if (ctrl == Giveaway_WarnFalseEntries)
            {
                Giveaway_AnnounceWarnedEntries.Enabled = Giveaway_WarnFalseEntries.Checked;
                if (!Giveaway_WarnFalseEntries.Checked) Giveaway_AnnounceWarnedEntries.Checked = false;
            }
            else if (ctrl == Giveaway_SubscribersWinMultiplier)
            {
                Giveaway_SubscribersWinMultiplierAmount.Enabled = Giveaway_SubscribersWinMultiplier.Checked;
            }
            SaveSettings();
        }

        public void SaveSettings(int SettingsPresent = -2, bool ReloadSettings = false)
        {
            new Thread(() =>
            {
                if (SettingsPresent == -2)
                {
                    if (iSettingsPresent != -2)
                    {
                        SettingsPresent = iSettingsPresent;
                    }
                }
                if (!bIgnoreUpdates)
                {
                    if (SettingsPresent > -1)
                    {
                        string Present = Giveaway_SettingsPresents.TabPages[SettingsPresent].Text;
                        if (dSettings.ContainsKey(Present))
                        {
                            //ini.SetValue("Settings", "SelectedPresent", Present);
                            if (Giveaway_TypeActive.Checked)
                            {
                                ini.SetValue(Present, "Giveaway_Type", "0");
                            }
                            else if (Giveaway_TypeKeyword.Checked)
                            {
                                ini.SetValue(Present, "Giveaway_Type", "1");
                            }
                            else if (Giveaway_TypeTickets.Checked)
                            {
                                ini.SetValue(Present, "Giveaway_Type", "2");
                            }
                            Giveaway_ActiveUserTime.Enabled = Giveaway_TypeActive.Checked;

                            ini.SetValue(Present, "Giveaway_TicketCost", Giveaway_TicketCost.Value.ToString());
                            ini.SetValue(Present, "Giveaway_MaxTickets", Giveaway_MaxTickets.Value.ToString());
                            ini.SetValue(Present, "Giveaway_MustFollow", Giveaway_MustFollow.Checked ? "1" : "0");
                            ini.SetValue(Present, "Giveaway_MustSubscribe", Giveaway_MustSubscribe.Checked ? "1" : "0");
                            ini.SetValue(Present, "Giveaway_MustWatch", Giveaway_MustWatch.Checked ? "1" : "0");
                            ini.SetValue(Present, "Giveaway_MustWatchDays", Giveaway_MustWatchDays.Value.ToString());
                            ini.SetValue(Present, "Giveaway_MustWatchHours", Giveaway_MustWatchHours.Value.ToString());
                            ini.SetValue(Present, "Giveaway_MustWatchMinutes", Giveaway_MustWatchMinutes.Value.ToString());
                            ini.SetValue(Present, "Giveaway_MinCurrencyChecked", Giveaway_MinCurrency.Checked ? "1" : "0");
                            ini.SetValue(Present, "Giveaway_MinCurrency", Giveaway_MinCurrencyBox.Value.ToString());
                            ini.SetValue(Present, "Giveaway_ActiveUserTime", Giveaway_ActiveUserTime.Value.ToString());
                            ini.SetValue(Present, "Giveaway_AutoBanWinner", Giveaway_AutoBanWinner.Checked ? "1" : "0");
                            ini.SetValue(Present, "Giveaway_WarnFalseEntries", Giveaway_WarnFalseEntries.Checked ? "1" : "0");
                            ini.SetValue(Present, "Giveaway_AnnounceWarnedEntries", Giveaway_AnnounceWarnedEntries.Checked ? "1" : "0");
                            ini.SetValue(Present, "Giveaway_SubscribersWinMultiplier", Giveaway_SubscribersWinMultiplier.Checked ? "1" : "0");
                            ini.SetValue(Present, "Giveaway_SubscribersWinMultiplierAmount", Giveaway_SubscribersWinMultiplierAmount.Value.ToString());
                            ini.SetValue(Present, "Giveaway_CustomKeyword", Giveaway_CustomKeyword.Text);
                            string items = "";
                            foreach (object item in Giveaway_BanListListBox.Items)
                            {
                                items = items + item.ToString() + ";";
                                //Console.WriteLine("Ban : " + item.ToString());
                            }
                            ini.SetValue(Present, "Giveaway_BanList", items);
                        }
                    }
                    if (ReloadSettings) GetSettings();
                }
            }).Start();
        }

        private void Giveaway_WinnerTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (Irc.ActiveUsers.ContainsKey(Giveaway_WinnerLabel.Text.ToLower()))
                {
                    int time = Api.GetUnixTimeNow() - Irc.ActiveUsers[Giveaway_WinnerLabel.Text.ToLower()];
                    int color = time - 120;
                    if (color >= 0 && color < 120)
                    {
                        color = 200 / 120 * color;
                        Giveaway_WinnerTimerLabel.ForeColor = Color.FromArgb(color, 200, 0);
                    }
                    else if (color >= 120)
                    {
                        if (color <= 180)
                        {
                            color = 255 / 60 * (color - 120);
                            int red = 200;
                            if (color > 200)
                            {
                                red = color;
                                color = 200;
                            }
                            Giveaway_WinnerTimerLabel.ForeColor = Color.FromArgb(red, 200 - color, 0);
                        }
                        else
                        {
                            Giveaway_WinnerTimerLabel.ForeColor = Color.FromArgb(255, 0, 0);
                        }
                    }

                    TimeSpan t = TimeSpan.FromSeconds(time);
                    if (t.Days > 0)
                    {
                        Giveaway_WinnerTimerLabel.Text = t.ToString(@"d\:hh\:mm\:ss");
                    }
                    else if (t.Hours > 0)
                    {
                        Giveaway_WinnerTimerLabel.Text = t.ToString(@"h\:mm\:ss");
                    }
                    else
                    {
                        Giveaway_WinnerTimerLabel.Text = t.ToString(@"m\:ss");
                    }
                }

                if (Giveaway.LastRoll > 0)
                {
                    int time = Api.GetUnixTimeNow() - Giveaway.LastRoll;
                    int color = time;
                    if (color >= 0 && color < 60)
                    {
                        color = 200 / 60 * color;
                        Giveaway_WinTimeLabel.ForeColor = Color.FromArgb(color, 200, 0);
                    }
                    else if (color >= 60)
                    {
                        if (color <= 90)
                        {
                            color = 255 / 30 * (color - 60);
                            int red = 200;
                            if (color > 200)
                            {
                                red = color;
                                color = 200;
                            }
                            Giveaway_WinTimeLabel.ForeColor = Color.FromArgb(red, 200 - color, 0);
                        }
                        else
                        {
                            Giveaway_WinTimeLabel.ForeColor = Color.FromArgb(255, 0, 0);
                        }
                    }

                    TimeSpan t = TimeSpan.FromSeconds(time);
                    if (t.Days > 0)
                    {
                        Giveaway_WinTimeLabel.Text = t.ToString(@"d\:hh\:mm\:ss");
                    }
                    else if (t.Hours > 0)
                    {
                        Giveaway_WinTimeLabel.Text = t.ToString(@"h\:mm\:ss");
                    }
                    else
                    {
                        Giveaway_WinTimeLabel.Text = t.ToString(@"m\:ss");
                    }
                }
            }
            catch
            {
            }
        }

        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!(e.Cancel = (MessageBox.Show(DisconnectButton.Enabled ? "ModBot is currently active! Are you sure you want to close it?" : "Are you sure you want to close ModBot?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.No)))
            {
                Program.Close();
                /*Console.WriteLine("Closing...");
                List<Thread> Ts = new List<Thread>();
                foreach (Thread t in Threads)
                {
                    t.Abort();
                    Ts.Add(t);
                }
                Threads.Clear();
                foreach (Thread t in Irc.Threads)
                {
                    t.Abort();
                    Ts.Add(t);
                }
                Irc.Threads.Clear();
                foreach (Thread t in Api.dCheckingDisplayName.Values)
                {
                    t.Abort();
                    Ts.Add(t);
                }
                Api.dCheckingDisplayName.Clear();
                foreach (Thread t in Ts)
                {
                    while (t.IsAlive) Thread.Sleep(10);
                }*/
            }
        }

        private void SettingsPresents_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!bIgnoreUpdates)
            {
                ini.SetValue("Settings", "SelectedPresent", Giveaway_SettingsPresents.TabPages[Giveaway_SettingsPresents.SelectedIndex].Text);
                SaveSettings(iSettingsPresent, true);
                iSettingsPresent = Giveaway_SettingsPresents.SelectedIndex;
            }
        }

        private void Currency_HandoutType_Changed(object sender, EventArgs e)
        {
            if (Currency_HandoutEveryone.Checked)
            {
                ini.SetValue("Settings", "Currency_Handout", "0");
            }
            else if (Currency_HandoutActiveStream.Checked)
            {
                ini.SetValue("Settings", "Currency_Handout", "1");
            }
            else if (Currency_HandoutActiveTime.Checked)
            {
                ini.SetValue("Settings", "Currency_Handout", "2");
            }
            Currency_HandoutLastActive.Enabled = Currency_HandoutActiveTime.Checked;
        }

        private void Currency_HandoutLastActive_ValueChanged(object sender, EventArgs e)
        {
            ini.SetValue("Settings", "Currency_HandoutTime", Currency_HandoutLastActive.Value.ToString());
        }

        public void WindowChanged(object sender, EventArgs e)
        {
            CheckBox CB = (CheckBox)sender;
            
            if (CB.Checked)
            {
                CurrentWindow = Windows.FromButton(CB);
                Windows.FromButton(CB).Control.BringToFront();
                foreach (CheckBox cb in Windows.Buttons)
                {
                    if (cb != CB)
                    {
                        cb.Checked = false;
                    }
                }
            }
            else
            {
                if (Windows.FromButton(CB) == CurrentWindow)
                {
                    CB.Checked = true;
                    Windows.FromButton(CB).Control.BringToFront();
                }
            }

            if (CurrentWindow.Control != ChannelWindow)
            {
                MetadataModified = false;
            }
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            ini.SetValue("Settings", "BOT_Name", Irc.nick = Bot_Name.Text);
            Irc.nick = Bot_Name.Text.ToLower();
            ini.SetValue("Settings", "BOT_Password", Irc.password = Bot_Token.Text);
            ini.SetValue("Settings", "Channel_Name", Channel_Name.Text);
            Irc.admin = Channel_Name.Text.Replace("#", "");
            Irc.channel = "#" + Channel_Name.Text.Replace("#", "").ToLower();
            ini.SetValue("Settings", "Channel_Token", Irc.channeltoken = Channel_Token.Text);
            if (Irc.channeltoken.StartsWith("oauth:")) Irc.channeltoken = Channel_Token.Text.Substring(6);
            ini.SetValue("Settings", "Currency_Name", Irc.currencyName = Currency_Name.Text);
            ini.SetValue("Settings", "Currency_Command", Irc.currency = Currency_Command.Text.StartsWith("!") ? Currency_Command.Text.Substring(1) : Currency_Command.Text);
            ini.SetValue("Settings", "Currency_Interval", Currency_HandoutInterval.Value.ToString());
            Irc.interval = Convert.ToInt32(Currency_HandoutInterval.Value.ToString());
            ini.SetValue("Settings", "Currency_Payout", Currency_HandoutAmount.Value.ToString());
            Irc.payout = Convert.ToInt32(Currency_HandoutAmount.Value.ToString());
            ini.SetValue("Settings", "Currency_SubscriberPayout", Currency_SubHandoutAmount.Value.ToString());
            Irc.subpayout = Convert.ToInt32(Currency_SubHandoutAmount.Value.ToString());
            ini.SetValue("Settings", "Donations_ClientID", Irc.donation_clientid = Donations_ST_ClientId.Text);
            ini.SetValue("Settings", "Donations_Token", Irc.donation_token = Donations_ST_Token.Text);
            if (Subscribers_Spreadsheet.Text != "")
            {
                if ((Subscribers_Spreadsheet.Text.StartsWith("https://spreadsheets.google.com") || Subscribers_Spreadsheet.Text.StartsWith("http://spreadsheets.google.com")) && Subscribers_Spreadsheet.Text.EndsWith("?alt=json"))
                {
                    ini.SetValue("Settings", "Subsribers_URL", Subscribers_Spreadsheet.Text);
                }
                else
                {
                    Console.WriteLine("Invalid subscriber link. Reverting to the last known good link, or blank. Restart the program to fix it.");
                }
            }

            new Thread(() => { Irc.Initialize(); }).Start();
        }

        private void DisconnectButton_Click(object sender, EventArgs e)
        {
            new Thread(() => { Irc.Disconnect(); }).Start();
        }

        private void WebsiteLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            //Process.Start("https://sourceforge.net/projects/twitchmodbot/");
            Process.Start("http://modbot.wordpress.com/");
        }

        private void SupportLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            //Process.Start("http://modbot.wordpress.com/about/");
            Process.Start("http://modbot.wordpress.com/");
        }

        private void EmailLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("mailto:CoMaNdO.ModBot@gmail.com");
        }

        private void Donations_List_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex > 4)
            {
                string sDonationsIgnoreRecent = "", sDonationsIgnoreLatest = "", sDonationsIgnoreTop = "";

                foreach (DataGridViewRow row in Donations_List.Rows)
                {
                    string sId = row.Cells["ID"].Value.ToString();

                    if (row.Cells["IncludeRecent"].Value.ToString().Equals("False")) sDonationsIgnoreRecent += sId + ",";

                    if (row.Cells["IncludeLatest"].Value.ToString().Equals("False")) sDonationsIgnoreLatest += sId + ",";

                    if (row.Cells["IncludeTop"].Value.ToString().Equals("False")) sDonationsIgnoreTop += sId + ",";
                }

                ini.SetValue("Settings", "Donations_Ignore_Recent", sDonationsIgnoreRecent);
                ini.SetValue("Settings", "Donations_Ignore_Latest", sDonationsIgnoreLatest);
                ini.SetValue("Settings", "Donations_Ignore_Top", sDonationsIgnoreTop);
            }
        }

        private void RecentDonorsLimit_ValueChanged(object sender, EventArgs e)
        {
            ini.SetValue("Settings", "Donations_Recent_Limit", RecentDonorsLimit.Value.ToString());
        }

        private void TopDonorsLimit_ValueChanged(object sender, EventArgs e)
        {
            ini.SetValue("Settings", "Donations_Top_Limit", TopDonorsLimit.Value.ToString());
        }

        private void Donations_List_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (e.Column.Name == "Date")
            {
                e.SortResult = Convert.ToDateTime(e.CellValue1.ToString()).CompareTo(Convert.ToDateTime(e.CellValue2.ToString()));
                e.Handled = true;
            }
            else if (e.Column.Name == "Amount")
            {
                e.SortResult = float.Parse(e.CellValue1.ToString()).CompareTo(float.Parse(e.CellValue2.ToString()));
                e.Handled = true;
            }
        }

        private void ConnectionDetailsChanged(object sender, EventArgs e)
        {
            //ConnectButton.Enabled = ((SettingsErrorLabel.Text = (BotNameBox.Text.Length < 3 ? "Bot name too short or the field is empty.\r\n" : "") + (!BotPasswordBox.Text.StartsWith("oauth:") ? (BotPasswordBox.Text == "" ? "Bot's oauth token field is empty.\r\n" : "Bot's oauth token field must contain \"oauth:\" at the beginning.\r\n") : "") + (ChannelBox.Text.Length < 3 ? "Channel name too short or the field is empty.\r\n" : "") + (ChannelTokenBox.Text == "" ? "Channel token is missing.\r\n" : "") + (CurrencyNameBox.Text.Length < 2 ? "Currency name too short or the field is empty.\r\n" : "") + (CurrencyCommandBox.Text.Length < 2 ? "Currency command too short or the field is empty.\r\n" : "") + (CurrencyCommandBox.Text.Contains(" ") ? "The currency command can not contain spaces.\r\n" : "")) == "");
            SettingsErrorLabel.Text = (Bot_Name.Text.Length < 3 ? "Bot name too short or the field is empty.\r\n" : "") + (!Bot_Token.Text.StartsWith("oauth:") ? (Bot_Token.Text == "" ? "Bot's oauth token field is empty.\r\n" : "Bot's oauth token field must contain \"oauth:\" at the beginning.\r\n") : "") + (Channel_Name.Text.Length < 3 ? "Channel name too short or the field is empty.\r\n" : "") + (Channel_Token.Text == "" ? "Channel token is missing.\r\n" : "") + (Currency_Name.Text.Length < 2 ? "Currency name too short or the field is empty.\r\n" : "") + (Currency_Command.Text.Length < 2 ? "Currency command too short or the field is empty.\r\n" : "") + (Currency_Command.Text.Contains(" ") ? "The currency command can not contain spaces.\r\n" : "");
        }

        private void GenerateToken_Request(object sender, EventArgs e)
        {
            foreach (Control ctrl in SettingsWindow.Controls)
            {
                if (ctrl.GetType() != typeof(Label) && ctrl != Misc_ShowConsole)
                {
                    ctrl.Enabled = false;
                }
            }
            Windows.FromControl(SettingsWindow).Button.Enabled = false;
            Windows.FromControl(AboutWindow).Button.Enabled = false;
            if ((Button)sender == Bot_TokenButton)
            {
                AuthenticationScopes = "chat_login";
            }
            else if ((Button)sender == Channel_TokenButton)
            {
                AuthenticationScopes = "user_read channel_editor channel_commercial channel_check_subscription channel_subscriptions chat_login";
            }
            //AuthenticationBrowser.Source = new Uri("https://api.twitch.tv/kraken/oauth2/authorize?response_type=token&client_id=9c70dw37ms89rfhn0jbkdxmtzf5egdq&redirect_uri=http://twitch.tv/&scope=" + AuthenticationScopes);
            AuthenticationBrowser.Url = new Uri("https://api.twitch.tv/kraken/oauth2/authorize?response_type=token&client_id=9c70dw37ms89rfhn0jbkdxmtzf5egdq&redirect_uri=http://twitch.tv/&scope=" + AuthenticationScopes);
        }

        private void UpdateTitleGameButton_Click(object sender, EventArgs e)
        {
            new Thread(() =>
            {
                if (Api.UpdateMetadata(Channel_Title.Text, Channel_Game.Text))
                {
                    if (MetadataModified) MetadataModified = false;
                }
            }).Start();
        }

        private void TitleGame_Modified(object sender, EventArgs e)
        {
            MetadataModified = true;
        }

        private void DonateImage_Click(object sender, EventArgs e)
        {
            Process.Start("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=4GJUF2L9KUKP8");
        }

        private void Spam_CWLBox_TextChanged(object sender, EventArgs e)
        {
            ini.SetValue("Settings", "Spam_CWhiteList", Spam_CWLBox.Text);
        }

        private void Channel_SteamID64_TextChanged(object sender, EventArgs e)
        {
            ini.SetValue("Settings", "Channel_SteamID64", Channel_SteamID64.Text);
        }

        private void About_Users_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (e.Column.Name == "Viewers")
            {
                e.SortResult = (int.Parse(e.CellValue2.ToString())).CompareTo(int.Parse(e.CellValue1.ToString()));
                if (e.SortResult == 0)
                {
                    e.SortResult = About_Users.Rows[e.RowIndex2].Cells["Status"].Value.ToString().CompareTo(About_Users.Rows[e.RowIndex1].Cells["Status"].Value.ToString());
                    if (e.SortResult == 0)
                    {
                        e.SortResult = About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().ToString() == "" && About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString().ToString() != "" ? 1 : About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString().ToString() == "" && About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().ToString() != "" ? -1 : About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().CompareTo(About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString());
                        if (e.SortResult == 0)
                        {
                            e.SortResult = Convert.ToDateTime(About_Users.Rows[e.RowIndex2].Cells["Updated"].Value.ToString()).CompareTo(Convert.ToDateTime(About_Users.Rows[e.RowIndex1].Cells["Updated"].Value.ToString()));
                            if (e.SortResult == 0)
                            {
                                string[] cell1 = About_Users.Rows[e.RowIndex2].Cells["Version"].Value.ToString().Split('.'), cell2 = About_Users.Rows[e.RowIndex1].Cells["Version"].Value.ToString().Split('.');
                                e.SortResult = TimeSpan.FromDays(int.Parse(cell1[2])).Add(TimeSpan.FromSeconds(int.Parse(cell1[3]))).CompareTo(TimeSpan.FromDays(int.Parse(cell2[2])).Add(TimeSpan.FromSeconds(int.Parse(cell2[3]))));
                            }
                        }
                    }
                }
                e.Handled = true;
            }
            else if (e.Column.Name == "Updated")
            {
                e.SortResult = Convert.ToDateTime(e.CellValue2.ToString()).CompareTo(Convert.ToDateTime(e.CellValue1.ToString()));
                if (e.SortResult == 0)
                {
                    string[] cell1 = About_Users.Rows[e.RowIndex2].Cells["Version"].Value.ToString().Split('.'), cell2 = About_Users.Rows[e.RowIndex1].Cells["Version"].Value.ToString().Split('.');
                    e.SortResult = TimeSpan.FromDays(int.Parse(cell1[2])).Add(TimeSpan.FromSeconds(int.Parse(cell1[3]))).CompareTo(TimeSpan.FromDays(int.Parse(cell2[2])).Add(TimeSpan.FromSeconds(int.Parse(cell2[3]))));
                    if (e.SortResult == 0)
                    {
                        e.SortResult = About_Users.Rows[e.RowIndex2].Cells["Status"].Value.ToString().CompareTo(About_Users.Rows[e.RowIndex1].Cells["Status"].Value.ToString());
                        if (e.SortResult == 0)
                        {
                            e.SortResult = int.Parse(About_Users.Rows[e.RowIndex2].Cells["Viewers"].Value.ToString()).CompareTo(int.Parse(About_Users.Rows[e.RowIndex1].Cells["Viewers"].Value.ToString()));
                            if (e.SortResult == 0)
                            {
                                e.SortResult = About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().ToString() == "" && About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString().ToString() != "" ? 1 : About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString().ToString() == "" && About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().ToString() != "" ? -1 : About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().CompareTo(About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString());
                            }
                        }
                    }
                }
                e.Handled = true;
            }
            else if (e.Column.Name == "Version")
            {
                string[] cell1 = e.CellValue2.ToString().Split('.'), cell2 = e.CellValue1.ToString().Split('.');
                e.SortResult = TimeSpan.FromDays(int.Parse(cell1[2])).Add(TimeSpan.FromSeconds(int.Parse(cell1[3]))).CompareTo(TimeSpan.FromDays(int.Parse(cell2[2])).Add(TimeSpan.FromSeconds(int.Parse(cell2[3]))));
                if (e.SortResult == 0)
                {
                    e.SortResult = About_Users.Rows[e.RowIndex2].Cells["Status"].Value.ToString().CompareTo(About_Users.Rows[e.RowIndex1].Cells["Status"].Value.ToString());
                    if (e.SortResult == 0)
                    {
                        e.SortResult = int.Parse(About_Users.Rows[e.RowIndex2].Cells["Viewers"].Value.ToString()).CompareTo(int.Parse(About_Users.Rows[e.RowIndex1].Cells["Viewers"].Value.ToString()));
                        if (e.SortResult == 0)
                        {
                            e.SortResult = About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().ToString() == "" && About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString().ToString() != "" ? 1 : About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString().ToString() == "" && About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().ToString() != "" ? -1 : About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().CompareTo(About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString());
                            if (e.SortResult == 0)
                            {
                                e.SortResult = Convert.ToDateTime(About_Users.Rows[e.RowIndex2].Cells["Updated"].Value.ToString()).CompareTo(Convert.ToDateTime(About_Users.Rows[e.RowIndex1].Cells["Updated"].Value.ToString()));
                            }
                        }
                    }
                }
                e.Handled = true;
            }
            else if (e.Column.Name == "Status")
            {
                e.SortResult = e.CellValue2.ToString().CompareTo(e.CellValue1.ToString());
                if (e.SortResult == 0)
                {
                    e.SortResult = int.Parse(About_Users.Rows[e.RowIndex2].Cells["Viewers"].Value.ToString()).CompareTo(int.Parse(About_Users.Rows[e.RowIndex1].Cells["Viewers"].Value.ToString()));
                    if (e.SortResult == 0)
                    {
                        e.SortResult = About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().ToString() == "" && About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString().ToString() != "" ? 1 : About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString().ToString() == "" && About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().ToString() != "" ? -1 : About_Users.Rows[e.RowIndex1].Cells["Game"].Value.ToString().CompareTo(About_Users.Rows[e.RowIndex2].Cells["Game"].Value.ToString());
                        if (e.SortResult == 0)
                        {
                            e.SortResult = Convert.ToDateTime(About_Users.Rows[e.RowIndex2].Cells["Updated"].Value.ToString()).CompareTo(Convert.ToDateTime(About_Users.Rows[e.RowIndex1].Cells["Updated"].Value.ToString()));
                            if (e.SortResult == 0)
                            {
                                string[] cell1 = About_Users.Rows[e.RowIndex2].Cells["Version"].Value.ToString().Split('.'), cell2 = About_Users.Rows[e.RowIndex1].Cells["Version"].Value.ToString().Split('.');
                                e.SortResult = TimeSpan.FromDays(int.Parse(cell1[2])).Add(TimeSpan.FromSeconds(int.Parse(cell1[3]))).CompareTo(TimeSpan.FromDays(int.Parse(cell2[2])).Add(TimeSpan.FromSeconds(int.Parse(cell2[3]))));
                            }
                        }
                    }
                }
                e.Handled = true;
            }
            else if (e.Column.Name == "Game")
            {
                e.SortResult = e.CellValue1.ToString() == "" && e.CellValue2.ToString() != "" ? 1 : e.CellValue2.ToString() == "" && e.CellValue1.ToString() != "" ? -1 : e.CellValue1.ToString().CompareTo(e.CellValue2.ToString());
                if (e.SortResult == 0)
                {
                    e.SortResult = About_Users.Rows[e.RowIndex2].Cells["Status"].Value.ToString().CompareTo(About_Users.Rows[e.RowIndex1].Cells["Status"].Value.ToString());
                    if (e.SortResult == 0)
                    {
                        e.SortResult = int.Parse(About_Users.Rows[e.RowIndex2].Cells["Viewers"].Value.ToString()).CompareTo(int.Parse(About_Users.Rows[e.RowIndex1].Cells["Viewers"].Value.ToString()));
                        if (e.SortResult == 0)
                        {
                            e.SortResult = Convert.ToDateTime(About_Users.Rows[e.RowIndex2].Cells["Updated"].Value.ToString()).CompareTo(Convert.ToDateTime(About_Users.Rows[e.RowIndex1].Cells["Updated"].Value.ToString()));
                            if (e.SortResult == 0)
                            {
                                string[] cell1 = About_Users.Rows[e.RowIndex2].Cells["Version"].Value.ToString().Split('.'), cell2 = About_Users.Rows[e.RowIndex1].Cells["Version"].Value.ToString().Split('.');
                                e.SortResult = TimeSpan.FromDays(int.Parse(cell1[2])).Add(TimeSpan.FromSeconds(int.Parse(cell1[3]))).CompareTo(TimeSpan.FromDays(int.Parse(cell2[2])).Add(TimeSpan.FromSeconds(int.Parse(cell2[3]))));
                            }
                        }
                    }
                }
                e.Handled = true;
            }
            else if (e.Column.Name == "Title")
            {
                e.SortResult = e.CellValue1.ToString() == "" && e.CellValue2.ToString() != "" ? 1 : e.CellValue2.ToString() == "" && e.CellValue1.ToString() != "" ? -1 : e.CellValue1.ToString().CompareTo(e.CellValue2.ToString());
                e.Handled = true;
            }
        }

        private void Settings_Changed(object sender, EventArgs e)
        {
            if (sender.GetType() == typeof(CheckBox))
            {
                CheckBox cb = (CheckBox)sender;

                if (cb == Currency_DisableCommand)
                {
                    Irc.LastCurrencyDisabledAnnounce = 0;
                }
                else if (cb == UpdateTopDonorsCheckBox)
                {
                    TopDonorsLimit.Enabled = cb.Checked;
                }
                else if (cb == UpdateRecentDonorsCheckBox)
                {
                    RecentDonorsLimit.Enabled = cb.Checked;
                }
                else if (cb == Channel_UseSteam)
                {
                    Channel_SteamID64.Enabled = cb.Checked;
                    long dummy;
                    if (!bIgnoreUpdates && cb.Checked && (Channel_SteamID64.Text.Length < 10 || !long.TryParse(Channel_SteamID64.Text, out dummy))) Process.Start("http://steamidconverter.com/");
                }
                else if (cb == Misc_ShowConsole)
                {
                    if (cb.Checked)
                    {
                        Program.ShowConsole();
                    }
                    else
                    {
                        Program.HideConsole();
                    }
                }
                else if (cb == Channel_SubscriptionRewards)
                {
                    Channel_SubscriptionsDate.Enabled = Channel_SubscriptionRewardsList.Enabled = cb.Checked;
                }
                else if (cb == Channel_ViewersChange)
                {
                    Channel_ViewersChangeInterval.Enabled = Channel_ViewersChangeRate.Enabled = Channel_ViewersChangeMessage.Enabled = cb.Checked;
                }
                else if (cb == Channel_WelcomeSub)
                {
                    Channel_WelcomeSubMessage.Enabled = cb.Checked;
                }

                ini.SetValue("Settings", cb.Name, cb.Checked ? "1" : "0");
            }
            else if (sender.GetType() == typeof(TextBox))
            {
                TextBox tb = (TextBox)sender;

                if (tb == MySQL_Password)
                {
                    /*foreach (char c in ";\\")
                    {
                        if (tb.Text.Contains(c))
                        {
                            if (!SettingsErrorLabel.Text.Contains("MySQL passwords can not contain semicolons (;) or backslashes (\\).\r\n")) SettingsErrorLabel.Text += "MySQL passwords can not contain semicolons (;) or backslashes (\\).\r\n";
                            return;
                        }
                    }
                    SettingsErrorLabel.Text = SettingsErrorLabel.Text.Replace("MySQL passwords can not contain semicolons (;) or backslashes (\\).\r\n", "");*/
                    foreach (char c in "'\"")
                    {
                        if (tb.Text.Contains(c))
                        {
                            if (!SettingsErrorLabel.Text.Contains("MySQL passwords can not contain quotation marks.\r\n")) SettingsErrorLabel.Text += "MySQL passwords can not contain quotation marks.\r\n";
                            return;
                        }
                    }
                    SettingsErrorLabel.Text = SettingsErrorLabel.Text.Replace("MySQL passwords can not contain quotation marks.\r\n", "");
                }
                else if (tb == Database_Table)
                {
                    foreach (char c in " !@#$%^&*()[]{}\\/|'\"~`")
                    {
                        if (tb.Text.Contains(c))
                        {
                            if (!SettingsErrorLabel.Text.Contains("Invalid MySQL table name.\r\n")) SettingsErrorLabel.Text += "Invalid MySQL table name.\r\n";
                            return;
                        }
                    }
                    SettingsErrorLabel.Text = SettingsErrorLabel.Text.Replace("Invalid MySQL table name.\r\n", "");
                }

                ini.SetValue("Settings", tb.Name, tb.Text);
            }
            else if (sender.GetType() == typeof(NumericUpDown) || sender.GetType() == typeof(FlatNumericUpDown))
            {
                NumericUpDown nud = (NumericUpDown)sender;

                if (nud == Channel_ViewersChangeInterval)
                {
                    if (Irc.newViewers != null) Irc.newViewers.Change((int)nud.Value * 60000, (int)nud.Value * 60000);
                }

                ini.SetValue("Settings", nud.Name, nud.Value.ToString());
            }
        }

        private void SettingsErrorLabel_TextChanged(object sender, EventArgs e)
        {
            ConnectButton.Enabled = (SettingsErrorLabel.Text == "" || SettingsErrorLabel.Text == "Unable to connect to MySQL server.\r\n");
        }

        private void Channel_SubscriptionRewardsList_Changed(object sender, EventArgs e)
        {
            lock (SubscriptionRewards)
            {
                SubscriptionRewards.Clear();
                string text = "";
                foreach (DataGridViewRow row in Channel_SubscriptionRewardsList.Rows)
                {
                    if (row.Cells["Reward"].Value != null && row.Cells["Instructions"].Value != null && row.Cells["Reward"].Value.ToString() != "")
                    {
                        SubscriptionRewards.Add(row.Cells["Reward"].Value.ToString(), row.Cells["Instructions"].Value.ToString());
                        text += row.Cells["Reward"].Value.ToString() + ";" + row.Cells["Instructions"].Value.ToString() + "\r\n";
                    }
                    if (!Directory.Exists(@"Data\Subscriptions")) Directory.CreateDirectory(@"Data\Subscriptions");
                    File.WriteAllText(@"Data\Subscriptions\Rewards.txt", text);
                }
            }
        }

        private void CopyrightLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Program.LegalNotice();
        }

        private void AuthenticationBrowser_Navigated(object sender, WebBrowserNavigatedEventArgs e)
        {
            if (e.Url.ToString().StartsWith("https://api.twitch.tv/kraken/oauth2/"))
            {
                AuthenticationBrowser.BringToFront();
                if (AuthenticationScopes == "chat_login")
                {
                    AuthenticationLabel.Text = "Connect to the bot's account";
                }
                else if (AuthenticationScopes == "user_read channel_editor channel_commercial channel_check_subscription channel_subscriptions chat_login")
                {
                    AuthenticationLabel.Text = "Connect to your account (channel)";
                }
                AuthenticationLabel.BringToFront();
            }
            else if (e.Url.Fragment.Contains("access_token"))
            {
                if (AuthenticationScopes == "chat_login")
                {
                    Bot_Token.Text = "oauth:" + e.Url.Fragment.Substring(14).Split('&')[0];
                }
                else if (AuthenticationScopes == "user_read channel_editor channel_commercial channel_check_subscription channel_subscriptions chat_login")
                {
                    Channel_Token.Text = e.Url.Fragment.Substring(14).Split('&')[0];
                }

                AuthenticationScopes = "";
                foreach (Control ctrl in SettingsWindow.Controls)
                {
                    ctrl.Enabled = true;
                }
                DisconnectButton.Enabled = false;
                SettingsWindow.BringToFront();
                Windows.FromControl(SettingsWindow).Button.Enabled = true;
                Windows.FromControl(AboutWindow).Button.Enabled = true;
                AuthenticationBrowser.Url = new Uri("http://www.twitch.tv/logout");
            }
            else if (e.Url.ToString() == "http://www.twitch.tv/" || e.Url.ToString().StartsWith("http://www.twitch.tv/?error="))
            {
                if (e.Url.ToString().StartsWith("http://www.twitch.tv/?error="))
                {
                    AuthenticationScopes = "";
                    foreach (Control ctrl in SettingsWindow.Controls)
                    {
                        ctrl.Enabled = true;
                    }
                    DisconnectButton.Enabled = false;
                    SettingsWindow.BringToFront();
                    Windows.FromControl(SettingsWindow).Button.Enabled = true;
                    Windows.FromControl(AboutWindow).Button.Enabled = true;
                    Bot_TokenButton.Enabled = true;
                }

                if (AuthenticationScopes == "")
                {
                    AuthenticationBrowser.Url = null;
                }
                else
                {
                    AuthenticationLabel.SendToBack();
                    AuthenticationBrowser.Dispose(); // A weird workaround I had to use as for some reason it wouldn't let me use the same Web Browser to get the token if a login was required before generating the token.
                    AuthenticationBrowser = new WebBrowser();
                    AuthenticationBrowser.ScriptErrorsSuppressed = true;
                    AuthenticationBrowser.Location = new Point(108, 30);
                    AuthenticationBrowser.Size = new Size(814, 562);
                    AuthenticationBrowser.Navigated += new WebBrowserNavigatedEventHandler(this.AuthenticationBrowser_Navigated);
                    AuthenticationBrowser.Url = new Uri("https://api.twitch.tv/kraken/oauth2/authorize?response_type=token&client_id=9c70dw37ms89rfhn0jbkdxmtzf5egdq&redirect_uri=http://twitch.tv/&scope=" + AuthenticationScopes);
                    Controls.Add(AuthenticationBrowser);
                }
            }
        }

        /*private void AuthenticationBrowser_Navigated(object sender, Awesomium.Core.UrlEventArgs e)
        {
            if (e.Url.ToString().StartsWith("https://api.twitch.tv/kraken/oauth2/"))
            {
                AuthenticationBrowser.BringToFront();
                if (AuthenticationScopes == "chat_login")
                {
                    AuthenticationLabel.Text = "Connect to the bot's account";
                }
                else if (AuthenticationScopes == "user_read channel_editor channel_commercial channel_check_subscription channel_subscriptions chat_login")
                {
                    AuthenticationLabel.Text = "Connect to your account (channel)";
                }
                AuthenticationLabel.BringToFront();
            }
            else if (e.Url.Fragment.Contains("access_token"))
            {
                if (AuthenticationScopes == "chat_login")
                {
                    Bot_Token.Text = "oauth:" + e.Url.Fragment.Substring(14).Split('&')[0];
                }
                else if (AuthenticationScopes == "user_read channel_editor channel_commercial channel_check_subscription channel_subscriptions chat_login")
                {
                    Channel_Token.Text = e.Url.Fragment.Substring(14).Split('&')[0];
                }

                AuthenticationScopes = "";
                foreach (Control ctrl in SettingsWindow.Controls)
                {
                    ctrl.Enabled = true;
                }
                DisconnectButton.Enabled = false;
                SettingsWindow.BringToFront();
                SettingsWindowButton.Enabled = true;
                AboutWindowButton.Enabled = true;
                AuthenticationBrowser.Source = new Uri("http://www.twitch.tv/logout");
            }
            else if (e.Url.ToString() == "http://www.twitch.tv/" || e.Url.ToString().StartsWith("http://www.twitch.tv/?error="))
            {
                if (e.Url.ToString().StartsWith("http://www.twitch.tv/?error="))
                {
                    AuthenticationScopes = "";
                    foreach (Control ctrl in SettingsWindow.Controls)
                    {
                        ctrl.Enabled = true;
                    }
                    DisconnectButton.Enabled = false;
                    SettingsWindow.BringToFront();
                    SettingsWindowButton.Enabled = true;
                    AboutWindowButton.Enabled = true;
                    Bot_TokenButton.Enabled = true;
                }

                if (AuthenticationScopes == "")
                {
                    AuthenticationBrowser.Source = new Uri("about:blank");
                }
                else
                {
                    AuthenticationLabel.SendToBack();
                }
            }
        }*/
    }

    class Window
    {
        public string Name, AlternativeName;
        public CheckBox Button;
        public Control Control;
        public bool RequiresConnection, RequiresMod, RequiresPartnership, ControlManually;

        public Window(string Name, Control Control, bool RequiresConnection = true, bool RequiresMod = false, bool RequiresPartnership = false, bool ControlManually = false, string AlternativeName = "", CheckBox Button = null, Font Font = null)
        {
            this.Name = Name;
            this.AlternativeName = AlternativeName;

            if (Button == null)
            {
                Button = new CheckBox();
                Button.FlatAppearance.BorderSize = 0;
                Button.FlatAppearance.CheckedBackColor = Color.FromArgb(230, 230, 230);
                Button.FlatStyle = FlatStyle.Flat;
                Button.Font = (Font != null ? Font : new Font("Segoe Print", 10F, FontStyle.Bold, GraphicsUnit.Point, (byte)(0)));
                Button.ForeColor = SystemColors.ControlText;
                Button.TextAlign = ContentAlignment.MiddleCenter;
                Button.UseVisualStyleBackColor = true;
            }
            Button.Appearance = Appearance.Button;
            Button.Name = Name.Replace(" ", "_").Replace("-", "_") + "_WindowButton";
            Button.Text = GetName();
            Button.Location = new Point(8, 30);
            Button.Size = new Size(100, 46);

            this.Button = Button;

            this.Control = Control;

            if (RequiresMod || RequiresPartnership || ControlManually) RequiresConnection = false;

            this.RequiresConnection = RequiresConnection;
            this.RequiresMod = RequiresMod;
            this.RequiresPartnership = RequiresPartnership;
            this.ControlManually = ControlManually;

            Button.Enabled = ControlManually ? false : ((!RequiresConnection || Irc.irc != null && Irc.irc.Connected) && (!RequiresMod || Irc.irc != null && Irc.Moderators.Contains(Irc.nick)) && (!RequiresPartnership || Irc.irc != null && Irc.partnered));

            Program.MainForm.Controls.Add(Button);
            Button.CheckedChanged += new System.EventHandler(Program.MainForm.WindowChanged);
        }

        public string GetName()
        {
            if (AlternativeName != "") return AlternativeName;

            return Name;
        }
    }

    class WindowsList : List<Window>
    {
        public List<CheckBox> Buttons { get { List<CheckBox> Buttons = new List<CheckBox>(); foreach (Window window in this) Buttons.Add(window.Button); return Buttons; } }
        public WindowsList ConnectionOnly { get { WindowsList Windows = new WindowsList(); foreach (Window window in this) if (window.RequiresConnection) Windows.Add(window); return Windows; } }
        public WindowsList ModOnly { get { WindowsList Windows = new WindowsList(); foreach (Window window in this) if (window.RequiresMod) Windows.Add(window); return Windows; } }
        public WindowsList PartnershipOnly { get { WindowsList Windows = new WindowsList(); foreach (Window window in this) if (window.RequiresPartnership) Windows.Add(window); return Windows; } }
        public WindowsList Manual { get { WindowsList Windows = new WindowsList(); foreach (Window window in this) if (window.ControlManually) Windows.Add(window); return Windows; } }

        public WindowsList()
        {
        }

        new void Add(Window window)
        {
            if (Contains(window) || ContainsControl(window.Control)) throw new Exception("The window is already in the list.");
            base.Add(window);
        }

        public bool ContainsControl(Control ctrl)
        {
            if (ctrl == null) throw new Exception("The control may not be null.");

            return FromControl(ctrl) != null;
        }

        public Window FromControl(Control ctrl)
        {
            if (ctrl == null) throw new Exception("The control may not be null.");

            foreach (Window window in this) if (window.Control == ctrl) return window;

            return null;
        }

        public Window FromButton(CheckBox Button)
        {
            if (Button == null) throw new Exception("The control may not be null.");

            foreach (Window window in this) if (window.Button == Button) return window;

            return null;
        }

        public void RemoveWindow(Control ctrl)
        {
            if (ctrl == null) throw new Exception("The control may not be null.");

            List<Window> Windows = new List<Window>();
            foreach (Window window in this) if (window.Control == ctrl) Windows.Add(window);
            foreach (Window window in Windows) this.Remove(window);
        }
    }
}