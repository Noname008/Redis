using Windows.Devices.Radios;
using System.Linq;
using Xunit.Abstractions;
using System;
using System.Diagnostics;

namespace RedisLib.Tests
{
    public class RedisTest(ITestOutputHelper output)
    {
        private readonly ITestOutputHelper output = output;
        private readonly int[] Values = [100000, 200000, 400000, 800000, 1600000];

        [Theory]
        [MemberData(nameof(GetData), parameters: 11)]
        public void ParallelAdd(int threads)
        {
            foreach(var count in Values)
            {
                using Redis<int> Redis = new(count);
                Stopwatch stopwatch = Stopwatch.StartNew();
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = threads }, x =>
                {
                    Redis.Set(x.ToString(), x);
                });
                stopwatch.Stop();
                output.WriteLine("Count = {0}, Time(ms) = {1}", count, stopwatch.ElapsedMilliseconds);
            }
        }

        [Theory]
        [MemberData(nameof(GetData), parameters: 11)]
        public void ParallelRemove(int threads)
        {
            int deletedTime = 1;
            foreach (var count in Values)
            {
                using Redis<int> Redis = new(length: count, ThreadCount: threads);
                Stopwatch stopwatch = Stopwatch.StartNew();
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = threads }, x =>
                {
                    Redis.Set(x.ToString(), x, deletedTime);
                });
                stopwatch.Stop();
                long timeAdd = stopwatch.ElapsedMilliseconds;
                stopwatch.Restart();
                while (true)
                {
                    try
                    {
                        Redis.Get((count - 1).ToString());
                    }
                    catch { break; }
                }
                stopwatch.Stop();
                output.WriteLine
                (
                    "Count = {0}, Time add(ms) = {1}, Delete time(ms) = {2}, Fact delete time(last item)(ms) = {3}",
                    count, timeAdd, deletedTime, stopwatch.ElapsedMilliseconds
                );
            }
        }

        [Fact]
        public void ParallelRemoveSThreads()
        {
            int deletedTime = 1000;
            Random random = Random.Shared;
            var count = 1000;
            using Redis<int> Redis = new(length: count, ThreadCount: 16);
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 32 }, x =>
            {
                Redis.Set(x.ToString(), x, random.Next(deletedTime));
            });
            Thread.Sleep(deletedTime + 100);
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 32 }, x =>
            {
                Assert.Throws<Exception>(() => Redis.Get(x.ToString()));
            });
        }

        [Fact]
        public void Serialize()
        {
            string ser;
            int count = 20;
            using (Redis<int> Redis = new(ThreadCount: 16, length: count))
            {
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 16 }, x =>
                {
                    Redis.Set(x.ToString(), x, 200);
                });
                ser = Redis.Serialize();
            }

            using Redis<int> r = Redis<int>.Deserialize(ser);
            string active = "active: ";
            string inactive = "inactive: ";
            for (int x = 0; x < count; x++)
            {
                try
                {
                    active += r.Get(x + "") + " ";
                }
                catch
                {
                    inactive += x + " ";
                }
            }
            output.WriteLine(active);
            output.WriteLine(inactive);
            active = "active: ";
            inactive = "inactive: ";
            Thread.Sleep(200);
            for (int x = 0; x < count; x++)
            {
                try
                {
                    active += r.Get(x + "") + " ";
                }
                catch
                {
                    inactive += x + " ";
                }
            }
            output.WriteLine(active);
            output.WriteLine(inactive);
        }

        [Fact]
        public void SerializeRuntime()
        {
            string ser;
            int count = 20;
            using (Redis<int> Redis = new(ThreadCount: 1, length: count))
            {
                Task[] t =
                [
                    Task.Run(() =>
                    {
                        for (int x = 0; x < count; x+=3)
                        {
                            Redis.Set(x.ToString(), x, 1000);
                            Thread.Sleep(1);
                        }
                    }),
                    Task.Run(() =>
                    {
                        for (int x = 1; x < count; x+=3)
                        {
                            Redis.Set(x.ToString(), x, 1000);
                            Thread.Sleep(1);
                        }
                    }),
                    Task.Run(() =>
                    {
                        for (int x = 2; x < count; x+=3)
                        {
                            Redis.Set(x.ToString(), x, 1000);
                            Thread.Sleep(1);
                        }
                    })
                ];
                Thread.Sleep(35);
                ser = Redis.Serialize();    ///12-15 items
                foreach(var x in t) x.Wait();
            }
            using Redis<int> r = Redis<int>.Deserialize(ser);
            string active = "active: ";
            string inactive = "inactive: ";
            for (int x = 0; x < count; x++)
            {
                try
                {
                    active += r.Get(x + "") + " ";
                }
                catch
                {
                    inactive += x + " ";
                }
            }
            output.WriteLine(active);
            output.WriteLine(inactive);
            active = "active: ";
            inactive = "inactive: ";
            Thread.Sleep(1000);
            for (int x = 0; x < count; x++)
            {
                try
                {
                    active += r.Get(x + "") + " ";
                }
                catch
                {
                    inactive += x + " ";
                }
            }
            output.WriteLine(active);
            output.WriteLine(inactive);
        }

        public static IEnumerable<object[]> GetData(int NumberTest)
        {
            return new object[][] { [1], [2], [4], [8], [16], [32], [64], [128], [256], [512], [1024] }.Take(NumberTest);
        }
    }
}