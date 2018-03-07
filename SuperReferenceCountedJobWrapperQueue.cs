using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace FunctionZero.ThreadManagerZero
{
	internal class SuperReferenceCountedJobWrapperQueue : IEnumerable<JobWrapper>
	{
		private readonly ThreadSafeQueue<JobWrapper> _queue;
		private readonly ReferenceCountedList<string> _refCount;
		private readonly object _refCountLock = new object();

		public SuperReferenceCountedJobWrapperQueue(int maxQueueSize)
		{
			_queue = new ThreadSafeQueue<JobWrapper>(maxQueueSize);
			_refCount = new ReferenceCountedList<string>("JobsByName");

			_queue.QueueSizeChangedEvent += QueueSizeChangedEventHandler;
		}

		/// <summary>
		/// DANGER. The queue is suspended whilst in this event handler.
		/// Deadlock will occur if the event handler attemts to queue or dequeue an item.
		/// </summary>
		public EventHandler<int> QueueSizeChangedEvent { get; set; }

		/// <summary>
		/// DANGER. The queue is suspended whilst in this event handler.
		/// Deadlock will occur if the event handler attempts to queue or dequeue an item.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="i"></param>
		private void QueueSizeChangedEventHandler(object sender, int i)
		{
			QueueSizeChangedEvent?.Invoke(this, i);
		}

		public int Count
		{// MAKE ATOMIC
			get
			{
				return _queue.Count;
			}
		}

		public IEnumerator<JobWrapper> GetEnumerator()
		{
			return _queue.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		private void ItemRemoved(object sender, ReferenceCountedListEventArgs<string> e)
		{
			Debug.WriteLine("Removed: " + e.Item);
		}

		private void ItemAdded(object sender, ReferenceCountedListEventArgs<string> e)
		{
			Debug.WriteLine("Added: " + e.Item);
		}

		/// <summary>
		/// This will block until the queue is not full.
		/// </summary>
		/// <param name="jobWrapper"></param>
		/// <returns>The number of jobs in the queue.</returns>
		public int Enqueue(JobWrapper jobWrapper)
		{
			// Race Condition: Modify the refCount BEFORE queueing the job so we cannot try to uncount it before it is counted.
			lock (_refCountLock)
			{
				_refCount.AddItem(jobWrapper.WrappedJob.Name);
			}
			return _queue.Enqueue(jobWrapper);
		}

		internal int Clear()
		{
			return _queue.Clear();
		}

		/// <summary>
		///     This will block until there is something in the queue to dequeue.
		/// </summary>
		/// <returns>The size of the queue after dequeueing.</returns>
		public int Dequeue(out JobWrapper dequeuedItem)
		{
			JobWrapper item;
			int count = _queue.Dequeue(out item);
			// When the job completes, remove it from the reference counter.
			item.WrappedJob.JobCompletedEvent += WrappedJob_JobCompletedEvent;
			dequeuedItem = item;
			return count;
		}

		private void WrappedJob_JobCompletedEvent(object sender, JobEventArgs e)
		{
			Job j = (Job)sender;
			lock (_refCountLock)
			{
				_refCount.RemoveItem(j.Name);
			}
		}

		public int GetNamedJobCount(string strName)
		{
			lock (_refCountLock)
			{
				return _refCount.ItemCount(strName);
			}
		}

		private readonly object _modifySuspendedStateLock = new object();

		public void Resume()
		{
			lock (_modifySuspendedStateLock)
			{
				_queue.Resume();
			}
		}

		public void Suspend()
		{
			lock (_modifySuspendedStateLock)
			{
				_queue.Suspend();
			}
		}


		/// <summary>
		/// Note while we're in this method we might have threads queueing and dequeueing concurrently.
		/// Jobs that are already running are unaffected.
		/// </summary>
		/// <param name="name"></param>
		/// <returns>The number of removed jobs.</returns>
		public int ClearQueue(Predicate<JobWrapper> predicate)
		{
			return _queue.Clear(predicate);



			////Debug.Assert(false, "Do I need to look at the state of any semaphores in here?");
			//Debug.WriteLine("Warning: ClearQueueByName: Ensure this method preserves the order of the queue. It might be important, e.g. one thread or if we have 'depends on'.");
			//int startCount;
			//int endCount;
			//// Don't even think about doing this on a 'live' queue!
			//// First ensure this thread has control over suspend/resume.
			//lock (_modifySuspendedStateLock)
			//{
			//	// Because we have _modifySuspendedStateLock other threads cannot suspend or resume the queue. We have full control.
			//	bool wasRunning = _queue.IsRunning();
			//	// Then suspend the queue if it is running. 
			//	if(wasRunning == true)
			//		_queue.Suspend();

			//	startCount = _queue.Count;
			//	// Now the queue is stopped and cannot be restarted, so we can safely mess with it.
			//	// Put the jobs we want to keep into a new queue ...
			//	var jobsToKeep = new Queue<JobWrapper>(_queue.Count);
			//	foreach(JobWrapper wrapper in _queue)
			//	{
			//		if(predicate(wrapper.WrappedJob) == false)
			//			jobsToKeep.Enqueue(wrapper);
			//	}
			//	_queue.Clear();
			//	foreach(var item in jobsToKeep)
			//	{
			//		_queue.Enqueue(item);
			//	}

			//	endCount = _queue.Count;

			//	if(wasRunning == true)
			//		_queue.Resume();
			//}
			//return startCount - endCount;

		}
	}
}