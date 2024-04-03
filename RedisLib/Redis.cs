using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows.Input;

namespace RedisLib
{
    interface IRedis<T>
    {
        public static T Get(string key) => throw new NotImplementedException();
        public static void Set(string key, T value) => throw new NotImplementedException();
        public static void Set(string key, T value, int ex) => throw new NotImplementedException();
    }

    sealed public class Redis<T> : IRedis<T>, IDisposable
    {
        internal readonly ConcurrentMap<T> concurrentMap;
        private readonly ConcurrentLinkedList<T> linkedList;

        public T Get(string key)
        {
            return concurrentMap[key];
        }

        public void Set(string key, T value)
        {
            concurrentMap.Add(key, value);
        }

        public void Set(string key, T value, int exp)
        {
            linkedList.Add(key, DateTime.Now.AddMilliseconds(exp), value);
        }

        public string Serialize()
        {
            return JsonConvert.SerializeObject(linkedList.Serialize());
        }

        public static Redis<T> Deserialize(string str)
        {
            string[] data = JsonConvert.DeserializeObject<string[]>(str)!;
            return new(concurrentMap: data[1], linkedList: data[0]);
        }

        public Redis(int length = 104729, int ThreadCount = 8)
        {
            concurrentMap = new((UInt64)length);
            linkedList = new(ThreadCount, concurrentMap);
        }

        private Redis(string concurrentMap, string linkedList)
        {
            this.concurrentMap = ConcurrentMap<T>.Deserialize(concurrentMap);
            this.linkedList = ConcurrentLinkedList<T>.Deserialize(this.concurrentMap, linkedList);
        }

        public void Dispose()
        {
            linkedList.Dispose();
            concurrentMap.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}