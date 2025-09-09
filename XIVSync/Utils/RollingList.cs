using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace XIVSync.Utils;

public class RollingList<T> : IEnumerable<T>, IEnumerable
{
	private readonly object _addLock = new object();

	private readonly LinkedList<T> _list = new LinkedList<T>();

	public int Count => _list.Count;

	public int MaximumCount { get; }

	public T this[int index]
	{
		get
		{
			if (index < 0 || index >= Count)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			return _list.Skip(index).First();
		}
	}

	public RollingList(int maximumCount)
	{
		if (maximumCount <= 0)
		{
			throw new ArgumentException(null, "maximumCount");
		}
		MaximumCount = maximumCount;
	}

	public void Add(T value)
	{
		lock (_addLock)
		{
			if (_list.Count == MaximumCount)
			{
				_list.RemoveFirst();
			}
			_list.AddLast(value);
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _list.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}
