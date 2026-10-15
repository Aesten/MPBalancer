using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using MPManagerLinking;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// In-game chat commands for admins (players who joined with the server's AdminPassword); "!b debug" is open to
// everyone, so testers can see what the mod changes on their own hits.
// "!mpb" and its short form "!b" both work:
//   !b                     list settings and their values
//   !b <setting>           show one setting's value and description
//   !b <setting> <value>   change a setting (announced to everyone)
//   !b reset               restore the mod's defaults
//   !b vanilla             set everything to vanilla behaviour, for A/B comparisons
//   !b debug               toggle a per-hit damage readout for yourself
//   !b speedo              toggle a once-per-second readout of your speed and top speed (horse, or on foot), as the server measures them
//   !b speedo stats        print your current horse's stats once, or on foot your movement stats
// Setting names can be shortened to any unique prefix ("!b horse_k 12"); unknown names get a suggestion.
//
// With MPManager on the server, the commands are routed through it (MPManagerLink): the command is hidden from
// chat, MPManager's aliases and !help include it, and it works from the server console (mpm cmd).
// Without MPManager, this mod hooks chat itself; the command then still appears in chat (the hook can observe
// messages but not swallow them).
//
// The hook runs inside the server's network packet handler. Sending network messages from there
// crashed the server (native SIGSEGV), so the hook only queues commands; they run on the next
// mission tick (ServerTickBehavior), and replies go out per peer, the way ChatBox sends chat.
internal static class ChatCommands
{
    private const string Prefix = "!mpb";

    private static readonly string[] Prefixes = { "!b", "!mpb" };

    private static ChatBox _chatBox;

    private static readonly Queue<(NetworkCommunicator Peer, string Message)> _pending = new Queue<(NetworkCommunicator, string)>();

    // ChatBox is added at the end of MultiplayerGame.OnInitialize, after InitializeGameStarter,
    // so this is called per mission and attaches once per ChatBox instance.
    public static void Attach()
    {
        if (!GameNetwork.IsDedicatedServer)
        {
            return;
        }
        if (RouteThroughMPManager())
        {
            if (_chatBox != null)
            {
                _chatBox.OnMessageReceivedAtDedicatedServer -= OnMessage;
                _chatBox = null;
            }
            return;
        }
        ChatBox chatBox = Game.Current?.GetGameHandler<ChatBox>();
        if (chatBox == null || chatBox == _chatBox)
        {
            return;
        }
        if (_chatBox != null)
        {
            _chatBox.OnMessageReceivedAtDedicatedServer -= OnMessage;
        }
        _chatBox = chatBox;
        _chatBox.OnMessageReceivedAtDedicatedServer += OnMessage;
        MPBalancerSubModule.Log("Chat commands attached.");
    }

    // Hands the commands to MPManager if it's loaded (then it handles chat for us); false without it. Called at
    // startup (every mod's DLL is loaded before any OnSubModuleLoad, so MPManager can be found), and again by Attach.
    public static bool RouteThroughMPManager()
    {
        bool wasRouted = MPManagerLink.Routed;
        bool routed = MPManagerLink.TryRegister(new[] { "mpb", "b" }, "[<setting> [value] | reset | vanilla | debug | speedo [stats]]",
            "MPBalancer settings: list them, show or change one, reset, vanilla values, or per-player readouts.", adminOnly: true, Run);
        if (routed)
        {
            // Defaults (the server's permissions file can change them): what only shows something is open to
            // everyone; changing settings is for admins.
            MPManagerLink.TryRegisterPermissions(
                "mpb = everyone", "mpb * = everyone", "mpb * * = admin", "mpb reset = admin", "mpb vanilla = admin",
                "mpb debug = everyone", "mpb speedo = everyone");
        }
        if (routed && !wasRouted)
        {
            MPBalancerSubModule.Log("Chat commands routed through MPManager.");
        }
        return routed;
    }

    private static void OnMessage(NetworkCommunicator fromPeer, string message)
    {
        if (message == null || !IsCommand(message.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)))
        {
            return;
        }
        lock (_pending)
        {
            _pending.Enqueue((fromPeer, message));
        }
    }

    // Called every mission tick by ServerTickBehavior.
    public static void ProcessPending()
    {
        while (true)
        {
            (NetworkCommunicator Peer, string Message) item;
            lock (_pending)
            {
                if (_pending.Count == 0)
                {
                    return;
                }
                item = _pending.Dequeue();
            }
            try
            {
                if (item.Peer.IsConnectionActive)
                {
                    HandleMessage(item.Peer, item.Message);
                }
            }
            catch (Exception e)
            {
                MPBalancerSubModule.Log("Chat command failed: " + e);
            }
        }
    }

    private static void HandleMessage(NetworkCommunicator fromPeer, string message)
    {
        string[] parts = message.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (!IsCommand(parts))
        {
            return;
        }
        Handle(fromPeer, parts.Skip(1).ToArray(), text => Reply(fromPeer, text), checkRights: true);
    }

    // Entry point when MPManager routes the command: it has already checked the caller's rights for these arguments
    // (its permissions file, or the defaults registered above). peer is null for the server console.
    private static void Run(NetworkCommunicator peer, string[] args, Action<string> reply)
    {
        Handle(peer, args, text => reply("[MPBalancer] " + text), checkRights: false);
    }

    // args: the words after "!mpb" (or "!b"). reply answers the caller. checkRights: standalone (no MPManager), only
    // "!b debug" is open to everyone.
    private static void Handle(NetworkCommunicator peer, string[] args, Action<string> reply, bool checkRights)
    {
        string by = peer?.UserName ?? "console";
        string command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        bool isAdmin = peer == null || peer.IsAdmin;
        if (checkRights && !isAdmin && command != "debug")
        {
            reply("Only \"!b debug\" is open to everyone; the rest is for admins (join with the server's admin password).");
            return;
        }
        switch (command)
        {
        case "help":
            foreach (ISetting s in BalanceSettings.All)
            {
                reply(s.ToString());
            }
            reply("Usage: " + Prefix + " <setting> [value] | reset | vanilla | debug | speedo [stats]");
            return;
        case "debug":
            if (peer == null)
            {
                reply("debug needs a player.");
                return;
            }
            reply(HitDebug.Toggle(peer) ? "Hit readout on." : "Hit readout off.");
            return;
        case "speedo":
            if (peer == null)
            {
                reply("speedo needs a player.");
                return;
            }
            if (args.Length > 1 && args[1].StartsWith("stat", StringComparison.OrdinalIgnoreCase))
            {
                Speedometer.PrintStats(peer);
                return;
            }
            reply(Speedometer.Toggle(peer) ? "Speedometer on (your speed, mounted or on foot, as the server measures it, every second)." : "Speedometer off.");
            return;
        case "reset":
            foreach (ISetting s in BalanceSettings.All)
            {
                s.Reset();
            }
            BalanceSettings.ApplyLive();
            Announce(by, "Reset to defaults: " + BalanceSettings.Summary());
            return;
        case "vanilla":
            foreach (ISetting s in BalanceSettings.All)
            {
                s.SetVanilla();
            }
            BalanceSettings.ApplyLive();
            Announce(by, "Set to vanilla values: " + BalanceSettings.Summary());
            return;
        }

        ISetting setting = FindSetting(reply, args[0]);
        if (setting == null)
        {
            return;
        }
        if (args.Length == 1)
        {
            reply(setting + " (default " + setting.DisplayDefault + ", vanilla " + setting.DisplayVanilla + "): " + setting.Description);
            return;
        }
        string old = setting.DisplayValue;
        if (!setting.TrySet(string.Join(" ", args, 1, args.Length - 1), out string error))
        {
            reply(error);
            return;
        }
        BalanceSettings.ApplyLive();
        Announce(by, setting.Name + ": " + old + " -> " + setting.DisplayValue);
    }

    private static bool IsCommand(string[] parts)
    {
        return parts.Length > 0 && Array.Exists(Prefixes, p => p.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
    }

    // Exact name, else a unique prefix; otherwise replies with the candidates or the closest name.
    private static ISetting FindSetting(Action<string> reply, string name)
    {
        ISetting setting = BalanceSettings.Find(name);
        if (setting != null)
        {
            return setting;
        }
        List<ISetting> matches = BalanceSettings.FindByPrefix(name);
        if (matches.Count == 1)
        {
            return matches[0];
        }
        if (matches.Count > 1)
        {
            reply("'" + name + "' matches " + string.Join(", ", matches.ConvertAll(s => s.Name)) + ".");
            return null;
        }
        reply("Unknown setting '" + name + "'. Did you mean " + BalanceSettings.Closest(name).Name + "? Type " + Prefix + " for the list.");
        return null;
    }

    private static void Reply(NetworkCommunicator peer, string text)
    {
        Send(peer, "[MPBalancer] " + text);
    }

    // Changes are shown to everyone, so all testers know which values are active.
    // To everyone, without who did it (that goes to the log only).
    private static void Announce(string by, string text)
    {
        MPBalancerSubModule.Log(by + " " + text);
        string line = text;
        if (!GameNetwork.NetworkPeersValid)
        {
            return;
        }
        foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
        {
            Send(peer, "[MPBalancer] " + line);
        }
    }

    internal static void Send(NetworkCommunicator peer, string text)
    {
        if (peer.IsServerPeer || !peer.IsSynchronized)
        {
            return;
        }
        GameNetwork.BeginModuleEventAsServer(peer);
        GameNetwork.WriteMessage(new ServerMessage(text));
        GameNetwork.EndModuleEventAsServer();
    }
}
