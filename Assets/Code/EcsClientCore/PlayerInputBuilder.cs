using System;
using System.Collections.Generic;
using ecs;
using SimMath;
using UnityEngine;

namespace Indigo.EcsClientCore
{
  public class PlayerLastInput
  {
    public int2 moveVector;
  }
  
  public class PlayerInputBuilder
  {
    private PlayerInputBuffer                _playerInputBuffer;
    private KeyboardReader                   _keyboardReader;
    private bool                             _joined;
    private int                              _localPlayerSlot;
    private List<int>                        _players;
    private Dictionary<int, PlayerLastInput> _playerLastInputs = new Dictionary<int, PlayerLastInput>();

    private IComponentFactory     _copier;
    private ComponentDefinitions _componentDefinitions;
    private ArchetypeGraph       _archetypeGraph;

    public PlayerInputBuilder(int localPlayerSlot, PlayerInputBuffer buffer, List<int> players, KeyboardReader.MoveKeyStyle moveStyle)
    {
      _playerInputBuffer = buffer;
      _keyboardReader = new KeyboardReader(moveStyle);
      _localPlayerSlot = localPlayerSlot;
      _players = players;
      _players.Sort();

      foreach (var p in _players)
      {
        if (p == _localPlayerSlot)
        {
          continue;
        }
        
        _playerLastInputs.Add(p, new PlayerLastInput());
      }
      
      _componentDefinitions = new ComponentDefinitions();
      _archetypeGraph = new ArchetypeGraph(_componentDefinitions, new AliasLookup());
    }

    public void BuildFirstEmptyInputFrame()
    {
      _playerInputBuffer.StartFrameWithInputDelay(1);
      _playerInputBuffer.PushPlayerInput(new int2(0, 0), false, _localPlayerSlot);
      //_playerInputBuffer.PushAddPlayerInput(0);
      //_playerInputBuffer.PushAddPlayerInput(1);
      _playerInputBuffer.FinishFrame();
    }
    
    public void ReadLatest()
    {
      _keyboardReader.ReadLatest();
    }

    public void BuildNextInputFrame(int nextSimFrame)
    {
      int2 moveInput = new int2(_keyboardReader.GetLastMoveInput().x, _keyboardReader.GetLastMoveInput().y) * 1000;

      _playerInputBuffer.StartFrameWithInputDelay(nextSimFrame);
      
      bool spacePressed = _keyboardReader.IsSpacePressed();

      if (_joined)
      {
        _playerInputBuffer.PushPlayerInput(moveInput, spacePressed, _localPlayerSlot);
      }
      else if (spacePressed)
      {
        _joined = true;
        _playerInputBuffer.PushAddPlayerInput(_localPlayerSlot);
      }
      
      _playerInputBuffer.FinishFrame();
      _keyboardReader.Clear();
    }

    public int LatestInputFrame()
    {
      return _playerInputBuffer.LatestInputFrame();
    }

    public IFrameInputData GetPredictedInput(int frame, FrameInputData copyTo, IComponentFactory pool)
    {
      if (!_playerInputBuffer.GetPreviousInput(frame, copyTo))
      {
        throw new Exception();
      }

      //Let's also predict what all our remote friends are doing, based on their last movement
      foreach (var player in _players)
      {
        if (player == _localPlayerSlot)
        {
          continue;
        }
                            
        ComponentTypeIndex playerInputIndex = _componentDefinitions.GetIndex<PlayerInputComponent>();
        PlayerInputComponent playerInputComponent = (PlayerInputComponent)pool.Get(playerInputIndex);

        playerInputComponent.moveInputX = _playerLastInputs[player].moveVector.x;
        playerInputComponent.jumpPressed = false;
    
        ComponentTypeIndex playerOwnedIndex = _componentDefinitions.GetIndex<PlayerOwnedComponent>();
        PlayerOwnedComponent playerOwnedComponent = (PlayerOwnedComponent)pool.Get(playerOwnedIndex);
        playerOwnedComponent.playerId = player;

        ComponentGroup componentGroup =
          pool.GetComponentGroup(_archetypeGraph, _componentDefinitions);
        
        componentGroup.Setup(pool, _archetypeGraph, _componentDefinitions);
        componentGroup.AddComponentType(playerInputIndex, playerInputComponent);
        componentGroup.AddComponentType(playerOwnedIndex, playerOwnedComponent);

        copyTo.AddComponentGroup(componentGroup); 
      }

      return copyTo;
    }

    public void UpdateOtherPlayerLastInput(IFrameInputData input)
    {
      foreach (var inputGroup in input.GetComponentGroups())
      {
        if (inputGroup.GetArchetype() != _archetypeGraph.GetAliasArchetype(AliasLookup.PlayerInput))
        {
          continue;
        }
        
        var pId = inputGroup.Get<PlayerOwnedComponent>().playerId;
        if (pId == _localPlayerSlot)
        {
          continue;
        }

        _playerLastInputs[pId].moveVector = new int2(inputGroup.Get<PlayerInputComponent>().moveInputX, 0);
      }
    }
  }
}