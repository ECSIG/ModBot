﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ModBot
{
    static class Giveaway
    {
        private static MainWindow MainForm = Program.MainForm;
        public static int LastRoll { get; private set; }
        public static bool Started { get; private set; }
        public static bool Open { get; private set; }
        public static int Cost { get; private set; }
        public static int MaxTickets { get; private set; }
        public static Dictionary<string, int> Users = new Dictionary<string, int>();
        public static float Chance { get; private set; }
        private static Dictionary<Control, bool> dState = new Dictionary<Control, bool>();

        public static void startGiveaway(int ticketcost = 0, int maxtickets = 1)
        {
            Program.Invoke(() =>
            {
                MainForm.Giveaway_SettingsPresents.Enabled = false;
                MainForm.Giveaway_StartButton.Enabled = false;
                MainForm.Giveaway_RerollButton.Enabled = false;
                MainForm.Giveaway_CloseButton.Enabled = true;
                MainForm.Giveaway_OpenButton.Enabled = false;
                MainForm.Giveaway_AnnounceWinnerButton.Enabled = false;
                MainForm.Giveaway_StopButton.Enabled = true;
                MainForm.Giveaway_AnnounceWinnerButton.Enabled = false;
                dState.Clear();
                foreach (Control ctrl in MainForm.GiveawayWindow.Controls)
                {
                    if (!dState.ContainsKey(ctrl) && (ctrl.GetType() == typeof(RadioButton) || ctrl == MainForm.Giveaway_ActiveUserTime || ctrl == MainForm.Giveaway_TicketCost || ctrl == MainForm.Giveaway_MaxTickets))
                    {
                        dState.Add(ctrl, ctrl.Enabled);
                        ctrl.Enabled = false;
                    }
                }
                MainForm.Giveaway_CopyWinnerButton.Enabled = false;
                MainForm.Giveaway_WinnerTimerLabel.Text = "0:00";
                MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinTimeLabel.Text = "0:00";
                MainForm.Giveaway_WinTimeLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinnerChat.Clear();
                MainForm.Giveaway_WinnerLabel.Text = "Entries open, close to roll for a winner...";
                MainForm.Giveaway_WinnerLabel.ForeColor = Color.Red;
                MainForm.Giveaway_WinnerStatusLabel.Text = "";
                LastRoll = 0;
                Cost = ticketcost;
                MaxTickets = maxtickets;
                Users.Clear();
                Started = true;
                Open = true;

                string msg = "";
                if (MainForm.Giveaway_TypeActive.Checked)
                {
                    msg = " who sent a message or joined within the last " + MainForm.Giveaway_ActiveUserTime.Value + " minutes";
                }
                if (MainForm.Giveaway_MustSubscribe.Checked)
                {
                    if (msg != "")
                    {
                        if (!MainForm.Giveaway_MustFollow.Checked && !MainForm.Giveaway_MustWatch.Checked && !MainForm.Giveaway_MinCurrency.Checked) msg += " and"; else msg += ",";
                    }
                    else
                    {
                        msg += " who";
                    }
                    msg += " subscribes to the stream";
                }
                if (MainForm.Giveaway_MustFollow.Checked)
                {
                    if (msg != "")
                    {
                        if (!MainForm.Giveaway_MustWatch.Checked && !MainForm.Giveaway_MinCurrency.Checked) msg += " and"; else msg += ",";
                    }
                    else
                    {
                        msg += " who";
                    }
                    msg += " follows the stream";
                }
                if (MainForm.Giveaway_MustWatch.Checked)
                {
                    if (msg != "")
                    {
                        if (!MainForm.Giveaway_MinCurrency.Checked) msg += " and"; else msg += ",";
                    }
                    else
                    {
                        msg += " who";
                    }
                    msg += " watched the stream for at least " + MainForm.Giveaway_MustWatchDays.Value + " days, " + MainForm.Giveaway_MustWatchHours.Value + " hours and " + MainForm.Giveaway_MustWatchMinutes.Value + " minutes";
                }
                if (MainForm.Giveaway_MinCurrency.Checked)
                {
                    if (msg != "")
                    {
                        msg += " and";
                    }
                    else
                    {
                        msg += " who";
                    }
                    msg += " has " + MainForm.Giveaway_MinCurrencyBox.Value + " " + Irc.currencyName;
                }
                if (MainForm.Giveaway_TypeTickets.Checked)
                {
                    MainForm.Giveaway_CancelButton.Enabled = true;

                    Irc.sendMessage("A giveaway has started! Ticket cost: " + ticketcost + ", max. tickets: " + maxtickets + ". Anyone" + msg + " can join!");
                    Irc.sendMessage("Join by typing \"!ticket AMOUNT\".");
                }
                else if (MainForm.Giveaway_TypeKeyword.Checked)
                {
                    Irc.sendMessage("A giveaway has started! Join by typing \"" + (MainForm.Giveaway_CustomKeyword.Text == "" ? "!ticket" : MainForm.Giveaway_CustomKeyword.Text) + "\". Anyone" + msg + " can join!");
                }
                else
                {
                    closeGiveaway(false, false);

                    Irc.sendMessage("A giveaway has started! Anyone" + msg + " qualifies!");
                }
            });
        }

        public static void closeGiveaway(bool announce = true, bool open = true)
        {
            Open = false;
            Program.Invoke(() =>
            {
                MainForm.Giveaway_WinnerLabel.Text = "Waiting for a roll...";
                MainForm.Giveaway_RerollButton.Text = "Roll";
                MainForm.Giveaway_RerollButton.Enabled = true;
                MainForm.Giveaway_CloseButton.Enabled = false;
                MainForm.Giveaway_OpenButton.Enabled = open;
            });
            if (announce)
            {
                if (!MainForm.Giveaway_TypeActive.Checked)
                {
                    Irc.giveawayQueue.Change(0, Timeout.Infinite);
                }
                Irc.sendMessage("Entries to the giveaway are now closed.");
            }
        }

        public static void openGiveaway()
        {
            Open = true;
            Program.Invoke(() =>
            {
                MainForm.Giveaway_WinnerLabel.Text = "Entries open, close to roll for a winner...";
                MainForm.Giveaway_RerollButton.Text = "Roll";
                MainForm.Giveaway_RerollButton.Enabled = false;
                MainForm.Giveaway_CloseButton.Enabled = true;
                MainForm.Giveaway_OpenButton.Enabled = false;
            });
            if (!MainForm.Giveaway_TypeActive.Checked)
            {
                Irc.giveawayQueue.Change(0, Timeout.Infinite);
            }
            Irc.sendMessage("Entries to the giveaway are now open.");
        }

        public static void endGiveaway(bool announce = true)
        {
            Program.Invoke(() =>
            {
                MainForm.Giveaway_SettingsPresents.Enabled = true;
                MainForm.Giveaway_StartButton.Enabled = true;
                MainForm.Giveaway_RerollButton.Enabled = false;
                MainForm.Giveaway_CloseButton.Enabled = false;
                MainForm.Giveaway_OpenButton.Enabled = false;
                MainForm.Giveaway_CancelButton.Enabled = false;
                MainForm.Giveaway_AnnounceWinnerButton.Enabled = false;
                MainForm.Giveaway_StopButton.Enabled = false;
                foreach (Control ctrl in dState.Keys)
                {
                    ctrl.Enabled = dState[ctrl];
                }
                dState.Clear();
                MainForm.Giveaway_CopyWinnerButton.Enabled = false;
                MainForm.Giveaway_WinnerTimerLabel.Text = "0:00";
                MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinTimeLabel.Text = "0:00";
                MainForm.Giveaway_WinTimeLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinnerChat.Clear();
                MainForm.Giveaway_WinnerLabel.Text = "Giveaway isn't active";
                MainForm.Giveaway_WinnerLabel.ForeColor = Color.Blue;
                MainForm.Giveaway_RerollButton.Text = "Roll";
                MainForm.Giveaway_WinnerStatusLabel.Text = "";
                LastRoll = 0;
                Cost = 0;
                MaxTickets = 0;
                Users.Clear();
                Started = false;
                Open = false;
            });
            if (announce) Irc.sendMessage("The giveaway has ended!");
        }

        public static void cancelGiveaway(bool announce = true)
        {
            foreach (string user in Users.Keys)
            {
                Database.addCurrency(user, Users[user] * Cost);
            }
            endGiveaway(false);
            if(announce) Irc.sendMessage("The giveaway has been cancelled" + (MainForm.Giveaway_TypeTickets.Checked ? ", entries has been refunded." : "."));
        }

        public static bool HasBoughtTickets(string user)
        {
            return Users.ContainsKey(user.ToLower());
        }

        public static bool BuyTickets(string user, int tickets=1)
        {
            user = user.ToLower();
            if (Started && (MainForm.Giveaway_TypeKeyword.Checked || MainForm.Giveaway_TypeTickets.Checked) && Open && tickets <= MaxTickets && CheckUser(user))
            {
                int paid = 0;
                if (Users.ContainsKey(user))
                {
                    paid = Users[user] * Cost;
                }
                if (Database.checkCurrency(user) + paid >= tickets * Cost)
                {
                    Database.addCurrency(user, paid);
                    Database.removeCurrency(user, tickets * Cost);

                    /*lock (MainForm.Giveaway_UserList.Items)
                    {
                        Program.Invoke(() =>
                        {
                            List<string> delete = new List<string>();
                            foreach (string name in MainForm.Giveaway_UserList.Items)
                            {
                                if ((MainForm.Giveaway_TypeTickets.Checked ? name.Split(' ')[0] : name) == user)
                                {
                                    delete.Add(name);
                                }
                            }
                            foreach (string name in delete)
                            {
                                MainForm.Giveaway_UserList.Items.Remove(name);
                            }
                            MainForm.Giveaway_UserList.Items.Add(user + (MainForm.Giveaway_TypeTickets.Checked ?  " (" + tickets + ")" : ""));
                            MainForm.Giveaway_UserCount.Text = "Users: " + MainForm.Giveaway_UserList.Items.Count;
                        });
                    }*/

                    if (Users.ContainsKey(user))
                    {
                        Users[user] = tickets;
                    }
                    else
                    {
                        Users.Add(user, tickets);
                    }
                    return true;
                }
            }
            return false;
        }

        public static int GetMinCurrency()
        {
            if (MainForm.Giveaway_MinCurrency.Checked)
            {
                return Convert.ToInt32(MainForm.Giveaway_MinCurrencyBox.Value);
            }
            return 0;
        }

        public static bool CheckUser(string user, bool checkfollow = true, bool checksubscriber = true, bool checktime = true)
        {
            user = user.ToLower();
            bool sub = false;
            sub = (!checksubscriber || !MainForm.Giveaway_MustSubscribe.Checked || Api.IsSubscriber(user));
            return (!Irc.IgnoredUsers.Any(c => c.Equals(user.ToLower())) && !MainForm.Giveaway_BanListListBox.Items.Contains(user) && Database.checkCurrency(user) >= GetMinCurrency() && (!checkfollow || !MainForm.Giveaway_MustFollow.Checked || Api.IsFollower(user)) && sub && (!checktime || !MainForm.Giveaway_MustWatch.Checked || Api.CompareTimeWatched(user) >= 0));
        }

        public static string getWinner()
        {
            string sWinner = "";
            Program.Invoke(() =>
            {
                MainForm.Giveaway_RerollButton.Enabled = false;
                MainForm.Giveaway_AnnounceWinnerButton.Enabled = false;
                MainForm.Giveaway_CopyWinnerButton.Enabled = false;
                MainForm.Giveaway_WinnerStatusLabel.Text = "";
                MainForm.Giveaway_WinnerLabel.Text = "Rolling...";
                MainForm.Giveaway_WinnerLabel.ForeColor = Color.Red;
                MainForm.Giveaway_RerollButton.Text = "Reroll";
                MainForm.Giveaway_WinnerTimerLabel.Text = "0:00";
                MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinTimeLabel.Text = "0:00";
                MainForm.Giveaway_WinTimeLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinnerChat.Clear();
            });
            LastRoll = 0;

            Thread thread = new Thread(() =>
            {
                //Irc.buildUserList();

                while (true)
                {
                    try
                    {
                        List<string> ValidUsers = new List<string>();
                        if (MainForm.Giveaway_TypeActive.Checked)
                        {
                            //int ActiveTime = Convert.ToInt32(MainForm.Giveaway_ActiveUserTime.Value) * 60, RollTime = Api.GetUnixTimeNow();
                            int ActiveTime = Convert.ToInt32(MainForm.Giveaway_ActiveUserTime.Value) * 60;
                            lock (Irc.ActiveUsers)
                            {
                                foreach (string user in Irc.ActiveUsers.Keys)
                                {
                                    //if (!ValidUsers.Contains(user) && RollTime - Irc.ActiveUsers[user] <= ActiveTime && CheckUser(user, Irc.ActiveUsers.Count < 100))
                                    if (!ValidUsers.Contains(user) && Api.GetUnixTimeNow() - Irc.ActiveUsers[user] <= ActiveTime && CheckUser(user, Irc.ActiveUsers.Count < 100))
                                    //if (!ValidUsers.Contains(user) && RollTime  Irc.ActiveUsers[user] <= ActiveTime && CheckUser(user, false, false))
                                    {
                                        ValidUsers.Add(user);
                                        if (MainForm.Giveaway_SubscribersWinMultiplier.Checked && Api.IsSubscriber(user))
                                        {
                                            for (int i = 1; i < MainForm.Giveaway_SubscribersWinMultiplierAmount.Value; i++)
                                            {
                                                ValidUsers.Add(user);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else if (MainForm.Giveaway_TypeKeyword.Checked || MainForm.Giveaway_TypeTickets.Checked)
                        {
                            lock (Users)
                            {
                                lock (Irc.ActiveUsers)
                                {
                                    foreach (string user in Users.Keys)
                                    {
                                        if (Irc.ActiveUsers.ContainsKey(user))
                                        {
                                            if (MainForm.Giveaway_SubscribersWinMultiplier.Checked && Api.IsSubscriber(user)) Users[user] = Users[user] * Convert.ToInt32(MainForm.Giveaway_SubscribersWinMultiplierAmount.Value);

                                            for (int i = 0; i < Users[user]; i++)
                                            {
                                                ValidUsers.Add(user);
                                            }
                                        }
                                    }
                                }

                                // Only refund if lastroll == 0
                                /*List<string> Delete = new List<string>();
                                foreach (string user in Users.Keys)
                                {
                                    if (Irc.ActiveUsers.ContainsKey(user) && CheckUser(user))
                                    {
                                        for (int i = 0; i < Users[user]; i++)
                                        {
                                            ValidUsers.Add(user);
                                        }
                                    }
                                    else
                                    {
                                        Database.addCurrency(user, Users[user] * Cost);
                                        Delete.Add(user);
                                    }
                                }
                                foreach (string user in Delete)
                                {
                                    if (Users.ContainsKey(user))
                                    {
                                        Users.Remove(user);
                                    }
                                }*/

                                /*lock (MainForm.Giveaway_UserList.Items)
                                {
                                    Program.Invoke(() =>
                                    {
                                        foreach (string user in ValidUsers)
                                        {
                                            List<string> delete = new List<string>();
                                            foreach (string name in MainForm.Giveaway_UserList.Items)
                                            {
                                                if ((MainForm.Giveaway_TypeTickets.Checked ? name.Split(' ')[0] : name) == user)
                                                {
                                                    delete.Add(name);
                                                }
                                            }
                                            foreach (string name in delete)
                                            {
                                                MainForm.Giveaway_UserList.Items.Remove(name);
                                            }
                                            MainForm.Giveaway_UserList.Items.Add(user + (MainForm.Giveaway_TypeTickets.Checked ? Users.ContainsKey(user) ? " (" + Users[user] + ")" : "" : ""));
                                        }
                                        MainForm.Giveaway_UserCount.Text = "Users: " + MainForm.Giveaway_UserList.Items.Count;
                                    });
                                }*/
                            }
                        }

                        if (ValidUsers.Count > 0)
                        {
                            List<string> Ignore = new List<string>();
                            sWinner = ValidUsers[new Random().Next(0, ValidUsers.Count)];
                            while (!CheckUser(sWinner) || Ignore.Contains(sWinner))
                            {
                                Ignore.Add(sWinner);
                                sWinner = ValidUsers[new Random().Next(0, ValidUsers.Count)];
                            }
                            //Chance = 100F / ValidUsers.Count;
                            int tickets = ValidUsers.Count;
                            int winnertickets = 1;
                            if (MainForm.Giveaway_TypeTickets.Checked)
                            {
                                tickets = 0;
                                foreach(string user in Users.Keys)
                                {
                                    tickets += Users[user];
                                    if(user.ToLower() == sWinner.ToLower())
                                    {
                                        winnertickets = Users[user];
                                    }
                                }
                            }
                            Chance = (float)winnertickets / tickets * 100;
                            Program.Invoke(() =>
                            {
                                //string WinnerLabel = "Winner : ";
                                string WinnerLabel = "";
                                if (Api.IsSubscriber(sWinner)) WinnerLabel += "Subscribing | ";
                                if (Api.IsFollower(sWinner)) WinnerLabel += "Following | ";
                                //WinnerLabel += Database.checkCurrency(sWinner) + " " + Irc.currencyName + " | Watched : " + Database.getTimeWatched(sWinner).ToString(@"d\d\ hh\h\ mm\m") + " | Chance : " + Chance.ToString("0.00") + "%";
                                WinnerLabel += Database.checkCurrency(sWinner) + " " + Irc.currencyName + " | Watched : " + Database.getTimeWatched(sWinner).ToString(@"d\d\ hh\h\ mm\m");
                                sWinner = Api.GetDisplayName(sWinner);
                                MainForm.Giveaway_WinnerStatusLabel.Text = WinnerLabel;
                                MainForm.Giveaway_WinnerLabel.Text = sWinner;
                                MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.FromArgb(0, 200, 0);
                                MainForm.Giveaway_WinTimeLabel.ForeColor = Color.FromArgb(0, 200, 0);
                                MainForm.Giveaway_WinnerLabel.ForeColor = Color.Green;
                                MainForm.Giveaway_CopyWinnerButton.Enabled = true;
                                MainForm.Giveaway_AnnounceWinnerButton.Enabled = true;
                                MainForm.Giveaway_RerollButton.Enabled = true;
                                LastRoll = Api.GetUnixTimeNow();
                                if (MainForm.Giveaway_AutoBanWinner.Checked && !MainForm.Giveaway_BanListListBox.Items.Contains(sWinner.ToLower())) MainForm.Giveaway_BanListListBox.Items.Add(sWinner.ToLower());
                            });
                            thread = new Thread(() =>
                            {
                                sWinner = Api.GetDisplayName(sWinner, true);
                                Program.Invoke(() =>
                                {
                                    MainForm.Giveaway_WinnerLabel.Text = sWinner;
                                });
                            });
                            thread.Name = "Use winner's (" + sWinner + ") display name";
                            thread.Start();
                            return;
                        }
                    }
                    catch
                    {
                        Program.Invoke(() =>
                        {
                            Console.WriteLine(MainForm.Giveaway_WinnerLabel.Text = "Error while rolling, retrying...");
                        });
                        continue;
                    }

                    Program.Invoke(() =>
                    {
                        MainForm.Giveaway_WinnerLabel.Text = "No valid winner found";
                        MainForm.Giveaway_RerollButton.Enabled = true;
                    });
                    return;
                }
            });
            thread.Name = "Roll for winner";
            thread.Start();
            thread.Join();
            return sWinner;
        }
    }
}