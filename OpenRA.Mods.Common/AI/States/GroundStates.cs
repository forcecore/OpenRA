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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using System.Collections.Generic;
using OpenRA.Mods.Common.Pathfinder;
using System;

namespace OpenRA.Mods.Common.AI
{
	abstract class GroundStateBase : StateBase
	{
		protected virtual bool ShouldFlee(Squad owner)
		{
			return base.ShouldFlee(owner, enemies => !AttackOrFleeFuzzy.Default.CanAttack(owner.Units, enemies));
		}
	}

	class GroundUnitsIdleState : GroundStateBase, IState
	{
		public void Activate(Squad owner)
		{
			// Let's choose enemy to attack.
		}

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (!owner.IsTargetValid)
			{
				var t = FindClosestEnemy(owner);
				if (t == null) return;
				owner.TargetActor = t;
			}

			var enemyUnits = owner.World.FindActorsInCircle(owner.TargetActor.CenterPosition, WDist.FromCells(10))
				.Where(unit => owner.Bot.IsEnemyUnit(unit)).ToList();

			if (enemyUnits.Any())
			{
				if (AttackOrFleeFuzzy.Default.CanAttack(owner.Units, enemyUnits))
				{
					// alternative attack path.
					//if (owner.Random.Next(4) == 0)
					// I'll just use detour all the time haha!

					owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsDetourState(), true);
					return;

					/*
					foreach (var u in owner.Units)
						owner.Bot.QueueOrder(new Order("AttackMove", u, false) { TargetLocation = owner.TargetActor.Location });

					// We have gathered sufficient units. Attack the nearest enemy unit.
					owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackMoveState(), true);
					return;
					*/
				}
				else
					owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState(), true);
			}
		}

		public void Deactivate(Squad owner) { }
	}

	class GroundUnitsDetourState : GroundStateBase, IState
	{
		List<CPos> path;
		Actor leader;
		readonly int MaxTries = 100;
		int tries = 0;

		public void Activate(Squad owner)
		{
			// Compute these at activation time and stick with it.
			// If enemy builds more towers, tough.
			tries = 0;

			IEnumerable<Actor> enemyBuildings;
			if (owner.Bot.AttackCenter != null)
				enemyBuildings = owner.World.FindActorsInCircle(
						owner.World.Map.CenterOfCell(owner.Bot.AttackCenter.Value),
						WDist.FromCells(20))
					.Where(owner.Bot.IsEnemyUnit);
			else
				enemyBuildings = owner.World.ActorsHavingTrait<Building>().Where(owner.Bot.IsEnemyUnit);
			if (!enemyBuildings.Any())
			{
				// We should have won by now, unless, short game.
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackMoveState(), true);
				return;
			}

			var defenses = enemyBuildings.Where(owner.Bot.IsStaticDefense);
			if (!defenses.Any())
			{
				// We are in good situation :)
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackMoveState(), true);
				return;
			}

			enemyBuildings = enemyBuildings.Where(b => !owner.Bot.IsStaticDefense(b));
			path = FindSafeRoute(owner, enemyBuildings, defenses);
			if (path.Count == 0)
			{
				// Unreachable target by terrain or something. Just attack move in this case.
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackMoveState(), true);
				return;
			}

			// Remove the paths in our base so that forces will move out first.
			var ourBuildings = owner.World.ActorsHavingTrait<Building>().Where(b => b.Owner == owner.Bot.Player);
			while (path.Count > 0)
			{
				var p = path.Last();
				if ((ourBuildings.ClosestTo(owner.World.Map.CenterOfCell(p)).Location - p).LengthSquared <= 100)
					path.RemoveAt(path.Count - 1);
				else
					break;
			}

			// Use beacon to show path haha
			foreach (var p in path)
			{
				var position = owner.World.Map.CenterOfCell(p);
				var beacon = owner.Bot.Player.PlayerActor.Info.TraitInfo<PlaceBeaconInfo>();
				var playerBeacon = new Effects.Beacon(owner.Bot.Player, position, 10 * 25, beacon.Palette,
					beacon.IsPlayerPalette, beacon.BeaconImage, beacon.ArrowSequence, beacon.CircleSequence);
				owner.Bot.Player.PlayerActor.World.AddFrameEndTask(w => w.Add(playerBeacon));
			}
		}

		public void Tick(Squad owner)
		{
			// Follow the path computed in Activate step until we run into something.
			if (leader == null || leader.IsDead || leader.Disposed)
				leader = PickLeader(owner);

			if (leader == null)
				return;

			// Scan nearby enemies and if they are near, switch to attack mode.
			var enemies = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(5))
				.Where(a1 => !a1.Disposed && !a1.IsDead);
			var enemynearby = enemies.Where(owner.Bot.IsStaticDefense);
			if (enemynearby.Any(a => a.Info.TraitInfoOrDefault<AircraftInfo>() == null)) // must be non-aircraft enemy.
			{
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackState(), true);
				return;
			}

			// As for leader, move along the path.
			// The leader can move this far (at best) until next update.
			// Erm... This makes units clutter. Make target position further.
			WDist radius = WDist.FromCells(5);

			while (path.Count > 0)
			{
				// pop until far enough point found.
				var p = path.Last();

				// Are the teams flocked around the waypoint?
				if ((leader.World.Map.CenterOfCell(p) - leader.CenterPosition).LengthSquared < radius.LengthSquared)
				{
					path.RemoveAt(path.Count - 1);
					tries = 0;
					continue;
				}

				// Move to the next waypoint.
				foreach (var u in owner.Units)
					owner.Bot.QueueOrder(new Order("AttackMove", u, false) { TargetLocation = p });

				if (tries++ > MaxTries)
					break; // and fall to attack move mode.

				var position = owner.World.Map.CenterOfCell(p);
				var beacon = owner.Bot.Player.PlayerActor.Info.TraitInfo<PlaceBeaconInfo>();
				var playerBeacon = new Effects.Beacon(owner.Bot.Player, position, 10 * 25, beacon.Palette,
					beacon.IsPlayerPalette, beacon.BeaconImage, beacon.ArrowSequence, beacon.CircleSequence);
				owner.Bot.Player.PlayerActor.World.AddFrameEndTask(w => w.Add(playerBeacon));

				return;
			}

			// We are at the destination safely. (What? No enemy encounter?)
			owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackMoveState(), false);
		}

		public void Deactivate(Squad bot) { }
	}

	class GroundUnitsGatherState : GroundStateBase, IState
	{
		public Actor Leader;

		public void Activate(Squad bot) { }

		public void Deactivate(Squad bot) { }

		public static bool IsGrouppedWell(Squad owner, Actor leader)
		{
			IEnumerable<Actor> tmp;
			return IsGrouppedWell(owner, leader, out tmp);
		}

		public static bool IsGrouppedWell(Squad owner, Actor leader, out IEnumerable<Actor> membersNearBy)
		{
			var membersNear = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(Math.Max(5, owner.Units.Count)) / 3)
				.Where(a => a.Owner == leader.Owner && owner.Units.Contains(a)).ToHashSet();
			membersNearBy = membersNear;
			return membersNear.Count >= 3 * owner.Units.Count / 4;
		}

		public void Tick(Squad owner)
		{
			IEnumerable<Actor> membersNearBy;
			if (IsGrouppedWell(owner, Leader, out membersNearBy))
			{
				owner.FuzzyStateMachine.RevertToPreviousState(owner, false);
				return;
			}

			owner.Bot.QueueOrder(new Order("Stop", Leader, false));
			foreach (var unit in owner.Units.Where(a => !membersNearBy.Contains(a)))
				owner.Bot.QueueOrder(new Order("AttackMove", unit, false) { TargetLocation = Leader.Location });
		}
	}

	class GroundUnitsAttackMoveState : GroundStateBase, IState
	{
		public void Activate(Squad owner) { }

		CPos? FindSeigePoint(Squad owner, Actor target)
		{
			var world = owner.World;
			var seige = owner.Units.First(owner.Bot.IsSeigeUnit);
			var maxRangeSq = seige.TraitsImplementing<Armament>().Min(a => a.MaxRange()).LengthSquared;
			var pathFinder = world.WorldActor.Trait<IPathFinder>();
			var mobileInfo = seige.Info.TraitInfo<MobileInfo>();
			DomainIndex domainIndex = world.WorldActor.Trait<DomainIndex>();

			// Maybe we are already within range. Then tall the squad to attack move by returning the target pos.
			if ((target.CenterPosition - seige.CenterPosition).LengthSquared < maxRangeSq)
				return target.Location;

			// Find seige point
			var passable = (uint)mobileInfo.GetMovementClass(world.Map.Rules.TileSet);
			List<CPos> path;
			using (var search = PathSearch.Search(world, mobileInfo, seige, true,
					loc => domainIndex.IsPassable(seige.Location, loc, mobileInfo, passable)
						&& (target.CenterPosition - world.Map.CenterOfCell(loc)).LengthSquared <= maxRangeSq)
					.FromPoint(seige.Location))
				path = pathFinder.FindPath(search);

			if (path.Count > 0)
				return path[0];

			return null;
		}

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (!owner.IsTargetValid)
			{
				var t = FindClosestEnemy(owner);
				if (t != null)
					owner.TargetActor = t;
				else
				{
					owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState(), true);
					return;
				}
			}

			// Check if the squad is in good formation.
			var leader = owner.Units.ClosestTo(owner.TargetActor.CenterPosition);
			if (leader == null)
				return;

			// Squad units are scattered too much. Gather around.
			if (!GroundUnitsGatherState.IsGrouppedWell(owner, leader))
			{
				var gather = new GroundUnitsGatherState();
				gather.Leader = leader;
				owner.FuzzyStateMachine.ChangeState(owner, gather, true);
				goto exit;
			}

			// Let's check enemy status, near this squad.
			var enemies = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(10))
				.Where(a1 => !a1.Disposed && !a1.IsDead);
			var enemynearby = enemies.Where(owner.Bot.IsEnemyUnit);
			var target = enemynearby.ClosestTo(leader.CenterPosition);
			if (target == null)
				goto exit_with_attack_move;

			// There's enemy near enough to this squad. Start attacking.

			// Do they have base defenses?
			if (!enemies.Any(owner.Bot.IsStaticDefense))
			{
				// Nope. Engage!
				owner.TargetActor = target;
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsAttackState(), true);
				return;
			}

			if (enemies.Count() <= 4)
			{
				// Don't bother seige. We can overwhelm them, unless we have a very OP defense in some crazy mod.
				// In case of such mod, it is the fun of the player to see units get busted :)
			}
			else if (owner.Units.Any(owner.Bot.IsSeigeUnit))
			{
				// Fortunately, we got seige units. Attack move into range of the seige unit.
				// Don't bother finding the defense structure. It's enought to move in and attack any target.
				var seigePos = FindSeigePoint(owner, owner.TargetActor);
				if (seigePos != null)
				{
					foreach (var unit in owner.Units)
						owner.Bot.QueueOrder(new Order("AttackMove", unit, false) { TargetLocation = seigePos.Value });

					goto exit;
				}

				// Maybe there's no path, when the map is an island or something.
				// In that case, do nothing here, as it used to be.
			}

			// No enemy near enough. Keep on attack moving.
			exit_with_attack_move:
			foreach (var a in owner.Units)
				owner.Bot.QueueOrder(new Order("AttackMove", a, false) { TargetLocation = owner.TargetActor.Location });

			// Wiped out
			//if (!owner.IsValid)
			//	LogBattle(owner, "AFTERB");

			exit:
			if (ShouldFlee(owner))
			{
				//LogBattle(owner, "AFTERB");
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState(), true);
			}
		}

		public void Deactivate(Squad owner) { }
	}

	class GroundUnitsAttackState : GroundStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (!owner.IsTargetValid)
			{
				var t = FindClosestEnemy(owner);
				if (t != null)
					owner.TargetActor = t;
				else
				{
					owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState(), true);
					return;
				}
			}

			// Switch target durign fight
			var targetActor = FindClosestEnemy(owner);
			foreach (var a in owner.Units)
				if (!BusyAttack(a))
				{
					if (!a.TraitsImplementing<Armament>().Any()) // no weapon, like mgg, mrj
						owner.Bot.QueueOrder(new Order("AttackMove", a, false) { TargetLocation = targetActor.Location });
					else if (owner.Bot.IsFriendlyUnitTargeter(a))
						owner.Bot.QueueOrder(new Order("AttackMove", a, false) { TargetLocation = targetActor.Location });
					else
						owner.Bot.QueueOrder(new Order("Attack", a, false) { TargetActor = targetActor });
				}

			// Wiped out
			//if (!owner.IsValid)
			//	LogBattle(owner, "AFTERB");

			if (ShouldFlee(owner))
			{
				//LogBattle(owner, "AFTERB");
				owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsFleeState(), true);
				return;
			}
		}

		public void Deactivate(Squad owner) { }
	}

	class GroundUnitsFleeState : GroundStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			GoToRandomOwnBuilding(owner);
			owner.FuzzyStateMachine.ChangeState(owner, new GroundUnitsIdleState(), true);
		}

		public void Deactivate(Squad owner) { owner.Disband(); }
	}
}
