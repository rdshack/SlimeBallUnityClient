
namespace Indigo.EcsClientCore
{
  public class GameClock
  {
    private double _timeStepMs;
    private double _elapsedGameTimeMs;
    private double _accumulator;

    public GameClock(double timeStepMs)
    {
      _timeStepMs = timeStepMs;
    }
  
    public double GetTimeStepMs()
    {
      return _timeStepMs;
    }
        
    public double GetElapsedGameTimeSeconds()
    {
      return _elapsedGameTimeMs / 1000f;
    }

    public void Reset()
    {
      _elapsedGameTimeMs = 0;
      _accumulator = 0;
    }

    public int AdvanceAndGetTicks(double frameMs)
    {
      _accumulator += frameMs;

      int gameTicksToProcess = 0;
      while (_accumulator >= GetTimeStepMs())
      {
        gameTicksToProcess++;
        _accumulator -= GetTimeStepMs();
        _elapsedGameTimeMs += GetTimeStepMs();
      }

      return gameTicksToProcess;
    }
  } 
}