using System;

namespace FunctionZero.ThreadManagerZero
{
	public class JobEventArgs : EventArgs
	{
		private readonly JobState _jobstate;
		private readonly object _userState;

		public JobEventArgs(JobState jobstate, object userState)
		{
			_jobstate = jobstate;
			_userState = userState;
		}

		public JobState Jobstate { get { return _jobstate; } }
		public object UserState { get { return _userState; } }
	}
}