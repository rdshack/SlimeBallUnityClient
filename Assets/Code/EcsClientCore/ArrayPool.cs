using System;
using System.Collections;
using System.Collections.Generic;
using ecs;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public class ArrayPool2<T>
  {
    private static ArrayPool2<T> _shared;
    public static ArrayPool2<T> Shared
    {
      get
      {
        if (_shared == null)
        {
          _shared = new ArrayPool2<T>();
        }

        return _shared;
      }
    }

    private Stack<T[]> _pool;
    private int        _lastSize;
    
    public ArrayPool2()
    {
      _pool = new Stack<T[]>(10);
      for (int i = 0; i < 10; i++)
      {
        _pool.Push(new T[20]);
      }

      _lastSize = 10;
    }

    public T[] Get(int minSize)
    {
      if (_pool.Count == 0)
      {
        _lastSize *= 2;
        for (int i = 0; i < _lastSize; i++)
        {
          _pool.Push(new T[minSize]);
        }
      }

      T[] arr = _pool.Pop();
      if (arr.Length < minSize)
      {
        Array.Resize(ref arr, minSize);
      }

      return arr;
    }

    public void Return(T[] arr)
    {
      _pool.Push(arr);
    }
  }
}