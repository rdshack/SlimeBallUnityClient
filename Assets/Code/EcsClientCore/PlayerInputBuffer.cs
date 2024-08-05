using System;
using ecs;
using SimMath;


namespace Indigo.EcsClientCore
{
  public class PlayerInputBuffer
  {
    private int                   _playerSlot;
    private ClientSyncer         _clientSyncer;
    private IComponentFactory         _componentPool;
    private ComponentDefinitions _componentDefinitions;
    private ArchetypeGraph        _archetypeGraph;
    private AliasLookup           _aliasLookup;
    
    private PlayerFrameInputData _recycledPlayerFrameInputData = new PlayerFrameInputData();
    
    private FrameInputData _curBuildingFrame;
    private int            _inputDelayFrameCount = 1;
    private int            _nextInputFrame       = 1;
    
    public PlayerInputBuffer(IComponentFactory     copier, 
                             ComponentDefinitions componentDefinitions, 
                             ArchetypeGraph       archetypeGraph,
                             ClientSyncer         syncer,
                             int                  playerSlot)
    {
      _playerSlot = playerSlot;
      _componentPool = new ComponentCopier(componentDefinitions);
      _aliasLookup = new AliasLookup();
      _componentDefinitions = componentDefinitions;
      _archetypeGraph = archetypeGraph;
      _clientSyncer = syncer;
      _recycledPlayerFrameInputData.Setup(_componentPool, _aliasLookup, componentDefinitions);

      //go ahead and push out empty frames equal to input delay
      for (int i = 1; i <= _inputDelayFrameCount; i++)
      {
        _curBuildingFrame = _recycledPlayerFrameInputData.playerInputData;
        _curBuildingFrame.Setup(_componentPool, _aliasLookup, _componentDefinitions);
        _curBuildingFrame.Reset();
        _curBuildingFrame.FrameNum = i;
        PushPlayerInput(new int2(0, 0), false, _playerSlot);
        FinishFrame();
      }
    }


    public bool GetPreviousInput(int frameNum, FrameInputData copyTo)
    {
      if (!_clientSyncer.TryGetClientInputData(_playerSlot, frameNum, _recycledPlayerFrameInputData))
      {
        return false;
      }
      
      FrameInputData.CopyTo(_recycledPlayerFrameInputData.playerInputData, copyTo, _componentPool, _archetypeGraph, _componentDefinitions);
      _recycledPlayerFrameInputData.Reset();
      
      return true;
    }

    private void Reset(FrameInputData obj)
    {
      obj.Reset();
    }

    private void Reset(ComponentGroup obj)
    {
      obj.Reset();
    }

    public void StartFrameWithInputDelay(int curSimulatingFrame)
    {
      int inputFrameNumToStart = curSimulatingFrame + _inputDelayFrameCount;
      if (inputFrameNumToStart != _nextInputFrame)
      {
        throw new Exception();
      }
      
      if (_curBuildingFrame != null)
      {
        throw new Exception();
      }

      _curBuildingFrame = _recycledPlayerFrameInputData.playerInputData;
      _curBuildingFrame.Reset();
      _curBuildingFrame.Setup(_componentPool, _aliasLookup, _componentDefinitions);
      _curBuildingFrame.FrameNum = inputFrameNumToStart;
    }
    
    public void FinishFrame()
    {
      if (_curBuildingFrame == null)
      {
        throw new Exception();
      }
      
      _recycledPlayerFrameInputData.locallyAppliedTimestamp = DateTime.UtcNow.ToFileTimeUtc();
      _clientSyncer.PushClientInputData(_playerSlot, _recycledPlayerFrameInputData);
      _recycledPlayerFrameInputData.Reset();
      _curBuildingFrame = null;
      _nextInputFrame++;
    }

    public void PushPlayerInput(int2 moveDir, bool jump, int playerId)
    {
      if (_curBuildingFrame == null)
      {
        throw new Exception();
      }
      
      ComponentTypeIndex playerInputIndex = _componentDefinitions.GetIndex<PlayerInputComponent>();
      PlayerInputComponent playerInputComponent = (PlayerInputComponent)_componentPool.Get(playerInputIndex);
      playerInputComponent.moveInputX = moveDir.x;
      playerInputComponent.jumpPressed = jump;
      
      ComponentTypeIndex playerOwnedIndex = _componentDefinitions.GetIndex<PlayerOwnedComponent>();
      PlayerOwnedComponent playerOwnedComponent = (PlayerOwnedComponent)_componentPool.Get(playerOwnedIndex);
      playerOwnedComponent.playerId = playerId;

      ComponentGroup componentGroup =
        _componentPool.GetComponentGroup(_archetypeGraph, _componentDefinitions);
      
      componentGroup.Setup(_componentPool, _archetypeGraph, _componentDefinitions);
      componentGroup.AddComponentType(playerInputIndex, playerInputComponent);
      componentGroup.AddComponentType(playerOwnedIndex, playerOwnedComponent);

      _curBuildingFrame.AddComponentGroup(componentGroup);
    }
    
    public void PushAddPlayerInput(int playerId)
    {
      if (_curBuildingFrame == null)
      {
        throw new Exception();
      }
      
      ComponentTypeIndex playerInputIndex = _componentDefinitions.GetIndex<CreateNewPlayerInputComponent>();
      CreateNewPlayerInputComponent addPlayerInputComponent = (CreateNewPlayerInputComponent)_componentPool.Get(playerInputIndex);
      addPlayerInputComponent.playerId = playerId;
      
      ComponentTypeIndex playerOwnedIndex = _componentDefinitions.GetIndex<PlayerOwnedComponent>();
      PlayerOwnedComponent playerOwnedComponent = (PlayerOwnedComponent)_componentPool.Get(playerOwnedIndex);
      playerOwnedComponent.playerId = playerId;
      
      ComponentGroup componentGroup =
        _componentPool.GetComponentGroup(_archetypeGraph, _componentDefinitions);
      
      componentGroup.Setup(_componentPool, _archetypeGraph, _componentDefinitions);
      componentGroup.AddComponentType(playerInputIndex, addPlayerInputComponent);
      componentGroup.AddComponentType(playerOwnedIndex, playerOwnedComponent);

      _curBuildingFrame.AddComponentGroup(componentGroup);
    }

    public int LatestInputFrame()
    {
      return _nextInputFrame - 1;
    }
  } 
}
