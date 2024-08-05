

using ecs;

namespace Indigo.EcsClientCore
{
  public interface IGameViewProcessor
  {
    void FeedTime(float                        ms);
    void PushFrame(IDeserializedFrameDataStore frame);
    void ResetToFrame(int                      frame);
  }
}