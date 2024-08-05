using System;
using System.Collections.Generic;
using CommunityToolkit.HighPerformance.Buffers;
using ecs;
using Messages;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  /// <summary>
  /// Used strictly for 1v1 networking model. In this model, each player sends inputs simultaneously, and validates game states
  /// hashes as they come in later from their peer.
  /// </summary>
  public class P2PPlayerNetworkAdapter
  {
    private ClientSyncer _syncer;

    private IFrameSerializer _frameSerializer; 
    private IP2PGameConnection _gameConnection;
    
    private ComponentDefinitions       _componentDefinitions;
    private ArchetypeGraph             _archetypeGraph;
    private PlayerFrameInputData       _recycledPlayerFrameInputData;
    
    private IComponentFactory _componentPool;

    private DateTime _lastSentInputsDatetime = DateTime.MinValue;
    private int      _latestAckInputFromPeer;
    private int      _latestAckHashFromPeer;

    private int _playerSlot;

    private int       _nextFrameHashToValidate = 1;
    private List<int> _tempHashList = new List<int>();
    
    private uint _authInputLockIdForValidation;
    private uint _authInputLockIdForPeerSync;

    private PlayerInputSerializationCache _serializationCache;
    private DeserializedFrameSyncStore    _deserializedFrameSyncStore;

    private int _fullSyncAcc = 0;

    private List<SerializedPlayerFrameInputData> _tempInputDataList = new List<SerializedPlayerFrameInputData>();

    public P2PPlayerNetworkAdapter(IP2PGameConnection gameConnection, ClientSyncer syncer,
                                   IFrameSerializer   frameSerializer,  int          playerSlot)
    {
      _syncer = syncer;
      _gameConnection = gameConnection;
      _frameSerializer = frameSerializer;
      _playerSlot = playerSlot;

      _componentDefinitions = new ComponentDefinitions();
      _componentPool = new ComponentCopier(_componentDefinitions);

      AliasLookup aliasLookup = new AliasLookup();
      _archetypeGraph = new ArchetypeGraph(_componentDefinitions, aliasLookup);
      _recycledPlayerFrameInputData = new PlayerFrameInputData();
      _recycledPlayerFrameInputData.Setup(_componentPool, aliasLookup, _componentDefinitions );
      
      _serializationCache =
        new PlayerInputSerializationCache(_playerSlot, _syncer, _frameSerializer, _archetypeGraph, _componentPool);
      
      _deserializedFrameSyncStore = new DeserializedFrameSyncStore(_componentPool, _componentDefinitions, _archetypeGraph);

      _authInputLockIdForValidation = _syncer.RegisterFullInputLock();
      //Debug.Log($"p2p networkactper validation Register full input lock {_authInputLockIdForValidation}");
      
      _authInputLockIdForPeerSync = _syncer.RegisterFullInputLock();
      //Debug.Log($"p2p networkactper peer sync Register full input lock {_authInputLockIdForPeerSync}");
      
      _gameConnection.OnPeerSyncReceived += HandlePeerSyncData;
      _syncer.NewPlayerInputPushed += OnNewPlayerInputPushed;
      //_syncer.NewAuthInputPushed += OnNewAuthInputPushed;
    }

    private int GetOtherPlayerSlot()
    {
      return (_playerSlot + 1) % 2;
    }

    private void HandlePeerSyncData(P2PPlayerSyncContent inputContent, string msgId, long timeUtc)
    {
      DateTime timeSent = DateTime.FromFileTimeUtc(timeUtc);
      double latencyMs = (DateTime.UtcNow - timeSent).TotalMilliseconds;
      _syncer.GetLogger().Log(ClientLogger.ClientLogFlags.Sync,
                              $"{msgId}: Received peer sync data: input start is {inputContent.FramesStart} of length {inputContent.InputsLength}. " +
                                                                $"Hash start is {inputContent.HashesStart} of length {inputContent.FrameHashesLength} " +
                                                                $"and latestInputAck is {_latestAckInputFromPeer} " +
                                                                $"and latestHashAck is {_latestAckHashFromPeer}");

      int latestFrameSentFromPeer = inputContent.FramesStart + (inputContent.InputsLength - 1);
      int peerAheadByValue = latestFrameSentFromPeer - inputContent.LatestInputAck;
      _syncer.AddAheadByValue(GetOtherPlayerSlot(), peerAheadByValue);
      
      _syncer.GetLogger().Log(ClientLogger.ClientLogFlags.Sync,
                              $"{msgId}: New peer ahead by value {peerAheadByValue}");
      
      if (inputContent.LatestInputAck > _latestAckInputFromPeer)
      {
        _latestAckInputFromPeer = inputContent.LatestInputAck;
        _serializationCache.ReleasePlayerInputLockForAnyFramesAtOrBelow(_latestAckInputFromPeer);
      }
      
      if (inputContent.LatestHashAck > _latestAckHashFromPeer)
      {
        _latestAckHashFromPeer = inputContent.LatestHashAck;
        _syncer.ReleaseFullInputLockForFramesAtOrBelow(_latestAckHashFromPeer, _authInputLockIdForPeerSync);
      }

      for (int i = 0; i < inputContent.FrameHashesLength; i++)
      {
        int curFrameNum = inputContent.HashesStart + i;
        //Since we re-send until acknowledged, we may get data the syncer already has
        if (curFrameNum != _nextFrameHashToValidate)
        {
          continue;
        }
        
        //it's possible we haven't simulated this locally yet, in which case we early out
        if(!_syncer.TryGetFullSyncHash(_nextFrameHashToValidate, out int ourHash))
        {
          break;
        }

        int hashFromPeer = inputContent.FrameHashes(i);
        if (ourHash != hashFromPeer)
        {
          throw new Exception("p2p desync");
        }
        
        _syncer.ReleaseFullInputLockForFramesAtOrBelow(_nextFrameHashToValidate, _authInputLockIdForValidation);
        _nextFrameHashToValidate++;
      }

      for (int i = 0; i < inputContent.InputsLength; i++)
      {
        int curFrameNum = inputContent.FramesStart + i;
        //Since we re-send until acknowledged, we may get data the syncer already has
        if (_syncer.InputEverReceivedFromPlayer(GetOtherPlayerSlot(), curFrameNum))
        {
          continue;
        }

        ByteArray bytesFromUpdate = inputContent.Inputs(i).Value;
        byte[] tempBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(bytesFromUpdate.DataLength);

        int posStart = tempBytes.Length - bytesFromUpdate.DataLength;
        bytesFromUpdate.GetDataBytes().Value.CopyTo(tempBytes, posStart);

        _deserializedFrameSyncStore.Reset();
        _frameSerializer.DeserializeInputFrame(tempBytes, _deserializedFrameSyncStore, posStart);
        System.Buffers.ArrayPool<byte>.Shared.Return(tempBytes);
            

        _recycledPlayerFrameInputData.Reset();
        FrameInputData.CopyTo(_deserializedFrameSyncStore, _recycledPlayerFrameInputData.playerInputData, _componentPool, _archetypeGraph, _componentDefinitions);
        _recycledPlayerFrameInputData.locallyAppliedTimestamp = inputContent.FrameCreationTimestamps(i);
            
        _syncer.PushClientInputData(GetOtherPlayerSlot(), _recycledPlayerFrameInputData);
      }
    }

    private void OnNewPlayerInputPushed(int playerSlot, int frameNum)
    {
      //if its our input coming in, forward to peer
      if (playerSlot == _playerSlot)
      {
        ProcessAndSendAllNonAckedInputsToPeer(); 
      }
    }

    public void Update()
    {
      if ((DateTime.UtcNow - _lastSentInputsDatetime).TotalMilliseconds > 100)
      {
        ProcessAndSendAllNonAckedInputsToPeer();
      }
    }
    
    private void ProcessAndSendAllNonAckedInputsToPeer()
    {
      _lastSentInputsDatetime = DateTime.UtcNow;
      
      int latestLocalPlayerInput = _syncer.LatestConsecutiveInputEverReceivedFromPlayer(_gameConnection.GetPlayerSlot());
      int firstFrame = _latestAckInputFromPeer + 1;
      
      
      for (int i = firstFrame; i <= latestLocalPlayerInput; i++)
      {
        _tempInputDataList.Add(_serializationCache.GetSerializedInput(i));
      }

      int ackForPeer = _syncer.LatestConsecutiveAuthInputEverReceived();
      
      _tempHashList.Clear();
      int hashStart = _latestAckHashFromPeer + 1;
      for(int i = hashStart; i <= ackForPeer; i++)
      {
        if (!_syncer.TryGetFullSyncHash(i, out int hash))
        {
          throw new Exception();
        }
        
        _tempHashList.Add(hash);
      }

      int hashAck = _nextFrameHashToValidate - 1;

      _gameConnection.SendPeerSyncData(_tempInputDataList, ackForPeer, firstFrame, _tempHashList, hashStart, hashAck);
      
      _tempInputDataList.Clear();
    }

    private void OnNewAuthInputPushed()
    {

    }
  } 
}
