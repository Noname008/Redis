using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace RedisLib
{
    internal abstract class MapBase<T>
    {
        private protected UInt64 Length;
        private protected Bucket<T>[] buckets;

        protected MapBase(UInt64 length)
        {
            Length = length;
            buckets = new Bucket<T>[Length];
            buckets.Initialize();
        }

        public abstract int Count { get; }
        internal abstract void Add(in string key, in T value);
        internal abstract T this[string key] { get; }
        internal abstract void Remove(in string key);
        private protected abstract int GetCodeToHash(in UInt64 hash);
        private protected abstract void Resize(double multiplier);
    }

    internal class Map<T> : MapBase<T>
    {
        public override int Count { get { return buckets.AsParallel().Sum(x => x.countCollisions); } }

        private protected Map(UInt64 length) : base(length) { }

        private protected override int GetCodeToHash(in UInt64 hash)
        {
            return (int)((hash & 0x7fffffff) % Length);
        }

        internal override void Add(in string key, in T value)
        {
            ref Bucket<T> bucket = ref buckets[GetCodeToHash(CalculateHash(key))];
            lock (bucket.locker)
            {
                if (bucket.next is null) bucket.next = new(key, value);
                else
                {
                    Item<T> item = bucket.next;
                    if (item.Equals(key)) throw new Exception("Item already exists");
                    for (int i = 1; i < bucket.countCollisions; i++)
                    {
                        if (item.Equals(key)) throw new Exception("Item already exists");
                        item = item.Next!;
                    }
                    item.Next = new(key, value);
                }
                bucket.countCollisions++;
            }
        }

        internal override T this[string key]
        {
            get
            {
                ref Bucket<T> bucket = ref buckets[GetCodeToHash(CalculateHash(key))];
                lock (bucket.locker)
                {
                    Item<T>? item = bucket.next;
                    while (item is not null)
                    {
                        if (item.Equals(key))
                        {
                            return item.Value;
                        }
                        item = item.Next;
                    }
                    throw new Exception("Item not found, Key:" + key);
                }
            }
        }

        static UInt64 CalculateHash(string read)
        {
            UInt64 hashedValue = 3074457345618258791ul;
            for (int i = 0; i < read.Length; i++)
            {
                hashedValue += read[i];
                hashedValue *= 3074457345618258799ul;
            }
            return hashedValue;
        }

        internal override void Remove(in string key)
        {
            ref Bucket<T> bucket = ref buckets[GetCodeToHash(CalculateHash(key))];
            lock (bucket.locker)
            {
                if (bucket.next is not null && bucket.next.Equals(key))
                {
                    bucket.next = bucket.next.Next;
                    bucket.countCollisions--;
                }
                else
                {
                    Item<T>? item = bucket.next;
                    for (int i = 1; i < bucket.countCollisions; i++)
                    {
                        if (item!.Next!.Equals(key))
                        {
                            item.Next = item.Next.Next;
                            bucket.countCollisions--;
                            return;
                        }
                        item = item.Next!;
                    }
                    throw new Exception("Element to be deleted was not found, Key:" + key);
                }
            }
        }

        private protected override void Resize(double multiplier = 2)
        {
            Length = (ulong)(buckets.Length * multiplier > int.MaxValue / 2 ? int.MaxValue / 2 : (int)(buckets.Length * multiplier));
            var buffer = buckets.AsParallel().Where((x) => x.next != null).ToArray();
            buckets = new Bucket<T>[Length];
            buckets.Initialize();
            Parallel.For(0, buffer.Length, (x) =>
            {
                Item<T>? item = buffer[x].next;
                for (int i = 0; i < buffer[x].countCollisions; i++)
                {
                    Add(item!.Key!, item.Value);
                    item = item.Next;
                }
            });
        }
    }

    internal class ConcurrentMap<T> : Map<T>, IDisposable
    {
        private readonly ReaderWriterLockSlim rwLock;
        private readonly Thread ThreadCheckAndResizeMap;
        private readonly CancellationTokenSource tokenSource;

        internal ConcurrentMap(UInt64 length) : base(length)
        {
            tokenSource = new();
            rwLock = new ReaderWriterLockSlim();
            ThreadCheckAndResizeMap = new(() =>
            {
                while (tokenSource.IsCancellationRequested)
                {
                    if ((UInt64)Count > Length)
                    {
                        rwLock.EnterWriteLock();
                        base.Resize();
                        rwLock.ExitWriteLock();
                    }
                }
            });
            ThreadCheckAndResizeMap.Start();
        }

        internal override void Add(in string key, in T value)
        {
            rwLock.EnterReadLock();
            try
            {
                base.Add(key, value);
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        internal override T this[string key]
        {
            get
            {
                rwLock.EnterReadLock();
                try
                {
                    return base[key];
                }
                finally
                {
                    rwLock.ExitReadLock();
                }
            }
        }

        internal override void Remove(in string key)
        {
            rwLock.EnterReadLock();
            try
            {
                base.Remove(key);
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        public string Serialize()
        {
            rwLock.EnterWriteLock();
            try
            {
                return JsonConvert.SerializeObject(new string[] { JsonConvert.SerializeObject(buckets), JsonConvert.SerializeObject(Length) });
            }
            finally 
            { 
                rwLock.ExitWriteLock();
            }
        }

        public static ConcurrentMap<T> Deserialize(string file)
        {
            string[] data = JsonConvert.DeserializeObject<string[]>(file)!;
            Bucket<T>[] _buckets = JsonConvert.DeserializeObject<Bucket<T>[]>(data[0])!;
            int length = JsonConvert.DeserializeObject<int>(data[1])!;
            return new ConcurrentMap<T>((UInt64)_buckets.Length) { buckets = _buckets, Length = (UInt64)length };
        }

        public void Dispose()
        {
            rwLock.Dispose();
            tokenSource.Cancel();
            ThreadCheckAndResizeMap.Join();
            tokenSource.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    [DataContract]
    internal struct Bucket<T>
    {
        [DataMember]
        internal object locker;
        [DataMember]
        internal int countCollisions = 0;
        [DataMember]
        internal Item<T>? next;

        public Bucket()
        {
            locker = new object();
        }
    }

    [DataContract]
    internal class Item<T>
    {
        [DataMember]
        internal readonly string Key = default!;
        [DataMember]
        internal T Value { get; set; } = default!;
        [DataMember]
        public Item<T>? Next { get; set; } = default!;

        internal Item(in string key, in T value)
        {
            Key = key;
            Value = value;
        }

        internal Item() { }

        public new bool Equals(object? obj)
        {
            Item<T>? other = obj as Item<T>;
            return other?.Key == Key;
        }

        public bool Equals(in string key)
        {
            return Key == key;
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }
    }
}

