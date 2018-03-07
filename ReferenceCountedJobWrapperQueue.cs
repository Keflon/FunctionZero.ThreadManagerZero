using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace FunctionZero.ThreadManagerZero
{
	internal class ReferenceCountedJobWrapperQueue : IEnumerable<JobWrapper>
	{
		private readonly Queue<JobWrapper> _queue;
		private readonly ReferenceCountedList<string> _refCount;

		public ReferenceCountedJobWrapperQueue()
		{
			_queue = new Queue<JobWrapper>();
			_refCount = new ReferenceCountedList<string>("JobsByName");
			//refCount.ItemAdded += ItemAdded;		// For test.
			//refCount.ItemRemoved += ItemRemoved;	// For test.
		}

		private void ItemRemoved(object sender, ReferenceCountedListEventArgs<string> e)
		{
			Debug.WriteLine("Removed: "+e.Item);

		}

		private void ItemAdded(object sender, ReferenceCountedListEventArgs<string> e)
		{
			Debug.WriteLine("Added: "+e.Item);
		}

		public int Count { get { return _queue.Count; } }

		object _refCountLock = new object();
		public void Enqueue(JobWrapper jobWrapper)
		{
			_queue.Enqueue(jobWrapper);
			lock(_refCountLock)
			{
				_refCount.AddItem(jobWrapper.WrappedJob.Name);
			}
		}

		internal void Clear()
		{
			_queue.Clear();
		}

		public JobWrapper Dequeue()
		{
			JobWrapper item =_queue.Dequeue();
			// When the job completes, remove it from the reference counter.
			item.WrappedJob.JobCompletedEvent += (sender, args) =>
			{
				lock(_refCountLock)
				{
					_refCount.RemoveItem(item.WrappedJob.Name);
				}
			};

			return item;
		}

		public IEnumerator<JobWrapper> GetEnumerator()
		{
			return _queue.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public int GetNamedJobCount(string strName)
		{
			lock(_refCountLock)
			{
				return _refCount.ItemCount(strName);
			}
		}
	}
}
