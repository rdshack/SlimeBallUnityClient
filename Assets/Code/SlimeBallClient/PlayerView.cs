using System;
using System.Collections.Generic;
using static SimMath.math;
using Indigo.EcsClientCore;
using UnityEngine;

namespace Indigo.SlimeBallClient
{
  public class PlayerView : MonoBehaviour
  {
    public Animator Animator;
    
    public AnimDriver             AnimDriver        { get; private set; }
    public Vector3                PosTarget         { get; set; }
    public InterpolatedPlayerData DataTarget        { get; set; }
    public bool                   IsLocalControlled { get; set; }
    public float                  LastMispredictX   { get; set; } = float.MinValue;
    public float                  LastMispredictY   { get; set; } = float.MinValue;

    private void Awake()
    {
      //AnimDriver = new AnimDriver(Animator);
    }
    

    private void Update()
    {
      float timeSinceMispredictX = Time.timeSinceLevelLoad - LastMispredictX;
      float timeSinceMispredictY = Time.timeSinceLevelLoad - LastMispredictY;
     // float timeSinceMispredictMult = max(1f, -0.6f * log(100 * timeSinceMispredict) + 4.3f);
      float timeSinceMispredictMultX = max(1f, -0.58f * log(300 * timeSinceMispredictX) + 4.3f);
      float timeSinceMispredictMultY = max(1f, -0.58f * log(300 * timeSinceMispredictY) + 4.3f);
      
      float movementLerpDenomX = IsLocalControlled ? 0.03f : 0.03f * 1;
      float movementLerpDenomY = IsLocalControlled ? 0.03f : 0.03f * 1;
      
      float movementLerpValX = clamp(Time.deltaTime / movementLerpDenomX, 0, 1);
      float movementLerpValY = clamp(Time.deltaTime / movementLerpDenomY, 0, 1);

      float finalX = Mathf.Lerp(transform.position.x, PosTarget.x, movementLerpValX);
      float finalY = Mathf.Lerp(transform.position.y, PosTarget.y, movementLerpValY);
      transform.position = new Vector3(finalX, finalY, 0);
    }
  } 
}
