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
        ChatCommands.RouteThroughMPManager(); // so !help lists !b from the start, not from the first mission
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

    // Items are loaded by now: apply the item overrides at startup, so a problem shows when the server starts rather
    // than when the first player joins (they're applied again at each mission and after every settings change).
    public override void OnGameInitializationFinished(Game game)
    {
        base.OnGameInitializationFinished(game);
        if (GameNetwork.IsDedicatedServer && game.GameType is MultiplayerGame)
        {
            ItemWeightOverrides.Apply();
            ThrustDamageOverrides.Apply();
            HorseHealthOverrides.Apply();
            SmallShieldPerk.Apply();
        }
    }

    public override void OnMissionBehaviorInitialize(Mission mission)
    {
        base.OnMissionBehaviorInitialize(mission);
        if (GameNetwork.IsDedicatedServer)
        {
            ChatCommands.Attach();
            HitLog.Start(); // once, at the first mission
            mission.AddMissionBehavior(new ServerTickBehavior());
            HorseCharge.ApplySpeedLimit(); // bump_speed lives in the Monster objects, read at each spawn
            ItemWeightOverrides.Apply(); // item weights live in the item objects, read at each spawn
            ThrustDamageOverrides.Apply(); // thrust damage factors too, read at each hit
            HorseHealthOverrides.Apply(); // horse health too
            SmallShieldPerk.Apply(); // perk templates, and the copies players hold
            NativeParameters.Apply();
        }
    }

    internal static void Log(string message)
    {
        Debug.Print("[MPBalancer] " + message, 0, Debug.DebugColor.Green);
    }
}
