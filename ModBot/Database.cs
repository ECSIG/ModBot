﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ModBot
{
    static class Database
    {
        private static MainWindow MainForm;
        public static SQLiteConnection DB;
        public static MySqlConnection MySqlDB;
        public static string table;

        public static void Initialize()
        {
            MainForm = Program.MainForm;
            
            if (DB != null) DB.Close();
            MySqlDB = null;

            Console.WriteLine("Setting up the database...");

            table = Irc.channel.Substring(1);

            if (MainForm.Database_Table.Text != "") table = MainForm.Database_Table.Text.ToLower();

            if (MainForm.MySQL_Host.Text == "" || MainForm.MySQL_Database.Text == "" || MainForm.MySQL_Username.Text == "")
            {
                if (!Directory.Exists(@"Data\Users")) Directory.CreateDirectory(@"Data\Users");

                while (ModBot.Api.IsFileLocked(@"Data\Users\ModBot.sqlite", FileShare.Read) && File.Exists(@"Data\Users\ModBot.sqlite")) if (MessageBox.Show("ModBot's database file is in use, Please close it in order to let ModBot use it.", "ModBot", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Cancel) Program.Close();

                if (!File.Exists(@"Data\Users\ModBot.sqlite")) SQLiteConnection.CreateFile(@"Data\Users\ModBot.sqlite");

                DB = new SQLiteConnection(@"Data Source=Data\Users\ModBot.sqlite;Version=3;");
                DB.Open();

                using (SQLiteCommand query = new SQLiteCommand("CREATE TABLE IF NOT EXISTS 'commands' (id INTEGER PRIMARY KEY AUTOINCREMENT, command TEXT, level INTEGER DEFAULT 0, output TEXT DEFAULT null);", DB)) query.ExecuteNonQuery();

                using (SQLiteCommand query = new SQLiteCommand("CREATE TABLE IF NOT EXISTS " + table + " (id INTEGER PRIMARY KEY AUTOINCREMENT, user TEXT, currency INTEGER DEFAULT 0, subscriber INTEGER DEFAULT 0, btag TEXT DEFAULT null, userlevel INTEGER DEFAULT 0, display_name TEXT DEFAULT null, time_watched INTEGER DEFAULT 0);", DB)) query.ExecuteNonQuery();

                // Handle old users
                using (SQLiteCommand query = new SQLiteCommand("SELECT display_name FROM " + table + ";", DB))
                {
                    try
                    {
                        query.ExecuteNonQuery();
                        /*using (query = new SQLiteCommand("SELECT * FROM " + channel + ";", myDB))
                        {
                            using (SQLiteDataReader r = query.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    if (r["display_name"].ToString() == "")
                                    {
                                        Console.WriteLine(r["user"].ToString());
                                        Api.GetDisplayName(r["user"].ToString(), true);
                                    }
                                }
                            }
                        }*/
                    }
                    catch (SQLiteException)
                    {
                        using (SQLiteCommand query2 = new SQLiteCommand("ALTER TABLE " + table + " ADD COLUMN display_name TEXT DEFAULT null;", DB)) query2.ExecuteNonQuery();
                    }
                }

                using (SQLiteCommand query = new SQLiteCommand("SELECT time_watched FROM " + table + ";", DB))
                {
                    try
                    {
                        query.ExecuteNonQuery();
                    }
                    catch (SQLiteException)
                    {
                        using (SQLiteCommand query2 = new SQLiteCommand("ALTER TABLE " + table + " ADD COLUMN time_watched INTEGER DEFAULT 0;", DB)) query2.ExecuteNonQuery();
                    }
                }

                using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + " LIMIT 5;", DB))
                {
                    using (SQLiteDataReader r = query.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            if (r["user"].ToString().ToLower() != r["user"].ToString())
                            {
                                using (SQLiteCommand query2 = new SQLiteCommand("UPDATE " + table + " SET user = lower(user);", DB))
                                {
                                    query2.ExecuteNonQuery();
                                    break;
                                }
                            }
                        }
                    }
                }

                //if (tableExists("transfers") && !tableHasData(channel))
                //{
                //    using (SQLiteCommand query = new SQLiteCommand("INSERT INTO " + channel + " SELECT * FROM transfers;", DB)) query.ExecuteNonQuery();

                //    /*using (SQLiteCommand query = new SQLiteCommand("DROP TABLE transfers;", myDB))
                //    {
                //        query.ExecuteNonQuery();
                //    }*/
                //}

                //DB.Close();

                /*Commands.Add("test", new CommandExecutedHandler((string command, string[] args) =>
                {
                    if (args != null) Console.WriteLine("Command : " + command + " Args : " + args[0]); else Console.WriteLine("Command : " + command);
                    System.Threading.Thread.Sleep(5000);
                    Console.WriteLine("Test 2");
                }));*/
            }
            else
            {
                Console.WriteLine("Creating connection string to MySQL server...");

                try
                {
                    MySqlDB = new MySqlConnection("Server=" + MainForm.MySQL_Host.Text + ";Port=" + MainForm.MySQL_Port.Value + ";Database='" + MainForm.MySQL_Database.Text + "';Uid='" + MainForm.MySQL_Username.Text + "';Pwd='" + MainForm.MySQL_Password.Text + "';");

                    Console.WriteLine("Created connection string to MySQL server.\r\nTesting connection...");

                    MySqlDB.Open();
                    MySqlDB.Close();

                    Console.WriteLine("Connection successful.");

                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("CREATE TABLE IF NOT EXISTS commands (id INTEGER PRIMARY KEY AUTO_INCREMENT, command TEXT, level INTEGER DEFAULT 0, output TEXT DEFAULT null);", con)) query.ExecuteNonQuery();

                        using (MySqlCommand query = new MySqlCommand("CREATE TABLE IF NOT EXISTS " + table + " (id INTEGER PRIMARY KEY AUTO_INCREMENT, user TEXT, currency INTEGER DEFAULT 0, subscriber INTEGER DEFAULT 0, btag TEXT DEFAULT null, userlevel INTEGER DEFAULT 0, display_name TEXT DEFAULT null, time_watched INTEGER DEFAULT 0);", con)) query.ExecuteNonQuery();
                    }

                    /*if (tableExists("transfers") && !tableHasData(channel))
                    {
                        using (MySqlCommand query = new MySqlCommand("INSERT INTO " + channel + " SELECT * FROM transfers;", MySqlDB)) query.ExecuteNonQuery();
                    }*/
                }
                catch (MySqlException e)
                {
                    //Console.WriteLine(e);
                    //Console.WriteLine(e.Number);
                    Program.Invoke(() =>
                    {
                        if (e.Number == 1042)
                        {
                            Console.WriteLine(MainForm.SettingsErrorLabel.Text += "Unable to connect to MySQL server.\r\n");
                        }
                        else if (e.Number == 0 || e.Number == 1045)
                        {
                            Console.WriteLine(MainForm.SettingsErrorLabel.Text += "Incorrect MySQL login details.\r\n");
                        }
                        else if (e.Number == 1064)
                        {
                            Console.WriteLine(MainForm.SettingsErrorLabel.Text += "Invalid MySQL table name.\r\n");
                        }
                    });

                    MySqlDB = null;

                    Thread.Sleep(10);

                    return;
                }
            }

            Console.WriteLine("Database set.\r\n");
        }

        public static void newUser(string user, bool bCheckDisplayName = true)
        {
            if (user == "") return;
            user = user.ToLower();
            if (!userExists(user))
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("INSERT INTO " + table + " (user) VALUES ('" + user + "');", DB)) query.ExecuteNonQuery();
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("INSERT INTO " + table + " (user) VALUES ('" + user + "');", con)) query.ExecuteNonQuery();
                    }
                }
                if (bCheckDisplayName)
                {
                    Api.GetDisplayName(user);
                }
            }
        }

        public static List<string> GetAllUsers()
        {
            List<string> users = new List<string>();
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + ";", DB))
                {
                    using (SQLiteDataReader r = query.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            if (!users.Contains(r["user"].ToString())) users.Add(r["user"].ToString());
                        }
                    }
                }
            }
            else if (MySqlDB != null)
            {
                using (MySqlConnection con = MySqlDB.Clone())
                {
                    con.Open();
                    using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + table + ";", con))
                    {
                        using (MySqlDataReader r = query.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                if (!users.Contains(r["user"].ToString())) users.Add(r["user"].ToString());
                            }
                        }
                    }
                }
            }
            return users;
        }

        public static void setDisplayName(string user, string name)
        {
            user = user.ToLower();
            if (!userExists(user)) newUser(user, false);
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET display_name = '" + name + "' WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
            }
            else if (MySqlDB != null)
            {
                using (MySqlConnection con = MySqlDB.Clone())
                {
                    con.Open();
                    using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET display_name = '" + name + "' WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                }
            }
        }

        public static string getDisplayName(string user)
        {
            user = user.ToLower();
            if (userExists(user))
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + " WHERE user = '" + user + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                return r["display_name"].ToString();
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + table + " WHERE user = '" + user + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    return r["display_name"].ToString();
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                newUser(user);
            }
            return "";
        }

        public static void setCurrency(string user, int amount)
        {
            user = user.ToLower();
            if (amount < 0) amount = 0;
            if (!userExists(user)) newUser(user, false);
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET currency = " + amount + " WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
            }
            else if (MySqlDB != null)
            {
                using (MySqlConnection con = MySqlDB.Clone())
                {
                    con.Open();
                    using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET currency = " + amount + " WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                }
            }
        }

        public static int checkCurrency(string user)
        {
            user = user.ToLower();
            if (userExists(user))
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + " WHERE user = '" + user + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                //Console.WriteLine("1: " + r["currency"].ToString());
                                int currency = int.Parse(r["currency"].ToString());
                                if (currency < 0)
                                {
                                    setCurrency(user, 0);
                                    return 0;
                                }
                                return currency;
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + table + " WHERE user = '" + user + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    int currency = int.Parse(r["currency"].ToString());
                                    if (currency < 0)
                                    {
                                        setCurrency(user, 0);
                                        return 0;
                                    }
                                    return currency;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                newUser(user);
            }
            return 0;
        }

        public static void addCurrency(string user, int amount)
        {
            user = user.ToLower();
            if (amount < 0) amount = -amount;
            if (!userExists(user)) newUser(user, false);
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET currency = currency + " + amount + " WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
            }
            else if (MySqlDB != null)
            {
                using (MySqlConnection con = MySqlDB.Clone())
                {
                    con.Open();
                    using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET currency = currency + " + amount + " WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                }
            }
        }

        public static void removeCurrency(string user, int amount)
        {
            user = user.ToLower();
            if (amount < 0) amount = -amount;
            if (amount > checkCurrency(user)) amount = checkCurrency(user);
            if (!userExists(user)) newUser(user, false);
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET currency = currency - " + amount + " WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
            }
            else if (MySqlDB != null)
            {
                using (MySqlConnection con = MySqlDB.Clone())
                {
                    con.Open();
                    using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET currency = currency - " + amount + " WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                }
            }
        }

        public static bool userExists(string user)
        {
            user = user.ToLower();
            try
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + " WHERE user = '" + user + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                if (r["user"].ToString().ToLower().Equals(user))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + table + " WHERE user = '" + user + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    if (r["user"].ToString().ToLower().Equals(user))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static string getBtag(string user)
        {
            user = user.ToLower();
            if (userExists(user))
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + " WHERE user = '" + user + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                /*//Console.WriteLine(r["btag"]);
                                if (System.DBNull.Value.Equals(r["btag"]))
                                {
                                    //Console.WriteLine("btag is null");
                                    return null;
                                }
                                else return r["btag"].ToString();*/
                                return r["btag"].ToString();
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + table + " WHERE user = '" + user + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    return r["btag"].ToString();
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                newUser(user);
            }
            return "";
        }

        public static void setBtag(string user, string btag)
        {
            user = user.ToLower();
            if (!userExists(user)) newUser(user, false);
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET btag = '" + btag + "' WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
            }
            else if (MySqlDB != null)
            {
                using (MySqlConnection con = MySqlDB.Clone())
                {
                    con.Open();
                    using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET btag = '" + btag + "' WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                }
            }
        }

        public static bool isSubscriber(string user)
        {
            user = user.ToLower();
            if (!userExists(user))
            {
                newUser(user);
            }
            else
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + " WHERE user = '" + user + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                return (int.Parse(r["subscriber"].ToString()) == 1);
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + table + " WHERE user = '" + user + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    return (int.Parse(r["subscriber"].ToString()) == 1);
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        public static bool addSub(string user)
        {
            user = user.ToLower();
            if (userExists(user))
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET subscriber = 1 WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET subscriber = 1 WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                    }
                }
                return true;
            }
            return false;
        }

        public static bool removeSub(string user)
        {
            user = user.ToLower();
            if (userExists(user))
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET subscriber = 0 WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET subscriber = 0 WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                    }
                }
                return true;
            }
            return false;
        }

        public static int getUserLevel(string user)
        {
            user = user.ToLower();
            if (!userExists(user))
            {
                newUser(user);
            }
            else
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + " WHERE user = '" + user + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                return int.Parse(r["userlevel"].ToString());
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + table + " WHERE user = '" + user + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    return int.Parse(r["userlevel"].ToString());
                                }
                            }
                        }
                    }
                }
            }
            return 0;
        }

        public static void setUserLevel(string user, int level)
        {
            user = user.ToLower();
            if (!userExists(user)) newUser(user, false);
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET userlevel = " + level + " WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
            }
            else if (MySqlDB != null)
            {
                using (MySqlConnection con = MySqlDB.Clone())
                {
                    con.Open();
                    using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET userlevel = " + level + " WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                }
            }
        }

        public static TimeSpan getTimeWatched(string user)
        {
            user = user.ToLower();
            if (!userExists(user))
            {
                newUser(user);
            }
            else
            {
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM " + table + " WHERE user = '" + user + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                return TimeSpan.FromMinutes(int.Parse(r["time_watched"].ToString()));
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM " + table + " WHERE user = '" + user + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                if (r.Read())
                                {
                                    return TimeSpan.FromMinutes(int.Parse(r["time_watched"].ToString()));
                                }
                            }
                        }
                    }
                }
            }
            return new TimeSpan();
        }

        public static void addTimeWatched(string user, int time)
        {
            user = user.ToLower();
            if (!userExists(user)) newUser(user, false);
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("UPDATE " + table + " SET time_watched = time_watched + " + time + " WHERE user = '" + user + "' COLLATE NOCASE;", DB)) query.ExecuteNonQuery();
            }
            else if (MySqlDB != null)
            {
                using (MySqlConnection con = MySqlDB.Clone())
                {
                    con.Open();
                    using (MySqlCommand query = new MySqlCommand("UPDATE " + table + " SET time_watched = time_watched + " + time + " WHERE user = '" + user + "';", con)) query.ExecuteNonQuery();
                }
            }
        }

        /*private static bool tableExists(string table)
        {
            try
            {
                using (SQLiteCommand query = new SQLiteCommand("SELECT COUNT(*) FROM sqlite_master WHERE name = '" + table + "';", DB))
                {
                    using (SQLiteDataReader r = query.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            if (int.Parse(r["COUNT(*)"].ToString()) != 0)
                            {
                                return true;
                            }
                        }
                    }
                }
                using (MySqlCommand query = new MySqlCommand("SHOW TABLES LIKE '" + table + "';", MySqlDB))
                {
                    using (MySqlDataReader r = query.ExecuteReader())
                    {
                        return r.HasRows;
                    }
                }
            }
            catch (SQLiteException e)
            {
                Console.WriteLine(e);
            }
            catch (MySqlException e)
            {
                Console.WriteLine(e);
            }
            return false;
        }

        private static bool tableHasData(string table)
        {
            if (DB != null)
            {
                using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM '" + table + "';", DB))
                {
                    using (SQLiteDataReader r = query.ExecuteReader())
                    {
                        return r.HasRows;
                    }
                }
            }
            else if (MySqlDB != null)
            {
                using (MySqlCommand query = new MySqlCommand("SELECT * FROM '" + table + "';", MySqlDB))
                {
                    using (MySqlDataReader r = query.ExecuteReader())
                    {
                        return r.HasRows;
                    }
                }
            }
            return false;
        }*/

        public static class Commands
        {
            public static bool cmdExists(string command)
            {
                command = command.ToLower();

                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM commands", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                if (r["command"].ToString().ToLower().Equals(command))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM commands", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    if (r["command"].ToString().ToLower().Equals(command))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
                return false;
            }

            public static void addCommand(string command, int level, string output)
            {
                command = command.Replace("'", "''");
                output = output.Replace("'", "''");

                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("INSERT INTO commands (command, level, output) VALUES ('" + command + "', " + level + ", '" + output + "');", DB)) query.ExecuteNonQuery();
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("INSERT INTO commands (command, level, output) VALUES ('" + command + "', " + level + ", '" + output + "');", con)) query.ExecuteNonQuery();
                    }
                }
            }

            //editCommand

            public static void removeCommand(string command)
            {
                command = command.Replace("'", "''");

                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("DELETE FROM commands WHERE command = '" + command + "';", DB)) query.ExecuteNonQuery();
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("DELETE FROM commands WHERE command = '" + command + "';", con)) query.ExecuteNonQuery();
                    }
                }
            }

            public static int LevelRequired(string command)
            {
                command = command.Replace("'", "''");

                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM commands WHERE command = '" + command + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                return int.Parse(r["level"].ToString());
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM commands WHERE command = '" + command + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    return int.Parse(r["level"].ToString());
                                }
                            }
                        }
                    }
                }
                return 0;
            }

            public static string getList()
            {
                string commands = "";
                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM commands;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                commands += r["command"].ToString() + ", ";
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM commands;", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    commands += r["command"].ToString() + ", ";
                                }
                            }
                        }
                    }
                }

                if (commands.Length < 2) return commands;

                return commands.Substring(0, commands.Length - 2);
            }

            public static string getOutput(string command)
            {
                command = command.Replace("'", "''");

                if (DB != null)
                {
                    using (SQLiteCommand query = new SQLiteCommand("SELECT * FROM commands WHERE command = '" + command + "' COLLATE NOCASE;", DB))
                    {
                        using (SQLiteDataReader r = query.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                return r["output"].ToString();
                            }
                        }
                    }
                }
                else if (MySqlDB != null)
                {
                    using (MySqlConnection con = MySqlDB.Clone())
                    {
                        con.Open();
                        using (MySqlCommand query = new MySqlCommand("SELECT * FROM commands WHERE command = '" + command + "';", con))
                        {
                            using (MySqlDataReader r = query.ExecuteReader())
                            {
                                while (r.Read())
                                {
                                    return r["output"].ToString();
                                }
                            }
                        }
                    }
                }
                return "";
            }
        }
    }
}