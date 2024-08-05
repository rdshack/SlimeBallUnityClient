
using System;
using System.Collections.Generic;
using ecs;
using Indigo.Math;
using UnityEngine;

namespace Indigo.EcsClientCore
{
    public class FrameToAckData
    {
        public readonly FrameInputData mergedInputData = new FrameInputData();
        public          int         stateHash;
        
        public void Setup(IComponentFactory pool, IAliasLookup aliasLookup, IComponentDefinitions componentDefinitions)
        {
            mergedInputData.Setup(pool, aliasLookup, componentDefinitions);
        }
        
        public int GetFullStateHash()
        {
            return stateHash;
        }

        public IFrameInputData GetClientInputData()
        {
            return mergedInputData;
        }

        public void Reset()
        {
            mergedInputData?.Reset();
            stateHash = default;
        }

        public static FrameToAckData Create()
        {
            return new FrameToAckData();
        }

        public static void Reset(FrameToAckData obj)
        {
            obj.Reset();
        }
    }

    public class PlayerFrameInputData
    {
        public readonly FrameInputData playerInputData = new FrameInputData();
        public          long           locallyAppliedTimestamp;

        public void Setup(IComponentFactory pool, IAliasLookup aliasLookup, IComponentDefinitions componentDefinitions)
        {
            playerInputData.Setup(pool, aliasLookup, componentDefinitions);
        }
        
        public void Reset()
        {
            playerInputData.Reset();
            locallyAppliedTimestamp = long.MinValue;
        }

        public static PlayerFrameInputData Create()
        {
            return new PlayerFrameInputData();
        }

        public static void Reset(PlayerFrameInputData obj)
        {
            obj.Reset();
        }
            
    }

    public class ClientSyncer
    {
        private ComponentDefinitions _componentDefinitions;
        private IComponentFactory    _componentPool;
        private AliasLookup          _aliasLookup;
        private ArchetypeGraph       _archetypeGraph;
        private int                  _playerCount;

        private Dictionary<int, IntervalMergeSet> _playerInputsEverReceived = new Dictionary<int, IntervalMergeSet>();
        private IntervalMergeSet                  _authInputsEverReceived   = new IntervalMergeSet();
        
        private ObjPool<FrameToAckData>       _ackFramePool;
        private ObjPool<PlayerFrameInputData> _playerFrameInputDataPool;

        private uint                  _fullInputLockRegistration;
        private Dictionary<int, uint> _playerInputLockRegistration = new Dictionary<int, uint>();
        private Dictionary<int, uint> _activeLocksForFullFrame     = new Dictionary<int, uint>();
        private List<int>             _activeFullInputLockedFrames = new List<int>();

        private Dictionary<int, Dictionary<int, uint>> _activeLocksForPlayerFrame =
            new Dictionary<int, Dictionary<int, uint>>();

        private Dictionary<int, List<int>> _activeLockedInputsForPlayer;

        private ClientLogger _clientLogger;
        
        private Dictionary<int, FrameToAckData> _authInputLookup               = new Dictionary<int, FrameToAckData>();
        private Dictionary<int, int>            _curMsAheadValuesPerPlayer = new Dictionary<int, int>();

        private Dictionary<int, Dictionary<int, PlayerFrameInputData>> _playerInputLookup;

        private Dictionary<int, RunningAverager> _playerAheadByValue = new Dictionary<int, RunningAverager>();

        public event Action<int, int> NewPlayerInputPushed;
        public event Action NewAuthInputPushed;

        public ClientSyncer(int playerCount, ClientLogger logger)
        {
            _playerCount = playerCount;
            _componentDefinitions = new ComponentDefinitions();
            _aliasLookup = new AliasLookup();
            _archetypeGraph = new ArchetypeGraph(_componentDefinitions, new AliasLookup());
            _componentPool = new ComponentCopier(_componentDefinitions);

            _clientLogger = logger;
            
            _ackFramePool = new ObjPool<FrameToAckData>(FrameToAckData.Create, FrameToAckData.Reset);
            _playerFrameInputDataPool = new ObjPool<PlayerFrameInputData>(PlayerFrameInputData.Create, PlayerFrameInputData.Reset);

            _playerInputLookup = new Dictionary<int, Dictionary<int, PlayerFrameInputData>>();
            _activeLockedInputsForPlayer = new Dictionary<int, List<int>>();
            for (int i = 0; i < playerCount; i++)
            {
                _playerInputLookup.Add(i, new Dictionary<int, PlayerFrameInputData>());
                _activeLocksForPlayerFrame.Add(i, new Dictionary<int, uint>());
                _playerInputsEverReceived.Add(i, new IntervalMergeSet());
                _activeLockedInputsForPlayer.Add(i, new List<int>());
            }
        }

        public ClientLogger GetLogger()
        {
            return _clientLogger;
        }
        
        public int GetPlayerCount()
        {
            return _playerCount;
        }

        public void SetMsAheadValue(int player, int frames)
        {
            _curMsAheadValuesPerPlayer[player] = frames;
        }

        public bool TryGetMsAheadValue(int player, out int aheadBy)
        {
            return _curMsAheadValuesPerPlayer.TryGetValue(player, out aheadBy);
        }

        public void PushFullSyncData(FrameToAckData syncData)
        {
            int frameNum = syncData.GetClientInputData().GetFrameNum();
            _clientLogger.Log(ClientLogger.ClientLogFlags.Sync, $"Pushing full sync data {frameNum}");
            
            FrameToAckData ackDataCopy = _ackFramePool.Get();
            ackDataCopy.stateHash = syncData.stateHash;
            ackDataCopy.Setup(_componentPool, _aliasLookup, _componentDefinitions);
            FrameInputData.CopyTo(syncData.mergedInputData, ackDataCopy.mergedInputData, _componentPool, _archetypeGraph, _componentDefinitions);
            
            _activeLocksForFullFrame[ackDataCopy.mergedInputData.GetFrameNum()] = _fullInputLockRegistration;
            _activeFullInputLockedFrames.Add(ackDataCopy.mergedInputData.GetFrameNum());
            
            _authInputLookup.Add(frameNum, ackDataCopy);
            _authInputsEverReceived.Insert(syncData.mergedInputData.GetFrameNum());

            NewAuthInputPushed?.Invoke();
        }
        
        public bool TryGetFullSyncData(int frame, FrameToAckData copyTo)
        {
            if (!_authInputLookup.TryGetValue(frame, out FrameToAckData toCopy))
            {
                return false;
            }
            
            copyTo.Reset();
            copyTo.mergedInputData.Reset();
            copyTo.stateHash = toCopy.stateHash;
            FrameInputData.CopyTo(toCopy.mergedInputData, copyTo.mergedInputData, _componentPool, _archetypeGraph, _componentDefinitions);

            return true;
        }
        
        public bool TryGetFullSyncHash(int frame, out int hash)
        {
            hash = 0;
            if (!_authInputLookup.TryGetValue(frame, out FrameToAckData toCopy))
            {
                return false;
            }

            hash = toCopy.stateHash;
            return true;
        }

        public uint RegisterFullInputLock()
        {
            var result = RegisterLockInternal(_fullInputLockRegistration);
            _fullInputLockRegistration = result.Item1;
            return result.Item2;
        }
        
        public uint RegisterPlayerInputLock(int player)
        {
            if (!_playerInputLockRegistration.TryGetValue(player, out uint registration))
            {
                _playerInputLockRegistration[player] = registration = 0;
            }
            
            var result = RegisterLockInternal(registration);
            _playerInputLockRegistration[player] = result.Item1;
            return result.Item2;
        }

        private (uint, uint) RegisterLockInternal(uint lockRegistration)
        {
            if (lockRegistration == 0)
            {
                return (1, 1);
            }
            
            uint newLockRegistration = (lockRegistration << 1) + 1;
            return (newLockRegistration, newLockRegistration ^ lockRegistration);
        }

        public void ReleaseFullInputLockForFrames(int startFrame, int endFrame, uint lockId)
        {
            if (startFrame > endFrame)
            {
                throw new Exception();
            }
            
            for (int i = startFrame; i <= endFrame; i++)
            {
                ReleaseFullInputLockForFrame(i, lockId);
            }
        }

        public void ReleaseFullInputLockForFrame(int frame, uint lockId)
        {
            if (!_activeLocksForFullFrame.TryGetValue(frame, out uint activeLocks))
            {
                return;
            }

            uint newLockFlags = activeLocks & ~lockId;
            _activeLocksForFullFrame[frame] = newLockFlags;
            
            //_clientLogger.Log(ClientLogger.ClientLogFlags.Sync,
            //                  $"Releasing full sync lock for frame {frame}, lock id {lockId}");

            if (newLockFlags == 0)
            {
                if (!_authInputLookup.TryGetValue(frame, out FrameToAckData recycle))
                {
                    throw new Exception();
                }
                
                _clientLogger.Log(ClientLogger.ClientLogFlags.Sync,
                                  $"Releasing full input frame {frame}");
                
                _ackFramePool.Return(recycle);
                _authInputLookup.Remove(frame);
                _activeLocksForFullFrame.Remove(frame);
                _activeFullInputLockedFrames.Remove(frame);
            }
        }

        public void ReleasePlayerInputLockForAnyFramesAtOrBelow(int player, int frameHead, uint lockId)
        {
            List<int> lockedInputFrames = _activeLockedInputsForPlayer[player];
            for(int i = lockedInputFrames.Count - 1; i >= 0; i--)
            {
                var lockedInputFrame = lockedInputFrames[i];
                if (lockedInputFrame <= frameHead)
                {
                    ReleasePlayerInputLockForFrame(player, lockedInputFrame, lockId);
                }
            }
        }

        public void ReleasePlayerInputLockForFrame(int player, int frame, uint lockId)
        {
            _activeLocksForPlayerFrame[player][frame] &= ~lockId;
            
            if ( _activeLocksForPlayerFrame[player][frame] == 0)
            {
                if (!_playerInputLookup[player].TryGetValue(frame, out PlayerFrameInputData recycle))
                {
                    throw new Exception();
                }
                
                _playerFrameInputDataPool.Return(recycle);
                _playerInputLookup[player].Remove(frame);
                _activeLocksForPlayerFrame[player].Remove(frame);
                _activeLockedInputsForPlayer[player].Remove(frame);
            }
        }

        public bool InputEverReceivedFromPlayer(int playerSlot, int frame)
        {
            return _playerInputsEverReceived[playerSlot].ContainsValue(frame);
        }

        public int LatestConsecutiveInputEverReceivedFromPlayer(int playerSlot)
        {
            var mergeSet = _playerInputsEverReceived[playerSlot];
            if (mergeSet.Empty())
            {
                return 0;
            }
            
            return mergeSet.GetLargestConsecutiveValue();
        }
        
        public bool AuthInputEverReceived(int frame)
        {
            return _authInputsEverReceived.ContainsValue(frame);
        }

        public bool HasInputForAllPlayersForFrame(int frame)
        {
            for(int i = 0; i < _playerCount; i++)
            {
                if (!_activeLockedInputsForPlayer[i].Contains(frame))
                {
                    return false;
                }
            }

            return true;
        }

        public int LatestConsecutiveAuthInputEverReceived()
        {
            if (_authInputsEverReceived.Empty())
            {
                return 0;
            }
            
            return _authInputsEverReceived.GetLargestConsecutiveValue();
        }

        public void PushClientInputData(int playerSlot, PlayerFrameInputData frameInputData)
        {
            //we pack multiple inputs in network messages for redundancy to cover packet loss. So we may get multiple of the same input from a player;
            if (_playerInputLookup[playerSlot].ContainsKey(frameInputData.playerInputData.FrameNum))
            {
                return;
            }
            
            PlayerFrameInputData newPlayerFrameInputData = _playerFrameInputDataPool.Get();
            newPlayerFrameInputData.Setup(_componentPool, _aliasLookup, _componentDefinitions);
            newPlayerFrameInputData.playerInputData.FrameNum = frameInputData.playerInputData.GetFrameNum();
            FrameInputData.CopyTo(frameInputData.playerInputData, newPlayerFrameInputData.playerInputData, _componentPool, _archetypeGraph, _componentDefinitions);
            newPlayerFrameInputData.locallyAppliedTimestamp = frameInputData.locallyAppliedTimestamp;

            _activeLocksForPlayerFrame[playerSlot][newPlayerFrameInputData.playerInputData.FrameNum] = _playerInputLockRegistration[playerSlot];
            _activeLockedInputsForPlayer[playerSlot].Add(newPlayerFrameInputData.playerInputData.FrameNum);
            
            _playerInputLookup[playerSlot].Add(newPlayerFrameInputData.playerInputData.FrameNum, newPlayerFrameInputData);
            _playerInputsEverReceived[playerSlot].Insert(newPlayerFrameInputData.playerInputData.FrameNum);

            NewPlayerInputPushed?.Invoke(playerSlot, newPlayerFrameInputData.playerInputData.FrameNum);
        }

        public bool TryGetClientInputData(int playerSlot, int frame, PlayerFrameInputData copyTo)
        {
            if (!_playerInputLookup[playerSlot].TryGetValue(frame, out PlayerFrameInputData frameInputData))
            {
                return false;
            }

            copyTo.locallyAppliedTimestamp = frameInputData.locallyAppliedTimestamp;
            FrameInputData.CopyTo(frameInputData.playerInputData, copyTo.playerInputData, _componentPool, _archetypeGraph, _componentDefinitions);
            
            return true;
        }

        public void ReleaseFullInputLockForFramesAtOrBelow(int frameHead, uint authInputLockId)
        {
            for(int i = _activeFullInputLockedFrames.Count - 1; i >= 0; i--)
            {
                var lockedInputFrame = _activeFullInputLockedFrames[i];
                if (lockedInputFrame <= frameHead)
                {
                    ReleaseFullInputLockForFrame(lockedInputFrame, authInputLockId);
                }
            }
        }

        public void AddAheadByValue(int player, int aheadByValue)
        {
            RunningAverager averager;
            if (!_playerAheadByValue.TryGetValue(player, out averager))
            {
                averager = new RunningAverager(TimeSpan.FromSeconds(1));
                _playerAheadByValue[player] = averager;
            }

            averager.AddEntry(aheadByValue);
        }

        public float GetAheadByValue(int playerSlot)
        {
            if (!_playerAheadByValue.TryGetValue(playerSlot, out RunningAverager averager))
            {
                return 0;
            }

            if (!averager.TryGetAverage(out float avg))
            {
                return 0;
            }

            return avg;
        }
    }   
}