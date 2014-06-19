﻿using System;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Windows.Forms;

namespace ModBot
{
    public class Giveaway
    {
        private MainWindow MainForm;
        private Irc IRC;
        private Api api;
        public string winner;
        public int iLastWin;

        public Giveaway(MainWindow MainForm)
        {
            this.MainForm = MainForm;
            IRC = MainForm.IRC;
            api = IRC.api;
        }

        public void startGiveaway()
        {
            MainForm.BeginInvoke((MethodInvoker)delegate
            {
                MainForm.Giveaway_StartButton.Visible = false;
                MainForm.Giveaway_RerollButton.Visible = true;
                MainForm.Giveaway_AnnounceWinnerButton.Visible = true;
                MainForm.Giveaway_StopButton.Enabled = true;
                MainForm.Giveaway_AnnounceWinnerButton.Enabled = false;
                MainForm.Giveaway_MustFollowCheckBox.Enabled = false;
                MainForm.Giveaway_MinCurrencyCheckBox.Enabled = false;
                MainForm.Giveaway_MinCurrency.Enabled = false;
                MainForm.Giveaway_ActiveUserTime.Enabled = false;
                MainForm.Giveaway_CopyWinnerButton.Enabled = false;
                MainForm.Giveaway_WinnerTimerLabel.Text = "0:00";
                MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinTimeLabel.Text = "0:00";
                MainForm.Giveaway_WinTimeLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinnerChat.Clear();
                MainForm.Giveaway_WinnerLabel.Text = "Waiting for a roll...";
                MainForm.Giveaway_WinnerLabel.ForeColor = Color.Red;
                MainForm.Giveaway_WinnerStatusLabel.Text = "";
            });
            winner = "";
            iLastWin = 0;
        }

        public void endGiveaway()
        {
            MainForm.BeginInvoke((MethodInvoker)delegate
            {
                MainForm.Giveaway_StartButton.Visible = true;
                MainForm.Giveaway_RerollButton.Visible = false;
                MainForm.Giveaway_AnnounceWinnerButton.Visible = false;
                MainForm.Giveaway_StopButton.Enabled = false;
                MainForm.Giveaway_MustFollowCheckBox.Enabled = true;
                MainForm.Giveaway_MinCurrencyCheckBox.Enabled = true;
                MainForm.Giveaway_MinCurrency.Enabled = MainForm.Giveaway_MinCurrencyCheckBox.Checked;
                MainForm.Giveaway_ActiveUserTime.Enabled = true;
                MainForm.Giveaway_CopyWinnerButton.Enabled = false;
                MainForm.Giveaway_WinnerTimerLabel.Text = "0:00";
                MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinTimeLabel.Text = "0:00";
                MainForm.Giveaway_WinTimeLabel.ForeColor = Color.Black;
                MainForm.Giveaway_WinnerChat.Clear();
                MainForm.Giveaway_WinnerLabel.Text = "Giveaway isn't active";
                MainForm.Giveaway_RerollButton.Text = "Roll";
                MainForm.Giveaway_WinnerLabel.ForeColor = Color.Blue;
                MainForm.Giveaway_WinnerStatusLabel.Text = "";
            });
            winner = "";
            iLastWin = 0;
        }

        private int GetMinCurrency()
        {
            if (MainForm.Giveaway_MinCurrencyCheckBox.Checked)
            {
                return Convert.ToInt32(MainForm.Giveaway_MinCurrency.Value);
            }
            return 0;
        }

        private void GetWinnerThread()
        {
            if (MainForm.Giveaway_StartButton.Visible) startGiveaway();
            winner = "";
            iLastWin = 0;
            MainForm.BeginInvoke((MethodInvoker)delegate
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
                IRC.buildUserList();
            });

            try
            {
                List<string> ValidUsers = new List<string>();
                int ActiveTime = Convert.ToInt32(MainForm.Giveaway_ActiveUserTime.Value) * 60, CurrentTime = api.GetUnixTimeNow();
                lock (IRC.ActiveUsers)
                {
                    foreach (KeyValuePair<string, int> user in IRC.ActiveUsers)
                    {
                        if (!IRC.IgnoredUsers.Any(c => c.Equals(user.Key.ToLower())) && IRC.IsUserInList(api.capName(user.Key)))
                        {
                            if (!MainForm.Giveaway_BanListListBox.Items.Contains(api.capName(user.Key)))
                            {
                                if (CurrentTime - IRC.ActiveUsers[api.capName(user.Key)] <= ActiveTime)
                                {
                                    if ((IRC.db.checkCurrency(api.capName(user.Key)) >= GetMinCurrency()))
                                    {
                                        if (MainForm.Giveaway_MustFollowCheckBox.Checked && api.IsFollowingChannel(api.capName(user.Key)) || !MainForm.Giveaway_MustFollowCheckBox.Checked)
                                        {
                                            ValidUsers.Add(api.capName(user.Key));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (ValidUsers.Count > 0)
                {
                    winner = api.GetDisplayName(ValidUsers[new Random().Next(0, ValidUsers.Count - 1)]);
                    MainForm.BeginInvoke((MethodInvoker)delegate
                    {
                        string WinnerLabel = "Winner : ";
                        if (api.IsFollowingChannel(winner)) WinnerLabel = WinnerLabel + "Following | ";
                        MainForm.Giveaway_WinnerStatusLabel.Text = WinnerLabel + IRC.db.checkCurrency(winner) + " " + IRC.currency;
                        MainForm.Giveaway_WinnerLabel.Text = winner;
                        MainForm.Giveaway_WinnerTimerLabel.ForeColor = Color.FromArgb(0, 200, 0);
                        MainForm.Giveaway_WinTimeLabel.ForeColor = Color.FromArgb(0, 200, 0);
                        MainForm.Giveaway_WinnerLabel.ForeColor = Color.Green;
                        MainForm.Giveaway_CopyWinnerButton.Enabled = true;
                        MainForm.Giveaway_AnnounceWinnerButton.Enabled = true;
                        MainForm.Giveaway_RerollButton.Enabled = true;
                        iLastWin = api.GetUnixTimeNow();
                        if (MainForm.Giveaway_AutoBanWinnerCheckBox.Checked && !MainForm.Giveaway_BanListListBox.Items.Contains(winner)) MainForm.Giveaway_BanListListBox.Items.Add(winner);
                    });
                    new Thread(() =>
                    {
                        winner = api.GetDisplayName(winner, true);
                        MainForm.BeginInvoke((MethodInvoker)delegate
                        {
                            MainForm.Giveaway_WinnerLabel.Text = winner;
                        });
                    }).Start();
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Error while rolling, retrying");
                Thread thread = new Thread(new ThreadStart(GetWinnerThread));
                thread.Start();
                thread.Join();
                return;
            }

            MainForm.BeginInvoke((MethodInvoker)delegate
            {
                MainForm.Giveaway_WinnerLabel.Text = "No valid winner found";
                MainForm.Giveaway_RerollButton.Enabled = true;
            });
        }

        public String getWinner()
        {
            Thread thread = new Thread(new ThreadStart(GetWinnerThread));
            thread.Start();
            thread.Join();
            return winner;
        }
    }
}