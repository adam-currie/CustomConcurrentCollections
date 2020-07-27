using CustomConcurrentCollections;
using System.Collections.Concurrent;
using System.Threading;
using Xunit;

namespace Tests {
    public class Tests {

        [Theory]
        [InlineData(16, 100000)]
        public void TestConcurrentWriteOnlyOrderedCollection(int threadCount, int insertsPerThread) {
            var collection = new ConcurrentGrowOnlyList<object>();
            Thread[] threads = new Thread[threadCount];

            for (int i = 0; i < threadCount; i++) {
                Thread thread = new Thread((x)
                    => {
                        for (int j=0; j<insertsPerThread; j++)
                            collection.Add(x);
                    });
                threads[i] = thread;
                thread.Start(i);
            }

            foreach (var t in threads) t.Join();

            int[] timesFound = new int[threadCount];
            int index = 0;
            foreach (int item in collection) {
                Assert.True(timesFound[item] < insertsPerThread, "same value inserted too many times: " + item);
                timesFound[item]++;
                index++;
            }

            //checking that we iterated over the correct number of items
            //if we did and no item was repeated too many times, then all items must have been added the correct number of times
            Assert.Equal(index, threadCount * insertsPerThread);
        }

        [Theory]
        [InlineData(16, 100000)]
        public void TestConcurrectHighVolumeEnqueueQueue(int threadCount, int insertsPerThread) {
            var collection = new ConcurrectHighVolumeEnqueueQueue<object>();
            Thread[] threads = new Thread[threadCount];

            for (int i = 0; i < threadCount; i++) {
                Thread thread = new Thread((x)
                    => {
                        for (int j = 0; j < insertsPerThread; j++)
                            collection.Enqueue(x);
                    });
                threads[i] = thread;
                thread.Start(i);
            }

            foreach (var t in threads) t.Join();

            int[] timesFound = new int[threadCount];
            int index = 0;
            foreach (int item in collection) {
                Assert.True(timesFound[item] < insertsPerThread, "same value inserted too many times: " + item);
                timesFound[item]++;
                index++;
            }

            //checking that we iterated over the correct number of items
            //if we did and no item was repeated too many times, then all items must have been added the correct number of times
            Assert.Equal(index, threadCount * insertsPerThread);
        }

    }
}
