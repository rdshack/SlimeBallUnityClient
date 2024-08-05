using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Indigo.EcsClientCore
{
 
  public class AnimDriver
  {
    private Animator _animator;
  
    public AnimDriver(Animator animator)
    {
      _animator = animator;
      _animator.speed = 0;
      _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
    }

    public void Advance(float dt)
    {
      _animator.speed = 1;
      _animator.Update(dt);
      _animator.speed = 0;
    }

    public void Play(string animName, float fixedTime = 0)
    {
      _animator.PlayInFixedTime(animName, 0, fixedTime);
    }

    public void SetFloat(string key, float v)
    {
      _animator.SetFloat(key, v); 
    }
  } 
}
