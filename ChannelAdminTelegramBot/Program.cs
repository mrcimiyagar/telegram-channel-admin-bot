using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineKeyboardButtons;
using Telegram.Bot.Types.ReplyMarkups;

namespace ChannelAdminTelegramBot
{
    class Program
    {
        private const string addChannelCommand = "/new_channel";
        private const string newTimedPostCommand = "/timed_post";
        private const string newInstantPostCommand = "/post";
        private const string sendingTimedPostCommand = "sending_timed_post ";
        private const string sendingInstantPostCommand = "sending_instant_post ";
        private const string newForbiddenWordCommand = "/forbidden_word";
        private const string newReplaceableWordCommand = "/replaceable_word";
        private const string addingForbiddenWordCommand = "adding_forbidden_word ";
        private const string addingRepalceableWordCommand = "adding_replaceable_word ";

        private static DatabaseManager dbManager;
        private static HashSet<long> channels;
        private static Dictionary<long, HashSet<PostThread>> postThreads;
        private static Dictionary<long, HashSet<string>> forbiddenWords;
        private static Dictionary<long, Dictionary<string, string>> replaceableWords;

        private static HashSet<int> usersAddingChannel;
        private static Dictionary<int, long> adminsSendingTimedPost;
        private static Dictionary<int, Tuple<long, string>> timePendingPosts;
        private static Dictionary<int, long> adminsSendingInstantPost;
        private static Dictionary<int, long> adminsEnteringForbiddenWord;
        private static Dictionary<int, long> adminsEnteringFromReplaceWord;
        private static Dictionary<int, Tuple<string, long>> adminsEnteringToReplaceWord;

        private static TelegramBotClient bot;
        private static User botUser;

        private static System.Timers.Timer[] feedTimers;
        private static string[] lastFeedUpdates;

        private static int updateCounter = 0;

        static void Main(string[] args)
        {
            bot = new TelegramBotClient("410575939:AAFNP8-4xzQRfMuhH2fOWRm7JI5gRaISO3E");
            botUser = bot.GetMeAsync().Result;

            Console.WriteLine("Configuring data sources...");
            configDataSources();
            Console.WriteLine("Connecting to Telegram server...");
            startTelegramBot();
            Console.WriteLine("Purifying data sources...");
            purifyDataSources();
            Console.WriteLine("Configuring rss feeds watchers...");
            conigRssFeedsWatchers();

            bot.StartReceiving();

            Console.WriteLine("Telegram bot started.");

            Console.Title = bot.GetMeAsync().Result.FirstName + " " + bot.GetMeAsync().Result.LanguageCode;

            while (true)
            {
                Console.ReadLine();
            }
        }

        private static void configDataSources()
        {
            dbManager = new DatabaseManager();
            channels = dbManager.GetChannelsSet();
            forbiddenWords = new Dictionary<long, HashSet<string>>();
            replaceableWords = new Dictionary<long, Dictionary<string, string>>();

            foreach (KeyValuePair<long, Tuple<HashSet<string>, Dictionary<string, string>>> pair in dbManager.GetWords(channels))
            {
                forbiddenWords.Add(pair.Key, pair.Value.Item1);
                replaceableWords.Add(pair.Key, pair.Value.Item2);
            }

            postThreads = new Dictionary<long, HashSet<PostThread>>();

            foreach (KeyValuePair<long, HashSet<Tuple<int, string, long>>> pair in dbManager.GetPostContentsIds(channels))
            {
                HashSet<PostThread> tempPostThreads = new HashSet<PostThread>();

                foreach (Tuple<int, string, long> resPost in pair.Value)
                {
                    double delay = (DateTime.MinValue.AddMilliseconds(resPost.Item3) - DateTime.Now).TotalMilliseconds;

                    if (delay > 0)
                    {
                        tempPostThreads.Add(new PostThread(resPost.Item1, pair.Key, resPost.Item2, Convert.ToInt32(delay), onPostThreadWakeUp));
                    }
                    else
                    {
                        onPostThreadWakeUp(resPost.Item1, pair.Key, resPost.Item2);
                    }
                }

                postThreads.Add(pair.Key, tempPostThreads);
            }

            usersAddingChannel = new HashSet<int>();
            adminsSendingTimedPost = new Dictionary<int, long>();
            timePendingPosts = new Dictionary<int, Tuple<long, string>>();
            adminsSendingInstantPost = new Dictionary<int, long>();
            adminsEnteringForbiddenWord = new Dictionary<int, long>();
            adminsEnteringFromReplaceWord = new Dictionary<int, long>();
            adminsEnteringToReplaceWord = new Dictionary<int, Tuple<string, long>>();
        }

        private static void startTelegramBot()
        {
            bot.OnUpdate += onUpdate;
        }

        private static void purifyDataSources()
        {
            HashSet<long> deletedChannels = new HashSet<long>();

            foreach (long channelId in channels)
            {
                try
                {
                    if (!bot.GetChatAdministratorsAsync(channelId).Result.Any(admin => admin.User.Id == botUser.Id))
                    {
                        deletedChannels.Add(channelId);
                    }
                }
                catch (Exception)
                {
                    deletedChannels.Add(channelId);
                }
            }

            foreach (long channelId in deletedChannels)
            {
                try
                {
                    bot.LeaveChatAsync(channelId);
                }
                catch (Exception) { }

                dbManager.RemoveChannel(channelId);
                channels.Remove(channelId);
                postThreads.Remove(channelId);
                forbiddenWords.Remove(channelId);
                replaceableWords.Remove(channelId);

                Console.WriteLine("Removed Channel : " + channelId);
            }
        }

        private static void conigRssFeedsWatchers()
        {
            if (System.IO.File.Exists(@"LastFeedUpdates.txt"))
            {
                lastFeedUpdates = System.IO.File.ReadAllLines(@"LastFeedUpdates.txt");
            }

            if (lastFeedUpdates == null || lastFeedUpdates.Length < 2)
            {
                lastFeedUpdates = new string[] { "", "" };
            }

            feedTimers = new System.Timers.Timer[2];

            // digiato

            feedTimers[0] = new System.Timers.Timer(30000);
            feedTimers[0].AutoReset = true;
            feedTimers[0].Elapsed += (sender, e) =>
            {
                checkRssFeed(@"http://feeds.feedburner.com/Digiato?format=xml", 0);
            };
            feedTimers[0].Start();

            checkRssFeed(@"http://feeds.feedburner.com/Digiato?format=xml", 0);

            // zoomit

            feedTimers[1] = new System.Timers.Timer(30000);
            feedTimers[1].AutoReset = true;
            feedTimers[1].Elapsed += (sender, e) =>
            {
                checkRssFeed(@"https://www.zoomit.ir/feed/", 1);
            };
            feedTimers[1].Start();

            checkRssFeed(@"https://www.zoomit.ir/feed/", 1);
        }

        private static void checkRssFeed(string url, int feedIndex)
        {
            try
            {
                WebClient request = new WebClient();
                request.OpenReadCompleted += (o, e) =>
                {
                    try
                    {
                        XDocument xDoc = XDocument.Load(e.Result);
                        IEnumerable<XElement> itemsList = xDoc.Root.Element("channel").Elements("item");

                        new Thread(() =>
                        {
                            try
                            {
                                List<XElement> neededOnes = new List<XElement>();

                                foreach (XElement item in itemsList)
                                {

                                    if (item.Element("pubDate").Value == lastFeedUpdates[feedIndex])
                                    {
                                        break;
                                    }

                                    Console.WriteLine(item.Element("pubDate").Value);

                                    neededOnes.Add(item);
                                }

                                neededOnes.Reverse();

                                if (itemsList != null && itemsList.Count() > 0)
                                {
                                    lastFeedUpdates[feedIndex] = itemsList.First().Element("pubDate").Value.ToString();
                                    System.IO.File.WriteAllLines(@"TabnakLastUpdate.txt", lastFeedUpdates);
                                }

                                foreach (XElement item in neededOnes)
                                {
                                    try
                                    {
                                        string title = item.Element("title").Value.ToString();
                                        string description = "";

                                        if (item.Element("description") != null && item.Element("description").Value != null && item.Element("description").Value.Length > 0)
                                        {
                                            description = item.Element("description").Value.ToString();

                                            try
                                            {
                                                if (description.Contains("<p>"))
                                                {
                                                    description = description.Substring(description.IndexOf("<p>") + 3, description.IndexOf("</p>") - description.IndexOf("<p>") - 3);
                                                }
                                                else if (description.Contains("<p "))
                                                {
                                                    description = description.Substring(description.IndexOf("<p ") + 3, description.IndexOf("</p>") - description.IndexOf("<p ") - 3);
                                                    description = description.Substring(description.IndexOf(">"));
                                                    description = description.Length > 0 ? description.Substring(1) : "";
                                                }
                                            }
                                            catch (Exception ex) { Console.WriteLine(ex.ToString()); }

                                            try
                                            {
                                                if (description.Contains("<a href"))
                                                {
                                                    while (description.Contains("<a href"))
                                                    {
                                                        string innerWordRaw = description.Substring(description.IndexOf("<a href"), description.IndexOf("</a>") - description.IndexOf("<a href"));
                                                        string innerWord = innerWordRaw.Substring(innerWordRaw.IndexOf(">"));
                                                        innerWord = innerWord.Length > 0 ? innerWord.Substring(1) : "";
                                                        description = description.Replace(description.Substring(description.IndexOf("<a href"), description.IndexOf("</a>") - description.IndexOf("<a href") + 4), innerWord);
                                                    }
                                                }
                                            }
                                            catch (Exception ex) { Console.WriteLine(ex.ToString()); }

                                            try
                                            {
                                                if (description.Contains("<span"))
                                                {
                                                    while (description.Contains("<span"))
                                                    {
                                                        string innerWordRaw = description.Substring(description.IndexOf("<span"), description.IndexOf("</span>") - description.IndexOf("<span"));
                                                        string innerWord = innerWordRaw.Substring(innerWordRaw.IndexOf(">"));
                                                        innerWord = innerWord.Length > 0 ? innerWord.Substring(1) : "";
                                                        description = description.Replace(description.Substring(description.IndexOf("<span"), description.IndexOf("</span>") - description.IndexOf("<span") + 7), innerWord);
                                                    }
                                                }
                                            }
                                            catch (Exception ex) { Console.WriteLine(ex.ToString()); }

                                            description = WebUtility.HtmlDecode(description);
                                        }

                                        string link = "";
                                        if (item.Element("link") != null && item.Element("link").Value != null && item.Element("link").Value.Length > 0)
                                        {
                                            link = item.Element("link").Value.ToString();
                                            link = WebUtility.UrlDecode(link);
                                        }

                                        bot.SendTextMessageAsync(channels.Single(), title + Environment.NewLine + description + Environment.NewLine + link).Wait();
                                    }
                                    catch (Exception ex) { Console.WriteLine(ex.ToString()); }

                                    Thread.Sleep(2000);
                                }
                            }
                            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
                        }).Start();
                    }
                    catch (Exception ex) { Console.WriteLine(ex.ToString()); }
                };

                request.OpenReadAsync(new Uri(url));
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        }

        static void onUpdate(object sender, UpdateEventArgs uea)
        {
            Console.WriteLine("Update Received " + updateCounter++);

            new Thread(() =>
            {
                if (uea.Update.Type == Telegram.Bot.Types.Enums.UpdateType.MessageUpdate && uea.Update.Message != null)
                {
                    if (usersAddingChannel.Contains(uea.Update.Message.From.Id))
                    {
                        notifyUserForwardedChannelActivatingPost(uea);
                    }
                    else if (adminsSendingTimedPost.ContainsKey(uea.Update.Message.From.Id))
                    {
                        notifyAdminSentTimedPostContent(uea);
                    }
                    else if (adminsSendingInstantPost.ContainsKey(uea.Update.Message.From.Id))
                    {
                        notifyAdminSentInstantPostContent(uea);
                    }
                    else
                    {
                        if (uea.Update.Message.Type == Telegram.Bot.Types.Enums.MessageType.TextMessage)
                        {
                            if (uea.Update.Message.Text != null && uea.Update.Message.Text.Length > 0)
                            {
                                if (timePendingPosts.ContainsKey(uea.Update.Message.From.Id))
                                {
                                    notifyAdminConfiguringTimedPost(uea);
                                }
                                else if (adminsEnteringForbiddenWord.ContainsKey(uea.Update.Message.From.Id))
                                {
                                    notifyAdminEnteringForbiddenWord(uea);
                                }
                                else if (adminsEnteringFromReplaceWord.ContainsKey(uea.Update.Message.From.Id))
                                {
                                    notifyAdminEnteringFromReplaceWord(uea);
                                }
                                else if (adminsEnteringToReplaceWord.ContainsKey(uea.Update.Message.From.Id))
                                {
                                    notifyAdminEnteringToReplaceWord(uea);
                                }
                                else
                                {
                                    if (uea.Update.Message.Text == addChannelCommand)
                                    {
                                        notifyUserAddingChannelToBot(uea);
                                    }
                                    else if (uea.Update.Message.Text == newTimedPostCommand)
                                    {
                                        bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Fetching your channels...");

                                        List<Chat> adminChannels = getAdminChannels(uea.Update.Message.From.Id);

                                        if (adminChannels.Count > 0)
                                        {
                                            notifyAdminSendingTimedPost(uea, adminChannels);
                                        }
                                    }
                                    else if (uea.Update.Message.Text == newInstantPostCommand)
                                    {
                                        bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Fetching your channels...");

                                        List<Chat> adminChannels = getAdminChannels(uea.Update.Message.From.Id);

                                        if (adminChannels.Count > 0)
                                        {
                                            notifyAdminSendingInstantPost(uea, adminChannels);
                                        }
                                    }
                                    else if (uea.Update.Message.Text == newForbiddenWordCommand)
                                    {
                                        bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Fetching your channels...");

                                        List<Chat> adminChannels = getAdminChannels(uea.Update.Message.From.Id);

                                        if (adminChannels.Count > 0)
                                        {
                                            notifyAdminAddingForbiddenWord(uea, adminChannels);
                                        }
                                    }
                                    else if (uea.Update.Message.Text == newReplaceableWordCommand)
                                    {
                                        bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Fetching your channels...");

                                        List<Chat> adminChannels = getAdminChannels(uea.Update.Message.From.Id);

                                        if (adminChannels.Count > 0)
                                        {
                                            notifyAdminAddingReplaceableWord(uea, adminChannels);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (uea.Update.Type == Telegram.Bot.Types.Enums.UpdateType.CallbackQueryUpdate)
                {
                    if (uea.Update.CallbackQuery != null && uea.Update.CallbackQuery.Data != null && uea.Update.CallbackQuery.Data.Length > 0)
                    {
                        if (uea.Update.CallbackQuery.Data.StartsWith(sendingTimedPostCommand))
                        {
                            notifyAdminSelectedChannelToSendTimedPost(uea);
                        }
                        else if (uea.Update.CallbackQuery.Data.StartsWith(sendingInstantPostCommand))
                        {
                            notifyAdminSelectedChannelToSendInstantPost(uea);
                        }
                        else if (uea.Update.CallbackQuery.Data.StartsWith(addingForbiddenWordCommand))
                        {
                            notifyAdminSelectedChannelToEnterForbiddenWord(uea);
                        }
                        else if (uea.Update.CallbackQuery.Data.StartsWith(addingRepalceableWordCommand))
                        {
                            notifyAdminSelectedChannelToEnterReplaceableWord(uea);
                        }
                    }
                }
            }).Start();
        }

        private static void notifyAdminSelectedChannelToEnterReplaceableWord(UpdateEventArgs uea)
        {
            long channelId = Convert.ToInt64(uea.Update.CallbackQuery.Data.Substring(addingRepalceableWordCommand.Length));

            lock (adminsEnteringFromReplaceWord)
            {
                if (adminsEnteringFromReplaceWord.ContainsKey(uea.Update.CallbackQuery.From.Id))
                {
                    adminsEnteringFromReplaceWord.Remove(uea.Update.CallbackQuery.From.Id);
                }

                adminsEnteringFromReplaceWord.Add(uea.Update.CallbackQuery.From.Id, channelId);
            }

            bot.EditMessageTextAsync(uea.Update.CallbackQuery.Message.Chat.Id, uea.Update.CallbackQuery.Message.MessageId, "Now enter the word to be replaced.");
        }

        private static void notifyAdminSelectedChannelToEnterForbiddenWord(UpdateEventArgs uea)
        {
            long channelId = Convert.ToInt64(uea.Update.CallbackQuery.Data.Substring(addingForbiddenWordCommand.Length));

            lock (adminsEnteringForbiddenWord)
            {
                if (adminsEnteringForbiddenWord.ContainsKey(uea.Update.CallbackQuery.From.Id))
                {
                    adminsEnteringForbiddenWord.Remove(uea.Update.CallbackQuery.From.Id);
                }

                adminsEnteringForbiddenWord.Add(uea.Update.CallbackQuery.From.Id, channelId);
            }

            bot.EditMessageTextAsync(uea.Update.CallbackQuery.Message.Chat.Id, uea.Update.CallbackQuery.Message.MessageId, "Now enter the word to be forbidded.");
        }

        private static void notifyAdminEnteringToReplaceWord(UpdateEventArgs uea)
        {
            string toWord = uea.Update.Message.Text;

            Tuple<string, long> replaceData;

            if (adminsEnteringToReplaceWord.TryGetValue(uea.Update.Message.From.Id, out replaceData))
            {
                lock (replaceableWords)
                {
                    if (replaceableWords[replaceData.Item2].ContainsKey(replaceData.Item1))
                    {
                        replaceableWords[replaceData.Item2].Remove(replaceData.Item1);
                        
                        dbManager.removeReplaceableWord(replaceData.Item2, replaceData.Item1);
                    }

                    dbManager.addReplaceableWord(replaceData.Item2, replaceData.Item1, toWord);
                    replaceableWords[replaceData.Item2].Add(replaceData.Item1, toWord);
                }

                adminsEnteringToReplaceWord.Remove(uea.Update.Message.From.Id);

                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "New replaceable word added successfully.");
            }
            else
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot can not find your state.");
            }
        }

        private static void notifyAdminEnteringFromReplaceWord(UpdateEventArgs uea)
        {
            string fromWord = uea.Update.Message.Text;

            long channelId;

            if (adminsEnteringFromReplaceWord.TryGetValue(uea.Update.Message.From.Id, out channelId))
            {
                lock (adminsEnteringToReplaceWord)
                {
                    if (adminsEnteringToReplaceWord.ContainsKey(uea.Update.Message.From.Id))
                    {
                        adminsEnteringToReplaceWord.Remove(uea.Update.Message.From.Id);
                    }

                    adminsEnteringToReplaceWord.Add(uea.Update.Message.From.Id, new Tuple<string, long>(fromWord, channelId));
                }

                adminsEnteringFromReplaceWord.Remove(uea.Update.Message.From.Id);

                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Now enter the word to be used instead of filtered word.");
            }
            else
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot can not find your state.");
            }
        }

        private static void notifyAdminEnteringForbiddenWord(UpdateEventArgs uea)
        {
            string word = uea.Update.Message.Text;

            long channelId;

            if (adminsEnteringForbiddenWord.TryGetValue(uea.Update.Message.From.Id, out channelId))
            {
                lock (forbiddenWords)
                {
                    if (!forbiddenWords[channelId].Contains(word))
                    {
                        dbManager.addForbiddenWord(channelId, word);
                        forbiddenWords[channelId].Add(word);
                    }
                }

                adminsEnteringForbiddenWord.Remove(uea.Update.Message.From.Id);

                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "New forbidden word added successfully.");
            }
            else
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot can not find your state.");
            }
        }

        private static void notifyAdminAddingReplaceableWord(UpdateEventArgs uea, List<Chat> userChannels)
        {
            InlineKeyboardButton[][] keyboard = new InlineKeyboardButton[userChannels.Count][];

            for (int counter = 0; counter < userChannels.Count; counter++)
            {
                keyboard[counter] = new InlineKeyboardButton[]
                {
                    new InlineKeyboardCallbackButton(userChannels[counter].Title, addingRepalceableWordCommand + userChannels[counter].Id)
                };
            }

            bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Your channels : ", Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0, new InlineKeyboardMarkup(keyboard));
        }

        private static void notifyAdminAddingForbiddenWord(UpdateEventArgs uea, List<Chat> userChannels)
        {
            InlineKeyboardButton[][] keyboard = new InlineKeyboardButton[userChannels.Count][];

            for (int counter = 0; counter < userChannels.Count; counter++)
            {
                keyboard[counter] = new InlineKeyboardButton[]
                {
                    new InlineKeyboardCallbackButton(userChannels[counter].Title, addingForbiddenWordCommand + userChannels[counter].Id)
                };
            }

            bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Your channels : ", Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0, new InlineKeyboardMarkup(keyboard));
        }

        private static void notifyAdminSentInstantPostContent(UpdateEventArgs uea)
        {
            Message message = uea.Update.Message;

            if (message.Text == null || message.Text.Length == 0)
            {
                message.Text = "";
            }

            if (message.Caption == null || message.Caption.Length == 0)
            {
                message.Caption = "";
            }

            long channelId = adminsSendingInstantPost[uea.Update.Message.From.Id];

            string contentTxt = message.Text;
            string contentCapt = message.Caption;
            string forbidWord;
            
            if (checkContentForbiddenState(channelId, contentTxt, out forbidWord))
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot can not send this post becaue it contains forbidden word : '" + forbidWord + "'");
            }
            else if (checkContentForbiddenState(channelId, contentCapt, out forbidWord))
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot can not send this post becaue it contains forbidden word : '" + forbidWord + "'");
            }
            else
            {
                contentTxt = filterContentByReplacement(channelId, contentTxt);
                contentCapt = filterContentByReplacement(channelId, contentCapt);

                message.Text = contentTxt;
                message.Caption = contentCapt;
                
                onPostInstantInvoke(adminsSendingInstantPost[uea.Update.Message.From.Id], message);

                adminsSendingInstantPost.Remove(uea.Update.Message.From.Id);

                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Instant post sent to channel successfully.");
            }
        }

        private static void notifyAdminSelectedChannelToSendInstantPost(UpdateEventArgs uea)
        {
            long channelId = Convert.ToInt64(uea.Update.CallbackQuery.Data.Substring(sendingInstantPostCommand.Length));

            lock (adminsSendingInstantPost)
            {
                if (adminsSendingInstantPost.ContainsKey(uea.Update.CallbackQuery.From.Id))
                {
                    adminsSendingInstantPost.Remove(uea.Update.CallbackQuery.From.Id);
                }

                adminsSendingInstantPost.Add(uea.Update.CallbackQuery.From.Id, channelId);
            }

            bot.EditMessageTextAsync(uea.Update.CallbackQuery.Message.Chat.Id, uea.Update.CallbackQuery.Message.MessageId, "Now Send me the post.", Telegram.Bot.Types.Enums.ParseMode.Default, false, null);
        }

        private static void notifyAdminSendingInstantPost(UpdateEventArgs uea, List<Chat> userChannels)
        {
            InlineKeyboardButton[][] keyboard = new InlineKeyboardButton[userChannels.Count][];

            for (int counter = 0; counter < userChannels.Count; counter++)
            {
                keyboard[counter] = new InlineKeyboardButton[]
                {
                    new InlineKeyboardCallbackButton(userChannels[counter].Title, sendingInstantPostCommand + userChannels[counter].Id)
                };
            }

            bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Your channels : ", Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0, new InlineKeyboardMarkup(keyboard));
        }

        private static void notifyAdminConfiguringTimedPost(UpdateEventArgs uea)
        {
            Tuple<long, string> postData = timePendingPosts[uea.Update.Message.From.Id];

            long channelId = postData.Item1;
            string msgContent = postData.Item2;

            string[] parts = uea.Update.Message.Text.Split(' ');
            string date = parts[0];
            string time = parts[1];

            string[] dateParts = date.Split('/');

            int day = Convert.ToInt16(dateParts[0]);
            int month = Convert.ToInt16(dateParts[1]);
            int year = Convert.ToInt16(dateParts[2]);

            string[] timeParts = time.Split(':');

            int hour = Convert.ToInt16(timeParts[0]);
            int minute = Convert.ToInt16(timeParts[1]);

            DateTime dateObj = new DateTime(year, month, day, hour, minute, 0, new PersianCalendar());
            int delay = Convert.ToInt32((dateObj - DateTime.Now).TotalMilliseconds);

            if (delay > 0)
            {
                timePendingPosts.Remove(uea.Update.Message.From.Id);

                int postId = dbManager.addResPost(channelId, msgContent, Convert.ToInt64((dateObj - DateTime.MinValue).TotalMilliseconds));

                postThreads[channelId].Add(new PostThread(postId, channelId, msgContent, delay, onPostThreadWakeUp));

                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "New timed post created and freezed.");
            }
            else
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot can not send post because reserved time is in past.");
            }
        }

        private static void notifyAdminSentTimedPostContent(UpdateEventArgs uea)
        {
            Message message = uea.Update.Message;

            if (message.Text == null || message.Text.Length == 0)
            {
                message.Text = "";
            }
            
            if (message.Caption == null || message.Caption.Length == 0)
            {
                message.Caption = "";
            }

            long channelId = adminsSendingTimedPost[uea.Update.Message.From.Id];

            string contentTxt = message.Text;
            string contentCapt = message.Caption;
            string forbidWord;

            if (checkContentForbiddenState(channelId, contentTxt, out forbidWord))
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot can not send this post becaue it contains forbidden word : '" + forbidWord + "'");
            }
            else if (checkContentForbiddenState(channelId, contentCapt, out forbidWord))
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot can not send this post becaue it contains forbidden word : '" + forbidWord + "'");
            }
            else
            {
                contentTxt = filterContentByReplacement(channelId, contentTxt);
                contentCapt = filterContentByReplacement(channelId, contentCapt);

                message.Text = contentTxt;
                message.Caption = contentCapt;

                lock (timePendingPosts)
                {
                    if (timePendingPosts.ContainsKey(uea.Update.Message.From.Id))
                    {
                        timePendingPosts.Remove(uea.Update.Message.From.Id);
                    }

                    timePendingPosts.Add(uea.Update.Message.From.Id, new Tuple<long, string>(adminsSendingTimedPost[uea.Update.Message.From.Id], JsonConvert.SerializeObject(message)));
                }

                adminsSendingTimedPost.Remove(uea.Update.Message.From.Id);

                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Now tell me the date and time to set.");
            }
        }

        private static void notifyAdminSelectedChannelToSendTimedPost(UpdateEventArgs uea)
        {
            long channelId = Convert.ToInt64(uea.Update.CallbackQuery.Data.Substring(sendingTimedPostCommand.Length));

            lock (adminsSendingTimedPost)
            {
                if (adminsSendingTimedPost.ContainsKey(uea.Update.CallbackQuery.From.Id))
                {
                    adminsSendingTimedPost.Remove(uea.Update.CallbackQuery.From.Id);
                }

                adminsSendingTimedPost.Add(uea.Update.CallbackQuery.From.Id, channelId);
            }

            bot.EditMessageTextAsync(uea.Update.CallbackQuery.Message.Chat.Id, uea.Update.CallbackQuery.Message.MessageId, "Now Send me the post.");
        }

        private static void notifyAdminSendingTimedPost(UpdateEventArgs uea, List<Chat> userChannels)
        {
            InlineKeyboardButton[][] keyboard = new InlineKeyboardButton[userChannels.Count][];

            for (int counter = 0; counter < userChannels.Count; counter++)
            {
                keyboard[counter] = new InlineKeyboardButton[]
                {
                    new InlineKeyboardCallbackButton(userChannels[counter].Title, sendingTimedPostCommand + userChannels[counter].Id)
                };
            }

            bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Your channels : ", Telegram.Bot.Types.Enums.ParseMode.Default, false, false, 0, new InlineKeyboardMarkup(keyboard));
        }

        private static void notifyUserForwardedChannelActivatingPost(UpdateEventArgs uea)
        {
            Chat chat = uea.Update.Message.ForwardFromChat;

            if (chat.Type == Telegram.Bot.Types.Enums.ChatType.Channel)
            {
                dbManager.AddChannel(chat.Id);

                lock (channels)
                {
                    if (!channels.Contains(chat.Id))
                    {
                        channels.Add(chat.Id);
                        postThreads.Add(chat.Id, new HashSet<PostThread>());
                        forbiddenWords.Add(chat.Id, new HashSet<string>());
                        replaceableWords.Add(chat.Id, new Dictionary<string, string>());
                    }
                }

                usersAddingChannel.Remove(uea.Update.Message.From.Id);

                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Bot activated in channel successfully.");
            }
            else
            {
                bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "Accessing forwarded chat as channel failed.");
            }
        }

        private static void notifyUserAddingChannelToBot(UpdateEventArgs uea)
        {
            lock (usersAddingChannel)
            {
                if (!usersAddingChannel.Contains(uea.Update.Message.From.Id))
                {
                    usersAddingChannel.Add(uea.Update.Message.From.Id);
                }
            }

            bot.SendTextMessageAsync(uea.Update.Message.Chat.Id, "forward me a post from the target channel.");
        }

        // ***

        private static List<Chat> getAdminChannels(int userId)
        {
            List<Chat> userChannels = new List<Chat>();

            foreach (long channelId in channels)
            {
                if (bot.GetChatAdministratorsAsync(channelId).Result
                    .Any(cm => cm.User.Id == userId))
                {
                    userChannels.Add(bot.GetChatAsync(channelId).Result);
                }
            }

            return userChannels;
        }

        private static bool checkContentForbiddenState(long channelId, string text, out string word)
        {
            bool forbidded = false;
            word = null;

            lock (forbiddenWords[channelId])
            {
                foreach (string tempWord in forbiddenWords[channelId])
                {
                    if (text.Contains(tempWord))
                    {
                        forbidded = true;
                        word = tempWord;
                        break;
                    }
                }
            }

            return forbidded;
        }

        private static string filterContentByReplacement(long channelId, string text)
        {
            foreach (KeyValuePair<string, string> pair in replaceableWords[channelId])
            {
                text = text.Replace(pair.Key, pair.Value);
            }

            return text;
        }

        private static void onPostThreadWakeUp(int postId, long channelId, string msgContent)
        {
            dbManager.RemoveResPost(channelId, postId);

            lock (postThreads)
            {
                if (postThreads.ContainsKey(channelId))
                {
                    postThreads[channelId].RemoveWhere(pt => pt.PostId == postId);
                }
            }

            Message message = JsonConvert.DeserializeObject<Message>(msgContent);
            
            if (message.Type == Telegram.Bot.Types.Enums.MessageType.TextMessage)
            {
                string text = message.Text;

                bot.SendTextMessageAsync(channelId, text);
            }
            else
            {
                string caption = message.Caption;

                if (message.Type == Telegram.Bot.Types.Enums.MessageType.PhotoMessage)
                {
                    bot.SendPhotoAsync(channelId, new FileToSend(message.Photo[0].FileId), message.Caption);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.AudioMessage)
                {
                    bot.SendAudioAsync(channelId, new FileToSend(message.Audio.FileId), message.Caption, message.Audio.Duration, message.Audio.Performer, message.Audio.Title);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.VideoMessage)
                {
                    bot.SendVideoAsync(channelId, new FileToSend(message.Video.FileId), message.Video.Duration, message.Video.Thumb.Width, message.Video.Thumb.Height, message.Caption);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.VoiceMessage)
                {
                    bot.SendVoiceAsync(channelId, new FileToSend(message.Voice.FileId), message.Caption);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.VideoNoteMessage)
                {
                    bot.SendVideoNoteAsync(channelId, new FileToSend(message.VideoNote.FileId), message.VideoNote.Duration, message.VideoNote.Length);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.DocumentMessage)
                {
                    bot.SendDocumentAsync(channelId, new FileToSend(message.Document.FileId), message.Caption);
                }
            }
        }

        private static void onPostInstantInvoke(long channelId, Message message)
        {
            if (message.Type == Telegram.Bot.Types.Enums.MessageType.TextMessage)
            {
                string text = message.Text;

                bot.SendTextMessageAsync(channelId, text);
            }
            else
            {
                string caption = message.Caption;

                if (message.Type == Telegram.Bot.Types.Enums.MessageType.PhotoMessage)
                {
                    bot.SendPhotoAsync(channelId, new FileToSend(message.Photo[0].FileId), message.Caption);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.AudioMessage)
                {
                    bot.SendAudioAsync(channelId, new FileToSend(message.Audio.FileId), message.Caption, message.Audio.Duration, message.Audio.Performer, message.Audio.Title);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.VideoMessage)
                {
                    bot.SendVideoAsync(channelId, new FileToSend(message.Video.FileId), message.Video.Duration, message.Video.Thumb.Width, message.Video.Thumb.Height, message.Caption);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.VoiceMessage)
                {
                    bot.SendVoiceAsync(channelId, new FileToSend(message.Voice.FileId), message.Caption);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.VideoNoteMessage)
                {
                    bot.SendVideoNoteAsync(channelId, new FileToSend(message.VideoNote.FileId), message.VideoNote.Duration, message.VideoNote.Length);
                }
                else if (message.Type == Telegram.Bot.Types.Enums.MessageType.DocumentMessage)
                {
                    bot.SendDocumentAsync(channelId, new FileToSend(message.Document.FileId), message.Caption);
                }
            }
        }
    }
}