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

        public void Enqueue(T item) {
            Node newNode = new Node(item);

            if (Interlocked.CompareExchange(ref tail.next, newNode, null) == null) {
                tail = newNode;
                return;
            }

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
            //DEBUG: just here as an alternative to alt queue to compare to naive implementation performance
            //SpinWait spin = default;
            //while(true) {
            //    spin.SpinOnce(-1);
            //    if (Interlocked.CompareExchange(ref tail.next, newNode, null) == null) {
            //        tail = newNode;
            //        return;
            //    }
            //}
        }

        public IEnumerator<T> GetEnumerator() {
            Node? current = head;
            while ((current = current.next) != null) {
                yield return current.value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private class Node {
            internal readonly T value;
            internal volatile Node? next;

            internal Node(T value) {
                this.value = value;
            }

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            internal Node() { } //only here for head
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        }

    }
}
