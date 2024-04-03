using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace RedisLib
{
    [DataContract]
    sealed internal class ConcurrentLinkedList<T> : IDisposable
    {
        [DataMember]
        private readonly ThreadItem<T>[] threadList;
        [NonSerialized]
        private readonly CancellationTokenSource tokenSource = new();
        [NonSerialized]
        private readonly ReaderWriterLockSlim lockSlim = new();
        [NonSerialized]
        private readonly ConcurrentMap<T> map;
        [DataMember]
        private int indexerThread = 0;

        internal ConcurrentLinkedList(int threadCount, ConcurrentMap<T> map)
        {
            this.map = map;
            threadList = new ThreadItem<T>[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                threadList[i] = new(tokenSource, map);
            }
        }

#pragma warning disable CS8618 // constructor for deserializing a class without null fields.
        internal ConcurrentLinkedList() { }
#pragma warning restore CS8618

        private ConcurrentLinkedList(ConcurrentMap<T> map, ConcurrentLinkedList<T> last)
        {
            this.map = map;
            threadList = new ThreadItem<T>[last.threadList.Length];
            for (int i = 0; i < threadList.Length; i++)
            {
                threadList[i] = new(tokenSource, map)
                {
                    First = last.threadList[i].First
                };
            }
        }

        public void Add(in string key, in DateTime timeValue, in T value)
        {
            lockSlim.EnterReadLock();
            try
            {
                map.Add(key, value);
                int thread;
                thread = Interlocked.Increment(ref indexerThread) % threadList.Length;

                if (Interlocked.CompareExchange(ref threadList[thread].First, new(key, timeValue), null) != null)
                {
                    LinkedListNodeToRemove<T>? item = threadList[thread].First;

                    SpinWait spinWait = new();
                    while (true)
                    {
                        var next = item?.Next;
                        while (item?.Next is not null && item.Next.TimeValue < timeValue)
                        {
                            item = item.Next;
                            next = item?.Next;
                        }
                        if ((item is not null && Interlocked.CompareExchange(ref item.Next, new(key, timeValue, item.Next), next) == next)
                            || (Interlocked.CompareExchange(ref threadList[thread].First, new(key, timeValue), null) is null))
                            break;
                        spinWait.SpinOnce();
                    }
                }
            }
            finally { lockSlim.ExitReadLock(); }
        }

        public string[] Serialize()
        {
            lockSlim.EnterWriteLock();
            string[] result = [JsonConvert.SerializeObject(this), map.Serialize()];
            lockSlim.ExitWriteLock();
            return result;
        }

        public static ConcurrentLinkedList<T> Deserialize(ConcurrentMap<T> map, string last)
        {
            var f = JsonConvert.DeserializeObject<ConcurrentLinkedList<T>>(last)!;
            return new(map, f);
        }

        public void Dispose()
        {
            tokenSource.Cancel();
            foreach (var item in threadList)
            {
                item.Dispose();
            }
            tokenSource.Dispose();
            lockSlim.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    [DataContract]
    sealed internal class ThreadItem<T> : IDisposable
    {
        [NonSerialized]
        public LinkedListNodeToRemove<T>? First = null;

        [DataMember]
        public LinkedListNodeToRemove<T>[] List 
        { 
            get 
            {
                var item = First;
                int count = 0;
                while(item is not null)
                {
                    count++;
                    item = item.Next;
                }
                item = First;
                var array = new LinkedListNodeToRemove<T>[count];
                for(int i = 0; i < count; i++)
                {
                    array[i] = item!;
                    item = item!.Next;
                }
                return array;
            }
            set
            {
                if (value is not null && value.Length != 0)
                {
                    First = value[0];
                    var item = First;
                    for (int i = 1; i < value.Length; i++)
                    {
                        item.Next = value[i];
                        item = item.Next;
                    }
                }
            }
        }

        [NonSerialized]
        private readonly Thread Thread = default!;
        public ThreadItem(CancellationTokenSource tokenSource, ConcurrentMap<T> map)
        {
            Thread = new(() =>
            {
                SpinWait spinWait = new();
                while (!tokenSource.IsCancellationRequested)
                {
                    while (!tokenSource.IsCancellationRequested)
                    {
                        var item = First;
                        if (item is not null && item.TimeValue < DateTime.Now)
                            if (Interlocked.CompareExchange(ref First, First!.Next, item) == item)
                            {
                                map.Remove(item.Key);
                                break;
                            }
                        spinWait.SpinOnce();
                    }
                }
            });
            Thread.Start();
        }

        public ThreadItem() { }

        public void Dispose()
        {
            Thread.Join();
            GC.SuppressFinalize(this);
        }
    }

    [DataContract]
    sealed internal class LinkedListNodeToRemove<T>
    {
        [NonSerialized]
        internal LinkedListNodeToRemove<T>? Next;
        [DataMember]
        private string key;
        internal string Key { get { return key; } }
        [DataMember]
        private DateTime timeValue;
        internal DateTime TimeValue { get { return timeValue; } }
        internal LinkedListNodeToRemove(in string key, in DateTime timeValue)
        {
            this.key = key;
            this.timeValue = timeValue;
        }

        internal LinkedListNodeToRemove(in string key, in DateTime timeValue, LinkedListNodeToRemove<T>? next)
        {
            this.key = key;
            this.timeValue = timeValue;
            this.Next = next;
        }

#pragma warning disable CS8618 // constructor for deserializing a class without null fields.
        public LinkedListNodeToRemove() { }
#pragma warning restore CS8618
    }
}
