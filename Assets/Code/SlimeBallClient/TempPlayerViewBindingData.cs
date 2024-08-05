
using System.Collections.Generic;
using Indigo.SlimeBallClient;
using Indigo.EcsClientCore;
using UnityEngine;

[CreateAssetMenu(menuName = "ScriptableObject/PlayerViewBinding")]
 public class TempPlayerViewBindingData : SingletonScriptableObject<TempPlayerViewBindingData>
 {
  public PlayerView PlayerViewPrefab;
  public BallView BallViewPrefab;
 }