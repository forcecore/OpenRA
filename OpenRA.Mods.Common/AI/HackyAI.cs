#region Copyright & License Information
/*
 * Copyright 2007-2017 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;
using OpenRA.Support;
using OpenRA.Traits;
using System.Threading;

namespace OpenRA.Mods.Common.AI
{
	class CaptureTarget<TInfoType> where TInfoType : class, ITraitInfoInterface
	{
		internal readonly Actor Actor;
		internal readonly TInfoType Info;

		/// <summary>The order string given to the capturer so they can capture this actor.</summary>
		/// <example>ExternalCaptureActor</example>
		internal readonly string OrderString;

		internal CaptureTarget(Actor actor, string orderString)
		{
			Actor = actor;
			Info = actor.Info.TraitInfoOrDefault<TInfoType>();
			OrderString = orderString;
		}
	}

	public sealed class HackyAIInfo : IBotInfo, ITraitInfo
	{
		public class UnitCategories
		{
			public readonly HashSet<string> Mcv = new HashSet<string>();
			public readonly HashSet<string> Seige = new HashSet<string>();
			public readonly HashSet<string> DemoUnits = new HashSet<string>(); // these units blow up badly that are unwelcome in base
			public readonly HashSet<string> NavalUnits = new HashSet<string>();
			public readonly HashSet<string> ExcludeFromSquads = new HashSet<string>();
			public readonly HashSet<string> ExcludeFromAttackSquads = new HashSet<string>(); // they can be seen by AI but not belong to attack force (like engis)
		}

		public class BuildingCategories
		{
			public readonly HashSet<string> ConstructionYard = new HashSet<string>();
			public readonly HashSet<string> VehiclesFactory = new HashSet<string>();
			public readonly HashSet<string> Refinery = new HashSet<string>();
			public readonly HashSet<string> Power = new HashSet<string>();
			public readonly HashSet<string> Barracks = new HashSet<string>();
			public readonly HashSet<string> Production = new HashSet<string>();
			public readonly HashSet<string> NavalProduction = new HashSet<string>();
			public readonly HashSet<string> Silo = new HashSet<string>();
			public readonly HashSet<string> StaticAntiAir = new HashSet<string>();
			public readonly HashSet<string> Fragile = new HashSet<string>();
			public readonly HashSet<string> Defense = new HashSet<string>();
			public readonly HashSet<string> NNTech = new HashSet<string>();
			public readonly HashSet<string> NNTier3Tech = new HashSet<string>();
			public readonly HashSet<string> SuperWeapon = new HashSet<string>();
		}

		[Desc("Ingame name this bot uses.")]
		public readonly string Name = "Unnamed Bot";

		[Desc("The Script AI will read and execute")]
		public readonly string LuaScript;

		[Desc("Build units according to Lua script? (false = old hacky AI behavior).")]
		public readonly bool EnableLuaUnitProduction = false;

		[Desc("EXPERIMENTAL")]
		public readonly bool NNProduction = false;
		public readonly bool NNRallyPoint = false;
		public readonly bool NNBuildingPlacer = false;
		public HashSet<string> NNBuildingPlacerTerrainTypes = new HashSet<string>() { "Clear", "Road" };

		[Desc("Minimum number of units AI must have before attacking.")]
		public readonly int SquadSize = 8;

		[Desc("Random number of up to this many units is added to squad size when creating an attack squad.")]
		public readonly int SquadSizeRandomBonus = 30;

		[Desc("Production queues AI uses for buildings.")]
		public readonly HashSet<string> BuildingQueues = new HashSet<string> { "Building" };

		[Desc("Production queues AI uses for defenses.")]
		public readonly HashSet<string> DefenseQueues = new HashSet<string> { "Defense" };

		[Desc("Delay (in ticks) between giving out orders to units.")]
		public readonly int AssignRolesInterval = 20;

		[Desc("Delay (in ticks) between attempting rush attacks.")]
		public readonly int RushInterval = 600;

		[Desc("Delay (in ticks) between updating squads.")]
		public readonly int AttackForceInterval = 30;

		[Desc("Delay (in ticks) between updating defense decision info")]
		public readonly int DefenseInterval = 30;

		[Desc("Minimum delay (in ticks) between creating squads.")]
		public readonly int MinimumAttackForceDelay = 0;

		[Desc("Minimum portion of pending orders to issue each tick (e.g. 5 issues at least 1/5th of all pending orders). Excess orders remain queued for subsequent ticks.")]
		public readonly int MinOrderQuotientPerTick = 5;

		[Desc("Minimum excess power the AI should try to maintain.")]
		public readonly int MinimumExcessPower = 0;

		[Desc("Additional delay (in ticks) between structure production checks when there is no active production.",
			"StructureProductionRandomBonusDelay is added to this.")]
		public readonly int StructureProductionInactiveDelay = 125;

		[Desc("Additional delay (in ticks) added between structure production checks when actively building things.",
			"Note: The total delay is gamespeed OrderLatency x 4 + this + StructureProductionRandomBonusDelay.")]
		public readonly int StructureProductionActiveDelay = 0;

		[Desc("A random delay (in ticks) of up to this is added to active/inactive production delays.")]
		public readonly int StructureProductionRandomBonusDelay = 10;

		[Desc("Delay (in ticks) until retrying to build structure after the last 3 consecutive attempts failed.")]
		public readonly int StructureProductionResumeDelay = 1500;

		[Desc("After how many failed attempts to place a structure should AI give up and wait",
			"for StructureProductionResumeDelay before retrying.")]
		public readonly int MaximumFailedPlacementAttempts = 3;

		[Desc("How many randomly chosen cells with resources to check when deciding refinery placement.")]
		public readonly int MaxResourceCellsToCheck = 3;

		[Desc("Delay (in ticks) until rechecking for new BaseProviders.")]
		public readonly int CheckForNewBasesDelay = 1500;

		[Desc("Minimum range at which to build defensive structures near a combat hotspot.")]
		public readonly int MinimumDefenseRadius = 5;

		[Desc("Maximum range at which to build defensive structures near a combat hotspot.")]
		public readonly int MaximumDefenseRadius = 20;

		[Desc("Try to build another production building if there is too much cash.")]
		public readonly int NewProductionCashThreshold = 5000;

		[Desc("Only produce units as long as there are less than this amount of units idling inside the base.")]
		public readonly int IdleBaseUnitsMaximum = 12;

		[Desc("Radius in cells around enemy BaseBuilder (Construction Yard) where AI scans for targets to rush.")]
		public readonly int RushAttackScanRadius = 15;

		[Desc("Radius in cells around the base that should be scanned for units to be protected.")]
		public readonly int ProtectUnitScanRadius = 15;

		[Desc("Radius in cells around a factory scanned for rally points by the AI.")]
		public readonly int RallyPointScanRadius = 8;

		[Desc("Minimum distance in cells from center of the base when checking for building placement.")]
		public readonly int MinBaseRadius = 2;

		[Desc("Same as MinBaseRadius but for fragile structures to push them further away.")]
		public readonly int MinFragilePlacementRadius = 8;

		[Desc("Radius in cells around the center of the base to expand.")]
		public readonly int MaxBaseRadius = 20;

		[Desc("Should deployment of additional MCVs be restricted to MaxBaseRadius if explicit deploy locations are missing or occupied?")]
		public readonly bool RestrictMCVDeploymentFallbackToBase = true;

		[Desc("Radius in cells around each building with ProvideBuildableArea",
			"to check for a 3x3 area of water where naval structures can be built.",
			"Should match maximum adjacency of naval structures.")]
		public readonly int CheckForWaterRadius = 8;

		[Desc("Terrain types which are considered water for base building purposes.")]
		public readonly HashSet<string> WaterTerrainTypes = new HashSet<string> { "Water" };

		[Desc("Avoid enemy actors nearby when searching for a new resource patch. Should be somewhere near the max weapon range.")]
		public readonly WDist HarvesterEnemyAvoidanceRadius = WDist.FromCells(8);

		[Desc("Production queues AI uses for producing units.")]
		public readonly HashSet<string> UnitQueues = new HashSet<string> { "Vehicle", "Infantry", "Plane", "Ship", "Aircraft" };

		[Desc("Should the AI repair its buildings if damaged?")]
		public readonly bool ShouldRepairBuildings = true;

		string IBotInfo.Name { get { return Name; } }

		[Desc("What units to the AI should build.", "What % of the total army must be this type of unit.")]
		public readonly Dictionary<string, float> UnitsToBuild = null;

		[Desc("What units should the AI have a maximum limit to train.")]
		public readonly Dictionary<string, int> UnitLimits = null;

		[Desc("What buildings to the AI should build.", "What % of the total base must be this type of building.")]
		public readonly Dictionary<string, float> BuildingFractions = null;

		[Desc("Tells the AI what unit types fall under the same common name. Supported entries are Mcv and ExcludeFromSquads.")]
		[FieldLoader.LoadUsing("LoadUnitCategories", true)]
		public readonly UnitCategories UnitsCommonNames;

		[Desc("Tells the AI what building types fall under the same common name.",
			"Possible keys are ConstructionYard, Power, Refinery, Silo , Barracks, Production, VehiclesFactory, NavalProduction.")]
		[FieldLoader.LoadUsing("LoadBuildingCategories", true)]
		public readonly BuildingCategories BuildingCommonNames;

		public readonly Dictionary<string, string> CoreDefinitions = null;

		[Desc("What buildings should the AI have a maximum limit to build.")]
		public readonly Dictionary<string, int> BuildingLimits = null;

		// TODO Update OpenRA.Utility/Command.cs#L300 to first handle lists and also read nested ones
		[Desc("Tells the AI how to use its support powers.")]
		[FieldLoader.LoadUsing("LoadDecisions")]
		public readonly List<SupportPowerDecision> PowerDecisions = new List<SupportPowerDecision>();

		[Desc("Actor types that can capture other actors (via `Captures` or `ExternalCaptures`).",
			"Leave this empty to disable capturing.")]
		public HashSet<string> CapturingActorTypes = new HashSet<string>();

		[Desc("Actor types that can be targeted for capturing.",
			"Leave this empty to include all actors.")]
		public HashSet<string> CapturableActorTypes = new HashSet<string>();

		[Desc("Minimum delay (in ticks) between trying to capture with CapturingActorTypes.")]
		public readonly int MinimumCaptureDelay = 375;

		[Desc("Maximum number of options to consider for capturing.",
			"If a value less than 1 is given 1 will be used instead.")]
		public readonly int MaximumCaptureTargetOptions = 10;

		[Desc("Should visibility (Shroud, Fog, Cloak, etc) be considered when searching for capturable targets?")]
		public readonly bool CheckCaptureTargetsForVisibility = true;

		[Desc("Player stances that capturers should attempt to target.")]
		public readonly Stance CapturableStances = Stance.Enemy | Stance.Neutral;

		static object LoadUnitCategories(MiniYaml yaml)
		{
			var categories = yaml.Nodes.First(n => n.Key == "UnitsCommonNames");
			return FieldLoader.Load<UnitCategories>(categories.Value);
		}

		static object LoadBuildingCategories(MiniYaml yaml)
		{
			var categories = yaml.Nodes.First(n => n.Key == "BuildingCommonNames");
			return FieldLoader.Load<BuildingCategories>(categories.Value);
		}

		static object LoadDecisions(MiniYaml yaml)
		{
			var ret = new List<SupportPowerDecision>();
			foreach (var d in yaml.Nodes)
				if (d.Key.Split('@')[0] == "SupportPowerDecision")
					ret.Add(new SupportPowerDecision(d.Value));

			return ret;
		}

		public object Create(ActorInitializer init) { return new HackyAI(this, init); }
	}

	public enum BuildingPlacementType { Building, Defense, Refinery, Fragile }
	public enum NNBuildingPlacementType { Production, Defense, AADefense, Refinery, Tech, Tier3Tech, SuperWeapon, Power, Other }

	public sealed class HackyAI : ITick, IBot, INotifyDamage
	{
		public MersenneTwister Random { get; private set; }
		public readonly HackyAIInfo Info;

		public IEnumerable<Actor> GetConstructionYards()
		{
			return World.ActorsHavingTrait<Building>().Where(b => b.Owner == Player
				&& !b.IsDead && !b.Disposed && Info.BuildingCommonNames.ConstructionYard.Contains(b.Info.Name));
		}

		public CPos GetRandomBaseCenter()
		{
			var randomConstructionYard = GetConstructionYards()
				.RandomOrDefault(Random);

			return randomConstructionYard != null ? randomConstructionYard.Location : initialBaseCenter;
		}

		public bool IsEnabled;
		public List<Squad> Squads = new List<Squad>();
		public Dictionary<Actor, Squad> WhichSquad = new Dictionary<Actor, Squad>();
		public Player Player { get; private set; }

		readonly DomainIndex domainIndex;
		readonly ResourceLayer resLayer;
		readonly ResourceClaimLayer territory;
		readonly IPathFinder pathfinder;

		public readonly Func<Actor, bool> IsEnemyUnit;
		Dictionary<SupportPowerInstance, int> waitingPowers = new Dictionary<SupportPowerInstance, int>();
		Dictionary<string, SupportPowerDecision> powerDecisions = new Dictionary<string, SupportPowerDecision>();

		CPos initialBaseCenter;
		PowerManager playerPower;
		SupportPowerManager supportPowerMngr;
		PlayerResources playerResource;
		int ticks;

		BitArray resourceTypeIndices;

		List<BaseBuilder> builders = new List<BaseBuilder>();

		List<Actor> unitsHangingAroundTheBase = new List<Actor>();
		List<Actor> capturableStuff = new List<Actor>();

		// Units that the ai already knows about. Any unit not on this list needs to be given a role.
		List<Actor> activeUnits = new List<Actor>();

		public const int FeedbackTime = 30; // ticks; = a bit over 1s. must be >= netlag.

		public readonly World World;
		public Map Map { get { return World.Map; } }
		IBotInfo IBot.Info { get { return Info; } }

		int rushTicks;
		int assignRolesTicks;
		int attackForceTicks;
		int defenseTicks;
		int minAttackForceDelayTicks;
		int minCaptureDelayTicks;
		readonly int maximumCaptureTargetOptions;

		readonly Queue<Order> orders = new Queue<Order>();

		readonly HashSet<Actor> luaOccupiedActors = new HashSet<Actor>();
		readonly Dictionary<Player, AIScriptContext> luaContexts = new Dictionary<Player, AIScriptContext>();

		public static Semaphore clientLock = new Semaphore(1, 1);
		static UdpClient client;

		public HackyAI(HackyAIInfo info, ActorInitializer init)
		{
			Info = info;
			World = init.World;

			if (World.Type == WorldType.Editor)
				return;

			domainIndex = World.WorldActor.Trait<DomainIndex>();
			resLayer = World.WorldActor.Trait<ResourceLayer>();
			territory = World.WorldActor.TraitOrDefault<ResourceClaimLayer>();
			pathfinder = World.WorldActor.Trait<IPathFinder>();

			IsEnemyUnit = unit =>
				IsOwnedByEnemy(unit)
					&& !unit.Info.HasTraitInfo<HuskInfo>()
					&& unit.Info.HasTraitInfo<ITargetableInfo>();

			foreach (var decision in info.PowerDecisions)
				powerDecisions.Add(decision.OrderName, decision);

			maximumCaptureTargetOptions = Math.Max(1, Info.MaximumCaptureTargetOptions);

			client = new UdpClient();
			client.Connect("localhost", 9999);
		}

		public void SetLuaOccupied(Actor a, bool occupied)
		{
			if (occupied)
				luaOccupiedActors.Add(a);
			else
				luaOccupiedActors.Remove(a);
		}

		public bool IsLuaOccupied(Actor a)
		{
			return luaOccupiedActors.Contains(a);
		}

		bool UnitCannotBeOrdered(Actor a)
		{
			if (a.Owner != Player || a.IsDead || !a.IsInWorld)
				return true;

			// Don't make aircrafts land and take off like crazy
			var pool = a.TraitOrDefault<AmmoPool>();
			if (pool != null && pool.Info.SelfReloads == false && AirStateBase.IsRearm(a))
				return true;

			// Actors in luaOccupiedActors are under control of scripted actions and
			// shouldn't be ordered by the default hacky controller.
			return IsLuaOccupied(a);
		}

		public static void BotDebug(string s, params object[] args)
		{
			if (Game.Settings.Debug.BotDebug)
				Game.Debug(s, args);
		}

		// Called by the host's player creation code
		public void Activate(Player p)
		{
			Player = p;
			IsEnabled = true;
			playerPower = p.PlayerActor.Trait<PowerManager>();
			supportPowerMngr = p.PlayerActor.Trait<SupportPowerManager>();
			playerResource = p.PlayerActor.Trait<PlayerResources>();

			if (luaContexts.ContainsKey(p))
			{
				luaContexts[p].Dispose();
				luaContexts.Remove(p);
			}

			AIScriptContext context = null;
			if (Info.LuaScript != null)
			{
				// when no script given, Hacky AI behavior is still in effect and will continue to work as before.
				string[] scripts = { Info.LuaScript };
				context = new AIScriptContext(World, scripts); // Create context (not yet activated)
				luaContexts.Add(p, context);
			}

			foreach (var building in Info.BuildingQueues)
				builders.Add(new BaseBuilder(this, building, p, playerPower, playerResource, context));
			foreach (var defense in Info.DefenseQueues)
				builders.Add(new BaseBuilder(this, defense, p, playerPower, playerResource, context));

			Random = new MersenneTwister(Game.CosmeticRandom.Next());

			// Avoid all AIs trying to rush in the same tick, randomize their initial rush a little.
			var smallFractionOfRushInterval = Info.RushInterval / 20;
			rushTicks = Random.Next(Info.RushInterval - smallFractionOfRushInterval, Info.RushInterval + smallFractionOfRushInterval);

			// Avoid all AIs reevaluating assignments on the same tick, randomize their initial evaluation delay.
			assignRolesTicks = Random.Next(0, Info.AssignRolesInterval);
			attackForceTicks = Random.Next(0, Info.AttackForceInterval);
			minAttackForceDelayTicks = Random.Next(0, Info.MinimumAttackForceDelay);
			minCaptureDelayTicks = Random.Next(0, Info.MinimumCaptureDelay);

			var tileset = World.Map.Rules.TileSet;
			resourceTypeIndices = new BitArray(tileset.TerrainInfo.Length); // Big enough
			foreach (var t in Map.Rules.Actors["world"].TraitInfos<ResourceTypeInfo>())
				resourceTypeIndices.Set(tileset.GetTerrainIndex(t.TerrainType), true);
		}

		// TODO: Possibly give this a more generic name when terrain type is unhardcoded
		public bool EnoughWaterToBuildNaval()
		{
			var baseProviders = World.ActorsHavingTrait<BaseProvider>()
				.Where(a => a.Owner == Player);

			foreach (var b in baseProviders)
			{
				// TODO: Properly check building foundation rather than 3x3 area
				var playerWorld = Player.World;
				var countWaterCells = Map.FindTilesInCircle(b.Location, Info.MaxBaseRadius)
					.Where(c => playerWorld.Map.Contains(c)
						&& Info.WaterTerrainTypes.Contains(playerWorld.Map.GetTerrainInfo(c).Type)
						&& Util.AdjacentCells(playerWorld, Target.FromCell(playerWorld, c))
							.All(a => Info.WaterTerrainTypes.Contains(playerWorld.Map.GetTerrainInfo(a).Type)))
					.Count();

				if (countWaterCells > 0)
					return true;
			}

			return false;
		}

		// Check whether we have at least one building providing buildable area close enough to water to build naval structures
		public bool CloseEnoughToWater()
		{
			var areaProviders = World.ActorsHavingTrait<GivesBuildableArea>()
				.Where(a => a.Owner == Player);

			foreach (var a in areaProviders)
			{
				// TODO: Properly check building foundation rather than 3x3 area
				var playerWorld = Player.World;
				var adjacentWater = Map.FindTilesInCircle(a.Location, Info.CheckForWaterRadius)
					.Where(c => playerWorld.Map.Contains(c)
						&& Info.WaterTerrainTypes.Contains(playerWorld.Map.GetTerrainInfo(c).Type)
						&& Util.AdjacentCells(playerWorld, Target.FromCell(playerWorld, c))
							.All(ac => Info.WaterTerrainTypes.Contains(playerWorld.Map.GetTerrainInfo(ac).Type)))
					.Count();

				if (adjacentWater > 0)
					return true;
			}

			return false;
		}

		public void QueueOrder(Order order)
		{
			orders.Enqueue(order);
		}

		ActorInfo ChooseRandomUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var unit = buildableThings.Random(Random);
			return HasAdequateAirUnitReloadBuildings(unit) ? unit : null;
		}

		ActorInfo ChooseUnitToBuild(ProductionQueue queue)
		{
			var buildableThings = queue.BuildableItems();
			if (!buildableThings.Any())
				return null;

			var myUnits = Player.World
				.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == Player)
				.Select(a => a.Info.Name).ToList();

			foreach (var unit in Info.UnitsToBuild.Shuffle(Random))
				if (buildableThings.Any(b => b.Name == unit.Key))
					if (myUnits.Count(a => a == unit.Key) < unit.Value * myUnits.Count)
						if (HasAdequateAirUnitReloadBuildings(Map.Rules.Actors[unit.Key]))
							return Map.Rules.Actors[unit.Key];

			return null;
		}

		int CountBuilding(string frac, Player owner)
		{
			return World.ActorsHavingTrait<Building>().Count(a => a.Owner == owner && a.Info.Name == frac);
		}

		int CountUnits(string unit, Player owner)
		{
			return World.ActorsHavingTrait<IPositionable>().Count(a => a.Owner == owner && a.Info.Name == unit);
		}

		int CountBuildingByCommonName(HashSet<string> buildings, Player owner)
		{
			return World.ActorsHavingTrait<Building>()
				.Count(a => a.Owner == owner && buildings.Contains(a.Info.Name));
		}

		public ActorInfo GetInfoByCommonName(HashSet<string> names, Player owner)
		{
			return Map.Rules.Actors.Where(k => names.Contains(k.Key)).Random(Random).Value;
		}

		public bool HasAdequateFact()
		{
			// Require at least one construction yard, unless we have no vehicles factory (can't build it).
			return CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) > 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.VehiclesFactory, Player) == 0;
		}

		public bool HasAdequateProc()
		{
			// Require at least one refinery, unless we can't build it.
			return CountBuildingByCommonName(Info.BuildingCommonNames.Refinery, Player) > 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Power, Player) == 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) == 0;
		}

		public bool HasMinimumProc()
		{
			// Require at least two refineries, unless we have no power (can't build it)
			// or barracks (higher priority?)
			return CountBuildingByCommonName(Info.BuildingCommonNames.Refinery, Player) >= 2 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Power, Player) == 0 ||
				CountBuildingByCommonName(Info.BuildingCommonNames.Barracks, Player) == 0;
		}

		// For mods like RA (number of building must match the number of aircraft)
		bool HasAdequateAirUnitReloadBuildings(ActorInfo actorInfo)
		{
			var aircraftInfo = actorInfo.TraitInfoOrDefault<AircraftInfo>();
			if (aircraftInfo == null)
				return true;

			var ammoPoolsInfo = actorInfo.TraitInfos<AmmoPoolInfo>();

			if (ammoPoolsInfo.Any(x => !x.SelfReloads))
			{
				var countOwnAir = CountUnits(actorInfo.Name, Player);
				var bldgs = World.ActorsHavingTrait<Building>().Where(a =>
					a.Owner == Player && aircraftInfo.RearmBuildings.Contains(a.Info.Name));
				int dockCount = 0;
				foreach (var b in bldgs)
					dockCount += b.TraitsImplementing<Dock>().Count();
				if (countOwnAir >= dockCount)
					return false;
			}

			return true;
		}

		// Given base center position and front vector (front vector starts from center and moves in front direction),
		// find a buildable cell in front semi-circle (or semi-annulus)
		// This function is currently used for placing structure in rear area.
		// (if you invert front vector you get rear)
		CPos? FindPosFront(CPos center, CVec front, int minRange, int maxRange,
			string actorType, BuildingInfo bi, bool distanceToBaseIsImportant)
		{
			// zero vector case. we can't define front or rear.
			if (front == CVec.Zero)
				return FindPos(center, center, minRange, maxRange, actorType, bi, distanceToBaseIsImportant);

			var cells = Map.FindTilesInAnnulus(center, minRange, maxRange).Shuffle(Random);
			foreach (var cell in cells)
			{
				var v = cell - center;
				if (!World.CanPlaceBuilding(actorType, bi, cell, null))
					continue;

				if (CVec.Dot(front, v) > 0)
					return cell;
			}

			// Front is so full of buildings that we can't place anything.
			return null;
		}

		// Find the buildable cell that is closest to pos and centered around center
		CPos? FindPos(CPos center, CPos target, int minRange, int maxRange,
			string actorType, BuildingInfo bi, bool distanceToBaseIsImportant)
		{
			var cells = Map.FindTilesInAnnulus(center, minRange, maxRange);

			// Sort by distance to target if we have one
			if (center != target)
				cells = cells.OrderBy(c => (c - target).LengthSquared);
			else
				cells = cells.Shuffle(Random);

			foreach (var cell in cells)
			{
				if (!World.CanPlaceBuilding(actorType, bi, cell, null))
					continue;

				if (distanceToBaseIsImportant && !bi.IsCloseEnoughToBase(World, Player, actorType, cell))
					continue;

				return cell;
			}

			return null;
		}

		public CPos? GetNoRefCY()
		{
			var cys = GetConstructionYards().ToList();
			var refs = World.Actors.Where(a => a.Owner == Player &&
				Info.BuildingCommonNames.Refinery.Contains(a.Info.Name));

			if (cys.Count() == 0)
				return null;

			// If there's a CY without any ref within 10 cells, return it.
			var coveredCYs = new List<Actor>();
			foreach (var cy in cys)
				foreach (var r in refs)
				{
					if ((r.Location - cy.Location).LengthSquared < 100)
					{
						coveredCYs.Add(cy);
						break;
					}
				}

			foreach (var cy in coveredCYs)
				cys.Remove(cy);

			if (cys.Count() == 0)
				return null;

			return cys.Random(Random).Location;
		}

		CPos defenseCenter;
		public CPos? ChooseBuildLocation(string actorType, bool distanceToBaseIsImportant, BuildingPlacementType type)
		{
			if (Info.NNBuildingPlacer && !Info.BuildingCommonNames.NavalProduction.Contains(actorType))
				return NNChooseBuildLocation(actorType);

			var bi = Map.Rules.Actors[actorType].TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return null;

			var baseCenter = GetRandomBaseCenter();

			switch (type)
			{
				case BuildingPlacementType.Defense:
					{
						// Build near the closest enemy structure
						var closestEnemy = World.ActorsHavingTrait<Building>().Where(a => !a.Disposed && IsOwnedByEnemy(a))
							.ClosestTo(World.Map.CenterOfCell(defenseCenter));

						var targetCell = closestEnemy != null ? closestEnemy.Location : baseCenter;
						return FindPos(defenseCenter, targetCell, Info.MinimumDefenseRadius, Info.MaximumDefenseRadius,
							actorType, bi, distanceToBaseIsImportant);
					}

				case BuildingPlacementType.Fragile:
					{
						// Build far from the closest enemy structure
						var closestEnemy = World.ActorsHavingTrait<Building>().Where(a => !a.Disposed && IsOwnedByEnemy(a))
							.ClosestTo(World.Map.CenterOfCell(defenseCenter));

						CVec direction = CVec.Zero;
						if (closestEnemy != null)
							direction = baseCenter - closestEnemy.Location;

						// MinFragilePlacementRadius introduced to push fragile buildings away from base center.
						// Resilient to nuke.
						var pos = FindPosFront(baseCenter, direction, Info.MinFragilePlacementRadius, Info.MaxBaseRadius,
							actorType, bi, distanceToBaseIsImportant);
						if (pos == null) // rear placement failed but we can still try placing anywhere.
							pos = FindPos(baseCenter, baseCenter, Info.MinBaseRadius,
								distanceToBaseIsImportant ? Info.MaxBaseRadius : Map.Grid.MaximumTileSearchRange,
								actorType, bi, distanceToBaseIsImportant);
						return pos;
					}

				case BuildingPlacementType.Refinery:
					var tmp = GetNoRefCY();
					if (tmp != null)
						baseCenter = tmp.Value;

					WDist RefineryCoverRadius = WDist.FromCells(8);

					// We'd only have only at most 6 of those so it won't be a performance issue.
					var refs = World.ActorsHavingTrait<Building>().Where(b => b.Owner == Player && !b.IsDead && !b.Disposed);

					// Try and place the refinery near a resource field
					// Not using hash set, we wouldn't have too much resources, usually so list should be faster in most cases.
					var nearbyResources = Map.FindTilesInAnnulus(baseCenter, Info.MinBaseRadius, Info.MaxBaseRadius)
						.Where(a => resourceTypeIndices.Get(Map.GetTerrainIndex(a)))
						.Shuffle(Random).Take(Info.MaxResourceCellsToCheck)
						.ToList();

					// Remove covered ones
					if (refs.Any())
						nearbyResources.RemoveAll(r => (refs.ClosestTo(r).CenterPosition - Map.CenterOfCell(r))
								.LengthSquared <= RefineryCoverRadius.LengthSquared);

					foreach (var r in nearbyResources)
					{
						var found = FindPos(baseCenter, r, Info.MinBaseRadius, Info.MaxBaseRadius,
							actorType, bi, distanceToBaseIsImportant);
						if (found != null)
							return found;
					}

					// Try and find a free spot somewhere else in the base
					return FindPos(baseCenter, baseCenter, Info.MinBaseRadius, Info.MaxBaseRadius,
						actorType, bi, distanceToBaseIsImportant);

				case BuildingPlacementType.Building:
					return FindPos(baseCenter, baseCenter, Info.MinBaseRadius,
						distanceToBaseIsImportant ? Info.MaxBaseRadius : Map.Grid.MaximumTileSearchRange,
						actorType, bi, distanceToBaseIsImportant);
			}

			// Can't find a build location
			return null;
		}

		public void Tick(Actor self)
		{
			if (!IsEnabled)
				return;

			ticks++;

			if (ticks == 1)
			{
				InitializeBase(self);

				// Activating here. Some variables aren't available in Lua at tick 0.
				foreach (var kv in luaContexts)
					kv.Value.ActivateAI(Player.Faction.Name, Player.InternalName);
			}
			else
			{
				// Don't tick at ticks 0 and 1.
				if (Info.EnableLuaUnitProduction) // tick each lua AI context only when told so.
					foreach (var kv in luaContexts)
						kv.Value.Tick(self);
			}

			// Fall back to hacky random production when lua production not set.
			if (!Info.EnableLuaUnitProduction && ticks % FeedbackTime == 0)
				ProductionUnits(self);

			AssignRolesToIdleUnits(self);
			SetRallyPointsForNewProductionBuildings(self);
			TryToUseSupportPower(self);

			foreach (var b in builders)
				b.Tick();

			DefenseTick();

			var ordersToIssueThisTick = Math.Min((orders.Count + Info.MinOrderQuotientPerTick - 1) / Info.MinOrderQuotientPerTick, orders.Count);
			for (var i = 0; i < ordersToIssueThisTick; i++)
				World.IssueOrder(orders.Dequeue());
		}

		internal Actor FindClosestEnemy(WPos pos)
		{
			return World.Actors.Where(IsEnemyUnit).ClosestTo(pos);
		}

		internal Actor FindClosestEnemy(WPos pos, WDist radius)
		{
			return World.FindActorsInCircle(pos, radius).Where(IsEnemyUnit).ClosestTo(pos);
		}

		IEnumerable<Actor> FindEnemyConstructionYards()
		{
			return World.ActorsHavingTrait<Building>().Where(b => IsOwnedByEnemy(b)
				&& !b.IsDead && !b.Disposed && Info.BuildingCommonNames.ConstructionYard.Contains(b.Info.Name));
		}

		void CleanSquads()
		{
			Squads.RemoveAll(s => !s.IsValid);
			foreach (var s in Squads)
				s.RemoveInvalidMembers(UnitCannotBeOrdered);
		}

		// Use of this function requires that one squad of this type. Hence it is a piece of shit
		Squad GetSquadOfType(SquadType type)
		{
			return Squads.FirstOrDefault(s => s.Type == type);
		}

		Squad RegisterNewSquad(SquadType type, Actor target = null)
		{
			var ret = new Squad(this, type, target);
			Squads.Add(ret);
			return ret;
		}

		void AssignRolesToIdleUnits(Actor self)
		{
			CleanSquads();

			activeUnits.RemoveAll(UnitCannotBeOrdered);
			unitsHangingAroundTheBase.RemoveAll(UnitCannotBeOrdered);

			if (--rushTicks <= 0)
			{
				rushTicks = Info.RushInterval;
				TryToRushAttack();
			}

			if (--attackForceTicks <= 0)
			{
				attackForceTicks = Info.AttackForceInterval;
				foreach (var s in Squads)
					s.Update();
			}

			if (--assignRolesTicks <= 0)
			{
				assignRolesTicks = Info.AssignRolesInterval;
				GiveOrdersToIdleHarvesters();
				FindNewUnits(self);
				SuicideDemoUnits(self);
				FindAndDeployBackupMcv(self);
				SellUselessRefinery();
			}

			if (--minAttackForceDelayTicks <= 0)
			{
				minAttackForceDelayTicks = Info.MinimumAttackForceDelay;
				CreateAttackForce();
			}

			if (--minCaptureDelayTicks <= 0)
			{
				minCaptureDelayTicks = Info.MinimumCaptureDelay;
				QueueCaptureOrders();
			}
		}

		IEnumerable<Actor> GetVisibleActorsBelongingToPlayer(Player owner)
		{
			foreach (var actor in GetActorsThatCanBeOrderedByPlayer(owner))
				if (actor.CanBeViewedByPlayer(Player))
					yield return actor;
		}

		IEnumerable<Actor> GetActorsThatCanBeOrderedByPlayer(Player owner)
		{
			foreach (var actor in World.Actors)
				if (actor.Owner == owner && !actor.IsDead && actor.IsInWorld)
					yield return actor;
		}

		// How many units are protecting this actor?
		int ProtectionLevel(Actor a)
		{
			int threat = 0;
			var neighbors = World.FindActorsInCircle(a.CenterPosition, WDist.FromCells(5));
			foreach (var nei in neighbors)
				if (nei.TraitsImplementing<AttackBase>().Count() > 0)
					threat++;
			return threat;
		}

		void QueueCaptureOrders()
		{
			if (!Info.CapturingActorTypes.Any() || Player.WinState != WinState.Undefined)
				return;

			var capturers = unitsHangingAroundTheBase.Where(a => a.IsIdle && Info.CapturingActorTypes.Contains(a.Info.Name)).ToArray();
			if (capturers.Length == 0)
				return;

			var randPlayer = World.Players.Where(p => !p.Spectating
				&& Info.CapturableStances.HasStance(Player.Stances[p])).Random(Random);

			var allTargetOptions = Info.CheckCaptureTargetsForVisibility
				? GetVisibleActorsBelongingToPlayer(randPlayer)
				: World.Actors.Where(a => a.IsInWorld && !a.IsDead && Info.CapturableStances.HasStance(Player.Stances[a.Owner]) &&
					(a.TraitOrDefault<Capturable>() != null || a.TraitOrDefault<ExternalCapturable>() != null));

			var targetOptions = allTargetOptions;

			if (Info.CapturableActorTypes.Any())
				targetOptions = targetOptions.Where(target => Info.CapturableActorTypes.Contains(target.Info.Name.ToLowerInvariant()));

			if (!targetOptions.Any())
				targetOptions = allTargetOptions;

			var capturableTargetOptions = targetOptions
				.Select(a => new CaptureTarget<CapturableInfo>(a, "CaptureActor"))
				.Where(target => target.Info != null && capturers.Any(capturer =>
					target.Actor.TraitOrDefault<Capturable>() != null &&
					target.Actor.TraitOrDefault<Capturable>().CanBeTargetedBy(capturer, target.Actor.Owner)))
				.OrderBy(target => ProtectionLevel(target.Actor))
				.Take(maximumCaptureTargetOptions);

			var externalCapturableTargetOptions = targetOptions
				.Select(a => new CaptureTarget<ExternalCapturableInfo>(a, "ExternalCaptureActor"))
				.Where(target => target.Info != null && capturers.Any(capturer =>
					target.Actor.TraitOrDefault<ExternalCapturable>() != null &&
					target.Actor.TraitOrDefault<ExternalCapturable>().CanBeTargetedBy(capturer, target.Actor.Owner)))
				.OrderBy(target => ProtectionLevel(target.Actor))
				.Take(maximumCaptureTargetOptions);

			if (!capturableTargetOptions.Any() && !externalCapturableTargetOptions.Any())
				return;

			var capturesCapturers = capturers.Where(a => a.Info.HasTraitInfo<CapturesInfo>());
			var externalCapturers = capturers.Except(capturesCapturers).Where(a => a.Info.HasTraitInfo<ExternalCapturesInfo>());

			// At this point, capture order will happen. Remove capturers from any unitsHangingAroundBase
			// So that they will not be recruited for now. (Will be recruited when they become idle again.)

			foreach (var capturer in capturesCapturers)
			{
				if (unitsHangingAroundTheBase.Contains(capturer))
					unitsHangingAroundTheBase.Remove(capturer);
				QueueCaptureOrderFor(capturer, GetCapturerTargetClosestToOrDefault(capturer, capturableTargetOptions));
			}

			foreach (var capturer in externalCapturers)
			{
				if (unitsHangingAroundTheBase.Contains(capturer))
					unitsHangingAroundTheBase.Remove(capturer);
				QueueCaptureOrderFor(capturer, GetCapturerTargetClosestToOrDefault(capturer, externalCapturableTargetOptions));
			}
		}

		void QueueCaptureOrderFor<TTargetType>(Actor capturer, CaptureTarget<TTargetType> target) where TTargetType : class, ITraitInfoInterface
		{
			if (capturer == null)
				return;

			if (target == null)
				return;

			if (target.Actor == null)
				return;

			QueueOrder(new Order(target.OrderString, capturer, true) { TargetActor = target.Actor });
			BotDebug("AI ({0}): Ordered {1} to capture {2}", Player.ClientIndex, capturer, target.Actor);
			activeUnits.Remove(capturer);
		}

		CaptureTarget<TTargetType> GetCapturerTargetClosestToOrDefault<TTargetType>(Actor capturer, IEnumerable<CaptureTarget<TTargetType>> targets)
			where TTargetType : class, ITraitInfoInterface
		{
			return targets.MinByOrDefault(target => (target.Actor.CenterPosition - capturer.CenterPosition).LengthSquared);
		}

		CPos FindNextResource(Actor harvester)
		{
			var harvInfo = harvester.Info.TraitInfo<HarvesterInfo>();
			var mobileInfo = harvester.Info.TraitInfo<MobileInfo>();
			var passable = (uint)mobileInfo.GetMovementClass(World.Map.Rules.TileSet);

			var path = pathfinder.FindPath(
				PathSearch.Search(World, mobileInfo, harvester, true,
					loc => domainIndex.IsPassable(harvester.Location, loc, mobileInfo, passable) && harvester.CanHarvestAt(loc, resLayer, harvInfo, territory))
					.WithCustomCost(loc => World.FindActorsInCircle(World.Map.CenterOfCell(loc), Info.HarvesterEnemyAvoidanceRadius)
						.Where(u => !u.IsDead && IsOwnedByEnemy(u))
						.Sum(u => Math.Max(WDist.Zero.Length, Info.HarvesterEnemyAvoidanceRadius.Length - (World.Map.CenterOfCell(loc) - u.CenterPosition).Length)))
					.FromPoint(harvester.Location));

			if (path.Count == 0)
				return CPos.Zero;

			return path[0];
		}

		void GiveOrdersToIdleHarvesters()
		{
			// Find idle harvesters and give them orders:
			foreach (var harvester in activeUnits)
			{
				var harv = harvester.TraitOrDefault<Harvester>();
				if (harv == null)
					continue;

				if (!harvester.IsIdle)
				{
					var act = harvester.CurrentActivity;
					if (act.NextActivity == null || act.NextActivity.GetType() != typeof(FindResources))
						continue;
				}

				if (!harv.IsEmpty)
					continue;

				// Tell the idle harvester to quit slacking:
				var newSafeResourcePatch = FindNextResource(harvester);
				BotDebug("AI: Harvester {0} is idle. Ordering to {1} in search for new resources.".F(harvester, newSafeResourcePatch));
				QueueOrder(new Order("Harvest", harvester, false) { TargetLocation = newSafeResourcePatch });
			}
		}

		void FindNewUnits(Actor self)
		{
			// Be sure to do InWorld check as well, they might be in transport.
			var newUnits = self.World.ActorsHavingTrait<IPositionable>()
				.Where(a => a.Owner == Player
					&& !a.IsDead && !a.Disposed && a.IsInWorld
					&& !Info.UnitsCommonNames.Mcv.Contains(a.Info.Name)
					&& !Info.UnitsCommonNames.ExcludeFromSquads.Contains(a.Info.Name)
					&& !activeUnits.Contains(a));

			foreach (var a in newUnits)
			{
				if (a.Info.HasTraitInfo<HarvesterInfo>())
				{
					QueueOrder(new Order("Harvest", a, false));
					activeUnits.Add(a);
					continue;
				}

				unitsHangingAroundTheBase.Add(a);
				if (Info.CapturingActorTypes.Contains(a.Info.Name))
					QueueCaptureOrders();
				else if (a.Info.HasTraitInfo<AircraftInfo>() && a.Info.HasTraitInfo<AttackBaseInfo>())
				{
					var air = GetSquadOfType(SquadType.Air);
					if (air == null)
						air = RegisterNewSquad(SquadType.Air);

					air.AddUnit(a);
					WhichSquad[a] = air;
				}
				else if (Info.UnitsCommonNames.NavalUnits.Contains(a.Info.Name))
				{
					var ships = GetSquadOfType(SquadType.Ships);
					if (ships == null)
						ships = RegisterNewSquad(SquadType.Ships);

					ships.AddUnit(a);
					WhichSquad[a] = ships;
				}

				activeUnits.Add(a);
			}
		}

		void CreateAttackForce()
		{
			// Create an attack force when we have enough units around our base.
			// (don't bother leaving any behind for defense)
			var randomizedSquadSize = Info.SquadSize + Random.Next(Info.SquadSizeRandomBonus);

			if (unitsHangingAroundTheBase.Count >= randomizedSquadSize)
			{
				var attackForce = RegisterNewSquad(SquadType.Assault);

				foreach (var a in unitsHangingAroundTheBase)
				{
					if (a.Info.HasTraitInfo<AircraftInfo>())
						continue;
					if (Info.UnitsCommonNames.NavalUnits.Contains(a.Info.Name))
						continue;
					if (Info.UnitsCommonNames.ExcludeFromAttackSquads.Contains(a.Info.Name))
						continue;
					attackForce.AddUnit(a);
					WhichSquad[a] = attackForce;
				}

				unitsHangingAroundTheBase.Clear();
			}
		}

		public bool IsOwnedByEnemy(Actor a)
		{
			return Player.Stances[a.Owner] == Stance.Enemy && (!a.Owner.NonCombatant);
		}

		void TryToRushAttack()
		{
			var allEnemyBaseBuilder = FindEnemyConstructionYards();
			var ownUnits = activeUnits
				.Where(unit => unit.IsIdle && unit.Info.HasTraitInfo<AttackBaseInfo>()
					&& !unit.Info.HasTraitInfo<AircraftInfo>()
					&& !Info.UnitsCommonNames.NavalUnits.Contains(unit.Info.Name)
					&& !unit.Info.HasTraitInfo<HarvesterInfo>()).ToList();

			if (!allEnemyBaseBuilder.Any() || (ownUnits.Count < Info.SquadSize))
				return;

			foreach (var b in allEnemyBaseBuilder)
			{
				var enemies = World.FindActorsInCircle(b.CenterPosition, WDist.FromCells(Info.RushAttackScanRadius))
					.Where(unit => IsOwnedByEnemy(unit) && unit.Info.HasTraitInfo<AttackBaseInfo>()).ToList();

				if (AttackOrFleeFuzzy.Rush.CanAttack(ownUnits, enemies))
				{
					Actor target = enemies.Any() ? enemies.Random(Random) : b;
					//// TODO: make on target selected Lua call

					var rush = GetSquadOfType(SquadType.Rush);
					if (rush == null || rush.Units.Count() > Info.SquadSize)
					{
						// Everybody gets lumped into one huge squad called SquadType.Rush, if we don't split up,
						// That's why we are examining units.count.
						rush = RegisterNewSquad(SquadType.Rush, target);
					}

					foreach (var a3 in ownUnits)
					{
						rush.AddUnit(a3);
						WhichSquad[a3] = rush;
					}

					return;
				}
			}
		}

		void ProtectOwn(Actor underAttack, Actor attacker)
		{
			var protectSq = GetSquadOfType(SquadType.Protection);
			if (protectSq == null)
				protectSq = RegisterNewSquad(SquadType.Protection, attacker);

			if (!protectSq.IsTargetValid)
				protectSq.TargetActor = attacker;

			if (!protectSq.IsValid)
			{
				var ownUnits = World.FindActorsInCircle(World.Map.CenterOfCell(underAttack.Location), WDist.FromCells(Info.ProtectUnitScanRadius))
					.Where(unit => unit.Owner == Player && !unit.Info.HasTraitInfo<BuildingInfo>() && !unit.Info.HasTraitInfo<HarvesterInfo>()
						&& unit.Info.HasTraitInfo<AttackBaseInfo>());

				foreach (var a in ownUnits)
				{
					protectSq.AddUnit(a);
					WhichSquad[a] = protectSq;
				}
			}
		}

		void ProtectMCV(Actor mcv)
		{
			// Don't bother searching for any existing squad. Make an independent one.
			var protectSq = RegisterNewSquad(SquadType.Escort, mcv);

			var ownUnits = World.FindActorsInCircle(mcv.CenterPosition, WDist.FromCells(Info.ProtectUnitScanRadius))
				.Where(unit => unit.Owner == Player && !unit.Info.HasTraitInfo<BuildingInfo>()
					&& !unit.Info.HasTraitInfo<HarvesterInfo>()
					&& unit.Info.HasTraitInfo<AttackBaseInfo>())
				.Take(10);

			if (!ownUnits.Any())
				ownUnits = World.ActorsHavingTrait<AttackBase>()
					.Where(unit => unit.Owner == Player && !unit.Info.HasTraitInfo<BuildingInfo>()
						&& !unit.Info.HasTraitInfo<HarvesterInfo>())
					.Take(10);

			foreach (var a in ownUnits)
			{
				protectSq.AddUnit(a);
				WhichSquad[a] = protectSq;
			}
		}

		bool IsRallyPointValid(CPos x, BuildingInfo info)
		{
			return info != null && World.IsCellBuildable(x, info);
		}

		int rallyPointTicks = 0;
		void SetRallyPointsForNewProductionBuildings(Actor self)
		{
			rallyPointTicks++;

			foreach (var rp in self.World.ActorsWithTrait<RallyPoint>())
				if (rp.Actor.Owner == Player
						&& (!IsRallyPointValid(rp.Trait.Location, rp.Actor.Info.TraitInfoOrDefault<BuildingInfo>())
							|| rallyPointTicks > 5 * 25) // periodically set rally point so that units are scattered around
					)
				{
					QueueOrder(new Order("SetRallyPoint", rp.Actor, false)
					{
						TargetLocation = ChooseRallyLocationNear(rp.Actor),
						SuppressVisualFeedback = true
					});
				}

			rallyPointTicks = 0;
		}

		// Won't work for shipyards...
		CPos ChooseRallyLocationNear(Actor producer)
		{
			var center = producer.Location;
			if (!Info.BuildingCommonNames.NavalProduction.Contains(producer.Info.Name))
			{
				var cys = GetConstructionYards();
				var enemy = FindClosestEnemy(producer.CenterPosition);
				if (cys.Any() && enemy != null)
					center = cys.ClosestTo(enemy).Location;
			}

			var possibleRallyPoints = Map.FindTilesInCircle(center, Info.RallyPointScanRadius)
				.Where(c => IsRallyPointValid(c, producer.Info.TraitInfoOrDefault<BuildingInfo>()));

			if (!possibleRallyPoints.Any())
			{
				BotDebug("Bot Bug: No possible rallypoint near {0}", center);
				return center;
			}

			return possibleRallyPoints.Random(Random);
		}

		void InitializeBase(Actor self)
		{
			// Find and deploy our mcv
			var mcv = self.World.Actors.FirstOrDefault(a => a.Owner == Player &&
				Info.UnitsCommonNames.Mcv.Contains(a.Info.Name));

			if (mcv != null)
			{
				initialBaseCenter = mcv.Location;
				defenseCenter = mcv.Location;
				QueueOrder(new Order("DeployTransform", mcv, false));
			}
			else
				BotDebug("AI: Can't find BaseBuildUnit.");
		}

		void SuicideDemoUnits(Actor self)
		{
			var demos = self.World.Actors.Where(a => a.Owner == Player &&
				Info.UnitsCommonNames.DemoUnits.Contains(a.Info.Name));

			foreach (var demo in demos)
			{
				if (!demo.IsIdle)
					continue;

				// Prevent this from groupping with other guys
				if (unitsHangingAroundTheBase.Contains(demo))
					unitsHangingAroundTheBase.Remove(demo);

				// Build near the closest enemy structure
				Actor closestEnemy = null;
				var enemyStructures = World.ActorsHavingTrait<Building>().Where(a => !a.Disposed && IsOwnedByEnemy(a));
				var defenses = enemyStructures.Where(b => b.TraitsImplementing<AttackBase>().Count() > 0);
				if (defenses.Count() > 0)
					closestEnemy = defenses.ClosestTo(World.Map.CenterOfCell(defenseCenter));
				else
					closestEnemy = enemyStructures.ClosestTo(World.Map.CenterOfCell(defenseCenter));

				// Just move. Don't blow up in my base!
				QueueOrder(new Order("Move", demo, false) { TargetLocation = closestEnemy.Location });
				if (demo.TraitOrDefault<AttackSuicides>() != null)
					QueueOrder(new Order("DetonateAttack", demo, false) { TargetActor = closestEnemy });
				else
					QueueOrder(new Order("AttackMove", demo, true) { TargetLocation = closestEnemy.Location });
			}
		}

		CPos? FindExpansionLocation(Actor mcv, string factType)
		{
			var mines = World.ActorsHavingTrait<SeedsResource>();
			if (mines.Count() == 0)
				return null;

			var bases = World.ActorsHavingTrait<Building>().Where(a => a.Owner == Player
				&& (Info.BuildingCommonNames.ConstructionYard.Contains(a.Info.Name)
					|| Info.BuildingCommonNames.Refinery.Contains(a.Info.Name)));
			var radius2 = 10 * 10;

			// Maybe I have no base!
			if (bases.Count() == 0)
			{
				var bi2 = Map.Rules.Actors[factType].TraitInfoOrDefault<BuildingInfo>();
				return FindPos(mcv.Location, mcv.Location, Info.MinBaseRadius, 10, factType, bi2, false);
			}

			// List should be faster than any other complex datastructures, in most maps.
			List<Actor> coveredMines = new List<Actor>();
			foreach (var m in mines)
				foreach (var b in bases)
				{
					var dist = (m.Location - b.Location).LengthSquared;
					if (dist <= radius2)
						coveredMines.Add(m);
				}

			// Get uncovered mines
			List<Actor> uncoveredMines = new List<Actor>();
			foreach (var m in World.ActorsHavingTrait<SeedsResource>().Where(a => a.Owner.NonCombatant))
				if (!coveredMines.Contains(m))
					uncoveredMines.Add(m);

			if (uncoveredMines.Count() == 0)
				return null; // All covered :(

			// Find a closest uncovered mine.
			Actor bestMine = uncoveredMines.ClosestTo(mcv);

			// With a good mine point, find a deployabe location.
			var bi = Map.Rules.Actors[factType].TraitInfoOrDefault<BuildingInfo>();

			// Max radius is too big. Starting from small radius
			// Use front vector to prevent deploying right deep in enemy territory
			CVec front = bestMine.Location - mcv.Location;
			var pos = FindPosFront(bestMine.Location, front, Info.MinBaseRadius, 10, factType, bi, false);
			if (pos == null)
				pos = FindPosFront(bestMine.Location, front, Info.MinBaseRadius, 15, factType, bi, false);
			if (pos == null)
				pos = FindPos(bestMine.Location, bestMine.Location, Info.MinBaseRadius, Info.MaxBaseRadius, factType, bi, false);
			if (pos == null)
				return null;

			defenseCenter = pos.Value;
			ProtectMCV(mcv);
			return pos;
		}

		// Find any newly constructed MCVs and deploy them at a sensible
		// backup location.
		void FindAndDeployBackupMcv(Actor self)
		{
			var mcvs = self.World.Actors.Where(a => a.Owner == Player &&
				Info.UnitsCommonNames.Mcv.Contains(a.Info.Name));

			foreach (var mcv in mcvs)
			{
				if (!mcv.IsIdle)
					continue;

				// If we lack a base, we need to make sure we don't restrict deployment of the MCV to the base!
				var transformInfo = mcv.Info.TraitInfo<TransformsInfo>();
				var restrictToBase =
					Info.RestrictMCVDeploymentFallbackToBase &&
					CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player) > 0;
				var factType = transformInfo.IntoActor;
				var desiredLocation = FindExpansionLocation(mcv, factType);
				if (desiredLocation == null)
					desiredLocation = ChooseBuildLocation(factType, restrictToBase, BuildingPlacementType.Building);
				if (desiredLocation == null)
					continue;

				var loc = desiredLocation.Value - transformInfo.Offset;
				QueueOrder(new Order("Move", mcv, true) { TargetLocation = loc });
				QueueOrder(new Order("DeployTransform", mcv, true));
			}
		}

		void TryToUseSupportPower(Actor self)
		{
			if (supportPowerMngr == null)
				return;

			foreach (var sp in supportPowerMngr.Powers.Values)
			{
				if (sp.Disabled)
					continue;

				// Add power to dictionary if not in delay dictionary yet
				if (!waitingPowers.ContainsKey(sp))
					waitingPowers.Add(sp, 0);

				if (waitingPowers[sp] > 0)
					waitingPowers[sp]--;

				// If we have recently tried and failed to find a use location for a power, then do not try again until later
				var isDelayed = waitingPowers[sp] > 0;
				if (sp.Ready && !isDelayed && powerDecisions.ContainsKey(sp.Info.OrderName))
				{
					var powerDecision = powerDecisions[sp.Info.OrderName];
					if (powerDecision == null)
					{
						BotDebug("Bot Bug: FindAttackLocationToSupportPower, couldn't find powerDecision for {0}", sp.Info.OrderName);
						continue;
					}

					var attackLocation = FindCoarseAttackLocationToSupportPower(sp);
					if (attackLocation == null)
					{
						BotDebug("AI: {1} can't find suitable coarse attack location for support power {0}. Delaying rescan.", sp.Info.OrderName, Player.PlayerName);
						waitingPowers[sp] += powerDecision.GetNextScanTime(this);

						continue;
					}

					// Found a target location, check for precise target
					attackLocation = FindFineAttackLocationToSupportPower(sp, (CPos)attackLocation);
					if (attackLocation == null)
					{
						BotDebug("AI: {1} can't find suitable final attack location for support power {0}. Delaying rescan.", sp.Info.OrderName, Player.PlayerName);
						waitingPowers[sp] += powerDecision.GetNextScanTime(this);

						continue;
					}

					// Valid target found, delay by a few ticks to avoid rescanning before power fires via order
					BotDebug("AI: {2} found new target location {0} for support power {1}.", attackLocation, sp.Info.OrderName, Player.PlayerName);
					waitingPowers[sp] += 10;
					QueueOrder(new Order(sp.Key, supportPowerMngr.Self, false) { TargetLocation = attackLocation.Value, SuppressVisualFeedback = true });
				}
			}
		}

		void DefenseTick()
		{
			if (defenseTicks++ < Info.DefenseInterval)
				return;
			defenseTicks = 0;

			var cys = GetConstructionYards();

			if (!cys.Any())
				return; // We are probably screwed already. Doesn't matter anymore.

			foreach (var cy in cys)
			{
				var visibleEnemies = World.FindActorsInCircle(cy.CenterPosition, WDist.FromCells(Info.MaxBaseRadius))
					.Where(a => IsEnemyUnit(a) && a.CanBeViewedByPlayer(Player));

				if (!visibleEnemies.Any())
					continue;

				var enemy = visibleEnemies.ClosestTo(cy);
				defenseCenter = enemy.Location;
				ProtectOwn(cy, enemy);
			}
		}

		/// <summary>Scans the map in chunks, evaluating all actors in each.</summary>
		CPos? FindCoarseAttackLocationToSupportPower(SupportPowerInstance readyPower)
		{
			CPos? bestLocation = null;
			var bestAttractiveness = 0;
			var powerDecision = powerDecisions[readyPower.Info.OrderName];
			if (powerDecision == null)
			{
				BotDebug("Bot Bug: FindAttackLocationToSupportPower, couldn't find powerDecision for {0}", readyPower.Info.OrderName);
				return null;
			}

			var map = World.Map;
			var checkRadius = powerDecision.CoarseScanRadius;
			for (var i = 0; i < map.MapSize.X; i += checkRadius)
			{
				for (var j = 0; j < map.MapSize.Y; j += checkRadius)
				{
					var consideredAttractiveness = 0;

					var tl = World.Map.CenterOfCell(new MPos(i, j).ToCPos(map));
					var br = World.Map.CenterOfCell(new MPos(i + checkRadius, j + checkRadius).ToCPos(map));
					var targets = World.ActorMap.ActorsInBox(tl, br);

					consideredAttractiveness = powerDecision.GetAttractiveness(targets, Player);
					if (consideredAttractiveness <= bestAttractiveness || consideredAttractiveness < powerDecision.MinimumAttractiveness)
						continue;

					bestAttractiveness = consideredAttractiveness;
					bestLocation = new MPos(i, j).ToCPos(map);
				}
			}

			return bestLocation;
		}

		/// <summary>Detail scans an area, evaluating positions.</summary>
		CPos? FindFineAttackLocationToSupportPower(SupportPowerInstance readyPower, CPos checkPos, int extendedRange = 1)
		{
			CPos? bestLocation = null;
			var bestAttractiveness = 0;
			var powerDecision = powerDecisions[readyPower.Info.OrderName];
			if (powerDecision == null)
			{
				BotDebug("Bot Bug: FindAttackLocationToSupportPower, couldn't find powerDecision for {0}", readyPower.Info.OrderName);
				return null;
			}

			var checkRadius = powerDecision.CoarseScanRadius;
			var fineCheck = powerDecision.FineScanRadius;
			for (var i = 0 - extendedRange; i <= (checkRadius + extendedRange); i += fineCheck)
			{
				var x = checkPos.X + i;

				for (var j = 0 - extendedRange; j <= (checkRadius + extendedRange); j += fineCheck)
				{
					var y = checkPos.Y + j;
					var pos = World.Map.CenterOfCell(new CPos(x, y));
					var consideredAttractiveness = 0;
					consideredAttractiveness += powerDecision.GetAttractiveness(pos, Player);

					if (consideredAttractiveness <= bestAttractiveness || consideredAttractiveness < powerDecision.MinimumAttractiveness)
						continue;

					bestAttractiveness = consideredAttractiveness;
					bestLocation = new CPos(x, y);
				}
			}

			return bestLocation;
		}

		internal IEnumerable<ProductionQueue> FindQueues(string category)
		{
			return World.ActorsWithTrait<ProductionQueue>()
				.Where(a => a.Actor.Owner == Player && a.Trait.Info.Type == category && a.Trait.Enabled)
				.Select(a => a.Trait);
		}

		void ProductionUnits(Actor self)
		{
			// Stop building until economy is restored
			if (!HasAdequateProc())
				return;

			// No construction yards - Build a new MCV
			if (!HasAdequateFact() && !self.World.Actors.Any(a => a.Owner == Player &&
				Info.UnitsCommonNames.Mcv.Contains(a.Info.Name)))
				BuildUnit("Vehicle", GetInfoByCommonName(Info.UnitsCommonNames.Mcv, Player).Name);

			foreach (var q in Info.UnitQueues)
			{
				if (Info.NNProduction)
					NNProductionQuery(q, true);
				else
					BuildUnit(q, unitsHangingAroundTheBase.Count < Info.IdleBaseUnitsMaximum);
			}
		}

		void BuildUnit(string category, bool buildRandom)
		{
			// Pick a free queue
			var queue = FindQueues(category).FirstOrDefault(q => q.CurrentItem() == null);
			if (queue == null)
				return;

			var unit = buildRandom ?
				ChooseRandomUnitToBuild(queue) :
				ChooseUnitToBuild(queue);

			if (unit == null)
				return;

			var name = unit.Name;

			if (Info.UnitsToBuild != null && !Info.UnitsToBuild.ContainsKey(name))
				return;

			if (Info.UnitLimits != null &&
				Info.UnitLimits.ContainsKey(name) &&
				World.Actors.Count(a => a.Owner == Player && a.Info.Name == name) >= Info.UnitLimits[name])
				return;

			var buildableThings = GetBuildableThings(queue);
			if (buildableThings == null)
				return;

			string msg = "BASE_AI_UNIT_LOG";
			msg += " " + CanonicalAIName(Player);
			msg += " " + name;

			msg += " " + buildableThings.Count().ToString();
			foreach (var b in buildableThings)
				msg += " " + b.Name;

			var mine = World.Actors.Where(a => a.Owner == Player);
			msg += " " + mine.Count().ToString();
			foreach (var u in mine)
				msg += " " + u.Info.Name;

			// The remaining are enemy + some useful neutral buildings
			foreach (var u in World.Actors.Where(IsEnemyUnit))
				msg += " " + u.Info.Name;
			// Capturable stuff
			foreach (var u in World.Actors.Where(a => Info.CapturableActorTypes.Contains(a.Info.Name)))
				msg += " " + u.Info.Name;

			clientLock.WaitOne();
			Send(msg);
			clientLock.Release();

			QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}

		void BuildUnit(string category, string name)
		{
			var queue = FindQueues(category).FirstOrDefault(q => q.CurrentItem() == null);
			if (queue == null)
				return;

			if (Map.Rules.Actors[name] != null)
				QueueOrder(Order.StartProduction(queue.Actor, name, 1));
		}

		public void Damaged(Actor self, AttackInfo e)
		{
			if (!IsEnabled || e.Attacker == null)
				return;

			if (e.Attacker.Owner.Stances[self.Owner] == Stance.Neutral)
				return;

			var rb = self.TraitOrDefault<RepairableBuilding>();

			if (Info.ShouldRepairBuildings && rb != null)
			{
				if (e.DamageState > DamageState.Light && e.PreviousDamageState <= DamageState.Light && !rb.RepairActive)
				{
					BotDebug("Bot noticed damage {0} {1}->{2}, repairing.",
						self, e.PreviousDamageState, e.DamageState);
					QueueOrder(new Order("RepairBuilding", self.Owner.PlayerActor, false) { TargetActor = self });
				}
			}

			if (e.Attacker.Disposed)
				return;

			if (!e.Attacker.Info.HasTraitInfo<ITargetableInfo>())
				return;

			// Protected harvesters or building
			if ((self.Info.HasTraitInfo<HarvesterInfo>() || self.Info.HasTraitInfo<BuildingInfo>()) &&
				Player.Stances[e.Attacker.Owner] == Stance.Enemy)
			{
				defenseCenter = e.Attacker.Location;
				ProtectOwn(self, e.Attacker);
			}
			else
			{
				if (e.Damage.Value == 0)
					return;

				// HARDCODE: Aurora keep target
				if (self.Info.Name == "mig")
					return;

				// U2 or spawned stuff
				if (self.TraitOrDefault<Selectable>() == null)
					return;

				// Aircraft micro/ ground retaliation control
				if (WhichSquad.ContainsKey(self))
				{
					// Determine flee or not. Fast reflex is essential for fragile aircrafts.
					if (self.Info.HasTraitInfo<AircraftInfo>())
					{
						WhichSquad[self].Update();
						WhichSquad[self].Damage(e);
					}
				}
			}
		}

		public void Send(string msg)
		{
			var sendBytes = Encoding.UTF8.GetBytes(msg);
			client.Send(sendBytes, sendBytes.Length);
		}

		// Check if CY can undeploy.
		// For RA3 Empire, you only get many mini-mcvs that can't undeply so we need to check.
		bool ConstructionYardCanUndeploy(string mcvActorName)
		{
			var transformInfo = World.Map.Rules.Actors[mcvActorName.ToLowerInvariant()].TraitInfoOrDefault<TransformsInfo>();
			if (transformInfo == null)
				return false; // You can't even deploy the actor

			var cy = transformInfo.IntoActor;
			var undeployInfo = World.Map.Rules.Actors[cy.ToLowerInvariant()].TraitInfoOrDefault<TransformsInfo>();
			if (undeployInfo == null)
				return false;

			return mcvActorName.ToLowerInvariant() == undeployInfo.IntoActor.ToLowerInvariant();
		}

		List<ActorInfo> GetBuildableThings(ProductionQueue queue)
		{
			var tmp = queue.BuildableItems();
			if (!tmp.Any())
				return null;

			var buildableThings = new List<ActorInfo>();
			foreach (var b in tmp)
			{
				if (Info.UnitLimits != null &&
					Info.UnitLimits.ContainsKey(b.Name) &&
					World.Actors.Count(a => a.Owner == Player && a.Info.Name == b.Name) >= Info.UnitLimits[b.Name])
					continue;

				buildableThings.Add(b);
			}

			return buildableThings;
		}

		public string NNProductionQuery(string category, bool issueQueueOrder)
		{
			// Pick a free queue
			var queue = FindQueues(category).FirstOrDefault(q => q.CurrentItem() == null);
			if (queue == null)
				return null;

			var buildableThings = GetBuildableThings(queue);
			if (buildableThings == null)
				return null;

			string msg = "UNIT_QUERY";
			msg += " " + CanonicalAIName(Player);

			msg += " " + buildableThings.Count().ToString();
			foreach (var b in buildableThings)
				msg += " " + b.Name;

			var mine = World.Actors.Where(a => a.Owner == Player);
			msg += " " + mine.Count().ToString();
			foreach (var u in mine)
				msg += " " + u.Info.Name;

			// The remaining are enemy + some useful neutral buildings
			foreach (var u in World.Actors.Where(IsEnemyUnit))
				msg += " " + u.Info.Name;
			// Capturable stuff
			foreach (var u in World.Actors.Where(a => Info.CapturableActorTypes.Contains(a.Info.Name)))
				msg += " " + u.Info.Name;

			clientLock.WaitOne();
			Send(msg);
			string name = ReceiveString();
			clientLock.Release();

			// Build nothing, on purpose.
			if (name == null)
				return null;

			// I made semaphore to prevent this from happening but sometimes I get bad response.
			if (!buildableThings.Any(bi => bi.Name == name))
				return null;

			if (Info.UnitsCommonNames.Mcv.Contains(name) && ConstructionYardCanUndeploy(name))
			{
				// Do we alrady have an MCV in the field?
				var mcvs = World.Actors.Where(a => a.Owner == Player && a.Info.Name == name && !a.IsDead && !a.Disposed);
				if (mcvs.Any())
					return null;

				var cys = GetConstructionYards();

				// We already have many CYs. Undeploy one instead.
				if (cys.Count() >= 2)
				{
					var closestEnemy = FindClosestEnemy(cys.Random(Random).CenterPosition);
					if (closestEnemy == null)
						return null; // We must have won!
					var furthestCY = cys.FurthestFrom(closestEnemy);

					// Does it have a refinery near?
					var refs = World.FindActorsInCircle(furthestCY.CenterPosition, WDist.FromCells(Info.MaxBaseRadius))
						.Where(a => a.Owner == Player && !a.IsDead && !a.Disposed
							&& Info.BuildingCommonNames.Refinery.Contains(a.Info.Name));

					if (!refs.Any())
						return null;

					QueueOrder(new Order("DeployTransform", furthestCY, false));
					return null;
				}
			}

			if (issueQueueOrder)
				QueueOrder(Order.StartProduction(queue.Actor, name, 1));
			return name;
		}

		// reference: reference point.
		int2? cpos2uv(CPos location, CPos reference, int sz, int scale)
		{
			int x = (location.X - reference.X) / scale;
			int y = (location.Y - reference.Y) / scale;
			if (x < 0 || y < 0)
				return null;
			if (x >= sz || y >= sz)
				return null;

			return new int2(x, y);
		}

		public string CanonicalAIName(Player p)
		{
			return (p.InternalName + p.PlayerName).Replace(" ", "");
		}

		// When receiving linearized coordinate from the network, decode it as x, y.
		// Return null on receive error.
		int2? ReceiveCoordinate(int sz)
		{
			// Get incoming result
			IPEndPoint remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
			client.Client.ReceiveTimeout = 2500;
			try
			{
				var recv = client.Receive(ref remoteIpEndPoint);
				int index = recv.Count() > 0 ? Int32.Parse(Encoding.UTF8.GetString(recv)) : -1;
				if (index == -1)
					return null;
				int y = index / sz;
				int x = index % sz;
				return new int2(x, y);
			}
			catch
			{
				return null;
			}
		}

		// When receiving linearized coordinate from the network, decode it as x, y.
		// Return null on receive error.
		string ReceiveString()
		{
			// Get incoming result
			IPEndPoint remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
			client.Client.ReceiveTimeout = 2500;
			try
			{
				var recv = client.Receive(ref remoteIpEndPoint);
				string result = Encoding.UTF8.GetString(recv);
				if (result == "NULL")
					return null;
				return result;
			}
			catch
			{
				return null;
			}
		}

		CVec? DirectionToEnemy(CPos from)
		{
			// Compute direction to enemy base vector
			Actor enemyBase = null;
			var enemyBaseBuilder = FindEnemyConstructionYards();
			if (enemyBaseBuilder.Any())
				enemyBase = enemyBaseBuilder.ClosestTo(World.Map.CenterOfCell(from));
			if (enemyBase == null)
			{
				var buildings = World.ActorsHavingTrait<Building>().Where(IsEnemyUnit);
				if (!buildings.Any())
					return null; // We should have already won.
				enemyBase = buildings.ClosestTo(World.Map.CenterOfCell(from));
			}
			var directionToEnemy = enemyBase.Location - from;
			return 10 * directionToEnemy / directionToEnemy.Length;
		}

		Dictionary<Player, int> latestGPComputed = new Dictionary<Player, int>();
		Dictionary<Player, CPos?> lastGoodPosition = new Dictionary<Player, CPos?>();
		public CPos? NNFindRallyPointPosition(Actor actor, Mobile mobile, Player requester)
		{
			if (latestGPComputed.ContainsKey(requester) && (World.WorldTick - latestGPComputed[requester]) < 75)
			{
				if (lastGoodPosition.ContainsKey(requester))
					return lastGoodPosition[requester];
				else
					return null;
			}

			int SZ = 32; // output feature size SZ x SZ
			int HSZ = 16; // half of the feature size
			int NCH = 6; // feature channels (except direction to enemy)
			int SCALE = 2; // Shrink 2x2 to 1x1

			// channel order: (production buildings, defenses, buildings) x 2, (units, harvs) x 2
			// x2 for mine and non-mine.
			// production: 0
			// defense: 1
			// other building: 2
			// units: 3
			// enemy pos: 4 (don't care building or not)
			// terrain: 5
			// direction to enemy: 6 (sent through request string)
			int[,,] minimap = new int[SZ, SZ, NCH];

			var c1 = new CPos(actor.Location.X - SCALE * HSZ, actor.Location.Y - SCALE * HSZ);
			var c2 = new CPos(c1.X + SCALE * SZ - 1, c1.Y + SCALE * SZ - 1);
			var p1 = Map.CenterOfCell(c1);
			var p2 = Map.CenterOfCell(c2);
			var stuff = World.ActorMap.ActorsInBox(p1, p2);

			foreach (var a in stuff)
			{
				int2? uv = cpos2uv(a.Location, c1, SZ, SCALE);
				if (uv == null)
					continue;

				// production
				if (a.Owner == Player)
				{
					if (Info.BuildingCommonNames.Barracks.Contains(a.Info.Name) ||
						Info.BuildingCommonNames.ConstructionYard.Contains(a.Info.Name) ||
						Info.BuildingCommonNames.NavalProduction.Contains(a.Info.Name) ||
						Info.BuildingCommonNames.VehiclesFactory.Contains(a.Info.Name))
					{
						minimap[uv.Value.Y, uv.Value.X, 0] += 1;
					}
					// defense
					else if (Info.BuildingCommonNames.StaticAntiAir.Contains(a.Info.Name) ||
						Info.BuildingCommonNames.Defense.Contains(a.Info.Name))
					{
						minimap[uv.Value.Y, uv.Value.X, 1] += 1;
					}
					else if (a.TraitOrDefault<Building>() != null)
					{
						minimap[uv.Value.Y, uv.Value.X, 2] += 1;
					}
					else if (a.Info.TraitInfoOrDefault<AircraftInfo>() == null)
					{
						// Aircrafts aren't reliable so let's ignore it.
						minimap[uv.Value.Y, uv.Value.X, 3] += 1;
					}
				}
				//else if (a.AppearsHostileTo(Player.PlayerActor) && a.TraitsImplementing<AttackBase>().Count() > 0)
				else if (a.AppearsHostileTo(Player.PlayerActor)) // confining to AttackBase makes AI so blind.
				{
					minimap[uv.Value.Y, uv.Value.X, 4] += 1;
				}
			}

			// Can be computed once per map? But then navy units have a different view haha.
			// I can optimize later.
			// 1:1 map
			/*
			for (int u = 0; u < SZ; u++)
				for (int v = 0; v < SZ; v++)
				{
					var pos = new CPos(c1.X + v, c1.Y + u);
					if (World.Map.Contains(pos) && mobile.CanEnterCell(pos))
						minimap[u, v, 4] += 1;
				}
			*/

			// 2:1 map
			for (int u = 0; u < SCALE * SZ; u++)
				for (int v = 0; v < SCALE * SZ; v++)
				{
					CPos pos = new CPos(c1.X + v, c1.Y + u);
					int2? uv = cpos2uv(pos, c1, SZ, SCALE);
					if (World.Map.Contains(pos) && mobile.CanEnterCell(pos))
						minimap[uv.Value.Y, uv.Value.X, 5] += 1;
				}

			// minimap to send, into sparse number list.
			// Too big so converting to bytes.
			List<int> vals = new List<int>();
			for (int i = 0; i < SZ; i++)
				for (int j = 0; j < SZ; j++)
					for (int k = 0; k < NCH; k++)
					{
						var val = minimap[i, j, k];
						if (val == 0)
							continue;
						vals.Add(i);
						vals.Add(j);
						vals.Add(k);
						vals.Add(val);
					}
			var rawData = new byte[vals.Count()];
			for (int i = 0; i < rawData.Length; i++)
				rawData[i] = (byte) vals[i];
	
			var directionToEnemy = DirectionToEnemy(actor.Location);
			if (directionToEnemy == null)
				return null; // we should have won already.

			//int2 selfLocation = cpos2uv(location);
			string msg = "RALLY_POINT_QUERY";
			msg += " " + CanonicalAIName(Player);
			msg += " " + directionToEnemy.Value.X;
			msg += " " + directionToEnemy.Value.Y;
			msg += " " + vals.Count();

			int2? coord;
			clientLock.WaitOne();
			{
				Send(msg);
				client.Send(rawData, rawData.Length);
				coord = ReceiveCoordinate(SZ);
			}
			clientLock.Release();

			latestGPComputed[requester] = World.WorldTick;

			if (coord == null)
			{
				lastGoodPosition[requester] = null;
				return null;
			}

			var newPos = new CPos(c1.X + SCALE * coord.Value.X, c1.Y + SCALE * coord.Value.Y);
			lastGoodPosition[requester] = newPos;
			return newPos;
		}

		int NNBuildingTypeToInt(string actorType)
		{
			if (Info.BuildingCommonNames.ConstructionYard.Contains(actorType))
				return (int)NNBuildingPlacementType.Production;

			if (Info.BuildingCommonNames.Barracks.Contains(actorType))
				return (int)NNBuildingPlacementType.Production;

			if (Info.BuildingCommonNames.VehiclesFactory.Contains(actorType))
				return (int)NNBuildingPlacementType.Production;

			if (Info.BuildingCommonNames.Defense.Contains(actorType))
				return (int)NNBuildingPlacementType.Defense;

			if (Info.BuildingCommonNames.StaticAntiAir.Contains(actorType))
				return (int)NNBuildingPlacementType.AADefense;

			if (Info.BuildingCommonNames.Refinery.Contains(actorType))
				return (int)NNBuildingPlacementType.Refinery;

			if (Info.BuildingCommonNames.Power.Contains(actorType))
				return (int)NNBuildingPlacementType.Power;

			if (Info.BuildingCommonNames.NNTech.Contains(actorType))
				return (int)NNBuildingPlacementType.Tech;
			
			if (Info.BuildingCommonNames.NNTier3Tech.Contains(actorType))
				return (int)NNBuildingPlacementType.Tier3Tech;

			if (Info.BuildingCommonNames.SuperWeapon.Contains(actorType))
				return (int)NNBuildingPlacementType.SuperWeapon;

			return (int)NNBuildingPlacementType.Other;
		}

		public CPos? NNChooseBuildLocation(string actorType)
		{
			// Alright, we can have multiple MCVs but that can be added later by adding another small network.
			CPos center = GetRandomBaseCenter();

			int HSZ = 16;
			int SZ = 32;
			var c1 = new CPos(center.X - HSZ, center.Y - HSZ);
			var c2 = new CPos(c1.X + SZ, c1.Y + SZ);

			// It doesn't makes much sense to use name <-> type intger in this context.
			// Hence using building categorizer.
			// 0. Production, 1. Defense, 2. AADefense, 3. Refinery, 4. Tech, 5. Tier3Tech, 6. SuperWeapon, 7. Power, 8. Other
			// 9. Ore patch, to determine ref position
			// 10. Non-enterable area (to find defensive pos)
			// 11. Placable tiles, to restrict placement choice
			// 12. (sent with request string) direction to enemy
			// 13. (sent with string) the category of the building we want to place
			// There are many channels but fortunately, all the values are intended to be binary.
			var features = new List<int[]>(); // sparse features: x, y, ch.

			var buildings = World.ActorsHavingTrait<Building>().Where(b => b.Owner == Player);
			foreach (var b in buildings)
			{
				var loc = b.Location - c1;
				if (loc.X < 0 || loc.Y < 0 || loc.X >= SZ || loc.Y >= SZ)
					continue;
				int ch = NNBuildingTypeToInt(b.Info.Name);
				features.Add(new int[3] { loc.X, loc.Y, ch });
			}

			// Resources
			for (int u = 0; u < SZ; u++)
				for (int v = 0; v < SZ; v++)
				{
					var loc = new CPos(c1.X + v, c1.Y + u);
					if (World.Map.Contains(loc) && resLayer.GetResource(loc) != null)
						features.Add(new int[3] { v, u, 9 });
				}

			// To be accurate, need MOBILE trait, not terrain. Trees aren't enterable, as you know.
			for (int u = 0; u < SZ; u++)
				for (int v = 0; v < SZ; v++)
				{
					var pos = new CPos(c1.X + v, c1.Y + u);
					if (World.Map.Contains(pos)
							&& Info.NNBuildingPlacerTerrainTypes.Contains(World.Map.GetTerrainInfo(pos).Type))
						features.Add(new int[3] { v, u, 10 });
				}

			// placaeble
			var bi = Map.Rules.Actors[actorType].TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return null;
			int placeableCnt = 0;
			foreach (var t in World.Map.FindTilesInCircle(center, Info.MaxBaseRadius))
			{
				if (World.Map.Contains(t) && World.CanPlaceBuilding(actorType, bi, t, null)
					&& bi.IsCloseEnoughToBase(World, Player, actorType, t))
				{
					var loc = t - c1;
					if (loc.X < 0 || loc.Y < 0 || loc.X >= SZ || loc.Y >= SZ)
						continue;
					features.Add(new int[3] { loc.X, loc.Y, 11 });
					placeableCnt++;
				}
			}

			if (placeableCnt == 0)
				return null;

			var rawData = new byte[3 * features.Count()];
			int i = 0;
			foreach (var coord in features)
			{
				rawData[i]   = (byte) coord[0];
				rawData[i+1] = (byte) coord[1];
				rawData[i+2] = (byte) coord[2];
				i += 3;
			}

			var directionToEnemy = DirectionToEnemy(center);
			if (directionToEnemy == null)
				return null; // should have won already

			//int2 selfLocation = cpos2uv(location);
			string msg = "BUILDING_PLACEMENT_QUERY";
			msg += " " + CanonicalAIName(Player);
			msg += " " + directionToEnemy.Value.X;
			msg += " " + directionToEnemy.Value.Y;
			msg += " " + NNBuildingTypeToInt(actorType);

			int2? xy;
			clientLock.WaitOne();
			{
				Send(msg);
				client.Send(rawData, rawData.Length);
				xy = ReceiveCoordinate(SZ);
			}
			clientLock.Release();

			if (xy == null)
				return null;

			return new CPos(c1.X + xy.Value.X, c1.Y + xy.Value.Y);
		}

		// Returns negative number when unable to count (no harvester for this player).
		int CountHarvestableCellsInRadius(CPos center, int radius)
		{
			var harvester = World.ActorsHavingTrait<Harvester>().FirstOrDefault(a => a.Owner == Player && !a.IsDead && !a.Disposed);
			if (harvester == null)
				return -1;

			int sum = 0;
			var harvInfo = harvester.Info.TraitInfo<HarvesterInfo>();
			foreach (var t in World.Map.FindTilesInCircle(center, radius))
				if (harvester.CanHarvestAt(t, resLayer, harvInfo, territory))
					sum += 1;

			return sum;
		}

		// Because AIs have build limits and to make them play like humans, we can SELL :)
		// If the closest one is within 8 cells and resources have ran out + we got multiple CYs then refs are useless.
		void SellUselessRefinery()
		{
			var cyCnt = CountBuildingByCommonName(Info.BuildingCommonNames.ConstructionYard, Player);
			if (cyCnt <= 1)
				return;

			var refs = World.ActorsHavingTrait<Building>().Where(b => b.Owner == Player // my
				&& !b.IsDead && !b.Disposed && Info.BuildingCommonNames.Refinery.Contains(b.Info.Name) // refineries
				&& Info.BuildingLimits.ContainsKey(b.Info.Name)); // which are build limited

			foreach (var r in refs)
			{
				// Did this one reach build limit in ai.yaml?
				// Redundant check but simple. (and there won't be too many refs)
				if (refs.Count(b => b.Info.Name == r.Info.Name) < Info.BuildingLimits[r.Info.Name])
					continue;

				var overlappingRef = refs.Where(a => a != r
					&& (r.CenterPosition - a.CenterPosition).LengthSquared <= WDist.FromCells(8).LengthSquared);
				if (!overlappingRef.Any())
					continue;

				var havestableCellsNear = CountHarvestableCellsInRadius(r.Location, 10);
				if (havestableCellsNear < 0) // No harvester. Don't sell!
					return;

				if (havestableCellsNear < 10)
				{
					QueueOrder(new Order("Sell", r, false) { TargetActor = r });
					break; // don't sell multiple refs all at once!!
				}
			}
		}	
	}
}
