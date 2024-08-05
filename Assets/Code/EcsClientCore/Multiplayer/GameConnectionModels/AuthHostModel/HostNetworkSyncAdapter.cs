using System;
using System.Collections.Generic;
using ecs;
using Messages;
using UnityEngine;

namespace Indigo.EcsClientCore
{
    public class HostNetworkSyncAdapter
    {
        private ClientSyncer                _syncer;
        private AuthInputSerializationCache _serializationCache;

        private IFrameSerializer    _frameSerializer;
        private IGameHostConnection _gameHostConnection;

        private ComponentDefinitions       _componentDefinitions;
        private ArchetypeGraph             _archetypeGraph;
        //private DeserializedFrameDataStore _deserializedFrameDataStore;

        private PlayerFrameInputData _recycledPlayerFrameInputData;
        private IComponentFactory    _componentPool;
        
        private List<int>            _players;
        private Dictionary<int, int> _latestAckPerPlayer = new Dictionary<int, int>();

        private List<SerializedAuthInputData> _tempAckDataList = new List<SerializedAuthInputData>();

        private DateTime _lastSentInputsDatetime = DateTime.MinValue;

        public HostNetworkSyncAdapter(IGameHostConnection gameHostConnection, ClientSyncer syncer, IFrameSerializer serializer, List<int> players)
        {
            _syncer = syncer;
            _gameHostConnection = gameHostConnection;
            _frameSerializer = serializer;

            _componentDefinitions = new ComponentDefinitions();
            _componentPool = new ComponentCopier(_componentDefinitions);

            AliasLookup aliasLookup = new AliasLookup();
            _archetypeGraph = new ArchetypeGraph(_componentDefinitions, aliasLookup);
            //_deserializedFrameDataStore = new DeserializedFrameDataStore(_componentPool, _componentDefinitions, _archetypeGraph);
            _recycledPlayerFrameInputData = new PlayerFrameInputData();
            _recycledPlayerFrameInputData.Setup(_componentPool, aliasLookup, _componentDefinitions);

            _players = new List<int>(players);
            foreach (var p in _players)
            {
                _latestAckPerPlayer[p] = 0;
            }

            _serializationCache =
                new AuthInputSerializationCache(_syncer, _frameSerializer, _archetypeGraph, _componentPool);
            
            _gameHostConnection.OnPlayerInputReceived += HandlePlayerInput;
            _syncer.NewAuthInputPushed += OnNewAuthInputPushed;
        }

        private void OnNewAuthInputPushed()
        {
            for(int i = 1; i < _players.Count; i++)
            {
                ProcessNewAuthInput();
            }
        }

        public void Update()
        {
            if ((DateTime.UtcNow - _lastSentInputsDatetime).TotalMilliseconds > 100)
            {
                ProcessNewAuthInput();
            }
        }

        private void ProcessNewAuthInput()
        {
            //start at 1. We don't need to send auth inputs to host, as they have them locally.
            //But we DO need to send them time dilation info (via syncer).
            for (int i = 1; i < _players.Count; i++)
            {
                ProcessAndSendAllNonAckedAuthInputs(_players[i]);
            }
        }

        private void ProcessAndSendAllNonAckedAuthInputs(int playerId)
        {
            _lastSentInputsDatetime = DateTime.UtcNow;
            
            _tempAckDataList.Clear();
            int firstNonAckedFrame = _latestAckPerPlayer[playerId] + 1;
            int nextFrameToSend = _syncer.LatestConsecutiveAuthInputEverReceived();
            for (int i = firstNonAckedFrame; i <= nextFrameToSend; i++)
            {
                _tempAckDataList.Add(_serializationCache.GetSerializedInput(i));
            }

            if (_tempAckDataList.Count > 0)
            {
                int latestReceivedFromPlayer = _syncer.LatestConsecutiveInputEverReceivedFromPlayer(playerId);
                int msAheadValue = _syncer.TryGetMsAheadValue(playerId, out int aheadBy) ? aheadBy : 0;
                _gameHostConnection.SendAllNonAckedAuthInput(playerId, _tempAckDataList, firstNonAckedFrame, latestReceivedFromPlayer, msAheadValue);
                _tempAckDataList.Clear();
            }
        }

        private void HandlePlayerInput(PlayerInputContent inputContent)
        {
            //Debug.Log($"receiving input {inputContent.InputsLength}");
            
            int curLatestAck = _latestAckPerPlayer[inputContent.PlayerId];
            if (inputContent.LastAuthInputReceivedFromHost > curLatestAck)
            {
                _latestAckPerPlayer[inputContent.PlayerId] = inputContent.LastAuthInputReceivedFromHost;

                bool allAcked = true;
                for(int i = 1; i < _players.Count; i++)
                {
                    if (_latestAckPerPlayer[i] < inputContent.LastAuthInputReceivedFromHost)
                    {
                        allAcked = false;
                        break;
                    }
                }

                if (allAcked)
                {
                    _serializationCache.ReleaseFullInputLockForFramesAtOrBelow(inputContent.LastAuthInputReceivedFromHost);
                }
            }

            for (int i = 0; i < inputContent.InputsLength; i++)
            {
                int curFrameNum = inputContent.FramesStart + i;
                //Since we re-send until acknowledged, we may get data the syncer already has
                if (_syncer.InputEverReceivedFromPlayer(inputContent.PlayerId, curFrameNum))
                {
                    continue;
                }

                ByteArray bytesFromUpdate = inputContent.Inputs(i).Value;
                byte[] tempBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(bytesFromUpdate.DataLength);

                int posStart = tempBytes.Length - bytesFromUpdate.DataLength;
                bytesFromUpdate.GetDataBytes().Value.CopyTo(tempBytes, posStart);
                
                var deserializedFrameDataStore = new DeserializedFrameSyncStore(_componentPool, _componentDefinitions, _archetypeGraph);
                _frameSerializer.DeserializeInputFrame(tempBytes, deserializedFrameDataStore, posStart);
                System.Buffers.ArrayPool<byte>.Shared.Return(tempBytes);

                _recycledPlayerFrameInputData.Reset();
                FrameInputData.CopyTo(deserializedFrameDataStore, _recycledPlayerFrameInputData.playerInputData, _componentPool, _archetypeGraph, _componentDefinitions);
                _recycledPlayerFrameInputData.locallyAppliedTimestamp = inputContent.FrameCreationTimestamps(i);
                
                _syncer.PushClientInputData(inputContent.PlayerId, _recycledPlayerFrameInputData);
            }
        }
    }   
}