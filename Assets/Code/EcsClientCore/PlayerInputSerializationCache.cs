using System;
using System.Buffers;
using System.Collections.Generic;
using ecs;

namespace Indigo.EcsClientCore
{
  public class PlayerInputSerializationCache
  {
    private int              _playerSlot;
    private ClientSyncer     _clientSyncer;
    private IFrameSerializer _frameSerializer;
    private ArchetypeGraph   _archetypeGraph;
    
    private IComponentFactory _componentPool;
    
    private Dictionary<int, SerializedPlayerFrameInputData> _cacheData  = new Dictionary<int, SerializedPlayerFrameInputData>();
    private int                                             _minKey     = int.MaxValue;

    private PlayerFrameInputData _recycledPlayerFrameInputData = new PlayerFrameInputData();

    private uint _myPlayerInputLockId;

    public PlayerInputSerializationCache(int playerSlot, ClientSyncer syncer, IFrameSerializer frameSerializer, ArchetypeGraph archetypeGraph, IComponentFactory pool)
    {
      _playerSlot = playerSlot;
      _clientSyncer = syncer;
      _frameSerializer = frameSerializer;
      _archetypeGraph = archetypeGraph;
      
      
      _myPlayerInputLockId = _clientSyncer.RegisterPlayerInputLock(playerSlot);
      _recycledPlayerFrameInputData.Setup(pool, new AliasLookup(), new ComponentDefinitions());
    }

    public SerializedPlayerFrameInputData GetSerializedInput(int frame)
    {
      if (_cacheData.TryGetValue(frame, out SerializedPlayerFrameInputData data))
      {
        return data;
      }
      
      _recycledPlayerFrameInputData.Reset();
      if (!_clientSyncer.TryGetClientInputData(_playerSlot, frame, _recycledPlayerFrameInputData))
      {
        throw new Exception();
      }
      
      var bytes = ArrayPool<byte>.Shared.Rent(1);
      int size = _frameSerializer.Serialize(_archetypeGraph, ArrayPoolResizer.Instance, _recycledPlayerFrameInputData.playerInputData, ref bytes);

      SerializedPlayerFrameInputData newInput = new SerializedPlayerFrameInputData();
      newInput.serializedData = bytes;
      newInput.serializedDataLength = size;
      newInput.frameNum = _recycledPlayerFrameInputData.playerInputData.FrameNum;
      newInput.locallyAppliedTimestamp = _recycledPlayerFrameInputData.locallyAppliedTimestamp;
      _cacheData.Add(frame, newInput);

      if (frame < _minKey)
      {
        _minKey = frame;
      }
      
      return newInput;
    }

    public void ReleasePlayerInputLockForAnyFramesAtOrBelow(int latestInputHostAckedFromUs)
    {
      for (int i = latestInputHostAckedFromUs; i >= _minKey; i--)
      {
        if (_cacheData.ContainsKey(i))
        {
          ArrayPool<byte>.Shared.Return(_cacheData[i].serializedData);
          _cacheData.Remove(i);
        }
      }
      
      _clientSyncer.ReleasePlayerInputLockForAnyFramesAtOrBelow(_playerSlot, latestInputHostAckedFromUs, _myPlayerInputLockId);
    }
  } 
}
