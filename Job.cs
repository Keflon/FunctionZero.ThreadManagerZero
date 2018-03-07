using System;
using System.Threading;

namespace FunctionZero.ThreadManagerZero
{
	/// <summary>
	/// Wrapper class for a job that can be queued in a ThreadManager
	/// </summary>
	public class Job
	{
		public string Name { get; private set; }
		private readonly Action<object> _jobAction;
		private readonly ManualResetEvent _jobStartedSignal;
		private readonly ManualResetEvent _jobCompletedSignal;

		/// <summary>
		/// 	This event is called when the job starts.
		/// </summary>
		/// TODO: Untested.
		public event EventHandler<JobEventArgs> JobStartedEvent;

		/// <summary>
		/// 	This event is called when the job completes.
		/// </summary>
		/// TODO: Untested.
		public event EventHandler<JobEventArgs> JobCompletedEvent;

		/// <summary>
		/// 	This event is called if the job fails.
		/// </summary>
		/// TODO: Untested.
		public event EventHandler<JobEventArgs> JobFailedEvent;

		/// <summary>
		/// 	Represents an argument passed to the job when it is created. It is passed into the job Action by the DoWork method. (If the jobAction is a lambda then the lambda parameter is sustituted with the param field. That's how lambdas work.) It is passed to the JobCompletedEvent on completion of the job.
		/// </summary>
		private readonly object _param;

		/// <summary>
		/// 	Represents a job that can be queued for execution.
		/// </summary>
		/// <param name="name"> A friendly name for the job. </param>
		/// <param name="jobAction"> The action that the job will invoke. The action takes a single parameter that represents a state object </param>
		/// <param name="jobCompletedEvent"> An event that fires when the job has been completed </param>
		/// <param name="param"> An optional state object for the job. </param>
		public Job(string name, Action<object> jobAction, EventHandler<JobEventArgs> jobCompletedEvent, object param)
		{
			Name = name;
			_jobAction = jobAction;
			JobCompletedEvent += jobCompletedEvent;

			_param = param;

			_jobStartedSignal = new ManualResetEvent(false);
			_jobCompletedSignal = new ManualResetEvent(false);
		}


		/// <summary>
		/// 	Blocks the current thread until the job starts. Do not call before adding the job to the job queue.
		/// </summary>
		/// <param name="timeout"> Milliseconds to wait. 0 returns immediately and -1 waits indefinitely </param>
		/// <returns> True if the job has started, otherwise false </returns>
		public bool JobstartedWaitOne(int timeout)
		{
			return this._jobStartedSignal.WaitOne(timeout);
		}

		/// <summary>
		/// 	Blocks the current thread until the job ends. Do not call before adding the job to the job queue.
		/// </summary>
		/// <param name="timeout"> Milliseconds to wait. 0 returns immediately and -1 waits indefinitely </param>
		/// <returns> True if the job has started, otherwise false </returns>
		public bool JobCompletedWaitOne(int timeout)
		{
			return this._jobCompletedSignal.WaitOne(timeout);
		}

		private Exception _exception = null;

		internal void DoWork()
		{
			_jobStartedSignal.Set();

			try
			{
				if(JobStartedEvent != null)
				{
					//JobStartedEvent(this, new JobEventArgs(JobState.Started, _param));
					SafeRaiseEvent<JobEventArgs>(JobStartedEvent, new JobEventArgs(JobState.Started, _param));
				}

				if(_jobAction != null)
				{
					try
					{
						_jobAction(_param);
					}
					catch(Exception ex)
					{
						throw new Exception("The queued job failed. See InnerException", ex);
					}
				}

				if(JobCompletedEvent != null)
				{
					//JobCompletedEvent(this, new JobEventArgs(JobState.Finished, _param));
					SafeRaiseEvent<JobEventArgs>(JobCompletedEvent, new JobEventArgs(JobState.Finished, _param));
				}
			}
			catch(Exception ex)
			{
				_exception = ex;
				//JobFailedEvent(this, new JobEventArgs(JobState.Failed, _param));
				SafeRaiseEvent<JobEventArgs>(JobFailedEvent, new JobEventArgs(JobState.Failed, _param));

				throw;
			}
			finally
			{
				_jobCompletedSignal.Set();
			}
		}

		private object _safeRaiseEventLock = new object();

		protected virtual void SafeRaiseEvent<TEventArgs>(EventHandler<TEventArgs> eventHandler, TEventArgs args)
		{
			EventHandler<TEventArgs> safeEventHandler = null;
			lock(_safeRaiseEventLock)
			//lock the current instance so that other threads cannot change del.
			{
				safeEventHandler = eventHandler;
			}
			if(safeEventHandler != null)
			{
				foreach(EventHandler<TEventArgs> handler in safeEventHandler.GetInvocationList())
				{
					try
					{
						handler(this, args);
					}
					catch(Exception e)
					{
						Console.WriteLine("Error in the handler {0}: {1}",
						handler.Method.Name, e.Message);
					}
				}
			}
		}
	}
}