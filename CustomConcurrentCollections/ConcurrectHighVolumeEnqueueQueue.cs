using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;

namespace CustomConcurrentCollections {
    public class ConcurrectHighVolumeEnqueueQueue<T> : IEnumerable<T> {
        volatile Node head;
        volatile Node tail;
        volatile Node? altTail = null;

        public ConcurrectHighVolumeEnqueueQueue() {
            head = tail = new Node();
        }

        public bool TryDequeue(out T item) {
            while (true) {
                Node cachedHead = head;
                bool took = cachedHead.TryTake(out item);

                /*
                 * Trying this whether take succeeded or not because if we take from the tail
                 * then we will return from this method without incrementing the head, so some other
                 * dequeue operation will have to fix that.
                 */
                if (cachedHead.next != null)
                    Interlocked.CompareExchange(ref head, cachedHead.next, cachedHead);

                if (took) {
                    return true;
                } else if (cachedHead.next == null) {
                    //didn't take and no next node to try
                    return false;
                }
            }
        }

        public void Enqueue(T item) {
            Node newNode = new Node(item);

            if (Interlocked.CompareExchange(ref tail.next, newNode, null) == null) {
                tail = newNode;
                return;
            }
            
            /*
             * Using a secondary queue as a buffer that will be merged back in.
             * Provides about 1.8x throughput compared to niave spinlock stuff.
             */
            while (true) {
                Node? cachedAltTail = Interlocked.CompareExchange(ref altTail, newNode, null);
                if (cachedAltTail == null) {
                    //we are the root of the alt queue
                    //so try to reattach to main queue
                    while (Interlocked.CompareExchange(ref tail.next, newNode, null) != null) {
                        Thread.Sleep(16);//todo: duration
                    }
                    tail = Interlocked.Exchange(ref altTail, null)!;
                    break;
                } else if (Interlocked.CompareExchange(ref cachedAltTail.next, newNode, null) == null) {
                    //cachedAlt queue must be altTail or tail here
                    if (cachedAltTail == Interlocked.CompareExchange(
                            location1: ref altTail,
                            value: newNode,
                            comparand: cachedAltTail)) {
                        break;
                    }

                    //if it's not the altTale then it either is or is about to be the real tale
                    while (cachedAltTail != Interlocked.CompareExchange(
                            location1: ref tail,
                            value: newNode,
                            comparand: cachedAltTail)) {
                        Thread.Sleep(1);
                    }
                    break;
                } else {
                    //retry
                    Thread.Sleep(1);
                }
            }
        }

        public IEnumerator<T> GetEnumerator() {
            Node? current = head;
            while ((current = current.next) != null) {
                yield return current.value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private class Node {
            internal T value;
            internal volatile Node? next;
            private int noValue = 0; //0 = false, 1 = true

            internal bool HasValue {
                get => noValue == 0;
            }

            /// <summary>
            ///     Tries to take the value atomically.
            /// </summary>
            /// <returns>true if we took the value, false if it isn't there to take</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]//todo: make sure this is inlined
            internal bool TryTake(out T result) {
                bool changed = 0 == Interlocked.CompareExchange(ref noValue, 1, 0);
                if (changed) {
                    result = value;
#pragma warning disable CS8601
                    value = default;
#pragma warning restore CS8601
                } else {
#pragma warning disable CS8601
                    result = default;
#pragma warning restore CS8601
                }
                return changed;
            }

            internal Node(T value) {
                this.value = value;
            }

            internal Node() {
                noValue = 1;
#pragma warning disable CS8601
                value = default;
#pragma warning restore CS8601
            }
        }

    }
}
