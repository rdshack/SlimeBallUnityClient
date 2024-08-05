
using System;
using System.Collections.Generic;
using ecs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using UnityEngine;

namespace Indigo.SlimeBallClient
{
  public class InterpolatedFrameData
  {
    public int2                         Score;
    public List<int>                    PlayersJoinedThisFrame = new List<int>();
    public List<InterpolatedPlayerData> PlayerDatas            = new List<InterpolatedPlayerData>();
    public List<InterpolatedBallData>   BallDatas              = new List<InterpolatedBallData>();

    public void Reset()
    {
      PlayersJoinedThisFrame.Clear();
      PlayerDatas.Clear();
      BallDatas.Clear();
    }
  }

  public struct InterpolatedPlayerData
  {
    public EntityId id;
    public int      playerOwner;
    public Vector3  pos;
  }

  public struct InterpolatedBallData
  {
    public EntityId   id;
    public Vector3    pos;
    public Quaternion rot;
  }

  public class FrameInterpolator
  {
    private EntityRepo                        _baseFrameRepo;
    private EntityRepo                        _targetFrameRepo;
    private int                               _latestFrame;
    private double                            _timeAcc;
    private int                               _ourPlayerSlot;
    private double                            _msPerFrame;
    private List<IDeserializedFrameDataStore> _sourceFrames = new List<IDeserializedFrameDataStore>();
    
    private Query _baseFramePlayerQuery;
    private Query _baseFrameBallQuery;
    private Query _baseFrameSpawnEventQuery;

    public FrameInterpolator(IWorldLogger logger, int ourPlayerSlot, double msPerFrame)
    {
      var compDef = new ComponentDefinitions();
      var aliasDef = new AliasLookup();
      var copier = new ComponentCopier(compDef);
      var archetypeGraph = new ArchetypeGraph(compDef, aliasDef);

      _msPerFrame = msPerFrame;
      _ourPlayerSlot = ourPlayerSlot;
      _baseFrameRepo = new EntityRepo(archetypeGraph, compDef, copier, logger);
      _targetFrameRepo = new EntityRepo(archetypeGraph, compDef, copier, logger);
      _baseFramePlayerQuery = new Query(archetypeGraph, compDef).SetContainsAliasFilter(AliasLookup.Slime);
      _baseFrameBallQuery = new Query(archetypeGraph, compDef).SetContainsAliasFilter(AliasLookup.Ball);
      _baseFrameSpawnEventQuery = new Query(archetypeGraph, compDef).SetContainsArchetypeFilter(archetypeGraph.With<PawnSpawnEventComponent>());
    }

    public void ResetToFrame(int frameNum)
    {
      for (int i = _sourceFrames.Count - 1; i >= 0; i--)
      {
        if (_sourceFrames[i].FrameNum >= frameNum)
        {
          RemoveSourceFrame(i);
        }
      }

      _timeAcc = 0;
      _latestFrame = frameNum - 1;
    }

    private void RemoveSourceFrame(int i)
    {
      _sourceFrames[i].Reset();
      _sourceFrames.RemoveAt(i);
    }

    public void PushFrame(IDeserializedFrameDataStore frameData)
    {
      //Debug.Log($"latest frame to frame interpolator {frameData.GetFrameNum()}");
      if (frameData.FrameNum != _latestFrame + 1)
      {
        //Debug.Log($"ERROR: Expected {_latestFrame + 1} and got {frameData.GetFrameNum()}");
        throw new Exception();
      }

      _latestFrame++;
      _sourceFrames.Add(frameData);
    }
    

    public void FeedTime(float ms, InterpolatedFrameData result)
    {
      if (_sourceFrames.Count < 2)
      {
        _timeAcc = 0;
        return;
      }
      
      
      _timeAcc += ms;
      while (_timeAcc > _msPerFrame)
      {
        if (_sourceFrames.Count > 2)
        {
          RemoveSourceFrame(0);
          _timeAcc -= _msPerFrame;
        }
        else
        {
          _timeAcc = _msPerFrame;
          break;
        }
      }
      
      while (_sourceFrames.Count > 2)
      {
        RemoveSourceFrame(0);
        _timeAcc = 0;
      }

      double lerpVal = min(1, _timeAcc / _msPerFrame);
      IDeserializedFrameDataStore baseFrame = _sourceFrames[0];
      IDeserializedFrameDataStore targetFrame = _sourceFrames[1];
      
      //Debug.LogWarning($"{_ourPlayerSlot}: view processor for {baseFrame.FrameNum}");


      InterpFrames(baseFrame, targetFrame, (float)lerpVal, result);
    }

    private void InterpFrames(IDeserializedFrameDataStore baseFrame, IDeserializedFrameDataStore targetFrame, float lerpVal, InterpolatedFrameData result)
    {
      result.Reset();
      _baseFrameRepo.ClearAndCopy(baseFrame);
      _targetFrameRepo.ClearAndCopy(targetFrame);

      GameComponent gameComponent = _baseFrameRepo.GetSingletonComponent<GameComponent>();
      result.Score = new int2(gameComponent.leftPlayerScore, gameComponent.rightPlayerScore);

      foreach (var spawnEvent in _baseFrameSpawnEventQuery.Resolve(_baseFrameRepo))
      {
        result.PlayersJoinedThisFrame.Add(spawnEvent.Get<PawnSpawnEventComponent>().playerId);
      }
      
      foreach (var baseFramePawn in _baseFramePlayerQuery.Resolve(_baseFrameRepo))
      {
        PositionComponent positionComponent = baseFramePawn.Get<PositionComponent>();
        int2 basePosRaw = new int2(positionComponent.posX, positionComponent.posY);
        float2 basePos = new float2(basePosRaw.x, basePosRaw.y) / 1000;

        if (_targetFrameRepo.Exists(baseFramePawn.GetEntityId()))
        {
          var targetFrameMatchingPawn = _targetFrameRepo.GetEntityData(baseFramePawn.GetEntityId());
          
          PositionComponent targetPosComp = targetFrameMatchingPawn.Get<PositionComponent>();
          int2 targetPosRaw = new int2(targetPosComp.posX, targetPosComp.posY);
          float2 targetPos = new float2(targetPosRaw.x, targetPosRaw.y) / 1000;
          float2 lerpedPos = lerp(basePos, targetPos, lerpVal);

          InterpolatedPlayerData interpData = new InterpolatedPlayerData();
          interpData.id = baseFramePawn.GetEntityId();
          interpData.playerOwner = baseFramePawn.Get<PlayerOwnedComponent>().playerId;
          interpData.pos = new Vector3(lerpedPos.x, lerpedPos.y, 0);

          result.PlayerDatas.Add(interpData);
        }
      }
      
      foreach (var baseFrameBall in _baseFrameBallQuery.Resolve(_baseFrameRepo))
      {
        PositionComponent basePosComp = baseFrameBall.Get<PositionComponent>();
        int2 basePosRaw = new int2(basePosComp.posX, basePosComp.posY);
        float2 basePos = new float2(basePosRaw.x, basePosRaw.y) / 1000;

        float baseRot = (float) baseFrameBall.Get<RotationComponent>().rot;

        if (_targetFrameRepo.Exists(baseFrameBall.GetEntityId()))
        {
          var targetFrameMatchingPawn = _targetFrameRepo.GetEntityData(baseFrameBall.GetEntityId());
          
          PositionComponent targetPosComp = targetFrameMatchingPawn.Get<PositionComponent>();
          int2 targetPosRaw = new int2(targetPosComp.posX, targetPosComp.posY);
          float2 targetPos = new float2(targetPosRaw.x, targetPosRaw.y) / 1000;
          float2 lerpedPos = lerp(basePos, targetPos, lerpVal);

          InterpolatedBallData interpData = new InterpolatedBallData();
          interpData.id = baseFrameBall.GetEntityId();
          interpData.pos = new Vector3(lerpedPos.x, lerpedPos.y, 0);
          
          float targetRot = (float) targetFrameMatchingPawn.Get<RotationComponent>().rot;
          float lerpedRot = lerp(baseRot, targetRot, lerpVal);
          
          interpData.rot = Quaternion.Euler(0, 0, lerpedRot);

          result.BallDatas.Add(interpData);
        }
      }
    }
  } 
}