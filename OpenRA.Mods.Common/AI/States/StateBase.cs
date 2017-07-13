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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Pathfinder;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.AI
{
	abstract class StateBase
	{
		protected const int DangerRadius = 10;

		protected static void GoToRandomOwnBuilding(Squad squad)
		{
			CPos loc;
			var nearByBuildings = squad.World.FindActorsInCircle(squad.CenterPosition, WDist.FromCells(squad.Bot.Info.MaxBaseRadius))
				.Where(b => b.Owner == squad.Bot.Player && b.TraitOrDefault<Building>() != null);
			if (nearByBuildings.Any())
				loc = nearByBuildings.Random(squad.Bot.Random).Location;
			else
				loc = RandomBuildingLocation(squad);

			foreach (var a in squad.Units)
				squad.Bot.QueueOrder(new Order("Move", a, false) { TargetLocation = loc });
		}

		protected static CPos RandomBuildingLocation(Squad squad)
		{
			var location = squad.Bot.GetRandomBaseCenter();
			var buildings = squad.World.ActorsHavingTrait<Building>()
				.Where(a => a.Owner == squad.Bot.Player).ToList();
			if (buildings.Count > 0)
				location = buildings.Random(squad.Random).Location;
			return location;
		}

		protected static bool BusyAttack(Actor a)
		{
			if (a.IsIdle)
				return false;

			var activity = a.CurrentActivity;
			var type = activity.GetType();
			if (type == typeof(Attack) || type == typeof(FlyAttack))
				return true;

			var next = activity.NextActivity;
			if (next == null)
				return false;

			var nextType = next.GetType();
			if (nextType == typeof(Attack) || nextType == typeof(FlyAttack))
				return true;

			return false;
		}

		protected static bool CanAttackTarget(Actor a, Actor target)
		{
			if (!a.Info.HasTraitInfo<AttackBaseInfo>())
				return false;

			var targetTypes = target.GetEnabledTargetTypes();
			if (!targetTypes.Any())
				return false;

			var arms = a.TraitsImplementing<Armament>();
			foreach (var arm in arms)
				if (arm.Weapon.IsValidTarget(targetTypes))
					return true;

			return false;
		}

		protected virtual Actor FindClosestEnemy(Squad owner)
		{
			return FindClosestEnemy(owner, owner.CenterPosition);
		}

		protected virtual Actor FindClosestEnemy(Squad owner, WPos pos)
		{
			// Closest and ATTACKABLE enemy.
			return owner.World.Actors.Where(owner.Bot.IsEnemyUnit)
				.Where(a => owner.Units.Any(u => CanAttackTarget(u, a)))
				.ClosestTo(pos);
		}

		protected virtual Actor FindClosestEnemy(Squad owner, WPos pos, WDist radius)
		{
			// Closest and ATTACKABLE enemy.
			return owner.World.FindActorsInCircle(pos, radius).Where(owner.Bot.IsEnemyUnit)
				.Where(a => owner.Units.Any(u => CanAttackTarget(u, a)))
				.ClosestTo(pos);
		}

		protected virtual bool ShouldFlee(Squad squad, Func<IEnumerable<Actor>, bool> flee)
		{
			if (!squad.IsValid)
				return false;

			var u = squad.Units.Random(squad.Random);
			var units = squad.World.FindActorsInCircle(u.CenterPosition, WDist.FromCells(DangerRadius)).ToList();
			var ownBaseBuildingAround = units.Where(unit => unit.Owner == squad.Bot.Player && unit.Info.HasTraitInfo<BuildingInfo>());
			if (ownBaseBuildingAround.Any())
				return false;

			var enemyAroundUnit = units.Where(unit => squad.Bot.Player.Stances[unit.Owner] == Stance.Enemy && unit.Info.HasTraitInfo<AttackBaseInfo>());
			if (!enemyAroundUnit.Any())
				return false;

			return flee(enemyAroundUnit);
		}

		// From bulidings, pick ones that aren't in range of defenses.
		List<Actor> UnprotectedBuildings(IEnumerable<Actor> buildings, IEnumerable<Actor> defenses)
		{
			var result = new List<Actor>();

			foreach (var b in buildings)
			{
				bool covered = false;

				foreach (var d in defenses)
				{
					var maxRangeSq = d.TraitsImplementing<Armament>().Min(a => a.MaxRange()).LengthSquared;
					if (d == b || ((b.CenterPosition - d.CenterPosition).LengthSquared < maxRangeSq))
					{
						covered = true;
						break;
					}
				}

				if (!covered)
					result.Add(b);
			}

			return result;
		}

		Dictionary<CPos, int> MakeInfluenceMap(IEnumerable<Actor> defenses, World world)
		{
			var result = new Dictionary<CPos, int>();

			foreach (var d in defenses)
			{
				// Tower ranges can be computed but, nah. Not very useful.
				// We need enough MARGIN (for both practical engagement avoidance and algorithm to work)
				foreach (var t in world.Map.FindTilesInCircle(d.Location, 14))
				{
					if (result.ContainsKey(t))
						result[t] += 1;
					else
						result[t] = 1;
				}
			}

			return result;
		}

		protected virtual List<CPos> FindSafeRoute(Squad owner, IEnumerable<Actor> buildings, IEnumerable<Actor> defenses)
		{
			if (!defenses.Any())
				throw new InvalidProgramException("Bad programmer called FindSafeRoute without any defenses");

			var influenceMap = MakeInfluenceMap(defenses, owner.World);

			// Find a detour.
			var world = owner.World;
			var unit = owner.Units.First(a => a.Info.TraitInfoOrDefault<MobileInfo>() != null);
			var pathFinder = world.WorldActor.Trait<IPathFinder>();
			var mobileInfo = unit.Info.TraitInfo<MobileInfo>();
			DomainIndex domainIndex = world.WorldActor.Trait<DomainIndex>();

			Func<CPos, int> costFunc = loc =>
			{
				if (influenceMap.ContainsKey(loc))
					return 100 * influenceMap[loc]; // 10 doesn't work. 100 works.
				return 1;
			};

			var passable = (uint)mobileInfo.GetMovementClass(world.Map.Rules.TileSet);
			List<CPos> path;
			var search = PathSearch.Search(world, mobileInfo, unit, true,
					loc => domainIndex.IsPassable(unit.Location, loc, mobileInfo, passable)
						&& (unit.Location - loc).LengthSquared < 4)
					.WithCustomCost(costFunc);
			foreach (var b in buildings)
				search = search.FromPoint(b.Location);
			path = pathFinder.FindPath(search);
			search.Dispose();

			path.Reverse();
			return path;
		}

		protected virtual Actor PickLeader(Squad owner)
		{
			// Sometimes Husk gets mixed in for some reason so we can't use MinBy.
			// owner.Units.MinBy(a => a.Info.TraitInfo<MobileInfo>().Speed);

			Actor leader = null;
			int speed = 0;

			foreach (var u in owner.Units)
			{
				var mi = u.Info.TraitInfoOrDefault<MobileInfo>();
				if (mi == null)
					continue;

				if (leader == null || mi.Speed < speed)
				{
					leader = u;
					speed = mi.Speed;
				}
			}

			return leader;
		}
	}
}
