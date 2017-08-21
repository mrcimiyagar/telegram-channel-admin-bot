using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChannelAdminTelegramBot
{
    public class DatabaseManager
    {
        private const string channelsDBPath = @"ChannelsDB";
        private const string resPostsDBPath = @"ResPostsDB";
        private const string postsContentsPath = @"ResPostsContents";
        private const string wordsDBPath = @"WordsDB";

        private SQLiteConnection channelsDB;
        private SQLiteConnection resPostsDB;
        private SQLiteConnection wordsDB;

        public DatabaseManager()
        {
            // creating files

            if (!File.Exists(channelsDBPath))
            {
                SQLiteConnection.CreateFile(channelsDBPath);
            }

            if (!File.Exists(resPostsDBPath))
            {
                SQLiteConnection.CreateFile(resPostsDBPath);
            }

            if (!File.Exists(wordsDBPath))
            {
                SQLiteConnection.CreateFile(wordsDBPath);
            }

            if (!Directory.Exists(postsContentsPath))
            {
                Directory.CreateDirectory(postsContentsPath);
            }

            // creating dbs connections

            channelsDB = new SQLiteConnection(@"Data Source=" + channelsDBPath + ";Version=3;");
            channelsDB.Open();

            resPostsDB = new SQLiteConnection(@"Data Source=" + resPostsDBPath + ";Version=3;");
            resPostsDB.Open();

            wordsDB = new SQLiteConnection(@"Data Source=" + wordsDBPath + ";Version=3;");
            wordsDB.Open();

            // creating tables

            SQLiteCommand command0 = new SQLiteCommand("create table if not exists Channels (channel_id bigint primary key);", channelsDB);
            command0.ExecuteNonQuery();
        }

        public HashSet<long> GetChannelsSet()
        {
            HashSet<long> result = new HashSet<long>();

            SQLiteCommand command0 = new SQLiteCommand("select * from Channels", channelsDB);
            SQLiteDataReader reader0 = command0.ExecuteReader();

            while (reader0.Read())
            {
                result.Add(Convert.ToInt64(reader0["channel_id"]));
            }

            return result;
        }

        public Dictionary<long, HashSet<Tuple<int, string, long>>> GetPostContentsIds(HashSet<long> channels)
        {
            Dictionary<long, HashSet<Tuple<int, string, long>>> result = new Dictionary<long, HashSet<Tuple<int, string, long>>>();

            foreach (long channelId in channels)
            {
                HashSet<Tuple<int, string, long>> contentIds = new HashSet<Tuple<int, string, long>>();

                SQLiteCommand command0 = new SQLiteCommand("select * from 'ResPosts" + channelId + "'", resPostsDB);
                SQLiteDataReader reader0 = command0.ExecuteReader();

                while (reader0.Read())
                {
                    contentIds.Add(new Tuple<int, string, long>(Convert.ToInt32(reader0["post_id"]), File.ReadAllText(postsContentsPath + @"\" + channelId + @"\" + reader0["post_id"].ToString() + ".txt"), Convert.ToInt64(reader0["time"])));
                }

                result.Add(channelId, contentIds);
            }

            return result;
        }

        public Dictionary<long, Tuple<HashSet<string>, Dictionary<string, string>>> GetWords(HashSet<long> channels)
        {
            Dictionary<long, Tuple<HashSet<string>, Dictionary<string, string>>> result = new Dictionary<long, Tuple<HashSet<string>, Dictionary<string, string>>>();

            foreach (long channelId in channels)
            {
                HashSet<string> forbiddenWordsSet = new HashSet<string>();

                SQLiteCommand command0 = new SQLiteCommand("select * from 'ForbiddenWords" + channelId + "'", wordsDB);
                SQLiteDataReader reader0 = command0.ExecuteReader();

                while (reader0.Read())
                {
                    forbiddenWordsSet.Add(reader0["word"].ToString());
                }

                Dictionary<string, string> replaceableWordsSet = new Dictionary<string, string>();

                SQLiteCommand command1 = new SQLiteCommand("select * from 'ReplaceableWords" + channelId + "'", wordsDB);
                SQLiteDataReader reader1 = command1.ExecuteReader();

                while (reader1.Read())
                {
                    replaceableWordsSet.Add(reader1["from_word"].ToString(), reader1["to_word"].ToString());
                }

                result.Add(channelId, new Tuple<HashSet<string>, Dictionary<string, string>>(forbiddenWordsSet, replaceableWordsSet));
            }

            return result;
        }

        public void AddChannel(long channelId)
        {
            lock (channelsDB)
            {
                SQLiteCommand command = new SQLiteCommand("select count(*) from Channels where channel_id = " + channelId, channelsDB);
                int count = Convert.ToInt32(command.ExecuteScalar());

                if (count == 0)
                {
                    SQLiteCommand command0 = new SQLiteCommand("insert into Channels (channel_id) values (" + channelId + ");", channelsDB);
                    command0.ExecuteNonQuery();
                }
            }

            SQLiteCommand command1 = new SQLiteCommand("create table if not exists 'ResPosts" + channelId + "' (post_id integer primary key autoincrement, time bigint);", resPostsDB);
            command1.ExecuteNonQuery();

            Directory.CreateDirectory(postsContentsPath + @"\" + channelId);

            SQLiteCommand command2 = new SQLiteCommand("create table if not exists 'ForbiddenWords" + channelId + "' (id integer primary key autoincrement, word var);", wordsDB);
            command2.ExecuteNonQuery();

            SQLiteCommand command3 = new SQLiteCommand("create table if not exists 'ReplaceableWords" + channelId + "' (id integer primary key autoincrement, from_word var, to_word var);", wordsDB);
            command3.ExecuteNonQuery();
        }

        public void RemoveChannel(long channelId)
        {
            SQLiteCommand command0 = new SQLiteCommand("delete from Channels where channel_id = " + channelId, channelsDB);
            command0.ExecuteNonQuery();

            SQLiteCommand command1 = new SQLiteCommand("drop table if exists 'ResPosts" + channelId + "'", resPostsDB);
            command1.ExecuteNonQuery();

            Directory.Delete(postsContentsPath + @"\" + channelId, true);

            SQLiteCommand command2 = new SQLiteCommand("drop table if exists 'ForbiddenWords" + channelId + "'", wordsDB);
            command2.ExecuteNonQuery();

            SQLiteCommand command3 = new SQLiteCommand("drop table if exists 'ReplaceableWords" + channelId + "'", wordsDB);
            command3.ExecuteNonQuery();
        }

        public int addResPost(long channelId, string content, long time)
        {
            int id = 0;

            lock (resPostsDB)
            {
                SQLiteCommand command0 = new SQLiteCommand("insert into 'ResPosts" + channelId + "' (time) values (" + time + ");", resPostsDB);
                command0.ExecuteNonQuery();

                SQLiteCommand command1 = new SQLiteCommand("select last_insert_rowid()", resPostsDB);
                id = Convert.ToInt32(command1.ExecuteScalar());
            }

            File.WriteAllText(postsContentsPath + @"\" + channelId + @"\" + id + ".txt", content);

            return id;
        }

        public void RemoveResPost(long channelId, int postId)
        {
            SQLiteCommand command0 = new SQLiteCommand("delete from 'ResPosts" + channelId + "' where post_id = " + postId, resPostsDB);
            command0.ExecuteNonQuery();
        }

        public void addForbiddenWord(long channelId, string word)
        {
            SQLiteCommand command0 = new SQLiteCommand("insert into 'ForbiddenWords" + channelId + "' (word) values ('" + word + "');", wordsDB);
            command0.ExecuteNonQuery();
        }

        public void removeForbiddenWord(long channelId, string word)
        {
            SQLiteCommand command0 = new SQLiteCommand("delete from 'ForbiddenWords" + channelId + "' where word = '" + word + "'", wordsDB);
            command0.ExecuteNonQuery();
        }

        public void addReplaceableWord(long channelId, string fromWord, string toWord)
        {
            SQLiteCommand command0 = new SQLiteCommand("insert into 'ReplaceableWords" + channelId + "' (from_word, to_word) values ('" + fromWord + "' , '" + toWord + "');", wordsDB);
            command0.ExecuteNonQuery();
        }

        public void removeReplaceableWord(long channelId, string fromWord)
        {
            SQLiteCommand command0 = new SQLiteCommand("delete from 'ReplaceableWords" + channelId + "' where from_word = '" + fromWord + "'", wordsDB);
            command0.ExecuteNonQuery();
        }
    }
}