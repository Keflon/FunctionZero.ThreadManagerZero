using System;
using System.Diagnostics;
using System.Threading;

namespace FunctionZero.ThreadManagerZero
{
	/// <summary>
	/// Wrapper for a thread used by a ThreadManager
	/// </summary>
	internal class GenericThread
	{
		private static int _count = 0;
		private readonly Thread _thread;
		private readonly AutoResetEvent _threadNewCommandSet = new AutoResetEvent(false);
		private Job _currentJob;

		internal GenericThread(EventHandler threadFinishedEventHandler)
		{
			JobFinished += threadFinishedEventHandler;

			_thread = new Thread(ThreadProc) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "Worker " + _count++.ToString() };
			// Threaddy steady GO!
			_thread.Start();
		}

		private event EventHandler JobFinished;

		protected virtual void OnJobFinished(EventArgs e)
		{
			JobFinished?.Invoke(this, e);
		}

		internal void Reset()
		{
			if(_threadNewCommandSet.WaitOne(0, false) == false) // If the thread is busy ... BUG: Race condition?
			{
				_thread.Abort();
				// Not really Abort. Hopefully the thread will catch the raised exception and just abort the current callback.
			}
		}

		internal bool StartJob(Job job)
		{
			//TODO: Reset the thread first?
			// TODO: Make sure the thread is not already busy?
			//Debug.Assert(_threadNewCommandSet.WaitOne(0, false) == false);		// Does this reset the event?

			_currentJob = job;
			_threadNewCommandSet.Set();
			return true;
		}

		private void ThreadProc()
		{
			bool bAbortThread = false;

			while(bAbortThread == false)
			{
				try
				{
					_threadNewCommandSet.WaitOne(); // Wait until a job has been assigned to this thread.
													//Debug.WriteLine(_thread.Name + " started job " + _currentJob.Name);
					_currentJob.DoWork(); // This is the user-defined function the thread is to run.
				}
				catch(Exception ex)
				{
					if((ex is ThreadAbortException) || (ex.InnerException is ThreadAbortException))
					{
						Thread.ResetAbort();
						bAbortThread = true;
						Debug.WriteLine("Thread function Aborted.");
					}
				}
				finally
				{
					try
					{
						// TODO: Pass args to show whether the thread was aborted.
						OnJobFinished(EventArgs.Empty);
					}
					catch(Exception ex)
					{
						Debug.WriteLine("Exception in GenericThread OnJobFinished event: " + ex.Message);
						if(ex.InnerException != null)
						{
							Debug.WriteLine("Inner Exception: " + ex.InnerException.Message);
						}
					}
				}
			}
			Debug.WriteLine("You're in trouble.");
		}
	}
}