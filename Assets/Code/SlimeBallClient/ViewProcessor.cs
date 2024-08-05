using System.Collections.Generic;
using ecs;
using Indigo.EcsClientCore;
using Indigo.SlimeBallClient;
using TMPro;
using UnityEngine;

namespace Indigo.SlimeBallClient
{
  public class ViewProcessor : MonoBehaviour, IGameViewProcessor
  {
    public GameObject      PressStartGameObject;
    public TextMeshProUGUI P1Score;
    public TextMeshProUGUI P2Score;
    
    private Vector2                          _worldOffset;
    private List<EntityId>                   _knownEntitiesIdList   = new List<EntityId>();
    private Dictionary<EntityId, PlayerView> _knownPlayerEntities   = new Dictionary<EntityId, PlayerView>();
    private Dictionary<EntityId, BallView>   _knownBallEntities     = new Dictionary<EntityId, BallView>();
    private HashSet<EntityId>                _seenEntitiesThisFrame = new HashSet<EntityId>();
    private int                              _localPlayerId;
    private FrameInterpolator                _frameInterpolator;
    private InterpolatedFrameData            _interpResult = new InterpolatedFrameData();

    public void Init(IWorldLogger logger, Vector2 worldOffset, int localPlayerId, double msPerFrame)
    {
      _localPlayerId = localPlayerId;
      _worldOffset = worldOffset;
      _frameInterpolator = new FrameInterpolator(logger, localPlayerId, msPerFrame);
    }

    public void ResetToFrame(int frame)
    {
      _frameInterpolator.ResetToFrame(frame);
    }

    private void ProcessInterpFrame(InterpolatedFrameData cur)
    {
      if (cur == null)
      {
        return;
      }

      P1Score.text = cur.Score.x.ToString();
      P2Score.text = cur.Score.y.ToString();

      foreach (var joinedPlayer in cur.PlayersJoinedThisFrame)
      {
        if (joinedPlayer == _localPlayerId)
        {
          PressStartGameObject.SetActive(false);
        }
      }
      
      foreach (var interpData in cur.PlayerDatas)
      {
        _seenEntitiesThisFrame.Add(interpData.id);
        
        if (!_knownPlayerEntities.TryGetValue(interpData.id, out PlayerView boundPlayerView))
        {
          boundPlayerView = Object.Instantiate(TempPlayerViewBindingData.Instance.PlayerViewPrefab);

          if (interpData.playerOwner == 1)
          {
            boundPlayerView.transform.localScale = new Vector3(boundPlayerView.transform.localScale.x * -1,
                                                               boundPlayerView.transform.localScale.y, 
                                                               boundPlayerView.transform.localScale.z);
          }
          
          _knownPlayerEntities[interpData.id] = boundPlayerView;
          _knownEntitiesIdList.Add(interpData.id);
        }

        boundPlayerView.IsLocalControlled = interpData.playerOwner == _localPlayerId;
        boundPlayerView.PosTarget = interpData.pos + new Vector3(_worldOffset.x, _worldOffset.y, 0);
        boundPlayerView.DataTarget = interpData;
      }
      
      foreach (var interpData in cur.BallDatas)
      {
        _seenEntitiesThisFrame.Add(interpData.id);
        
        if (!_knownBallEntities.TryGetValue(interpData.id, out BallView boundBallView))
        {
          boundBallView = Object.Instantiate(TempPlayerViewBindingData.Instance.BallViewPrefab);
          _knownBallEntities[interpData.id] = boundBallView;
          _knownEntitiesIdList.Add(interpData.id);
        }
        
        boundBallView.PosTarget = interpData.pos + new Vector3(_worldOffset.x, _worldOffset.y, 0);
        boundBallView.RotTarget = interpData.rot;
      }

      for (int i = _knownEntitiesIdList.Count - 1; i >= 0; i--)
      {
        if (!_seenEntitiesThisFrame.Contains(_knownEntitiesIdList[i]))
        {
          if (_knownPlayerEntities.TryGetValue(_knownEntitiesIdList[i], out PlayerView playerView))
          {
            Object.Destroy(playerView.gameObject);
            _knownPlayerEntities.Remove(_knownEntitiesIdList[i]);
            _knownEntitiesIdList.RemoveAt(i);
          }
          else if (_knownBallEntities.TryGetValue(_knownEntitiesIdList[i], out BallView ballView))
          {
            ballView.MarkForDestroy();
            _knownBallEntities.Remove(_knownEntitiesIdList[i]);
            _knownEntitiesIdList.RemoveAt(i);
          }
        }
      }
      
      _seenEntitiesThisFrame.Clear();
    }

    public void FeedTime(float ms)
    {
      _interpResult.Reset();
      _frameInterpolator.FeedTime(ms, _interpResult);
      ProcessInterpFrame(_interpResult);
    }

    public void PushFrame(IDeserializedFrameDataStore frame)
    {
      _frameInterpolator.PushFrame(frame);
    }
  }
}