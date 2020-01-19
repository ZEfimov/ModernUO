using System;
using System.Collections.Generic;
using System.Linq;
using Server.Accounting;
using Server.Commands.Generic;
using Server.Engines.ConPVP;
using Server.Ethics;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Prompts;
using Server.Targeting;

namespace Server.Factions
{
  [CustomEnum(new[] { "Minax", "Council of Mages", "True Britannians", "Shadowlords" })]
  public abstract class Faction : IComparable<Faction>
  {
    public const int StabilityFactor = 300; // 300% greater (3 times) than smallest faction
    public const int StabilityActivation = 200; // Stability code goes into effect when largest faction has > 200 people

    public static readonly TimeSpan LeavePeriod = TimeSpan.FromDays(3.0);

    public static readonly Map Facet = Map.Felucca;

    private FactionDefinition m_Definition;
    public int ZeroRankOffset;

    public Faction() => State = new FactionState(this);

    public StrongholdRegion StrongholdRegion{ get; set; }

    public FactionDefinition Definition
    {
      get => m_Definition;
      set
      {
        m_Definition = value;
        StrongholdRegion = new StrongholdRegion(this);
      }
    }

    public FactionState State{ get; set; }

    public Election Election
    {
      get => State.Election;
      set => State.Election = value;
    }

    public Mobile Commander
    {
      get => State.Commander;
      set => State.Commander = value;
    }

    public int Tithe
    {
      get => State.Tithe;
      set => State.Tithe = value;
    }

    public int Silver
    {
      get => State.Silver;
      set => State.Silver = value;
    }

    public List<PlayerState> Members
    {
      get => State.Members;
      set => State.Members = value;
    }

    public bool FactionMessageReady => State.FactionMessageReady;

    public virtual int MaximumTraps => 15;

    public List<BaseFactionTrap> Traps
    {
      get => State.Traps;
      set => State.Traps = value;
    }

    public static List<Faction> Factions => Reflector.Factions;

    public int CompareTo(Faction f) => m_Definition.Sort - (f?.m_Definition.Sort ?? 0);

    public void Broadcast(string text)
    {
      Broadcast(0x3B2, text);
    }

    public void Broadcast(int hue, string text)
    {
      List<PlayerState> members = Members;

      for (int i = 0; i < members.Count; ++i)
        members[i].Mobile.SendMessage(hue, text);
    }

    public void Broadcast(int number)
    {
      List<PlayerState> members = Members;

      for (int i = 0; i < members.Count; ++i)
        members[i].Mobile.SendLocalizedMessage(number);
    }

    public void Broadcast(string format, params object[] args)
    {
      Broadcast(string.Format(format, args));
    }

    public void Broadcast(int hue, string format, params object[] args)
    {
      Broadcast(hue, string.Format(format, args));
    }

    public void BeginBroadcast(Mobile from)
    {
      from.SendLocalizedMessage(1010265); // Enter Faction Message
      from.Prompt = new BroadcastPrompt(this);
    }

    public void EndBroadcast(Mobile from, string text)
    {
      if (from.AccessLevel == AccessLevel.Player)
        State.RegisterBroadcast();

      Broadcast(Definition.HueBroadcast, "{0} [Commander] {1} : {2}", from.Name, Definition.FriendlyName, text);
    }

    public static void HandleAtrophy()
    {
      foreach (Faction f in Factions)
        if (!f.State.IsAtrophyReady)
          return;

      List<PlayerState> activePlayers = new List<PlayerState>();

      foreach (Faction f in Factions)
      foreach (PlayerState ps in f.Members)
        if (ps.KillPoints > 0 && ps.IsActive)
          activePlayers.Add(ps);

      int distrib = 0;

      foreach (Faction f in Factions)
        distrib += f.State.CheckAtrophy();

      if (activePlayers.Count == 0)
        return;

      for (int i = 0; i < distrib; ++i)
        activePlayers[Utility.Random(activePlayers.Count)].KillPoints++;
    }

    public static void DistributePoints(int distrib)
    {
      List<PlayerState> activePlayers = new List<PlayerState>();

      foreach (Faction f in Factions)
      foreach (PlayerState ps in f.Members)
        if (ps.KillPoints > 0 && ps.IsActive)
          activePlayers.Add(ps);

      if (activePlayers.Count > 0)
        for (int i = 0; i < distrib; ++i)
          activePlayers[Utility.Random(activePlayers.Count)].KillPoints++;
    }

    public void BeginHonorLeadership(Mobile from)
    {
      from.SendLocalizedMessage(502090); // Click on the player whom you wish to honor.
      from.BeginTarget(12, false, TargetFlags.None, HonorLeadership_OnTarget);
    }

    public void HonorLeadership_OnTarget(Mobile from, object obj)
    {
      if (obj is Mobile recv)
      {
        PlayerState giveState = PlayerState.Find(from);
        PlayerState recvState = PlayerState.Find(recv);

        if (giveState == null)
          return;

        if (recvState == null || recvState.Faction != giveState.Faction)
        {
          from.SendLocalizedMessage(1042497); // Only faction mates can be honored this way.
        }
        else if (giveState.KillPoints < 5)
        {
          from.SendLocalizedMessage(1042499); // You must have at least five kill points to honor them.
        }
        else
        {
          recvState.LastHonorTime = DateTime.UtcNow;
          giveState.KillPoints -= 5;
          recvState.KillPoints += 4;

          // TODO: Confirm no message sent to giver
          recv.SendLocalizedMessage(1042500); // You have been honored with four kill points.
        }
      }
      else
      {
        from.SendLocalizedMessage(1042496); // You may only honor another player.
      }
    }

    public virtual void AddMember(Mobile mob)
    {
      Members.Insert(ZeroRankOffset, new PlayerState(mob, this, Members));

      mob.AddToBackpack(FactionItem.Imbue(new Robe(), this, false, Definition.HuePrimary));
      mob.SendLocalizedMessage(1010374); // You have been granted a robe which signifies your faction

      mob.InvalidateProperties();
      mob.Delta(MobileDelta.Noto);

      mob.FixedEffect(0x373A, 10, 30);
      mob.PlaySound(0x209);
    }

    public static bool IsNearType(Mobile mob, Type type, int range)
    {
      bool mobs = type.IsSubclassOf(typeof(Mobile));
      bool items = type.IsSubclassOf(typeof(Item));

      if (!(items || mobs))
        return false;

      IPooledEnumerable<IEntity> eable = mob.Map.GetObjectsInRange(mob.Location, range, items, mobs);
      bool isInstance = eable.Any(type.IsInstanceOfType);
      eable.Free();

      return isInstance;
    }

    public static bool IsNearType(Mobile mob, Type[] types, int range)
    {
      IPooledEnumerable<IEntity> eable = mob.GetObjectsInRange(range);
      bool found = eable.Any(obj => types.Any(t => t.IsInstanceOfType(obj)));
      eable.Free();
      return found;
    }

    public void RemovePlayerState(PlayerState pl)
    {
      if (pl == null || !Members.Contains(pl))
        return;

      int killPoints = pl.KillPoints;

      if (pl.RankIndex != -1)
      {
        while (pl.RankIndex + 1 < ZeroRankOffset)
        {
          PlayerState pNext = Members[pl.RankIndex + 1];
          Members[pl.RankIndex + 1] = pl;
          Members[pl.RankIndex] = pNext;
          pl.RankIndex++;
          pNext.RankIndex--;
        }

        ZeroRankOffset--;
      }

      Members.Remove(pl);

      PlayerMobile pm = (PlayerMobile)pl.Mobile;
      if (pm == null)
        return;

      Mobile mob = pl.Mobile;
      if (pm.FactionPlayerState == pl)
      {
        pm.FactionPlayerState = null;

        mob.InvalidateProperties();
        mob.Delta(MobileDelta.Noto);

        if (Election.IsCandidate(mob))
          Election.RemoveCandidate(mob);

        if (pl.Finance != null)
          pl.Finance.Finance = null;

        if (pl.Sheriff != null)
          pl.Sheriff.Sheriff = null;

        Election.RemoveVoter(mob);

        if (Commander == mob)
          Commander = null;

        pm.ValidateEquipment();
      }

      if (killPoints > 0)
        DistributePoints(killPoints);
    }

    public void RemoveMember(Mobile mob)
    {
      PlayerState pl = PlayerState.Find(mob);

      if (pl == null || !Members.Contains(pl))
        return;

      int killPoints = pl.KillPoints;

      //Ordinarily, through normal faction removal, this will never find any sigils.
      //Only with a leave delay less than the ReturnPeriod or a Faction Kick/Ban, will this ever do anything
      mob.Backpack?.FindItemsByType<Sigil>().ForEach(sigil => sigil.ReturnHome());

      if (pl.RankIndex != -1)
      {
        while (pl.RankIndex + 1 < ZeroRankOffset)
        {
          PlayerState pNext = Members[pl.RankIndex + 1];
          Members[pl.RankIndex + 1] = pl;
          Members[pl.RankIndex] = pNext;
          pl.RankIndex++;
          pNext.RankIndex--;
        }

        ZeroRankOffset--;
      }

      Members.Remove(pl);

      if (mob is PlayerMobile mobile)
        mobile.FactionPlayerState = null;

      mob.InvalidateProperties();
      mob.Delta(MobileDelta.Noto);

      if (Election.IsCandidate(mob))
        Election.RemoveCandidate(mob);

      Election.RemoveVoter(mob);

      if (pl.Finance != null)
        pl.Finance.Finance = null;

      if (pl.Sheriff != null)
        pl.Sheriff.Sheriff = null;

      if (Commander == mob)
        Commander = null;

      if (mob is PlayerMobile playerMobile)
        playerMobile.ValidateEquipment();

      if (killPoints > 0)
        DistributePoints(killPoints);
    }

    public void JoinGuilded(PlayerMobile mob, Guild guild)
    {
      if (mob.Young)
      {
        guild.RemoveMember(mob);
        mob.SendLocalizedMessage(
          1042283); // You have been kicked out of your guild!  Young players may not remain in a guild which is allied with a faction.
      }
      else if (AlreadyHasCharInFaction(mob))
      {
        guild.RemoveMember(mob);
        mob.SendLocalizedMessage(1005281); // You have been kicked out of your guild due to factional overlap
      }
      else if (IsFactionBanned(mob))
      {
        guild.RemoveMember(mob);
        mob.SendLocalizedMessage(1005052); // You are currently banned from the faction system
      }
      else
      {
        AddMember(mob);
        mob.SendLocalizedMessage(1042756, true, $" {m_Definition.FriendlyName}"); // You are now joining a faction:
      }
    }

    public void JoinAlone(Mobile mob)
    {
      AddMember(mob);
      mob.SendLocalizedMessage(1005058); // You have joined the faction
    }

    private bool AlreadyHasCharInFaction(Mobile mob)
    {
      if (mob.Account is Account acct)
        for (int i = 0; i < acct.Length; ++i)
        {
          Mobile c = acct[i];

          if (Find(c) != null)
            return true;
        }

      return false;
    }

    public static bool IsFactionBanned(Mobile mob)
    {
      if (!(mob.Account is Account acct))
        return false;

      return acct.GetTag("FactionBanned") != null;
    }

    public void OnJoinAccepted(Mobile mob)
    {
      if (!(mob is PlayerMobile pm))
        return; // sanity

      PlayerState pl = PlayerState.Find(pm);

      if (pm.Young)
      {
        pm.SendLocalizedMessage(1010104); // You cannot join a faction as a young player
      }
      else if (pl?.IsLeaving == true)
      {
        pm.SendLocalizedMessage(
          1005051); // You cannot use the faction stone until you have finished quitting your current faction
      }
      else if (AlreadyHasCharInFaction(pm))
      {
        pm.SendLocalizedMessage(
          1005059); // You cannot join a faction because you already declared your allegiance with another character
      }
      else if (IsFactionBanned(mob))
      {
        pm.SendLocalizedMessage(1005052); // You are currently banned from the faction system
      }
      else if (pm.Guild != null)
      {
        Guild guild = pm.Guild as Guild;

        if (guild?.Leader != pm)
        {
          pm.SendLocalizedMessage(
            1005057); // You cannot join a faction because you are in a guild and not the guildmaster
        }
        else if (guild.Type != GuildType.Regular)
        {
          pm.SendLocalizedMessage(
            1042161); // You cannot join a faction because your guild is an Order or Chaos type.
        }
        else if (!Guild.NewGuildSystem && guild.Enemies?.Count > 0
        ) //CAN join w/wars in new system
        {
          pm.SendLocalizedMessage(1005056); // You cannot join a faction with active Wars
        }
        else if (Guild.NewGuildSystem && guild.Alliance != null)
        {
          pm.SendLocalizedMessage(
            1080454); // Your guild cannot join a faction while in alliance with non-factioned guilds.
        }
        else if (!CanHandleInflux(guild.Members.Count))
        {
          pm.SendLocalizedMessage(
            1018031); // In the interest of faction stability, this faction declines to accept new members for now.
        }
        else
        {
          List<Mobile> members = new List<Mobile>(guild.Members);

          for (int i = 0; i < members.Count; ++i)
          {
            if (!(members[i] is PlayerMobile member))
              continue;

            JoinGuilded(member, guild);
          }
        }
      }
      else if (!CanHandleInflux(1))
      {
        pm.SendLocalizedMessage(
          1018031); // In the interest of faction stability, this faction declines to accept new members for now.
      }
      else
      {
        JoinAlone(mob);
      }
    }

    public bool IsCommander(Mobile mob)
    {
      if (mob == null)
        return false;

      return mob.AccessLevel >= AccessLevel.GameMaster || mob == Commander;
    }

    public override string ToString() => m_Definition.FriendlyName;

    public static bool CheckLeaveTimer(Mobile mob)
    {
      PlayerState pl = PlayerState.Find(mob);

      if (pl?.IsLeaving != true)
        return false;

      if (pl.Leaving + LeavePeriod >= DateTime.UtcNow)
        return false;

      mob.SendLocalizedMessage(1005163); // You have now quit your faction

      pl.Faction.RemoveMember(mob);

      return true;
    }

    public static void Initialize()
    {
      EventSink.Login += EventSink_Login;
      EventSink.Logout += EventSink_Logout;

      Timer.DelayCall(TimeSpan.FromMinutes(1.0), TimeSpan.FromMinutes(10.0), HandleAtrophy);

      Timer.DelayCall(TimeSpan.FromSeconds(30.0), TimeSpan.FromSeconds(30.0), ProcessTick);

      CommandSystem.Register("FactionElection", AccessLevel.GameMaster, FactionElection_OnCommand);
      CommandSystem.Register("FactionCommander", AccessLevel.Administrator, FactionCommander_OnCommand);
      CommandSystem.Register("FactionItemReset", AccessLevel.Administrator, FactionItemReset_OnCommand);
      CommandSystem.Register("FactionReset", AccessLevel.Administrator, FactionReset_OnCommand);
      CommandSystem.Register("FactionTownReset", AccessLevel.Administrator, FactionTownReset_OnCommand);
    }

    public static void FactionTownReset_OnCommand(CommandEventArgs e)
    {
      List<BaseMonolith> monoliths = BaseMonolith.Monoliths;

      for (int i = 0; i < monoliths.Count; ++i)
        monoliths[i].Sigil = null;

      List<Town> towns = Town.Towns;

      for (int i = 0; i < towns.Count; ++i)
      {
        towns[i].Silver = 0;
        towns[i].Sheriff = null;
        towns[i].Finance = null;
        towns[i].Tax = 0;
        towns[i].Owner = null;
      }

      List<Sigil> sigils = Sigil.Sigils;

      for (int i = 0; i < sigils.Count; ++i)
      {
        sigils[i].Corrupted = null;
        sigils[i].Corrupting = null;
        sigils[i].LastStolen = DateTime.MinValue;
        sigils[i].GraceStart = DateTime.MinValue;
        sigils[i].CorruptionStart = DateTime.MinValue;
        sigils[i].PurificationStart = DateTime.MinValue;
        sigils[i].LastMonolith = null;
        sigils[i].ReturnHome();
      }

      List<Faction> factions = Factions;

      for (int i = 0; i < factions.Count; ++i)
      {
        Faction f = factions[i];

        List<FactionItem> list = new List<FactionItem>(f.State.FactionItems);

        for (int j = 0; j < list.Count; ++j)
        {
          FactionItem fi = list[j];

          if (fi.Expiration == DateTime.MinValue)
            fi.Item.Delete();
          else
            fi.Detach();
        }
      }
    }

    public static void FactionReset_OnCommand(CommandEventArgs e)
    {
      List<BaseMonolith> monoliths = BaseMonolith.Monoliths;

      for (int i = 0; i < monoliths.Count; ++i)
        monoliths[i].Sigil = null;

      List<Town> towns = Town.Towns;

      for (int i = 0; i < towns.Count; ++i)
      {
        towns[i].Silver = 0;
        towns[i].Sheriff = null;
        towns[i].Finance = null;
        towns[i].Tax = 0;
        towns[i].Owner = null;
      }

      List<Sigil> sigils = Sigil.Sigils;

      for (int i = 0; i < sigils.Count; ++i)
      {
        sigils[i].Corrupted = null;
        sigils[i].Corrupting = null;
        sigils[i].LastStolen = DateTime.MinValue;
        sigils[i].GraceStart = DateTime.MinValue;
        sigils[i].CorruptionStart = DateTime.MinValue;
        sigils[i].PurificationStart = DateTime.MinValue;
        sigils[i].LastMonolith = null;
        sigils[i].ReturnHome();
      }

      List<Faction> factions = Factions;

      for (int i = 0; i < factions.Count; ++i)
      {
        Faction f = factions[i];

        List<PlayerState> playerStateList = new List<PlayerState>(f.Members);

        for (int j = 0; j < playerStateList.Count; ++j)
          f.RemoveMember(playerStateList[j].Mobile);

        List<FactionItem> factionItemList = new List<FactionItem>(f.State.FactionItems);

        for (int j = 0; j < factionItemList.Count; ++j)
        {
          FactionItem fi = factionItemList[j];

          if (fi.Expiration == DateTime.MinValue)
            fi.Item.Delete();
          else
            fi.Detach();
        }

        List<BaseFactionTrap> factionTrapList = new List<BaseFactionTrap>(f.Traps);

        for (int j = 0; j < factionTrapList.Count; ++j)
          factionTrapList[j].Delete();
      }
    }

    public static void FactionItemReset_OnCommand(CommandEventArgs e)
    {
      List<Item> items = new List<Item>();

      foreach (Item item in World.Items.Values)
        if (item is IFactionItem && !(item is HoodedShroudOfShadows))
          items.Add(item);

      int[] hues = new int[Factions.Count * 2];

      for (int i = 0; i < Factions.Count; ++i)
      {
        hues[0 + i * 2] = Factions[i].Definition.HuePrimary;
        hues[1 + i * 2] = Factions[i].Definition.HueSecondary;
      }

      int count = 0;

      for (int i = 0; i < items.Count; ++i)
      {
        Item item = items[i];
        IFactionItem fci = (IFactionItem)item;

        if (fci.FactionItemState != null || item.LootType != LootType.Blessed)
          continue;

        bool isHued = false;

        for (int j = 0; j < hues.Length; ++j)
          if (item.Hue == hues[j])
          {
            isHued = true;
            break;
          }

        if (isHued)
        {
          fci.FactionItemState = null;
          ++count;
        }
      }

      e.Mobile.SendMessage("{0} items reset", count);
    }

    public static void FactionCommander_OnCommand(CommandEventArgs e)
    {
      e.Mobile.SendMessage("Target a player to make them the faction commander.");
      e.Mobile.BeginTarget(-1, false, TargetFlags.None, FactionCommander_OnTarget);
    }

    public static void FactionCommander_OnTarget(Mobile from, object obj)
    {
      if (obj is PlayerMobile mobile)
      {
        Mobile targ = mobile;
        PlayerState pl = PlayerState.Find(targ);

        if (pl != null)
        {
          pl.Faction.Commander = targ;
          from.SendMessage("You have appointed them as the faction commander.");
        }
        else
        {
          from.SendMessage("They are not in a faction.");
        }
      }
      else
      {
        from.SendMessage("That is not a player.");
      }
    }

    public static void FactionElection_OnCommand(CommandEventArgs e)
    {
      e.Mobile.SendMessage("Target a faction stone to open its election properties.");
      e.Mobile.BeginTarget(-1, false, TargetFlags.None, FactionElection_OnTarget);
    }

    public static void FactionElection_OnTarget(Mobile from, object obj)
    {
      if (obj is FactionStone stone)
      {
        Faction faction = stone.Faction;

        if (faction != null)
          from.SendGump(new ElectionManagementGump(faction.Election));
        //from.SendGump( new Gumps.PropertiesGump( from, faction.Election ) );
        else
          from.SendMessage("That stone has no faction assigned.");
      }
      else
      {
        from.SendMessage("That is not a faction stone.");
      }
    }

    public static void FactionKick_OnCommand(CommandEventArgs e)
    {
      e.Mobile.SendMessage("Target a player to remove them from their faction.");
      e.Mobile.BeginTarget(-1, false, TargetFlags.None, FactionKick_OnTarget);
    }

    public static void FactionKick_OnTarget(Mobile from, object obj)
    {
      if (obj is Mobile mob)
      {
        PlayerState pl = PlayerState.Find(mob);

        if (pl != null)
        {
          pl.Faction.RemoveMember(mob);

          mob.SendMessage("You have been kicked from your faction.");
          from.SendMessage("They have been kicked from their faction.");
        }
        else
        {
          from.SendMessage("They are not in a faction.");
        }
      }
      else
      {
        from.SendMessage("That is not a player.");
      }
    }

    public static void ProcessTick()
    {
      List<Sigil> sigils = Sigil.Sigils;

      for (int i = 0; i < sigils.Count; ++i)
      {
        Sigil sigil = sigils[i];

        if (!sigil.IsBeingCorrupted && sigil.GraceStart != DateTime.MinValue &&
            sigil.GraceStart + Sigil.CorruptionGrace < DateTime.UtcNow)
        {
          if (sigil.LastMonolith is StrongholdMonolith &&
              (sigil.Corrupted == null || sigil.LastMonolith.Faction != sigil.Corrupted))
          {
            sigil.Corrupting = sigil.LastMonolith.Faction;
            sigil.CorruptionStart = DateTime.UtcNow;
          }
          else
          {
            sigil.Corrupting = null;
            sigil.CorruptionStart = DateTime.MinValue;
          }

          sigil.GraceStart = DateTime.MinValue;
        }

        if (sigil.LastMonolith?.Sigil == null)
        {
          if (sigil.LastStolen + Sigil.ReturnPeriod < DateTime.UtcNow)
            sigil.ReturnHome();
        }
        else
        {
          if (sigil.IsBeingCorrupted && sigil.CorruptionStart + Sigil.CorruptionPeriod < DateTime.UtcNow)
          {
            sigil.Corrupted = sigil.Corrupting;
            sigil.Corrupting = null;
            sigil.CorruptionStart = DateTime.MinValue;
            sigil.GraceStart = DateTime.MinValue;
          }
          else if (sigil.IsPurifying && sigil.PurificationStart + Sigil.PurificationPeriod < DateTime.UtcNow)
          {
            sigil.PurificationStart = DateTime.MinValue;
            sigil.Corrupted = null;
            sigil.Corrupting = null;
            sigil.CorruptionStart = DateTime.MinValue;
            sigil.GraceStart = DateTime.MinValue;
          }
        }
      }
    }

    public static void HandleDeath(Mobile mob)
    {
      HandleDeath(mob, null);
    }

    public int AwardSilver(Mobile mob, int silver)
    {
      if (silver <= 0)
        return 0;

      int tithed = silver * Tithe / 100;

      Silver += tithed;

      silver = silver - tithed;

      if (silver > 0)
        mob.AddToBackpack(new Silver(silver));

      return silver;
    }

    public static Faction FindSmallestFaction()
    {
      List<Faction> factions = Factions;
      Faction smallest = null;

      for (int i = 0; i < factions.Count; ++i)
      {
        Faction faction = factions[i];

        if (smallest == null || faction.Members.Count < smallest.Members.Count)
          smallest = faction;
      }

      return smallest;
    }

    public static bool StabilityActive()
    {
      List<Faction> factions = Factions;

      for (int i = 0; i < factions.Count; ++i)
      {
        Faction faction = factions[i];

        if (faction.Members.Count > StabilityActivation)
          return true;
      }

      return false;
    }

    public bool CanHandleInflux(int influx)
    {
      if (!StabilityActive())
        return true;

      Faction smallest = FindSmallestFaction();

      if (smallest == null)
        return true; // sanity

      if ((Members.Count + influx) * 100 / StabilityFactor > smallest.Members.Count)
        return false;

      return true;
    }

    public static void HandleDeath(Mobile victim, Mobile killer)
    {
      killer ??= victim.FindMostRecentDamager(true);

      PlayerState killerState = PlayerState.Find(killer);
      Container killerPack = killer?.Backpack;
      victim.Backpack?.FindItemsByType<Sigil>().ForEach(sigil =>
      {
        if (killerState == null || killerPack == null)
        {
          sigil.ReturnHome();
          return;
        }

        if (killer?.GetDistanceToSqrt(victim) > 64)
        {
          sigil.ReturnHome();
          killer.SendLocalizedMessage(1042230); // The sigil has gone back to its home location.
        }
        else if (Sigil.ExistsOn(killer))
        {
          sigil.ReturnHome();
          killer?.SendLocalizedMessage(
            1010258); // The sigil has gone back to its home location because you already have a sigil.
        }
        else if (!killerPack.TryDropItem(killer, sigil, false))
        {
          sigil.ReturnHome();
          killer?.SendLocalizedMessage(1010259); // The sigil has gone home because your backpack is full.
        }
      });

      if (killerState == null)
        return;

      if (victim is BaseCreature bc)
      {
        Faction victimFaction = bc.FactionAllegiance;

        if (bc.Map == Facet && victimFaction != null && killerState.Faction != victimFaction)
        {
          int silver = killerState.Faction.AwardSilver(killer, bc.FactionSilverWorth);

          if (silver > 0)
            killer.SendLocalizedMessage(1042748,
              silver.ToString("N0")); // Thou hast earned ~1_AMOUNT~ silver for vanquishing the vile creature.
        }

        #region Ethics

        if (bc.Map == Facet && bc.GetEthicAllegiance(killer) == BaseCreature.Allegiance.Enemy)
        {
          Player killerEPL = Player.Find(killer);

          if (killerEPL != null && 100 - killerEPL.Power > Utility.Random(100))
          {
            ++killerEPL.Power;
            ++killerEPL.History;
          }
        }

        #endregion

        return;
      }

      PlayerState victimState = PlayerState.Find(victim);

      if (victimState == null)
        return;

      #region Dueling

      if (victim.Region.IsPartOf<SafeZone>())
        return;

      #endregion

      if (killer == victim || killerState.Faction != victimState.Faction)
        ApplySkillLoss(victim);

      if (killerState.Faction != victimState.Faction)
      {
        if (victimState.KillPoints <= -6)
        {
          killer.SendLocalizedMessage(501693); // This victim is not worth enough to get kill points from.

          #region Ethics

          Player killerEPL = Player.Find(killer);
          Player victimEPL = Player.Find(victim);

          if (killerEPL != null && victimEPL?.Power > 0 && victimState.CanGiveSilverTo(killer))
          {
            int powerTransfer = Math.Max(1, victimEPL.Power / 5);

            if (powerTransfer > 100 - killerEPL.Power)
              powerTransfer = 100 - killerEPL.Power;

            if (powerTransfer > 0)
            {
              victimEPL.Power -= (powerTransfer + 1) / 2;
              killerEPL.Power += powerTransfer;

              killerEPL.History += powerTransfer;

              victimState.OnGivenSilverTo(killer);
            }
          }

          #endregion
        }
        else
        {
          int award = Math.Max(victimState.KillPoints / 10, 1);

          if (award > 40)
            award = 40;

          if (victimState.CanGiveSilverTo(killer))
          {
            PowerFactionItem.CheckSpawn(killer, victim);

            if (victimState.KillPoints > 0)
            {
              victimState.IsActive = true;

              if (1 > Utility.Random(3))
                killerState.IsActive = true;

              int silver = killerState.Faction.AwardSilver(killer, award * 40);

              if (silver > 0)
                killer.SendLocalizedMessage(1042736,
                  $"{silver:N0} silver\t{victim.Name}"); // You have earned ~1_SILVER_AMOUNT~ pieces for vanquishing ~2_PLAYER_NAME~!
            }

            victimState.KillPoints -= award;
            killerState.KillPoints += award;

            int offset = award != 1 ? 0 : 2; // for pluralization

            string args = $"{award}\t{victim.Name}\t{killer.Name}";

            killer.SendLocalizedMessage(1042737 + offset,
              args); // Thou hast been honored with ~1_KILL_POINTS~ kill point(s) for vanquishing ~2_DEAD_PLAYER~!
            victim.SendLocalizedMessage(1042738 + offset,
              args); // Thou has lost ~1_KILL_POINTS~ kill point(s) to ~3_ATTACKER_NAME~ for being vanquished!

            #region Ethics

            Player killerEPL = Player.Find(killer);
            Player victimEPL = Player.Find(victim);

            if (killerEPL != null && victimEPL?.Power > 0)
            {
              int powerTransfer = Math.Max(1, victimEPL.Power / 5);

              if (powerTransfer > 100 - killerEPL.Power)
                powerTransfer = 100 - killerEPL.Power;

              if (powerTransfer > 0)
              {
                victimEPL.Power -= (powerTransfer + 1) / 2;
                killerEPL.Power += powerTransfer;

                killerEPL.History += powerTransfer;
              }
            }

            #endregion

            victimState.OnGivenSilverTo(killer);
          }
          else
          {
            killer.SendLocalizedMessage(
              1042231); // You have recently defeated this enemy and thus their death brings you no honor.
          }
        }
      }
    }

    private static void EventSink_Logout(LogoutEventArgs e)
    {
      e.Mobile.Backpack?.FindItemsByType<Sigil>().ForEach(sigil => sigil.ReturnHome());
    }

    private static void EventSink_Login(LoginEventArgs e)
    {
      Mobile mob = e.Mobile;

      CheckLeaveTimer(mob);
    }

    public static void WriteReference(IGenericWriter writer, Faction fact)
    {
      int idx = Factions.IndexOf(fact);

      writer.WriteEncodedInt(idx + 1);
    }

    public static Faction ReadReference(IGenericReader reader)
    {
      int idx = reader.ReadEncodedInt() - 1;

      if (idx >= 0 && idx < Factions.Count)
        return Factions[idx];

      return null;
    }

    public static Faction Find(Mobile mob, bool inherit = false, bool creatureAllegiances = false)
    {
      PlayerState pl = PlayerState.Find(mob);

      if (pl != null)
        return pl.Faction;

      if (inherit && mob is BaseCreature bc)
      {
        if (bc.Controlled)
          return Find(bc.ControlMaster);
        if (bc.Summoned)
          return Find(bc.SummonMaster);
        if (creatureAllegiances && bc is BaseFactionGuard guard)
          return guard.Faction;
        if (creatureAllegiances)
          return bc.FactionAllegiance;
      }

      return null;
    }

    public static Faction Parse(string name)
    {
      List<Faction> factions = Factions;

      for (int i = 0; i < factions.Count; ++i)
      {
        Faction faction = factions[i];

        if (Insensitive.Equals(faction.Definition.FriendlyName, name))
          return faction;
      }

      return null;
    }

    private class BroadcastPrompt : Prompt
    {
      private Faction m_Faction;

      public BroadcastPrompt(Faction faction) => m_Faction = faction;

      public override void OnResponse(Mobile from, string text)
      {
        m_Faction.EndBroadcast(from, text);
      }
    }

    #region Skill Loss

    public const double SkillLossFactor = 1.0 / 3;
    public static readonly TimeSpan SkillLossPeriod = TimeSpan.FromMinutes(20.0);

    private static Dictionary<Mobile, SkillLossContext> m_SkillLoss = new Dictionary<Mobile, SkillLossContext>();

    private class SkillLossContext
    {
      public List<SkillMod> m_Mods;
      public Timer m_Timer;
    }

    public static bool InSkillLoss(Mobile mob) => m_SkillLoss.ContainsKey(mob);

    public static void ApplySkillLoss(Mobile mob)
    {
      if (InSkillLoss(mob))
        return;

      SkillLossContext context = new SkillLossContext();
      m_SkillLoss[mob] = context;

      List<SkillMod> mods = context.m_Mods = new List<SkillMod>();

      for (int i = 0; i < mob.Skills.Length; ++i)
      {
        Skill sk = mob.Skills[i];
        double baseValue = sk.Base;

        if (baseValue > 0)
        {
          SkillMod mod = new DefaultSkillMod(sk.SkillName, true, -(baseValue * SkillLossFactor));

          mods.Add(mod);
          mob.AddSkillMod(mod);
        }
      }

      context.m_Timer = Timer.DelayCall(SkillLossPeriod, () => ClearSkillLoss(mob));
    }

    public static bool ClearSkillLoss(Mobile mob)
    {
      if (!m_SkillLoss.TryGetValue(mob, out SkillLossContext context))
        return false;

      m_SkillLoss.Remove(mob);

      List<SkillMod> mods = context.m_Mods;

      for (int i = 0; i < mods.Count; ++i)
        mob.RemoveSkillMod(mods[i]);

      context.m_Timer.Stop();

      return true;
    }

    #endregion
  }

  public enum FactionKickType
  {
    Kick,
    Ban,
    Unban
  }

  public class FactionKickCommand : BaseCommand
  {
    private FactionKickType m_KickType;

    public FactionKickCommand(FactionKickType kickType)
    {
      m_KickType = kickType;

      AccessLevel = AccessLevel.GameMaster;
      Supports = CommandSupport.AllMobiles;
      ObjectTypes = ObjectTypes.Mobiles;

      switch (m_KickType)
      {
        case FactionKickType.Kick:
        {
          Commands = new[] { "FactionKick" };
          Usage = "FactionKick";
          Description =
            "Kicks the targeted player out of his current faction. This does not prevent them from rejoining.";
          break;
        }
        case FactionKickType.Ban:
        {
          Commands = new[] { "FactionBan" };
          Usage = "FactionBan";
          Description =
            "Bans the account of a targeted player from joining factions. All players on the account are removed from their current faction, if any.";
          break;
        }
        case FactionKickType.Unban:
        {
          Commands = new[] { "FactionUnban" };
          Usage = "FactionUnban";
          Description = "Unbans the account of a targeted player from joining factions.";
          break;
        }
      }
    }

    public override void Execute(CommandEventArgs e, object obj)
    {
      Mobile mob = (Mobile)obj;

      switch (m_KickType)
      {
        case FactionKickType.Kick:
        {
          PlayerState pl = PlayerState.Find(mob);

          if (pl != null)
          {
            pl.Faction.RemoveMember(mob);
            mob.SendMessage("You have been kicked from your faction.");
            AddResponse("They have been kicked from their faction.");
          }
          else
          {
            LogFailure("They are not in a faction.");
          }

          break;
        }
        case FactionKickType.Ban:
        {
          if (mob.Account is Account acct)
          {
            if (acct.GetTag("FactionBanned") == null)
            {
              acct.SetTag("FactionBanned", "true");
              AddResponse("The account has been banned from joining factions.");
            }
            else
            {
              AddResponse("The account is already banned from joining factions.");
            }

            for (int i = 0; i < acct.Length; ++i)
            {
              mob = acct[i];

              if (mob != null)
              {
                PlayerState pl = PlayerState.Find(mob);

                if (pl != null)
                {
                  pl.Faction.RemoveMember(mob);
                  mob.SendMessage("You have been kicked from your faction.");
                  AddResponse("They have been kicked from their faction.");
                }
              }
            }
          }
          else
          {
            LogFailure("They have no assigned account.");
          }

          break;
        }
        case FactionKickType.Unban:
        {
          if (mob.Account is Account acct)
          {
            if (acct.GetTag("FactionBanned") == null)
            {
              AddResponse("The account is not already banned from joining factions.");
            }
            else
            {
              acct.RemoveTag("FactionBanned");
              AddResponse("The account may now freely join factions.");
            }
          }
          else
          {
            LogFailure("They have no assigned account.");
          }

          break;
        }
      }
    }
  }
}