using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace FunctionZero.ThreadManagerZero
{
	public class SuperThreadManager : IThreadManager
	{
		private readonly SuperReferenceCountedJobWrapperQueue _jobQueue;

		/// <summary>
		///     This is the queue of available worker threads.
		/// </summary>
		private readonly ThreadSafeQueue<GenericThread> _threadAvailableList;

		/// <summary>
		///     This is the queue of busy worker threads.
		/// </summary>
		private readonly List<GenericThread> _threadBusyList;

		/// <summary>
		///     The number of worker threads this instance contains.
		/// </summary>
		private readonly int _threadCount;

		private readonly Thread _siphonThread;

		public SuperThreadManager(int threadCount, int maxJobQueueSize = int.MaxValue)
		{
			QueueIsEmpty = new ManualResetEvent(true);
			ThreadsAreIdle = new ManualResetEvent(true);
			if(threadCount < 1)
			{
				throw new ArgumentOutOfRangeException("threadCount", "threadCount must be greater than 0.");
			}
			else
			{
				_threadCount = threadCount;

				_threadAvailableList = new ThreadSafeQueue<GenericThread>(threadCount);
				_threadBusyList = new List<GenericThread>(threadCount);
				_jobQueue = new SuperReferenceCountedJobWrapperQueue(maxJobQueueSize);
				_jobQueue.QueueSizeChangedEvent += JobQueueQueueSizeChangedEvent;
				for(int c = 0; c < _threadCount; c++)
				{
					_threadAvailableList.Enqueue(new GenericThread(JobFinished));
				}
			}

			_siphonThread = new Thread(SiphonThread)
			{
				IsBackground = true,
				Priority = ThreadPriority.Lowest,
				Name = "ThreadSiphon"
			};
			// Threaddy steady GO!
			_siphonThread.Start();
		}

		private int _oldSize = 0;

		/// <summary>
		/// This is called from within the JobQueue and it has QueueModifyLock. Do not try to enqueue or dequeue anything or you risk deadlock.
		/// We can safely interrogate the queue because it is ModifyLocked. It might also be EnqueueLocked or DequeueLocked, but not both.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="newSize"></param>
		private void JobQueueQueueSizeChangedEvent(object sender, int newSize)
		{
			// QueueModifyLock is held, meaning the queue cannot be modified, therefore it's safe to 
			// make decisions based on the size of the queue and it is safe to raise an event without the queue size changing during the event.
			if(newSize == 0)
				QueueIsEmpty.Set();
			else if(_oldSize == 0)
				QueueIsEmpty.Reset();

			// This event must not try to enqueue or dequeue anything in the JobQueue.
			QueueSizeChangedEvent?.Invoke(this, new SizeChangedEventArgs(_oldSize, newSize));

		}

		public object ModifyQueueEmptyLock = new object();

		public ManualResetEvent QueueIsEmpty { get; }
		public ManualResetEvent ThreadsAreIdle { get; }

		private readonly object _queueSizeChangedLock = new object();
		private readonly object _threadBusyListLock = new object();

		/// <summary>
		// DO NOT MODIFY THE QUEUE IN THIS CALLBACK!
		/// </summary>
		public EventHandler<SizeChangedEventArgs> QueueSizeChangedEvent { get; set; }
		public EventHandler<SizeChangedEventArgs> ThreadBusyCountChangedEvent { get; set; }

		/// <summary>
		/// Adds a new job to the job queue.
		/// This will block until there is space in the queue.
		/// </summary>
		/// <param name="job"> </param>
		/// <param name="skipIfJobNameAlreadyQueued"></param>
		/// <returns>The guid associated with the newly queued Job, or -1 if it was skipped.</returns>
		public long QueueJob(Job job, bool skipIfJobNameAlreadyQueued = false)
		{
			bool skipIt = false;
			if(skipIfJobNameAlreadyQueued)
				skipIt = _jobQueue.GetNamedJobCount(job.Name) != 0;
			if(!skipIt)
			{
				JobWrapper jobWrapper = new JobWrapper(job);
				_jobQueue.Enqueue(jobWrapper); // This will block until there is space in the queue.
				return jobWrapper.Guid;
			}
			else
			{
				Debug.WriteLine("Skipped: " + job.Name);
			}
			return -1;
		}

		/// <summary>
		/// Queues a list of Job instances.
		/// </summary>
		/// <param name="jobList">The list of Job instances to queue.</param>
		/// <param name="skipIfJobNameAlreadyQueued">If true, skips Jobs if their name matches an existing queued Job.</param>
		/// <returns>A List of guids associated with the newly queued Job instances. -1 means a Job was skipped.</returns>
		public IEnumerable<long> QueueJobList(IEnumerable<Job> jobList, bool skipIfJobNameAlreadyQueued = false)
		{
			List<long> jobGuidList = new List<long>();

			foreach(Job j in jobList)
			{
				long jobGuid = QueueJob(j, skipIfJobNameAlreadyQueued);
				if(jobGuid != -1)
					jobGuidList.Add(jobGuid);
			}
			return jobGuidList;
		}

		protected void OnThreadBusyCountChangedEvent(int oldSize, int newSize)
		{
			ThreadBusyCountChangedEvent?.Invoke(this, new SizeChangedEventArgs(oldSize, newSize));
		}



		public void SuspendQueue()
		{
			_jobQueue.Suspend();
		}

		public void ResumeQueue()
		{
			_jobQueue.Resume();
		}

		public int ClearQueue()
		{
			int oldCount = _jobQueue.Clear();
			return oldCount;
		}

		/// <summary>
		/// Removes any Jobs that satisfy the predicate.
		/// </summary>
		/// <param name="predicate"></param>
		/// <returns>The number of removed Jobs.</returns>
		public int ClearQueue(Predicate<JobWrapper> predicate)
		{
			int startCount = _jobQueue.Count;
			_jobQueue.ClearQueue(predicate);
			int endCount = _jobQueue.Count;
			return startCount - endCount;
		}

		public int ActiveJobCount
		{
			get { return _threadBusyList.Count; }
		}

		/// <summary>
		/// Return the number of queued jobs, including if one is waiting in _nextJobWrapper
		/// </summary>
		public int QueueSize
		{
			get
			{
				return _jobQueue.Count;
			}
		}

		public int ClearThreads()
		{
			throw new NotImplementedException();
		}

		//private JobWrapper _nextJobWrapper;

		private void SiphonThread()
		{
			while(true)
			{
				// Get thread.
				// Get job.
				// Do it.

				// TODO: Block until there is both a queued job AND an available thread, otherwise 
				// TODO: if we clear the queue, there'll be one dequeued job 'in limbo' waiting to be given to a thread.
				// TODO: Swap the _jobQueue and _threadAvailableList dequeueing order?

				// Since the _threadAvailableList is only dequeued in this method we can be sure the dequeue further down will not block.
				_threadAvailableList.WaitForQueueContent();

				// This will block until there is a Job in the queue to dequeue.
				// Do it before we acquire the targetThread, so the TargetThread counter doesn't decrement before we're ready to use the thread.
				JobWrapper nextJobWrapper;
				int newCount = _jobQueue.Dequeue(out nextJobWrapper);
				GenericThread targetThread;
				int busyListCount;
				lock (_threadBusyListLock)
				{
					// If we're starting the first thread, the threads now aren't idle.
					if(_threadBusyList.Count == 0)
						this.ThreadsAreIdle.Reset();

					// This is a ThreadSafe queue so we don't need to lock before Dequeueing.
					// This will block until there is something in the queue to dequeue.
					// We've already waited for content (see above), so this will not block!
					_threadAvailableList.Dequeue(out targetThread);
					_threadBusyList.Add(targetThread);
					busyListCount = _threadBusyList.Count;
				}

				OnThreadBusyCountChangedEvent(busyListCount - 1, busyListCount); // Keep this outside of any lock.
				targetThread.StartJob(nextJobWrapper.WrappedJob); // Do the job.
			}
		}

		/// <summary>
		///     This is called by the worker thread when it finishes it's current job. It adds the worker thread back into the
		///     queue of available threads.
		/// </summary>
		/// <param name="sender"> This should always be a reference to the GenericThread that has finished. </param>
		/// <param name="e"> Nothing interesting in here. </param>
		private void JobFinished(object sender, EventArgs e)
		{
			Debug.Assert(sender is GenericThread);
			GenericThread gt = (GenericThread)sender;
			// Add the thread back into the 'free' list.

			int busyListCount;
			lock (_threadBusyListLock)
			{
				bool cock = _threadBusyList.Remove(gt);
				Debug.Assert(cock == true);
				busyListCount = _threadBusyList.Count;

				// If we're ending the last thread, the threads are now idle.
				if(busyListCount == 0)
					this.ThreadsAreIdle.Set();

				// Keep this outside of any lock.
				OnThreadBusyCountChangedEvent(busyListCount + 1, busyListCount);
			}
			// This is a ThreadSafe queue so we don't need to lock before Enqueueing.
			_threadAvailableList.Enqueue(gt);
		}
	}
}