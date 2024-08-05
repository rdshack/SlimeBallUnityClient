using System;
using System.Collections.Generic;
using ecs;
using UnityEngine;
// ReSharper disable All

namespace Indigo.EcsClientCore
{
    public class LocalWorldManager : MonoBehaviour, IFrameResolver
    {
        private enum State
        {
            Inactive,
            Init,
            Joined
        }
        
        public int                       AllowedToBeAheadTicks = 6;
        public bool                      LogFrameTimings;
        
        private GameClock                 _gameClock;
        private IGame                     _game;
        private PlayerInputBuilder        _playerInputBuilder;
        private IComponentFactory         _componentPool;
        private ComponentCopier           _componentCopier;
        private ArchetypeGraph            _graph;
        private ComponentDefinitions      _componentDefinitions;
        private IGameViewProcessor        _viewProcessor;
        private FlatBufferFrameSerializer _frameSerializer;
        private FrameInputData            _predictedInput;
        private int                       _playerSlot;
        private uint                      _playerInputLockId;
        private Logger                    _worldLogger;
        private PeerWorldStateResolver    _peerWorldStateResolver;

        private byte[] _tempBytes = new byte [100];

        private State        _curState;
        private ClientSyncer _clientSyncer;
        private int          _nextAckFrame = 1;
        private AliasLookup  _aliasLookup;
        
        private bool _isLocal;
        private bool _isHost;
        
        public void Init(IGame game, IGameViewProcessor gameViewProcessor, Logger logger, List<int> players, 
                         int ourPlayerSlot, bool isHost, ClientSyncer syncer, bool isLocal = false)
        {
            _curState = State.Init;
            _gameClock = new GameClock(game.GetSettings().GetMsPerFrame());
            _game = game;
            _isHost = isHost;
            _worldLogger = logger;

            _playerSlot = ourPlayerSlot;
            _game.BuildWorld(logger);

            _aliasLookup = new AliasLookup();
            _componentDefinitions = new ComponentDefinitions();
            _viewProcessor = gameViewProcessor;
            _componentPool = new ComponentCopier(_componentDefinitions);
            _graph = new ArchetypeGraph(_componentDefinitions, _aliasLookup);
            _frameSerializer = new FlatBufferFrameSerializer();
            _predictedInput = new FrameInputData();
            _predictedInput.Setup(_componentPool, _aliasLookup, _componentDefinitions);

            _clientSyncer = syncer;
            _isLocal = isLocal;
            
            PlayerInputBuffer playerInput = new PlayerInputBuffer(_componentPool, _componentDefinitions, 
                                                                  _game.GetWorld().GetArchetypes(), _clientSyncer, ourPlayerSlot);

            KeyboardReader.MoveKeyStyle moveKeyStyle = KeyboardReader.MoveKeyStyle.WASD;
            if (isLocal)
            {
                moveKeyStyle = isHost ? KeyboardReader.MoveKeyStyle.WASD : KeyboardReader.MoveKeyStyle.ARROWS;
            }
            _playerInputBuilder = new PlayerInputBuilder(ourPlayerSlot, playerInput, players, moveKeyStyle);

            if (!_isLocal)
            {
                _peerWorldStateResolver = new PeerWorldStateResolver(_game, _playerInputBuilder, _clientSyncer, _viewProcessor, this,
                                                                     LogFrameTimings, _clientSyncer.GetLogger(), AllowedToBeAheadTicks);
                //Debug.Log($"LocalWorldManager Register full input lock {_fullSyncLockId}");
            }
            
            _playerInputLockId = _clientSyncer.RegisterPlayerInputLock(ourPlayerSlot);  
            
            _playerInputBuilder.BuildFirstEmptyInputFrame();
            _game.Tick(_playerInputBuilder.GetPredictedInput(1, _predictedInput, _componentPool));
            _predictedInput.Reset();

            PushLatestFrameToInterpolator();
        }
        
        private void Update()
        {
            if (_curState == State.Inactive)
            {
                return;
            }

            _playerInputBuilder.ReadLatest();
            float dtMs = Time.deltaTime * 1000;

            if (!_isLocal)
            {
                if(!_peerWorldStateResolver.TryModifyDeltaTime(_playerSlot, dtMs, out float modifiedDtMs))
                {
                    return;
                }

                dtMs = modifiedDtMs;
            }

            int ticks = _gameClock.AdvanceAndGetTicks(dtMs);
            
            for (int i = 0; i < ticks; i++)
            {
                int nextFrame = _game.GetWorld().GetNextFrameNum();
                //Debug.LogWarning($"ticking to {nextFrame}");
                _playerInputBuilder.BuildNextInputFrame(nextFrame);
                _game.Tick(_playerInputBuilder.GetPredictedInput(nextFrame, _predictedInput, _componentPool));
                _predictedInput.Reset();
                
                PushLatestFrameToInterpolator();

                if (_isLocal)
                {
                    AdvanceAckFrame();
                }
                
                _worldLogger.LogWarning($"{_playerSlot}: new frame {nextFrame}");
            }


            if (!_isLocal)
            {
                _peerWorldStateResolver.Resolve();
            }
            
            _viewProcessor.FeedTime(dtMs);

            if (LogFrameTimings)
            {
                string s = $"M:{DateTime.UtcNow.Minute}:{DateTime.UtcNow.Second}:{DateTime.UtcNow.Millisecond}";
                //Debug.Log(s);
            }
        }

        public int GetNextAckFrame()
        {
            return _nextAckFrame;
        }

        public void AdvanceAckFrame()
        {
            _clientSyncer.ReleasePlayerInputLockForFrame(_playerSlot, _nextAckFrame, _playerInputLockId);
            _nextAckFrame++;  
        }

        public void PushLatestFrameToInterpolator()
        {
            DeserializedFrameDataStore dataStore = new DeserializedFrameDataStore();
            dataStore.Setup(_componentPool, _componentDefinitions, _graph);
            int serializedSize = _game.GetWorld().GetLatestFrameSerialized(true, ref _tempBytes);
            _frameSerializer.DeserializeFrame(_tempBytes, dataStore, _tempBytes.Length - serializedSize);
            _viewProcessor.PushFrame(dataStore);
        }
    }   
}
