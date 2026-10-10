using MPBalancer.Models;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

public class MPBalancerSubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        Log("Loaded.");
    }

    // MultiplayerGame registers its own models before this runs, and GetGameModel<T>()
    // returns the most recently added one, so ours replace the built-in models.
    protected override void InitializeGameStarter(Game game, IGameStarter starterObject)
    {
        base.InitializeGameStarter(game, starterObject);
        if (game.GameType is MultiplayerGame)
        {
            starterObject.AddModel(new BalancedStrikeMagnitudeModel());
            starterObject.AddModel(new BalancedApplyDamageModel());
            starterObject.AddModel(new BalancedAgentStatModel());
            Log("Registered models (" + BalanceSettings.Summary() + ").");
        }
    }

    public override void OnMissionBehaviorInitialize(Mission mission)
    {
        base.OnMissionBehaviorInitialize(mission);
        if (GameNetwork.IsDedicatedServer)
        {
            ChatCommands.Attach();
            mission.AddMissionBehavior(new ServerTickBehavior());
            WeaponFactors.EnsureAutoReferences(); // logs the auto references once, at the first mission
            HorseCharge.ApplySpeedLimit(); // bump_speed lives in the Monster objects, read at each spawn
            NativeParameters.Apply();
        }
    }

    internal static void Log(string message)
    {
        Debug.Print("[MPBalancer] " + message, 0, Debug.DebugColor.Green);
    }
}
