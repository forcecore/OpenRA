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

namespace OpenRA.Mods.Common.AI
{
	class UnitsForProtectionIdleState : GroundStateBase, IState
	{
		public void Activate(Squad owner) { }
		public void Tick(Squad owner) { owner.FuzzyStateMachine.ChangeState(owner, new UnitsForProtectionAttackState(), true); }
		public void Deactivate(Squad owner) { }
	}

	class UnitsForProtectionAttackState : GroundStateBase, IState
	{
		public const int BackoffTicks = 4;
		internal int Backoff = BackoffTicks;

		public void Activate(Squad owner) { }

		bool ShouldAttack(Squad owner, Actor target)
		{
			if (owner.IsTargetVisible)
				return true;

			// Is it seige unit?
			if (owner.Bot.IsSeigeUnit(target))
				return true;

			return false;
		}

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (!owner.IsTargetValid)
			{
				owner.TargetActor = FindClosestEnemy(owner, owner.CenterPosition, WDist.FromCells(8));

				if (owner.TargetActor == null)
				{
					owner.FuzzyStateMachine.ChangeState(owner, new UnitsForProtectionFleeState(), true);
					return;
				}
			}

			if (!ShouldAttack(owner, owner.TargetActor))
			{
				if (Backoff < 0)
				{
					owner.FuzzyStateMachine.ChangeState(owner, new UnitsForProtectionFleeState(), true);
					Backoff = BackoffTicks;
					return;
				}

				Backoff--;
			}
			else
			{
				foreach (var a in owner.Units)
					owner.Bot.QueueOrder(new Order("AttackMove", a, false) { TargetLocation = owner.TargetActor.Location });
			}
		}

		public void Deactivate(Squad owner) { }
	}

	class UnitsForProtectionEscortState : GroundStateBase, IState
	{
		Actor referenceEnemy;

		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			if (owner.TargetActor == null || owner.TargetActor.IsDead || owner.TargetActor.Disposed
					|| !owner.TargetActor.AppearsFriendlyTo(owner.Units.First()))
				owner.FuzzyStateMachine.ChangeState(owner, new UnitsForProtectionFleeState(), true);

			var pos = owner.CenterPosition;

			if (referenceEnemy == null || referenceEnemy.IsDead || referenceEnemy.Disposed)
				referenceEnemy = FindClosestEnemy(owner, pos);

			// We won!
			if (referenceEnemy == null)
				return;

			var vec = referenceEnemy.Location - owner.TargetActor.Location;
			if (vec == CVec.Zero)
				return;
			vec = 10 * vec / vec.Length;

			// MCV is closer. Catch up.
			foreach (var a in owner.Units)
				owner.Bot.QueueOrder(new Order("AttackMove", a, false) { TargetLocation = owner.TargetActor.Location + vec });
		}

		public void Deactivate(Squad owner) { owner.Disband(); }
	}

	class UnitsForProtectionFleeState : GroundStateBase, IState
	{
		public void Activate(Squad owner) { }

		public void Tick(Squad owner)
		{
			if (!owner.IsValid)
				return;

			GoToRandomOwnBuilding(owner);
			owner.FuzzyStateMachine.ChangeState(owner, new UnitsForProtectionIdleState(), true);
		}

		public void Deactivate(Squad owner) { owner.Disband(); }
	}
}
