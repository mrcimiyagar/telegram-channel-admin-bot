using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChannelAdminTelegramBot
{
    public class FixedSizedQueue<T>
    {
        public ConcurrentQueue<T> q = new ConcurrentQueue<T>();
        public int Limit { get; set; }

        private object lockObject = new object();

        public void Enqueue(T obj)
        {
            q.Enqueue(obj);
            lock (lockObject)
            {
                T overflow;
                while (q.Count > Limit && q.TryDequeue(out overflow)) ;
            }
        }
    }
}