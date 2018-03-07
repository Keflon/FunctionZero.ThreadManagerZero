using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FunctionZero.ThreadManagerZero
{
	public class ReferenceCountedList<T> : /*INotifyCollectionChanged,*/ /*INotifyPropertyChanged,*/ IEnumerable<T>
	{
		public override string ToString()
		{
			return base.ToString() + " Name:" + Name;
		}

		public string Name { get; private set; }

		/// <summary>
		/// Stores a dictionary of items against a reference count.
		/// </summary>
		private readonly Dictionary<T, int> _usageMonitor = new Dictionary<T, int>();

		/// <summary>
		/// Event raised when an item is actually added.
		/// </summary>
		public EventHandler<ReferenceCountedListEventArgs<T>> ItemAdded;

		/// <summary>
		/// Event raised when an item is actually removed.
		/// </summary>
		public EventHandler<ReferenceCountedListEventArgs<T>> ItemRemoved;

		/// <summary>
		/// Event raised when an item count is changed, including to and from 0.
		/// </summary>
		public EventHandler<ReferenceCountedListEventArgs<T>> ItemCountChanged;


		public ReferenceCountedList(string strName)
		{
			Name = strName;
		}

		/// <summary>
		/// Increments the reference count for the supplied item unless it isn't in the monitor
		/// in which case it is both added with a reference count of 1 and the ItemAdded event is raised.
		/// </summary>
		/// <param name="item">The item to add.</param>
		/// <returns>true if the item is added or false if the item is already present.</returns>
		public bool AddItem(T item)
		{
			return ChangeItemCountByDelta(item, 1) == 1;

		}

		/// <summary>
		/// Decrements the reference count for the supplied item. If it reaches 0
		/// the item is removed from the monitor and the ItemRemoved event is raised.
		/// </summary>
		/// <param name="item">The item to remove</param>
		/// <returns>true if the item is removed from the list, otherwise false (meaning the reference count is decremented)</returns>
		public bool RemoveItem(T item)
		{
			return ChangeItemCountByDelta(item, -1) == 0;
		}

		/// <summary>
		/// Sets the item count for a supplied item to an explicit value.
		/// If that means the item is added, the ItemAdded event is raised.
		/// If that means the item is removed, the ItemRemoved event is raised.
		/// </summary>
		/// <param name="item">The item whose count will be modified</param>
		/// <param name="count">The new value for the item count</param>
		/// <returns></returns>
		public void SetItemCount(T item, int count)
		{
			int oldCount;
			if(_usageMonitor.TryGetValue(item, out oldCount) == true)
			{
				Debug.Assert(oldCount != 0, "Zero counted item in _usageMonitor in ReferenceCountedList!");
			}
			ChangeItemCount(item, oldCount, count);
		}

		/// <summary>
		/// d
		/// </summary>
		/// <param name="item"></param>
		/// <param name="delta"></param>
		/// <returns>New count</returns>
		public int ChangeItemCountByDelta(T item, int delta)
		{
			if(delta == 0)
				return 0;

			int count;
			if(_usageMonitor.TryGetValue(item, out count) == true)
			{
				Debug.Assert(count != 0, "Zero counted item in _usageMonitor in ReferenceCountedList!");
			}
			ChangeItemCount(item, count, count + delta);
			return count + delta;
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ChangeItemCount(T item, int oldCount, int newCount)
		{
			Debug.Assert(oldCount != newCount, "oldCount == newCount!");
			if(newCount >= 0)
			{
				ItemCountChanged?.Invoke(this, new ReferenceCountedListEventArgs<T>(item, ActionType.Changed, oldCount, newCount));

				if(oldCount == 0)
				{
					// This line could be done before the if, but isn't necessary if newCount == 0.
					_usageMonitor[item] = newCount;
					ItemAdded?.Invoke(this, new ReferenceCountedListEventArgs<T>(item, ActionType.Added, oldCount, newCount));
				}
				else if(newCount == 0)
				{
					_usageMonitor.Remove(item);
					ItemRemoved?.Invoke(this, new ReferenceCountedListEventArgs<T>(item, ActionType.Removed, oldCount, newCount));
				}
				else
					// This line could be done before the if, but isn't necessary if newCount == 0.
					_usageMonitor[item] = newCount;
			}
			else
			{
				throw new ArgumentOutOfRangeException(nameof(newCount));
			}
		}


		public bool ContainsItem(T item)
		{
			return (_usageMonitor.ContainsKey(item));
		}

		public int ItemCount(T item)
		{
			int count = 0;
			_usageMonitor.TryGetValue(item, out count);
			return count;
		}


		public int Count { get { return _usageMonitor.Keys.Count; } }


		#region IEnumerable

		public virtual IEnumerator<T> GetEnumerator()
		{
			return _usageMonitor.Keys.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#endregion
	}

	public enum ActionType
	{
		Added = 0,
		Removed,
		Changed
	}

	public class ReferenceCountedListEventArgs<T> : EventArgs
	{
		public T Item { get; }
		public ActionType ActionType { get; }
		public int OldCount { get; }
		public int NewCount { get; }

		public ReferenceCountedListEventArgs(T item, ActionType actionType, int oldCount, int newCount)
		{
			Item = item;
			ActionType = actionType;
			OldCount = oldCount;
			NewCount = newCount;
		}
	}
}
