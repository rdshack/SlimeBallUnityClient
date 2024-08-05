using System;
using System.Collections.Generic;
using ecs;
using Messages;

namespace Indigo.EcsClientCore
{
  public class ClientNetworkSyncAdapter
  {
    private ClientSyncer _syncer;

    private IFrameSerializer    _frameSerializer;
    private IGameClientConnection _gameClientConnection;
    
    private ComponentDefinitions       _componentDefinitions;
    private IComponentFactory          _componentPool;
    private ArchetypeGraph             _archetypeGraph;
    private DeserializedFrameSyncStore _deserializedFrameSyncStore;
    private AliasLookup                _aliasLookup;
    private DateTime                   _lastSentInputsDatetime = DateTime.MinValue;

    private int _playerSlot;
    private int _latestInputHostAckedFromUs;

    private PlayerInputSerializationCache _serializationCache;

    private List<SerializedPlayerFrameInputData> _tempInputDataList = new List<SerializedPlayerFrameInputData>();

    public ClientNetworkSyncAdapter(IGameClientConnection clientConnection, ClientSyncer syncer,
                                    IFrameSerializer      frameSerializer, int playerSlot)
    {
      _syncer = syncer;
      _gameClientConnection = clientConnection;
      _frameSerializer = frameSerializer;
      _playerSlot = playerSlot;

      _componentDefinitions = new ComponentDefinitions();
      _componentPool = new ComponentCopier(_componentDefinitions);
      _aliasLookup = new AliasLookup();

      AliasLookup aliasLookup = new AliasLookup();
      _archetypeGraph = new ArchetypeGraph(_componentDefinitions, aliasLookup);
      _deserializedFrameSyncStore = new DeserializedFrameSyncStore(_componentPool, _componentDefinitions, _archetypeGraph);

      _serializationCache =
        new PlayerInputSerializationCache(_playerSlot, _syncer, _frameSerializer, _archetypeGraph, _componentPool);
          
      _gameClientConnection.OnAuthInputReceived += HandleAuthInputFromHost;
      _syncer.NewPlayerInputPushed += OnNewPlayerInputPushed;
    }

    private void OnNewPlayerInputPushed(int playerSlot, int frameNum)
    {
      ProcessAndSendAllNonAckedInputsToHost();
    }

    public void Update()
    {
      if ((DateTime.UtcNow - _lastSentInputsDatetime).TotalMilliseconds > 100)
      {
        ProcessAndSendAllNonAckedInputsToHost();
      }
    }

    private void ProcessAndSendAllNonAckedInputsToHost()
    {
      _lastSentInputsDatetime = DateTime.UtcNow;
      
      int latestLocalPlayerInput = _syncer.LatestConsecutiveInputEverReceivedFromPlayer(_gameClientConnection.PlayerSlot);
      int firstFrame = _latestInputHostAckedFromUs + 1;
      for (int i = firstFrame; i <= latestLocalPlayerInput; i++)
      {
        _tempInputDataList.Add(_serializationCache.GetSerializedInput(i));
      }

      int latestAuthInput = _syncer.LatestConsecutiveAuthInputEverReceived();
      _gameClientConnection.SendNonAckedInputsToHost(_tempInputDataList, latestAuthInput, firstFrame);
      
      _tempInputDataList.Clear();
    }

    private void HandleAuthInputFromHost(AuthInputContent authInputContent)
    {
      if (authInputContent.LastInputHostReceivedFromYou > _latestInputHostAckedFromUs)
      {
        _latestInputHostAckedFromUs = authInputContent.LastInputHostReceivedFromYou;
        _serializationCache.ReleasePlayerInputLockForAnyFramesAtOrBelow(_latestInputHostAckedFromUs);
      }

      var latestAuthInput = _syncer.LatestConsecutiveAuthInputEverReceived();
      for (int i = 0; i < authInputContent.FramesLength; i++)
      {
        int frameNum = authInputContent.FramesStart + i;
        if (latestAuthInput >= frameNum)
        {
          continue;
        }
        
        ByteArray bytesFromUpdate = authInputContent.Frames(i).Value;
        byte[] tempBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(bytesFromUpdate.DataLength);

        int startIndex = tempBytes.Length - bytesFromUpdate.DataLength;
        bytesFromUpdate.GetDataBytes().Value.CopyTo(tempBytes, startIndex);
              
        _deserializedFrameSyncStore.Reset();
        _frameSerializer.DeserializeSyncFrame(tempBytes, _deserializedFrameSyncStore, startIndex);
        System.Buffers.ArrayPool<byte>.Shared.Return(tempBytes);
              
        FrameToAckData ackData = new FrameToAckData();
        ackData.Setup(_componentPool, _aliasLookup, _componentDefinitions);
        FrameInputData.CopyTo(_deserializedFrameSyncStore, ackData.mergedInputData, _componentPool, _archetypeGraph, _componentDefinitions);
        
        ackData.stateHash = _deserializedFrameSyncStore.FullStateHash;

        _syncer.PushFullSyncData(ackData);
        _syncer.SetMsAheadValue(_playerSlot,  authInputContent.MsAheadOfAvgPlayer);
      }
    }
  } 
}
