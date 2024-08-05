using System;
using System.Collections.Generic;
using static SimMath.math;
using Indigo.EcsClientCore;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Indigo.SlimeBallClient
{
  public class BallView : MonoBehaviour
  {
    public Vector3                PosTarget         { get; set; }
    public Quaternion             RotTarget         { get; set; }

    private bool  _markedForDestroy;
    private bool  _reachedFinalPos;
    private float _destroyTimer;

    public void MarkForDestroy()
    {
      _markedForDestroy = true;
    }

    private void Update()
    {
      if (_markedForDestroy)
      {
        if(_reachedFinalPos)
        {
          _destroyTimer += Time.deltaTime;
          if (_destroyTimer > 0.1f)
          {
            Object.Destroy(gameObject);
          }
        }
        else
        {
          float distX = Mathf.Abs(transform.position.x - PosTarget.x);
          float distY = Mathf.Abs(transform.position.y - PosTarget.y);
          if (distX < 0.01f && distY < 0.001f)
          {
            _reachedFinalPos = true;
          }
        }
      }
      
      float movementLerpDenomX = 0.03f;
      float movementLerpDenomY = 0.03f;
      
      float movementLerpValX = clamp(Time.deltaTime / movementLerpDenomX, 0, 1);
      float movementLerpValY = clamp(Time.deltaTime / movementLerpDenomY, 0, 1);

      float finalX = Mathf.Lerp(transform.position.x, PosTarget.x, movementLerpValX);
      float finalY = Mathf.Lerp(transform.position.y, PosTarget.y, movementLerpValY);
      transform.position = new Vector3(finalX, finalY, 0);
      transform.rotation = RotTarget;
    }
  } 
}
