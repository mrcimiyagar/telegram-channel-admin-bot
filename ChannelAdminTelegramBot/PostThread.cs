using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChannelAdminTelegramBot
{
    public class PostThread
    {
        public delegate void OnPostThreadWakeUp(int postId, long channelId, string postContent);

        public int PostId { get; private set; }
        public long ChannelId { get; private set; }
        public string PostContent { get; private set; }
        public Thread Thread { get; private set; }
        public bool Cancel { get; set; }

        private OnPostThreadWakeUp wakeUpCallback;

        public PostThread(int postId, long channelId, string postContent, int delay, OnPostThreadWakeUp wakeUpCallback)
        {
            this.PostId = postId;
            this.ChannelId = channelId;
            this.PostContent = postContent;
            this.wakeUpCallback = wakeUpCallback;
            this.Thread = new Thread(() =>
            {
                Thread.Sleep(delay);

                if (!Cancel)
                {
                    wakeUpCallback.Invoke(this.PostId, this.ChannelId, this.PostContent);
                }
            });
            this.Thread.Start();
        }
    }
}