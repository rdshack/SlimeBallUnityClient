using System;
using System.Collections.Generic;
using ecs;
using UnityEngine;

// ReSharper disable All

namespace Indigo.EcsClientCore
{
    public class AuthWorldManager
    {
        private IGame                _game;
        private ArchetypeGraph       _archetypeGraph;
        private AliasLookup          _aliasLookup;
        private ClientSyncer         _syncer;
        private ComponentCopier      _componentCopier;
        private ComponentDefinitions _componentDefinitions;
        private int                  _frameAwaitingInput = 1;
        private int                  _latestFrameAckByLocalSim;

        private ObjPool<PlayerFrameInputData> _playerFrameInputPool;
        private int                           _playerCount;
        private Dictionary<int, uint>         _playerInputLockIds = new Dictionary<int, uint>();
        private List<PlayerFrameInputData>    _inputsReceivedForAwaitingFrame;

        private long                             _timeRefPoint;
        private Dictionary<int, RunningAverager> _timeOffsetAveragers = new Dictionary<int, RunningAverager>();

        private FrameToAckData _tempFrameToAckData = new FrameToAckData();

        public AuthWorldManager(IGame game, ClientSyncer syncer, int playerCount)
        {
            _syncer = syncer;
            _game = game;
            _playerCount = playerCount;

            _game.BuildWorld(new Logger("Auth", LogFlags.None));
            _componentDefinitions = new ComponentDefinitions();
            _archetypeGraph = _game.GetWorld().GetArchetypes();
            _componentCopier = new ComponentCopier(_componentDefinitions);
            _aliasLookup = new AliasLookup();
            _playerFrameInputPool = new ObjPool<PlayerFrameInputData>(PlayerFrameInputData.Create, PlayerFrameInputData.Reset);
            _tempFrameToAckData.Setup(_componentCopier, _aliasLookup, _componentDefinitions);

            for (int i = 0; i < _playerCount; i++)
            {
                _playerInputLockIds[i] = _syncer.RegisterPlayerInputLock(i);
                _timeOffsetAveragers[i] = new RunningAverager(TimeSpan.FromHours(1), 3);
            }

            _inputsReceivedForAwaitingFrame = new List<PlayerFrameInputData>(); 
            _syncer.NewPlayerInputPushed += OnNewPlayerInput;

            _timeRefPoint = DateTime.UtcNow.ToFileTimeUtc();
        }

        private void OnNewPlayerInput(int playerSlot, int frameNum)
        {
            if (frameNum != _frameAwaitingInput)
            {
                return;
            }
            
            if (_syncer.HasInputForAllPlayersForFrame(frameNum))
            {
                ReceiveInput();
            }
        }

        private void ReceiveInput()
        {
            _inputsReceivedForAwaitingFrame.Clear();
            for (int i = 0; i < _playerCount; i++)
            {
                var playerInput = _playerFrameInputPool.Get();
                playerInput.Setup(_componentCopier, _aliasLookup, _componentDefinitions);
                
                if (!_syncer.TryGetClientInputData(i, _frameAwaitingInput, playerInput))
                {
                    throw new Exception();
                }
                
                _inputsReceivedForAwaitingFrame.Add(playerInput);
                _syncer.ReleasePlayerInputLockForFrame(i, _frameAwaitingInput, _playerInputLockIds[i]);
            }

            _tempFrameToAckData.mergedInputData.FrameNum = _frameAwaitingInput;

            long timeSum = 0;
            foreach (var pInput in _inputsReceivedForAwaitingFrame)
            {
                timeSum += (pInput.locallyAppliedTimestamp - _timeRefPoint);
                
                foreach (var entityData in pInput.playerInputData.GetComponentGroups())
                {
                    ComponentGroup componentGroup = _componentCopier.GetComponentGroup(_archetypeGraph, _componentDefinitions);
                    componentGroup.Setup(_componentCopier, _archetypeGraph, _componentDefinitions);
                        
                    foreach (var cIdx in _archetypeGraph.GetComponentIndicesForArchetype(entityData.GetArchetype()))
                    {
                        var copyTarget = _componentCopier.Get(cIdx);
                        _componentCopier.Copy(entityData.GetComponent(cIdx), copyTarget);
                        componentGroup.AddComponentType(cIdx, copyTarget);
                    }
                    
                    _tempFrameToAckData.mergedInputData.AddComponentGroup(componentGroup);   
                }
            }
            
            long timeAvg = timeSum / ((long)(_playerCount));
            for (int i = 0; i < _playerCount; i++)
            {
                long theirTime = _inputsReceivedForAwaitingFrame[i].locallyAppliedTimestamp - _timeRefPoint;
                long msBehind = (timeAvg - theirTime) / 10_000L; //100ns => ms

                _timeOffsetAveragers[i].AddEntry((float)msBehind);
                _timeOffsetAveragers[i].TryGetAverage(out float avg);
                //Debug.Log($"ms ahead value is {avg}");
                _syncer.SetMsAheadValue(i, (int)avg);
            }
            
            foreach (var pInput in _inputsReceivedForAwaitingFrame)
            {
                _playerFrameInputPool.Return(pInput);
            }

            _game.Tick(_tempFrameToAckData.mergedInputData);
            int hash = _game.GetWorld().GetLatestFrameHash();
            
            _tempFrameToAckData.stateHash = hash;
            _syncer.PushFullSyncData(_tempFrameToAckData);
            
            _tempFrameToAckData.Reset();

            _frameAwaitingInput++;
            _inputsReceivedForAwaitingFrame.Clear();   
        }
    }   
}
