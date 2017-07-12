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
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;
using System.Collections.ObjectModel;

namespace OpenRA.Mods.Common.AI
{
	public enum SquadType { Assault, Air, Rush, Protection, Ships, Escort }

	public class Squad
	{
		readonly List<Actor> units = new List<Actor>();

		public SquadType Type;

		internal World World;
		internal HackyAI Bot;
		internal MersenneTwister Random;

		internal Target Target;
		internal StateMachine FuzzyStateMachine;

		public Squad(HackyAI bot, SquadType type) : this(bot, type, null) { }

		public Squad(HackyAI bot, SquadType type, Actor target)
		{
			Bot = bot;
			World = bot.World;
			Random = bot.Random;
			Type = type;
			Target = Target.FromActor(target);
			FuzzyStateMachine = new StateMachine();

			switch (type)
			{
				case SquadType.Assault:
				case SquadType.Rush:
					FuzzyStateMachine.ChangeState(this, new GroundUnitsIdleState(), true);
					break;
				case SquadType.Air:
					FuzzyStateMachine.ChangeState(this, new AirIdleState(), true);
					break;
				case SquadType.Protection:
					FuzzyStateMachine.ChangeState(this, new UnitsForProtectionIdleState(), true);
					break;
				case SquadType.Escort:
					FuzzyStateMachine.ChangeState(this, new UnitsForProtectionEscortState(), true);
					break;
				case SquadType.Ships:
					FuzzyStateMachine.ChangeState(this, new NavyUnitsIdleState(), true);
					break;
			}
		}

		public void Update()
		{
			if (IsValid)
				FuzzyStateMachine.Update(this);
		}

		public bool IsValid { get { return units.Any(); } }

		public Actor TargetActor
		{
			get { return Target.Actor; }
			set { Target = Target.FromActor(value); }
		}

		public bool IsTargetValid
		{
			get { return Target.IsValidFor(units.FirstOrDefault()) && !Target.Actor.Info.HasTraitInfo<HuskInfo>(); }
		}

		public bool IsTargetVisible
		{
			get { return Bot.Player.PlayerActor.Owner.CanTargetActor(TargetActor); }
		}

		public WPos CenterPosition { get { return units.Select(u => u.CenterPosition).Average(); } }

		public CPos CenterLocation { get { return World.Map.CellContaining(CenterPosition); } }

		public ReadOnlyCollection<Actor> Units { get { return units.AsReadOnly(); } }

		public void AddUnit(Actor unit)
		{
			units.Add(unit);
		}

		public void Disband()
		{
			units.Clear();
		}

		public void RemoveInvalidMembers(Func<Actor, bool> isInvalid)
		{
			units.RemoveAll(a => isInvalid(a));
		}

		void ReflexAvoidance(Actor attacker)
		{
			// Like when you retract your finger when it touches hot stuff,
			// let air untis avoid the attacker very quickly. (faster than flee state's response)
			WVec vec = CenterPosition - attacker.CenterPosition;
			WPos dest = CenterPosition + vec;
			CPos cdest = World.Map.CellContaining(dest);

			foreach (var a in units)
				Bot.QueueOrder(new Order("Move", a, false) { TargetLocation = cdest });
		}

		internal void Damage(AttackInfo e)
		{
			// Friendly fire damage can happen, as weapons have spread damage.
			// Here we don't use AppearsFriendlyTo, to kill the disguised enemy.
			if (Bot.Player.IsAlliedWith(e.Attacker.Owner))
				return;

			if (Type == SquadType.Air)
			{
				// decide flee or retaliate.
				if (AirStateBase.NearToPosSafely(this, this.CenterPosition))
				{
					TargetActor = e.Attacker;
					FuzzyStateMachine.ChangeState(this, new AirAttackState(), true);
					return;
				}

				// Flee
				ReflexAvoidance(e.Attacker);
				FuzzyStateMachine.ChangeState(this, new AirFleeState(), true);
			}
			else if (Type == SquadType.Assault || Type == SquadType.Rush)
			{
				// Return fire, if tue current target actor is a non-attacker.
				if (IsTargetValid && TargetActor.TraitsImplementing<AttackBase>().Count() == 0)
				{
					foreach (var a in units)
					{
						TargetActor = e.Attacker;
						a.CancelActivity();
						Bot.QueueOrder(new Order("Attack", a, false) { TargetActor = e.Attacker });
					}
				}
			}
		}
	}
}
