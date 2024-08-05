using System;
using ecs;

namespace Indigo.EcsClientCore
{
    public interface IFrameResolver
    {
        void PushLatestFrameToInterpolator();
        void AdvanceAckFrame();
        int  GetNextAckFrame();
    }

    public class PeerWorldStateResolver
    {
        private bool _logFrameTimings;
        private int  _allowedToBeAheadByTicks;
        
        private ObjPool<FrameToAckData> _frameToAckDataPool =
        new ObjPool<FrameToAckData>(FrameToAckData.Create, FrameToAckData.Reset); 
        
        private IComponentFactory    _componentPool;
        private AliasLookup          _aliasLookup;
        private ComponentDefinitions _componentDefinitions;
        private IGame                _game;
        private ClientLogger         _worldLogger;
        private PlayerInputBuilder   _playerInputBuilder;
        private ClientSyncer         _clientSyncer;
        private IGameViewProcessor   _viewProcessor;
        private IFrameResolver       _frameResolver;
        private FrameInputData       _predictedInput;
        
        private uint _fullSyncLockId;
        
        public PeerWorldStateResolver(IGame game, PlayerInputBuilder playerInputBuilder, ClientSyncer clientSyncer,
                                      IGameViewProcessor viewProcessor, IFrameResolver frameResolver,
                                      bool  logFrameTimings, ClientLogger logger,
                                      int allowedToBeAheadByTicks)
        {
            _logFrameTimings = logFrameTimings;
            _clientSyncer = clientSyncer;
            _frameResolver = frameResolver;
            _componentDefinitions = new ComponentDefinitions();
            _viewProcessor = viewProcessor;
            _componentPool = new ComponentCopier(_componentDefinitions);
            _aliasLookup = new AliasLookup();
            _game = game;
            _worldLogger = logger;
            _playerInputBuilder = playerInputBuilder;
            
            _predictedInput = new FrameInputData();
            _predictedInput.Setup(_componentPool, _aliasLookup, _componentDefinitions);

            _allowedToBeAheadByTicks = allowedToBeAheadByTicks;
            
            _fullSyncLockId = _clientSyncer.RegisterFullInputLock();
        }

      public void Resolve()
      {
          FrameToAckData ackData = _frameToAckDataPool.Get(); 
          ackData.Setup(_componentPool, _aliasLookup, _componentDefinitions); 
          int latestLocalFrame = _game.GetWorld().GetNextFrameNum() - 1;
          _worldLogger.Log($"latestLocal-{latestLocalFrame}, fullsync-{_clientSyncer.LatestConsecutiveAuthInputEverReceived()}");
          while (latestLocalFrame >= _frameResolver.GetNextAckFrame() && 
                 _clientSyncer.TryGetFullSyncData(_frameResolver.GetNextAckFrame(), ackData))
          {
              _worldLogger.Log($"localworldmanager Received ack frame {_frameResolver.GetNextAckFrame()}");
              _playerInputBuilder.UpdateOtherPlayerLastInput(ackData.GetClientInputData());
                
              int ourHash = _game.GetWorld().GetFrameHash(ackData.GetClientInputData().GetFrameNum());
              if (ourHash != ackData.GetFullStateHash())
              {
                  //1. Roll back to latest Ack'd frame
                  //2. Apply next ack'd input
                  //3. Confirm hash (if failure, de-sync error)
                  //4. If success, continue to pull ack'd inputs and apply, confirming hash until finished.
                  //5. Apply all local inputs that have not yet been ack'd on top of new latest ack frame.
                    
                  if (_logFrameTimings)
                  {
                      _worldLogger.LogError($"Mispredict on {_frameResolver.GetNextAckFrame()}, auth: {ackData.GetFullStateHash()}, client: {ourHash}");
                  }
                    
                  _game.GetWorld().RestoreToFrame(_frameResolver.GetNextAckFrame() - 1);
                  _viewProcessor.ResetToFrame(ackData.GetClientInputData().GetFrameNum());
                  _game.GetWorld().Tick(ackData.GetClientInputData());
                  _frameResolver.PushLatestFrameToInterpolator();
                  ourHash = _game.GetWorld().GetFrameHash(ackData.GetClientInputData().GetFrameNum());
                  if (ourHash != ackData.GetFullStateHash())
                  {
                      throw new Exception(); //de-sync error
                  }
                  else
                  {
                      if (_logFrameTimings)
                      { 
                          _worldLogger.LogError($"Rolled back successfully to {_frameResolver.GetNextAckFrame() - 1}");
                      }
                        
                      _frameResolver.AdvanceAckFrame();
                      while (latestLocalFrame >= _frameResolver.GetNextAckFrame() && 
                             _clientSyncer.TryGetFullSyncData(_frameResolver.GetNextAckFrame(), ackData))
                      { 
                          _game.GetWorld().Tick(ackData.GetClientInputData());
                          _frameResolver.PushLatestFrameToInterpolator();
                          ourHash = _game.GetWorld().GetFrameHash(ackData.GetClientInputData().GetFrameNum());
                          if (ourHash != ackData.GetFullStateHash())
                          {
                              throw new Exception(); //de-sync error
                          }
                            
                          _frameResolver.AdvanceAckFrame(); 
                      }
                        
                      //let's apply our input back on top
                      for(int i = _frameResolver.GetNextAckFrame(); i <= latestLocalFrame; i++)
                      {
                          var input = _playerInputBuilder.GetPredictedInput(i, _predictedInput, _componentPool);
                          _game.GetWorld().Tick(input);
                          _predictedInput.Reset();
                          _frameResolver.PushLatestFrameToInterpolator();
                      }
                  }
              }
              else
              {
                  if (_logFrameTimings)
                  {
                      _worldLogger.Log("Successful prediciton");
                  }
                  
                  _frameResolver.AdvanceAckFrame(); 
              }
                
              _worldLogger.Log($"LocalWorldManager Releasing full sync lock for frame {ackData.mergedInputData.FrameNum}, lock id {_fullSyncLockId}");
              _clientSyncer.ReleaseFullInputLockForFramesAtOrBelow(ackData.mergedInputData.FrameNum, _fullSyncLockId);
          }
            
          _frameToAckDataPool.ReturnAll();
        }

      public bool TryModifyDeltaTime(int playerSlot, float dtMs, out float modifiedDt)
      {
          modifiedDt = dtMs;
          int aheadBy = _game.GetWorld().GetNextFrameNum() - _clientSyncer.LatestConsecutiveAuthInputEverReceived();
          
          _clientSyncer.AddAheadByValue(playerSlot, aheadBy);

          float avgAheadBy = _clientSyncer.GetAheadByValue(playerSlot);
          float peerAvgAheadBy = _clientSyncer.GetAheadByValue((playerSlot + 1) % 2);

          float aheadByDelta = avgAheadBy - peerAvgAheadBy;
          
          _worldLogger.Log($"next local is '{_game.GetWorld().GetNextFrameNum()}', ahead by {aheadByDelta}");
          if (aheadByDelta >= _allowedToBeAheadByTicks)
          {
              return false;
          }
          
          if (aheadByDelta > 2)
          {
              modifiedDt *= 0.9f;   
          }
          else if (aheadByDelta > 1)
          {
              modifiedDt *= 0.96f;
          }
          else if (aheadByDelta > 0)
          {
              modifiedDt *= 0.99f;
          }

          return true;
      }
    }
}
