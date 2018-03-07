namespace FunctionZero.ThreadManagerZero
{
	public class JobWrapper
	{
		public Job WrappedJob { get; private set; }
		public long Guid { get; private set; }

		private static long _lastGuid = 0;

		public JobWrapper(Job wrappedJob)
		{
			WrappedJob = wrappedJob;
			Guid = _lastGuid;
			_lastGuid++;
		}
	}
}
