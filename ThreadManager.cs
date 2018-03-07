using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace FunctionZero.ThreadManagerZero
{
	[Obsolete]
	public class ThreadManager : IThreadManager
	{
		/// <summary>
		/// 	This is the queue of pending jobs.
		/// </summary>
		private readonly ReferenceCountedJobWrapperQueue _jobQueue;

		/// <summary>
		/// 	This is the queue of available worker threads.
		/// </summary>
		private readonly Queue<GenericThread> _threadAvailableList;

		/// <summary>
		/// 	This is the queue of busy worker threads.
		/// </summary>
		private readonly List<GenericThread> _threadBusyList;

		/// <summary>
		/// 	The number of worker threads this instance contains.
		/// </summary>
		private readonly int _threadCount;

		/// <summary>
		/// 	If true, the job queue is suspended and no new jobs are sent to the worker threads.
		/// </summary>
		private bool _bSuspendQueue;


		/// <summary>
		/// 	Creates a thread manager and initialises it with GenericThreads.
		/// </summary>
		/// <param name="threadCount"> The number of worker threads this instance contains. </param>
		public ThreadManager(int threadCount)
		{
			if(threadCount < 1)
			{
				throw new ArgumentOutOfRangeException("threadCount", "threadCount must be greater than 0.");
			}
			else
			{
				_threadCount = threadCount;

				_threadAvailableList = new Queue<GenericThread>(threadCount);
				_threadBusyList = new List<GenericThread>(threadCount);
				_jobQueue = new ReferenceCountedJobWrapperQueue();



				for(int c = 0; c < _threadCount; c++)
				{
					_threadAvailableList.Enqueue(new GenericThread(JobFinished));
				}
			}
		}

		#region Public methods.

		public ManualResetEvent QueueIsEmpty { get; }
		public ManualResetEvent ThreadsAreIdle { get; }

		/// <summary>
		/// 	Adds a new job to the job queue and gives the queue a poke because it may be idle.
		/// </summary>
		/// <param name="job"> </param>
		/// <param name="skipIfJobNameAlreadyQueued"></param>
		public long QueueJob(Job job, bool skipIfJobNameAlreadyQueued = false)
		{
			int oldQueueSize;
			long retval;

			lock(_jobQueue)
			{
				oldQueueSize = _jobQueue.Count;
				retval = _queueJob(job, skipIfJobNameAlreadyQueued);
			}
			if(retval != -1)
			{
				OnThreadManagerQueueSizeChangedEvent(oldQueueSize, oldQueueSize + 1); // Keep this outside of the lock.
				PokeTheQueue();
			}
			return retval;
		}

		private long _queueJob(Job job, bool skipIfJobNameAlreadyQueued)
		{
			//Debug.WriteLine("Adding Job named '"+job.Name+"'. Queue Size: " + _jobQueue.Count);
			bool skipIt = false;
			if(skipIfJobNameAlreadyQueued)
				skipIt = _jobQueue.GetNamedJobCount(job.Name) != 0;
			if(!skipIt)
			{
				JobWrapper jobWrapper = new JobWrapper(job);
				long retval = jobWrapper.Guid;
				_jobQueue.Enqueue(jobWrapper);
				return retval;
			}
			else
			{
				Debug.WriteLine("Skipped: "+job.Name);
			}
			return -1;
		}

		/// <summary>
		/// 	Adds a load of new jobs to the job queue and gives the queue a poke because it may be idle.
		/// </summary>
		/// <param name="jobList"> A collection of jobs to be queued. </param>
		/// <param name="skipIfJobNameAlreadyQueued"></param>
		public IEnumerable<long> QueueJobList(IEnumerable<Job> jobList, bool skipIfJobNameAlreadyQueued = false)
		{
			Debug.WriteLine("Adding " + jobList.Count() + " jobs. Queue Size: " + _jobQueue.Count);
			int oldJobCount, newJobCunt;
			List<long> queuedJobGuidList = new List<long>();
			lock(_jobQueue)
			{
				oldJobCount = _jobQueue.Count;
				foreach(var job in jobList)
				{
					long jobGuid = _queueJob(job, skipIfJobNameAlreadyQueued);
					if(jobGuid != -1)
						queuedJobGuidList.Add(jobGuid);
				}
				newJobCunt = _jobQueue.Count;
				Debug.Assert(newJobCunt == oldJobCount + queuedJobGuidList.Count());
			}
			if(oldJobCount != newJobCunt) // Keep this outside of the lock.
				OnThreadManagerQueueSizeChangedEvent(oldJobCount, newJobCunt);
			PokeTheQueue();
			return queuedJobGuidList;
		}

		/// <summary>
		/// 	Suspends the job queue so that no new jobs are sent to the worker threads. Jobs still queue up in the job queue.
		/// </summary>
		public void SuspendQueue()
		{
			_bSuspendQueue = true;
		}

		/// <summary>
		/// 	Resumes processing of jobs in the job queue to the worker threads.
		/// </summary>
		public void ResumeQueue()
		{
			_bSuspendQueue = false;
			PokeTheQueue();
		}

		/// <summary>
		/// 	Clears any pending jobs in the job queue.
		/// </summary>
		/// <returns> The number of cleared jobs. </returns>
		public int ClearQueue()
		{
			int oldQueueSize;
			lock(_jobQueue)
			{
				oldQueueSize = _jobQueue.Count;
				_jobQueue.Clear();
			}
			if(oldQueueSize != 0) // Keep this outside of the lock.
				OnThreadManagerQueueSizeChangedEvent(oldQueueSize, 0); // Keep this outside of the lock.
			return oldQueueSize;
		}

		public int ClearQueue(Predicate<JobWrapper> predicate)
		{
			throw new NotImplementedException();
		}


		public int ClearQueueByName(string name)
		{
			// TODO: Does this maintain the order of the queue?
			var jobsToKeep = new Queue<JobWrapper>(_jobQueue.Count);
			int oldQueueSize, newQueueSize;
			lock(_jobQueue)
			{
				oldQueueSize = _jobQueue.Count;
				foreach(JobWrapper item in _jobQueue)
				{
					if(item.WrappedJob.Name != name)
						jobsToKeep.Enqueue(item);
				}
				_jobQueue.Clear();
				foreach(var item in jobsToKeep)
				{
					_jobQueue.Enqueue(item);
				}
				newQueueSize = _jobQueue.Count;
			}
			if(oldQueueSize != newQueueSize) // Keep this outside of the lock.
				OnThreadManagerQueueSizeChangedEvent(oldQueueSize, newQueueSize);

			return _jobQueue.Count;
		}


		/// <summary>
		/// 	Doesn't work properly. Don't use or you risk deadlock. At a glance I suspect at the very least it needs more locks.
		/// </summary>
		/// <returns> </returns>
		public int ClearThreads()
		{
			int numClearedThreads;
			List<GenericThread> threadBusyListCopy;
			lock(_threadBusyList)
			{
				threadBusyListCopy = new List<GenericThread>(_threadBusyList.Count);
				numClearedThreads = _threadBusyList.Count;
				foreach(GenericThread gt in _threadBusyList)
				{
					threadBusyListCopy.Add(gt);
				}
			}
			foreach(GenericThread genericThread in threadBusyListCopy)
			{
				genericThread.Reset();
			}
			return numClearedThreads;
		}

		#endregion Public methods.

		public object ThreadCount
		{
			get { return this._threadCount; }
		}

		#region IThreadManager Members

		public EventHandler<SizeChangedEventArgs> QueueSizeChangedEvent { get; set; }
		// Not implemented.
		public EventHandler<SizeChangedEventArgs> ThreadBusyCountChangedEvent { get; set; }

		#endregion

		protected void OnThreadManagerQueueSizeChangedEvent(int oldSize, int newSize)
		{
			QueueSizeChangedEvent?.Invoke(this, new SizeChangedEventArgs(oldSize, newSize));
		}

		/// <summary>
		/// 	This is called by the worker thread when it finishes it's current job. It adds the worker thread back into the queue of available threads and if there are any outstanding jobs it makes sure that one is started.
		/// </summary>
		/// <param name="sender"> This should always be a reference to the GenericThread that has finished. </param>
		/// <param name="e"> Nothing interesting in here. </param>
		private void JobFinished(object sender, EventArgs e)
		{
			Debug.Assert(sender is GenericThread);
			// Add the thread back into the 'free' list.
			lock(_threadAvailableList)
			{
				lock(_threadBusyList)
				{
					_threadAvailableList.Enqueue((GenericThread)sender);
					_threadBusyList.Remove((GenericThread)sender);
				}
			}
			PokeTheQueue();
		}


		/// <summary>
		/// 	If the job queue is not empty and there is an available worker thread then the next job is sent to the worker thread.
		/// </summary>
		private void PokeTheQueue()
		{
			if(_bSuspendQueue == false)
			{
				int oldQueueSize, newQueueSize;
				lock(_jobQueue)
				{
					oldQueueSize = _jobQueue.Count;
					if(_jobQueue.Count > 0)
					{
						lock(_threadAvailableList)
						{
							// Use a 'while' rather than an 'if' because although this function is called each time a job is queued
							// and each time a worker thread becomes available and there therefore the worker threads should always
							// be saturated, if the queue is suspended then jobs aren't picked out of the queue when threads become available.
							while(_threadAvailableList.Count > 0 && _jobQueue.Count > 0)
							{
								GenericThread genericThread = _threadAvailableList.Dequeue();
								lock(_threadBusyList)
								{
									_threadBusyList.Add(genericThread);
								}
								Job nextJob = _jobQueue.Dequeue().WrappedJob;
								//Debug.WriteLine("Starting Job named '"+nextJob.Name+"'. Queue Size: " + _jobQueue.Count);

								genericThread.StartJob(nextJob);
							}
						}
					}
					newQueueSize = _jobQueue.Count;
				}
				if(oldQueueSize != newQueueSize) // Keep this outside of the lock.
					OnThreadManagerQueueSizeChangedEvent(_jobQueue.Count + 1, _jobQueue.Count);
			}
		}
	}
}