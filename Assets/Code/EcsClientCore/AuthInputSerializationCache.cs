using System;
using System.Buffers;
using System.Collections.Generic;
using ecs;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public class AuthInputSerializationCache
  {
    private ClientSyncer     _clientSyncer;
    private IFrameSerializer _frameSerializer;
    private ArchetypeGraph   _archetypeGraph;
    

    private Dictionary<int, SerializedAuthInputData> _cacheData = new Dictionary<int, SerializedAuthInputData>();
    private int                                      _minKey    = int.MaxValue;

    private FrameToAckData _recycledFrameToAckData = new FrameToAckData();

    private ObjPool<FrameSyncData> _frameSyncDataPool =
      new ObjPool<FrameSyncData>(FrameSyncData.Create, FrameSyncData.Reset);

    private uint                 _authInputLockId;
    private AliasLookup          _aliasLookup;
    private ComponentDefinitions _componentDefinitions;

    public AuthInputSerializationCache(ClientSyncer syncer, IFrameSerializer frameSerializer, ArchetypeGraph archetypeGraph, IComponentFactory pool)
    {
      _clientSyncer = syncer;
      _frameSerializer = frameSerializer;
      _archetypeGraph = archetypeGraph;

      _authInputLockId = _clientSyncer.RegisterFullInputLock();
      Debug.Log($"Serialization cache Register full input lock {_authInputLockId}");
      
      _aliasLookup = new AliasLookup();
      _componentDefinitions = new ComponentDefinitions();

      _recycledFrameToAckData.Setup(pool, _aliasLookup, _componentDefinitions);
    }

    public SerializedAuthInputData GetSerializedInput(int frame)
    {
      if (_cacheData.TryGetValue(frame, out SerializedAuthInputData data))
      {
        return data;
      }
      
      _recycledFrameToAckData.Reset();
      if (!_clientSyncer.TryGetFullSyncData(frame, _recycledFrameToAckData))
      {
        throw new Exception();
      }

      List<IComponentGroup> componentGroups = new List<IComponentGroup>(_recycledFrameToAckData.mergedInputData.GetComponentGroups());
      FrameSyncData frameSyncData = _frameSyncDataPool.Get();
      frameSyncData.Init(frame, _recycledFrameToAckData.stateHash, componentGroups, _recycledFrameToAckData.mergedInputData.ComponentPool);
      var bytes = ArrayPool<byte>.Shared.Rent(1000);
      int size = _frameSerializer.Serialize(_archetypeGraph, ArrayPoolResizer.Instance, frameSyncData, ref bytes);
      
      _frameSyncDataPool.Return(frameSyncData);

      SerializedAuthInputData newInput = new SerializedAuthInputData();
      newInput.serializedData = bytes;
      newInput.serializedDataLength = size;
      _cacheData.Add(frame, newInput);

      if (frame < _minKey)
      {
        _minKey = frame;
      }
      
      return newInput;
    }

    public void ReleaseFullInputLockForFramesAtOrBelow(int lastAuthInputReceivedFromHost)
    {
      for (int i = lastAuthInputReceivedFromHost; i >= _minKey; i--)
      {
        if (_cacheData.ContainsKey(i))
        {
          ArrayPool<byte>.Shared.Return(_cacheData[i].serializedData);
          _cacheData.Remove(i);
        }
      }
      
      _clientSyncer.ReleaseFullInputLockForFramesAtOrBelow(lastAuthInputReceivedFromHost, _authInputLockId);
    }
  } 
}
