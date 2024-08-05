using System.Collections;
using System.Collections.Generic;
using ecs;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public class Logger : IWorldLogger
  {
    private string   _prefix;
    private LogFlags _flags;

    public Logger(string prefix, LogFlags flags)
    {
      _prefix = prefix;
      _flags = flags;
    }

    public LogFlags GetLogFlags()
    {
      return _flags;
    }

    public bool HasFlag(LogFlags flag)
    {
      return (_flags & flag) == flag;
    }

    public void Log(LogFlags categories, string s)
    {
      if ((_flags & categories) != LogFlags.None)
      {
        Log(s);
      }
    }

    public void Log(string s)
    {
      //Debug.Log($"{_prefix}: {s}");
    }
        
    public void LogError(string s)
    {
      //Debug.LogError($"{_prefix}: {s}");
    }

    public void LogWarning(string s)
    {
      //Debug.LogWarning($"{_prefix}: {s}");
    }
  }
}