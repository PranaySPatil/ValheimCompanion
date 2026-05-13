using System.Collections.Generic;
using Jotunn.Entities;
using JotunnModStub.Companions.AI;
using JotunnModStub.Companions.Data;
using JotunnModStub.Companions.Diagnostics;
using JotunnModStub.Companions.Identity;
using UnityEngine;

namespace JotunnModStub.Companions.Acquisition
{
    internal sealed class OrderCommand : ConsoleCommand
    {
        public override string Name => "valhein_order";

        public override string Help => "valhein_order follow|stay|aggressive|defensive [name] — issue an order to your wolf.";

        public override void Run(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.instance?.Print(Help);
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null)
            {
                Console.instance?.Print("No local player.");
                return;
            }

            string steamId = OwnerIdentity.GetSteamIdOf(player);
            if (string.IsNullOrEmpty(steamId))
            {
                Console.instance?.Print("Could not resolve your SteamID.");
                return;
            }

            CompanionOrder order;
            switch (args[0].ToLowerInvariant())
            {
                case "follow":     order = CompanionOrder.Follow; break;
                case "stay":       order = CompanionOrder.Stay; break;
                case "aggressive": order = CompanionOrder.Aggressive; break;
                case "defensive":  order = CompanionOrder.Defensive; break;
                default:
                    Console.instance?.Print($"Unknown order '{args[0]}'.");
                    return;
            }

            string nameFilter = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : null;

            var owned = new List<CompanionHandle>();
            foreach (var h in CompanionRegistry.AllOwnedBy(steamId)) owned.Add(h);

            CompanionHandle target = null;
            if (owned.Count == 0)
            {
                Console.instance?.Print("You have no companions.");
                return;
            }
            else if (nameFilter == null)
            {
                if (owned.Count == 1)
                {
                    target = owned[0];
                }
                else
                {
                    var sb = new System.Text.StringBuilder("You have multiple companions — specify a name: ");
                    for (int i = 0; i < owned.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(owned[i].Name);
                    }
                    Console.instance?.Print(sb.ToString());
                    return;
                }
            }
            else
            {
                foreach (var h in owned)
                {
                    if (string.Equals(h.Name, nameFilter, System.StringComparison.OrdinalIgnoreCase))
                    {
                        target = h;
                        break;
                    }
                }
                if (target == null)
                {
                    var loc = Localization.instance;
                    string text = loc != null ? loc.Localize("$valhein.cmd.unknown_companion") : "No companion named '{0}' found.";
                    Console.instance?.Print(string.Format(text, nameFilter));
                    return;
                }
            }

            var ai = FindAIById(target.ZdoId);
            if (ai == null)
            {
                Console.instance?.Print($"'{target.Name}' is not currently loaded.");
                return;
            }

            ai.SetOrder(order, ai.transform.position);
            AnnounceOrder(target.Name, order);
            Log.Info($"order set: {target.Name} -> {order}");
        }

        private static WolfCompanionAI FindAIById(ZDOID id)
        {
            if (ZNetScene.instance == null) return null;
            foreach (var view in Object.FindObjectsOfType<ZNetView>())
            {
                if (view == null || !view.IsValid()) continue;
                if (view.GetZDO().m_uid != id) continue;
                return view.GetComponent<WolfCompanionAI>();
            }
            return null;
        }

        private static void AnnounceOrder(string name, CompanionOrder order)
        {
            try
            {
                if (MessageHud.instance == null) return;
                var loc = Localization.instance;
                string text = loc != null ? loc.Localize("$valhein.order.set") : "{0}: {1}.";
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, string.Format(text, name, order));
            }
            catch { }
        }

        internal static WolfCompanionAI FindAIByIdPublic(ZDOID id) => FindAIById(id);
    }

    internal sealed class DismissCommand : ConsoleCommand
    {
        public override string Name => "valhein_dismiss";
        public override string Help => "valhein_dismiss [name] — permanently remove a companion (frees a slot).";

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                Console.instance?.Print("No local player.");
                return;
            }

            string steamId = OwnerIdentity.GetSteamIdOf(player);
            if (string.IsNullOrEmpty(steamId))
            {
                Console.instance?.Print("Could not resolve your SteamID.");
                return;
            }

            string nameFilter = args != null && args.Length > 0 ? string.Join(" ", args, 0, args.Length) : null;

            var owned = new List<CompanionHandle>();
            foreach (var h in CompanionRegistry.AllOwnedBy(steamId)) owned.Add(h);
            if (owned.Count == 0)
            {
                Console.instance?.Print("You have no companions.");
                return;
            }

            CompanionHandle target = null;
            if (nameFilter == null)
            {
                if (owned.Count == 1) target = owned[0];
                else
                {
                    var sb = new System.Text.StringBuilder("Specify a name: ");
                    for (int i = 0; i < owned.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(owned[i].Name); }
                    Console.instance?.Print(sb.ToString());
                    return;
                }
            }
            else
            {
                foreach (var h in owned)
                {
                    if (string.Equals(h.Name, nameFilter, System.StringComparison.OrdinalIgnoreCase))
                    {
                        target = h;
                        break;
                    }
                }
                if (target == null)
                {
                    Console.instance?.Print($"No companion named '{nameFilter}'.");
                    return;
                }
            }

            var ai = OrderCommand.FindAIByIdPublic(target.ZdoId);
            CompanionRegistry.Remove(target.ZdoId);

            if (ai != null)
            {
                var view = ai.GetComponent<ZNetView>();
                if (view != null && view.IsValid())
                {
                    if (!view.IsOwner()) view.ClaimOwnership();
                    view.Destroy();
                }
                else
                {
                    Object.Destroy(ai.gameObject);
                }
            }
            else
            {
                // Not currently loaded — mark the ZDO for destruction directly.
                if (ZDOMan.instance != null)
                {
                    var zdo = ZDOMan.instance.GetZDO(target.ZdoId);
                    if (zdo != null)
                    {
                        ZDOMan.instance.DestroyZDO(zdo);
                    }
                }
            }

            Console.instance?.Print($"Dismissed '{target.Name}'.");
            Log.Info($"dismissed companion '{target.Name}' ({target.ZdoId})");
        }
    }
}
