using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// Server-only mission logic that sends queued chat output (command replies, hit readouts, speedometer) on the
// mission tick, outside the game's network and combat code that produced it.
public class ServerTickBehavior : MissionLogic
{
    public override void OnMissionTick(float dt)
    {
        ChatCommands.ProcessPending();
        HitDebug.ProcessPending();
        Speedometer.Tick(dt);
    }
}
