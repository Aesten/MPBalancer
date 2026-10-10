using TaleWorlds.MountAndBlade;

namespace MPBalancer.Models;

// Horse top speed, maneuver and acceleration. Agent stats are not sent to clients, but the server is in
// charge of movement: tested with "!b speedo", horses reach exactly the server's MountSpeed, without
// rubber-banding. Maneuver and acceleration are untested that way yet (turning is client-predicted, so
// watch for snapping). Couching needs at least 9 m/s (native parameter mounted_passive_action_minimum_speed,
// read by clients from their own files), so top speeds must stay well above that.
public class BalancedAgentStatModel : MultiplayerAgentStatCalculateModel
{
    public override void UpdateAgentStats(Agent agent, AgentDrivenProperties agentDrivenProperties)
    {
        base.UpdateAgentStats(agent, agentDrivenProperties);
        if (agent.IsMount)
        {
            agentDrivenProperties.MountSpeed = Combat.SoftCap(
                agentDrivenProperties.MountSpeed * BalanceSettings.HorseSpeed.Value,
                BalanceSettings.HorseKnee.Value, BalanceSettings.HorseCap.Value);
            agentDrivenProperties.MountManeuver *= BalanceSettings.HorseManeuver.Value;
            agentDrivenProperties.TopSpeedReachDuration *= BalanceSettings.HorseAccelTime.Value;
        }
    }

    // Re-runs the stat model on every horse, so a horse setting change applies without respawning.
    public static void RefreshAllMounts()
    {
        if (Mission.Current == null)
        {
            return;
        }
        foreach (Agent agent in Mission.Current.Agents)
        {
            if (agent.IsMount && agent.IsActive())
            {
                agent.UpdateAgentProperties();
            }
        }
    }
}
