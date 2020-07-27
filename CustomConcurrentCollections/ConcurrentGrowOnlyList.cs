#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CustomConcurrentCollections {

    /// <summary>
    /// Fully lock-free(including enumeration) thread-safe list.
    /// Basically a read-only list but you can add to the end and replace existing values.
    /// </summary>
    public sealed class ConcurrentGrowOnlyList<T> : 
        System.Collections.Generic.ICollection<T>, 
        System.Collections.Generic.IEnumerable<T>, 
        System.Collections.Generic.IList<T>, 
        System.Collections.Generic.IReadOnlyCollection<T>, 
        System.Collections.Generic.IReadOnlyList<T>, 
        System.Collections.IList where T : class {

        private T[] _valueArr;
        private bool[] _hasValueArr;//code depends on this always being grown before values array grows
        private int theoreticalCapacity;//not actual capacity because arrays might not be updated yet
        private int fullyAddedCount = 0;
        private int nextIndex = 0;//can't reliably be used as a count because adders increment this BEFORE they actually add items

        public int Capacity
            //Only need to check value array and not has value array because has value array is updated first.
            => VolatileValueArr.Length;

        public int Count 
            => Volatile.Read(ref fullyAddedCount);

        private T[] VolatileValueArr {
            get => Volatile.Read(ref _valueArr);
            set => Volatile.Write(ref _valueArr, value);
        }

        private bool[] VolatileHasValueArr {
            get => Volatile.Read(ref _hasValueArr);
            set => Volatile.Write(ref _hasValueArr, value);
        }

        public ConcurrentGrowOnlyList(int initialCapacity = 16) {
            Volatile.Write(ref theoreticalCapacity, initialCapacity);
            VolatileHasValueArr = new bool[initialCapacity];
            VolatileValueArr = new T[initialCapacity];
        }

        /// <summary>
        /// Adds an item to the end of the list.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <exception cref="OverflowException">Can't increase capacity beyond int.MaxValue.</exception>
        public void Add(T item) => AddAndGetIndex(item);

        /// <summary>
        /// Adds an item to the end of the list and returns the index.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <returns>Index of added item.</returns>
        /// <exception cref="OverflowException">Can't increase capacity beyond int.MaxValue.</exception>
        public int AddAndGetIndex(T item) {
            int index = Interlocked.Increment(ref nextIndex) - 1;

            EnsureCapacity(index);

            Volatile.Write(ref VolatileValueArr[index], item);
            Volatile.Write(ref VolatileHasValueArr[index], true);

            //increment count as far as we can, giving up when needed to let previous adds do it
            while (
                index == Interlocked.CompareExchange(ref fullyAddedCount, index + 1, index) &&
                ++index < Volatile.Read(ref nextIndex) &&
                index < Capacity &&
                Volatile.Read(ref VolatileHasValueArr[index])
            ) ;

            return index;
        }

        /// <summary>
        ///     Grows the capacity when needed, or waits for another thread to do it.
        /// </summary>
        /// <exception cref="OverflowException"></exception>
        private void EnsureCapacity(int index) {
            while (true) {
                int cachedCapacity = Volatile.Read(ref theoreticalCapacity);

                if (index < cachedCapacity) {
                    //Doesn't need to grow, just wait for other grow/grows.
                    while (index >= Capacity) Thread.Sleep(1);
                    break;
                }

                /* 
                 * Spin until actual capacity is caught up, signaling that
                 * previous grows on other threads are done.
                 */
                while (Capacity < cachedCapacity) Thread.Sleep(1);

                if (TryToGrow(cachedCapacity)) {
                    break;
                }
            }
        }

        /// <exception cref="OverflowException"></exception>
        private bool TryToGrow(int cachedCapacity) {
            if (cachedCapacity == int.MaxValue)
                throw new OverflowException();

            const int MIN_VAL_THAT_WILL_OVERFLOW_IF_DOUBLED = 1073741824;
            int newCapacity = (cachedCapacity < MIN_VAL_THAT_WILL_OVERFLOW_IF_DOUBLED) ?
                    cachedCapacity * 2 :
                    int.MaxValue;

            bool growing = cachedCapacity == Interlocked.CompareExchange(
                ref theoreticalCapacity, newCapacity, cachedCapacity);
            if (growing) {
                T[] newArr = new T[newCapacity];
                bool[] newHasValue = new bool[newCapacity];
                //safe to cache the arrays here because we are the only thread that could change the ref
                T[] oldArr = VolatileValueArr;
                bool[] oldHasValue = VolatileHasValueArr;


                //copy values
                for (int i = 0; i < cachedCapacity; i++) {
                    //spin until set
                    while (!Volatile.Read(ref oldHasValue[i])) ;
                    newArr[i] = Volatile.Read(ref oldArr[i]);
                    newHasValue[i] = true;
                }

                /*
                 * Need to assign has value array first because other code 
                 * assumes it will be set by the time the value array is set.
                 */
                VolatileHasValueArr = newHasValue;
                VolatileValueArr = newArr;
            }

            return growing;
        }

        /// <summary>
        ///     Iterates over the collection.
        /// </summary>
        /// <param name="from">
        ///     inclusive minimum, can be larger than current count in which case this
        ///     wont produce anything unless the collection grows from other threads.
        /// </param>
        /// <param name="to">
        ///     inclusive maximum, can be larger than current count in which case 
        ///     we wont get those values unless the collection grows from other threads.
        /// </param>
        /// <exception cref="ArgumentException"> from > to || from < 0 || to < 0 </exception>
        /// <returns>The Enumerator</returns>
        public IEnumerator<T> GetEnumerator(int from, int to) {
            if (from < 0) throw new ArgumentException("from < 0");
            if (from < 0) throw new ArgumentException("from < 0");
            if (from > to) throw new ArgumentException("from > to");
            return GetEnumeratorUnchecked(to, from);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IEnumerator<T> GetEnumeratorUnchecked(int from, int to) {
            int i = from;
            int max = Math.Min(to, Count - 1);
            while (i <= max) {
                yield return Volatile.Read(ref VolatileValueArr[i]);
                i++;
            }
        }

        public IEnumerator<T> GetEnumerator() => GetEnumeratorUnchecked(0, int.MaxValue);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumeratorUnchecked(0, int.MaxValue);

        public bool Contains(T item) {
            //need to get the count first so it doesn't end up bigger than the array
            var cachedCount = Count;
            var cachedValues = VolatileValueArr;

            if (item == null) {
                for (int i = 0; i < cachedCount; i++)
                    if (Volatile.Read(ref cachedValues[i]) == null)
                        return true;
                return false;
            } else {
                EqualityComparer<T> c = EqualityComparer<T>.Default;
                for (int i = 0; i < cachedCount; i++)
                    if (c.Equals(Volatile.Read(ref cachedValues[i]), item))
                        return true;
                return false;
            }
        }

        public int IndexOf(T value) {
            //need to get the count first so it doesn't end up bigger than the array
            int cachedCount = Count;
            var cachedValues = VolatileValueArr;

            if (value == null) {
                for (int i = 0; i < cachedCount; i++)
                    if (Volatile.Read(ref cachedValues[i]) == null)
                        return i;
                return -1;
            } else {
                EqualityComparer<T> c = EqualityComparer<T>.Default;
                for (int i = 0; i < cachedCount; i++)
                    if (c.Equals(Volatile.Read(ref cachedValues[i]), value))
                        return i;
                return -1;
            }
        }

        public void CopyTo(T[] array, int index) {
            if (array == null) throw new ArgumentNullException("array");
            if (index < 0) throw new ArgumentOutOfRangeException("index");

            //need to cache count before values because we don't want array to grow bigger than count
            int cachedCount = Count;
            if (cachedCount > array.Length - index) throw new ArgumentException("not enough space");

            T[] cachedValues = VolatileValueArr;

            for (int i = 0; i < cachedCount; i++) {
                array[index + i] = Volatile.Read(ref cachedValues[i]);
            }
        }

        public T this[int index] { 
            get {
                if (index >= Count) throw new ArgumentOutOfRangeException("index");
                return Volatile.Read(ref VolatileValueArr[index]);
            }
            set {
                /*
                 * this checks not just the bounds of the array but also that the item has already been written, 
                 * so we don't update it before the add operation finishes
                 */
                if (index >= Count) throw new ArgumentOutOfRangeException("index");
                Volatile.Write(ref VolatileValueArr[index], value);
            }
        }

        /// <summary>
        ///     Compares two instances of the specified reference type T for reference equality
        ///     and, if they are equal, replaces the first one.
        /// </summary>
        /// <param name="index">Index of the item to replace.</param>
        /// <param name="value">New value to replace old with.</param>
        /// <param name="comparand">Value compared to original.</param>
        /// <returns>The original value.</returns>
        public T CompareAndSwap(int index, T value, T comparand) {
            if (index >= Count) throw new IndexOutOfRangeException();
            return Interlocked.CompareExchange(ref VolatileValueArr[index], value, comparand);
        }

        void ICollection<T>.Clear() {
            throw new NotImplementedException();
        }

        bool ICollection<T>.Remove(T item) {
            throw new NotImplementedException();
        }

        void IList<T>.Insert(int index, T item) {
            throw new NotImplementedException();
        }

        void IList<T>.RemoveAt(int index) {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This returns true because no writing operations are supported except <see cref="Add(T)"/> and <see cref="this[int]"/>
        /// and returning false would lead preexisting code to call other write methods.
        /// </summary>
        bool ICollection<T>.IsReadOnly => true;

        bool ICollection.IsSynchronized { get; }
        object ICollection.SyncRoot { get; }

        object IList.this[int index] {
            get => this[index];
            set => this[index] = (T)value;
        }

        int IList.Add(object value) => AddAndGetIndex((T)value);
        void IList.Clear() => throw new NotImplementedException();
        bool IList.Contains(object value) => Contains((T)value);
        int IList.IndexOf(object value) => IndexOf((T)value);
        void IList.Insert(int index, object value) => throw new NotImplementedException();
        void IList.Remove(object value) => throw new NotImplementedException();
        void IList.RemoveAt(int index) => throw new NotImplementedException();

        void ICollection.CopyTo(Array array, int index) => CopyTo((T[])array, index);


        bool IList.IsFixedSize { get; }
        bool IList.IsReadOnly { get; }

    }
}
