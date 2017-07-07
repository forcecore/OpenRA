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

namespace OpenRA.Mods.Common.AI
{
	abstract class NavyStateBase : StateBase
	{
		protected virtual bool ShouldFlee(Squad owner)
		{
			return base.ShouldFlee(owner, enemies => !AttackOrFleeFuzzy.Default.CanAttack(owner.Units, enemies));
		}

		protected Actor FindClosestEnemy(Squad owner)
		{
			if (!owner.IsValid)
				return null;

			// For ships, cheat and move towards enemy naval production, if any.
			var t = owner.World.Actors.Where(a
				=> owner.Bot.Info.BuildingCommonNames.NavalProduction.Contains(a.Info.Name)
				&& a.AppearsHostileTo(owner.Bot.Player.PlayerActor)).FirstOrDefault();

			// If naval yard is too far away, return it.
			// Else, FindClosest below will find suitable enemy targets :)
			if (t != null && (t.Location - owner.Units.First().Location).LengthSquared > 20 * 20)
				return t;

			return owner.Bot.FindClosestEnemy(owner.Units.FirstOrDefault().CenterPosition);
		}
	}

	class NavyUnitsIdleState : NavyStateBase, IState
	{
		public void Activate(Squad owner) { }

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
				.Where(unit => owner.Bot.Player.Stances[unit.Owner] == Stance.Enemy).ToList();

			if (enemyUnits.Any())
			{
				if (AttackOrFleeFuzzy.Default.CanAttack(owner.Units, enemyUnits))
				{
					foreach (var u in owner.Units)
						owner.Bot.QueueOrder(new Order("AttackMove", u, false) { TargetLocation = owner.TargetActor.Location });

					// We have gathered sufficient units. Attack the nearest enemy unit.
					owner.FuzzyStateMachine.ChangeState(owner, new NavyUnitsAttackMoveState(), true);

					// start log
					/*
					owner.Bot.Send("MINE");
					owner.Bot.Send(owner.Bot.Player.InternalName);
					foreach (var u in owner.Units)
						owner.Bot.Send(u.Info.Name);
					owner.Bot.Send("END");

					var enemy = owner.World.Players.Where(p => p.InternalName.ToLower().StartsWith("multi") && p != owner.Bot.Player).First();
					owner.Bot.Send("ENEMY");
					owner.Bot.Send(enemy.InternalName);
					foreach (var a in owner.Bot.World.Actors.Where(x => x.Owner == enemy))
						owner.Bot.Send(a.Info.Name);
					owner.Bot.Send("END");

					LogBattle(owner, "B4B");
					*/
					return;
				}
				else
					owner.FuzzyStateMachine.ChangeState(owner, new NavyUnitsFleeState(), true);
			}
		}

		public void Deactivate(Squad owner) { }
	}

	class NavyUnitsAttackMoveState : NavyStateBase, IState
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
					owner.FuzzyStateMachine.ChangeState(owner, new NavyUnitsFleeState(), true);
					return;
				}
			}

			var leader = owner.Units.ClosestTo(owner.TargetActor.CenterPosition);
			if (leader == null)
				return;
			var ownUnits = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(owner.Units.Count) / 3)
				.Where(a => a.Owner == leader.Owner && owner.Units.Contains(a)).ToHashSet();
			if (ownUnits.Count < owner.Units.Count)
			{
				owner.Bot.QueueOrder(new Order("Stop", leader, false));
				foreach (var unit in owner.Units.Where(a => !ownUnits.Contains(a)))
					owner.Bot.QueueOrder(new Order("AttackMove", unit, false) { TargetLocation = leader.Location });
			}
			else
			{
				var enemies = owner.World.FindActorsInCircle(leader.CenterPosition, WDist.FromCells(12))
					.Where(a1 => !a1.Disposed && !a1.IsDead);
				var enemynearby = enemies.Where(a1 => a1.Info.HasTraitInfo<ITargetableInfo>() && owner.Bot.IsOwnedByEnemy(a1));
				var target = enemynearby.ClosestTo(leader.CenterPosition);
				if (target != null)
				{
					owner.TargetActor = target;
					owner.FuzzyStateMachine.ChangeState(owner, new NavyUnitsAttackState(), true);
					return;
				}
				else
					foreach (var a in owner.Units)
						owner.Bot.QueueOrder(new Order("AttackMove", a, false) { TargetLocation = owner.TargetActor.Location });
			}

			// Wiped out
			//if (!owner.IsValid)
			//	LogBattle(owner, "AFTERB");

			if (ShouldFlee(owner))
			{
				//LogBattle(owner, "AFTERB");
				owner.FuzzyStateMachine.ChangeState(owner, new NavyUnitsFleeState(), true);
				return;
			}
		}

		public void Deactivate(Squad owner) { }
	}

	class NavyUnitsAttackState : NavyStateBase, IState
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
					owner.FuzzyStateMachine.ChangeState(owner, new NavyUnitsFleeState(), true);
					return;
				}
			}

			// Switch target durign fight
			var targetActor = owner.Bot.FindClosestEnemy(owner.Units.First().CenterPosition);
			foreach (var a in owner.Units)
				if (!BusyAttack(a))
					owner.Bot.QueueOrder(new Order("Attack", a, false) { TargetActor = targetActor });

			// Wiped out
			//if (!owner.IsValid)
			//	LogBattle(owner, "AFTERB");

			if (ShouldFlee(owner))
			{
				//LogBattle(owner, "AFTERB");
				owner.FuzzyStateMachine.ChangeState(owner, new NavyUnitsFleeState(), true);
				return;
			}
		}

		public void Deactivate(Squad owner) { }
	}

	class NavyUnitsFleeState : NavyStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			GoToRandomOwnBuilding(owner);
			owner.FuzzyStateMachine.ChangeState(owner, new NavyUnitsIdleState(), true);
		}

		public void Deactivate(Squad owner) { owner.Units.Clear(); }
	}
}
