using System;
using System.Collections.Generic;
using System.Threading;

namespace FunctionZero.ThreadManagerZero
{
	public interface IThreadManager
	{
		/// <summary>
		// DO NOT MODIFY THE QUEUE IN THIS CALLBACK!
		/// </summary>
		EventHandler<SizeChangedEventArgs> QueueSizeChangedEvent { get; set; }
		EventHandler<SizeChangedEventArgs> ThreadBusyCountChangedEvent { get; set; }
		ManualResetEvent QueueIsEmpty { get; }
		ManualResetEvent ThreadsAreIdle { get; }

		/// <summary>
		/// Adds a new job to the job queue and gives the queue a poke because it may be idle.
		/// </summary>
		/// <param name="job"></param>
		/// <param name="skipIfJobNameAlreadyQueued"></param>
		/// <returns>A unique job id.</returns>
		long QueueJob(Job job, bool skipIfJobNameAlreadyQueued = false);

		/// <summary>
		/// 	Adds a collection of new jobs to the job queue and gives the queue a poke because it may be idle.
		/// </summary>
		/// <param name="jobList"> </param>
		/// <param name="skipIfJobNameAlreadyQueued"></param>
		IEnumerable<long> QueueJobList(IEnumerable<Job> jobList, bool skipIfJobNameAlreadyQueued = false);

		/// <summary>
		/// 	Suspends the job queue so that no new jobs are sent to the worker threads. Jobs still queue up in the job queue.
		/// </summary>
		void SuspendQueue();

		/// <summary>
		/// 	Resumes processing of jobs in the job queue to the worker threads.
		/// </summary>
		void ResumeQueue();

		/// <summary>
		/// 	Clears any pending jobs in the job queue.
		/// </summary>
		/// <returns> </returns>
		int ClearQueue();
		
		/// <summary>
		/// 	Clears any pending jobs in the job queue if they meet the predicate requirements.
		/// </summary>
		/// <returns> </returns>
		int ClearQueue(Predicate<JobWrapper> predicate);

		/// <summary>
		/// 	Doesn't work properly. Don't use or you risk deadlock.
		/// </summary>
		/// <returns> </returns>
		int ClearThreads();
	}

	public enum JobState
	{
		Unknown = 0,
		Started,
		Running,
		Finished,
		Failed
	}

	public class SizeChangedEventArgs : EventArgs
	{
		private readonly int _newSize;
		private readonly int _oldSize;

		public SizeChangedEventArgs(int oldSize, int newSize)
		{
			_oldSize = oldSize;
			_newSize = newSize;
		}

		public int NewSize
		{
			get { return _newSize; }
		}

		public int OldSize
		{
			get { return _oldSize; }
		}
	}
}