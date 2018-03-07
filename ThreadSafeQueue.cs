using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

// See also ConcurrentQueue<>.
// This one is better because it has a size limit and can block on Enqueue / Dequeue.

// TODO: GetEnumerator - Lock the queue or somehow else ensure the queue can't be modified while the enumerator is active?
// TODO: QueueIsRunning can cause race conditions.(?)

namespace FunctionZero.ThreadManagerZero
{
	// Notes:
	// Enqueue can only block on 'lock(_enqueueLock)'.
	// Dequeue can only block on 'lock(_dequeueLock)'.
	// Therefore, deadlock cannot occur between queueing and dequeueing.
	//
	// Both Enqueue and Dequeue acquire _queueModifyLock, but neither can block within that lock.
	//
	// ManualResetEvents are only changed inside the _queueModifyLock.
	// Therefore race conditions cannot occur between Enqueue and Dequeue.
	//
	// Needs a thorough test. Ought to think of a unit test!

	public class ThreadSafeQueue<T> : IEnumerable<T>, INotifyPropertyChanged
	{
		public int MaxSize { get; set; }
		public int Count { get { return _theQueue.Count; } }

		private Queue<T> _theQueue;

		private readonly ManualResetEvent _queueHasSpaceForMore;
		private readonly ManualResetEvent _queueHasSomethingInIt;

		private readonly object _enqueueLock = new object();
		private readonly object _dequeueLock = new object();
		private readonly object _queueModifyLock = new object();

		/// <summary>
		/// DANGER. The queue is suspended whilst in this event handler.
		/// Deadlock will occur if the event handler attemts to queue or dequeue an item.
		/// </summary>
		public EventHandler<int> QueueSizeChangedEvent { get; set; }

		public ThreadSafeQueue(int maxSize, bool startSuspended = false)
		{
			_queueIsRunning = new ManualResetEvent(!startSuspended);

			_queueHasSomethingInIt = new ManualResetEvent(false);
			_queueHasSpaceForMore = new ManualResetEvent(true);
			MaxSize = maxSize;
			_theQueue = new Queue<T>(Math.Min(100, MaxSize));   // int.MaxValue is used for unbounded queues.
		}

		/// <summary>
		/// This will block until there is space in the queue.
		/// </summary>
		/// <param name="item"></param>
		public int Enqueue(T item)
		{
			// Do not enqueue if the queue is suspended.
			_queueIsRunning.WaitOne();

			lock (_enqueueLock)
			{
				_queueHasSpaceForMore.WaitOne();
				lock (_queueModifyLock)
				{
					if(_theQueue.Count == 0)
						_queueHasSomethingInIt.Set(); // It doesn't actually have something in it until the next line but because we have the _queueModifyLock nothing will be able to dequeue.
					_theQueue.Enqueue(item);
					if(_theQueue.Count == MaxSize)
						_queueHasSpaceForMore.Reset();

					// The queue is locked.
					// This will deadlock if the event handler tries to modify the queue.
					QueueSizeChangedEvent?.Invoke(this, _theQueue.Count);
					return _theQueue.Count;
				}
			}
		}

		/// <summary>
		/// This will block until there is something in the queue to dequeue.
		/// </summary>
		/// <returns>The size of the queue after dequeueing.</returns>
		public int Dequeue(out T dequeuedItem)
		{
			// Do not dequeue if the queue is suspended.
			_queueIsRunning.WaitOne();
			lock (_dequeueLock)
			{
				while(true)
				{
					// Only one thread can be waiting here, because we have the dequeueLock.
					_queueHasSomethingInIt.WaitOne();

					// The queue might be reset here. That's why we're in a while loop.

					lock (_queueModifyLock)
					{
						// If the queue hasn't been reset ...
						if(_theQueue.Count != 0)
						{

							if(_theQueue.Count == MaxSize)
								_queueHasSpaceForMore.Set();    // It doesn't actually have space for more until the next line but because we have the _queueModifyLock nothing will be able to enqueue.

							var retval = _theQueue.Dequeue();
							if(_theQueue.Count == 0)
							{
								_queueHasSomethingInIt.Reset();
							}
							// The queue is locked.
							// This will deadlock if the event handler tries to modify the queue.
							QueueSizeChangedEvent?.Invoke(this, _theQueue.Count);
							dequeuedItem = retval;
							return _theQueue.Count;
						}
					}
				}
			}
		}

		/// <summary>
		/// Wait until the queue is not empty.
		/// DANGER: When signalled, something else might empty the queue before the waiting thread can get to the content!
		/// </summary>
		/// <param name="timeout">Milliseconds, or -1 (default) for infinite timeout.</param>
		public void WaitForQueueContent(int timeout = Timeout.Infinite)
		{
			_queueHasSomethingInIt.WaitOne(timeout);
		}

		private readonly ManualResetEvent _queueIsRunning;

		public void Resume()
		{
			_queueIsRunning.Set();
		}

		public void Suspend()
		{
			_queueIsRunning.Reset();
		}

		public bool IsRunning()
		{
			return _queueIsRunning.WaitOne(0);
		}

		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		/// <summary>
		/// Clears the queue of all items.
		/// </summary>
		/// <returns>The number of items cleared.</returns>
		public int Clear()
		{
			int retVal;
			lock (_queueModifyLock)
			{
				retVal = _theQueue.Count;
				_theQueue.Clear();
				// The queue is locked.
				// This will deadlock if the event handler tries to modify the queue.
				QueueSizeChangedEvent?.Invoke(this, _theQueue.Count);
				_queueHasSomethingInIt.Reset();
				_queueHasSpaceForMore.Set();
			}
			return retVal;
		}

		/// <summary>
		/// Clears the queue of any items that match the predicate.
		/// </summary>
		/// <param name="predicate">Predicate used to decide whether an item can stay.</param>
		/// <returns>The number of items removed.</returns>
		public int Clear(Predicate<T> predicate)
		{
			lock (_queueModifyLock)
			{
				//Debug.Assert(false, "Do I need to look at the state of any semaphores in here?");
				Debug.WriteLine("Warning: Clear Queue By Predicate: Ensure this method preserves the order of the queue. It might be important, e.g. one thread or if we have 'depends on'.");
				int startCount;
				int endCount;

				startCount = _theQueue.Count;
				// Now the queue is locked, so we can safely mess with it.
				// Put the jobs we want to keep into a new queue ...
				var thingsToKeep = new Queue<T>(_theQueue.Count);
				foreach(T thing in _theQueue)
				{
					if(predicate(thing) == true)
						thingsToKeep.Enqueue(thing);
				}
				_theQueue.Clear();
				foreach(var item in thingsToKeep)
				{
					_theQueue.Enqueue(item);
				}

				endCount = _theQueue.Count;

				// The queue is locked.
				// This will deadlock if the event handler tries to queue an item.
				QueueSizeChangedEvent?.Invoke(this, _theQueue.Count);
				if(_theQueue.Count == 0)
					_queueHasSomethingInIt.Reset();
				else
					_queueHasSomethingInIt.Set();

				if(_theQueue.Count == MaxSize)
					_queueHasSpaceForMore.Reset();
				else
					_queueHasSpaceForMore.Set();

				return startCount - endCount;
			}
		}

		public IEnumerator<T> GetEnumerator()
		{
			lock (_queueModifyLock) // ???
			{
				return _theQueue.GetEnumerator();
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
