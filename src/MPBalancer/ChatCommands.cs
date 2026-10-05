using System;
using System.Collections.Generic;
using System.Globalization;
using MPBalancer.Models;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace MPBalancer;

// In-game chat commands for admins (players who joined with the server's AdminPassword).
// "!b" and "!mpb" both work:
//   !b                     list settings and their values
//   !b <setting>           show one setting's value and description
//   !b <setting> <value>   change a setting (announced to everyone)
//   !b reset               restore the mod's defaults
//   !b vanilla             set everything to vanilla behaviour, for A/B comparisons
//   !b debug               toggle a per-hit damage readout for yourself
//   !b speedo              toggle a once-per-second readout of your horse's speed as the server sees it
// Setting names can be shortened to any unique prefix ("!b horse_k 12"); unknown names get a suggestion.
// The command itself still appears in chat: the hook can observe messages but not swallow them.
//
// The hook runs inside the server's network packet handler. Sending network messages from there
// crashed the server (native SIGSEGV), so the hook only queues commands; they run on the next
// mission tick (ServerTickBehavior), and replies go out per peer, the way ChatBox sends chat.
internal static class ChatCommands
{
    private const string Prefix = "!b";

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
        if (!fromPeer.IsAdmin)
        {
            Reply(fromPeer, "Commands are admin-only. Join with the server's admin password.");
            return;
        }

        string command = parts.Length > 1 ? parts[1].ToLowerInvariant() : "help";
        switch (command)
        {
        case "help":
            foreach (ISetting s in BalanceSettings.All)
            {
                Reply(fromPeer, s.ToString());
            }
            Reply(fromPeer, "Usage: " + Prefix + " <setting> [value] | reset | vanilla | debug | speedo");
            return;
        case "debug":
            Reply(fromPeer, HitDebug.Toggle(fromPeer) ? "Hit readout on." : "Hit readout off.");
            return;
        case "speedo":
            Reply(fromPeer, Speedometer.Toggle(fromPeer) ? "Speedometer on (your horse, as the server measures it, every second)." : "Speedometer off.");
            return;
        case "reset":
            foreach (ISetting s in BalanceSettings.All)
            {
                s.Reset();
            }
            BalancedAgentStatModel.RefreshAllMounts();
            Announce(fromPeer, "reset to defaults: " + BalanceSettings.Summary());
            return;
        case "vanilla":
            foreach (ISetting s in BalanceSettings.All)
            {
                s.SetVanilla();
            }
            BalancedAgentStatModel.RefreshAllMounts();
            Announce(fromPeer, "set vanilla values: " + BalanceSettings.Summary());
            return;
        }

        ISetting setting = FindSetting(fromPeer, parts[1]);
        if (setting == null)
        {
            return;
        }
        if (parts.Length == 2)
        {
            Reply(fromPeer, setting + " (default " + setting.DisplayDefault + ", vanilla " + setting.DisplayVanilla + "): " + setting.Description);
            return;
        }
        string old = setting.DisplayValue;
        if (!setting.TrySet(string.Join(" ", parts, 2, parts.Length - 2), out string error))
        {
            Reply(fromPeer, error);
            return;
        }
        if (BalanceSettings.IsHorseSetting(setting))
        {
            BalancedAgentStatModel.RefreshAllMounts();
        }
        Announce(fromPeer, setting.Name + ": " + old + " -> " + setting.DisplayValue);
    }

    private static bool IsCommand(string[] parts)
    {
        return parts.Length > 0 && Array.Exists(Prefixes, p => p.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
    }

    // Exact name, else a unique prefix; otherwise replies with the candidates or the closest name.
    private static ISetting FindSetting(NetworkCommunicator fromPeer, string name)
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
            Reply(fromPeer, "'" + name + "' matches " + string.Join(", ", matches.ConvertAll(s => s.Name)) + ".");
            return null;
        }
        Reply(fromPeer, "Unknown setting '" + name + "'. Did you mean " + BalanceSettings.Closest(name).Name + "? Type " + Prefix + " for the list.");
        return null;
    }

    private static void Reply(NetworkCommunicator peer, string text)
    {
        Send(peer, "[MPBalancer] " + text);
    }

    // Changes are shown to everyone, so all testers know which values are active.
    private static void Announce(NetworkCommunicator byPeer, string text)
    {
        string line = byPeer.UserName + " " + text;
        MPBalancerSubModule.Log(line);
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
