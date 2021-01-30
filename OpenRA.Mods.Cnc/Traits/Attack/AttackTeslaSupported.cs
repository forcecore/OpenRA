#region Copyright & License Information
/*
 * OpenRA License:
 *
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

/*
 * I tried implementing distance vector routing algorithm
 * but now I think it is an overkill,
 * because they take memory for each actor and they need to eachange information.
 * Also, C&C games, people just sell towers when they are done with it so
 * that makes these overheads less worthy.
 */

namespace OpenRA.Mods.Cnc.Traits
{
	[Desc("Implements the charge-then-burst attack logic specific to the RA tesla coil.")]
	public class AttackTeslaSupportedInfo : AttackTeslaInfo
	{
		// These props are inherited automatically:
		/*
		[Desc("How many charges this actor has to attack with, once charged.")]
		public readonly int MaxCharges = 1;

		[Desc("Reload time for all charges (in ticks).")]
		public readonly int ReloadDelay = 120;

		[Desc("Delay for initial charge attack (in ticks).")]
		public readonly int InitialChargeDelay = 22;

		[Desc("Delay between charge attacks (in ticks).")]
		public readonly int ChargeDelay = 3;

		[Desc("Sound to play when actor charges.")]
		public readonly string ChargeAudio = null;
		*/
		[Desc("Max hops the supporters can reach.")]
		public readonly int MaxHops = 3;

		[Desc("Ticks each hop will take for each hop of support weapon to jump across support network")]
		public readonly int TicksPerHop = 1;

		[Desc("Max support an attacker can get.")]
		public readonly int MaxSupportersPerAttacker = 9;

		[Desc("Can support allies too? Instead of just owners?")]
		public readonly bool CanSupportAllies = false;

		[Desc("When supporting, what armament will this actor use?")]
		public readonly string SupportArmament = "support";

		[Desc("Only actors with this trait and the matching SupportType can buff each other.")]
		public readonly string SupportType = "tesla";

		[Desc("When receiving the support, where in this actor will be the hit location?")]
		public readonly WVec ReceiverOffset = WVec.Zero;

		[GrantedConditionReference]
		[Desc("Condition stack to grant upon receiving the buffs.")]
		public readonly string BuffCondition = "tesla-stack";

		public override object Create(ActorInitializer init) { return new AttackTeslaSupported(init.Self, this); }
	}

	public class AttackTeslaSupported : AttackTesla, ITick, INotifyAttack, INotifyBecomingIdle
	{
		readonly AttackTeslaSupportedInfo info;
		readonly Stack<int> buffTokens = new Stack<int>();

		public AttackTeslaSupported(Actor self, AttackTeslaSupportedInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		public string SupportType
		{
			get { return info.SupportType; }
		}

		public WVec ReceiverOffset
		{
			get { return info.ReceiverOffset; }
		}

		void ITick.Tick(Actor self)
		{
			if (--timeToRecharge <= 0)
				charges = info.MaxCharges;
		}

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			--charges;
			timeToRecharge = info.ReloadDelay;
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }

		public override Activity GetAttackActivity(Actor self, AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor = null)
		{
			return new ChargeSupportedAttack(this, newTarget, forceAttack, info, targetLineColor);
		}

		public virtual void FireSupportArmament(Actor self, in Target target, Actor buffReceiver)
		{
			if (!CanAttack(self, target))
				return;

			var receiverTrait = buffReceiver.Trait<AttackTeslaSupported>();
			var offsetedTarget = Target.FromPos(target.CenterPosition + receiverTrait.ReceiverOffset);

			var supportArmament = self.TraitsImplementing<Armament>().First(a => a.Info.Name == info.SupportArmament);
			supportArmament.CheckFire(self, facing, offsetedTarget);

			// Grant the buff condition
			receiverTrait.AddBuffStack(buffReceiver);
		}

		// Check if self can reach target with the support armament.
		public bool CheckSupportRange(Actor self, Actor target)
		{
			var t = Target.FromActor(target);
			var supportArmament = self.TraitsImplementing<Armament>().First(a => a.Info.Name == info.SupportArmament);
			return t.IsInRange(self.CenterPosition, supportArmament.MaxRange());
		}

		void AddBuffStack(Actor self)
		{
			buffTokens.Push(self.GrantCondition(info.BuffCondition));
		}

		void ClearBuffStack(Actor self)
		{
			while (buffTokens.Count > 0)
				self.RevokeCondition(buffTokens.Pop());
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			ClearBuffStack(self);
		}

		class ChargeSupportedAttack : Activity
		{
			readonly AttackTeslaSupported attack;
			readonly Target target;
			readonly bool forceAttack;
			readonly Color? targetLineColor;
			readonly AttackTeslaSupportedInfo supportInfo;

			public ChargeSupportedAttack(
				AttackTeslaSupported attack,
				in Target target,
				bool forceAttack,
				AttackTeslaSupportedInfo supportInfo,
				Color? targetLineColor = null)
			{
				this.attack = attack;
				this.target = target;
				this.forceAttack = forceAttack;
				this.supportInfo = supportInfo;
				this.targetLineColor = targetLineColor;
			}

			// Run BFS rooted at self, to get multi-hop supporters.
			// Returns (supporter, relay, hops) triplets
			public IEnumerable<(Actor, Actor, int)> RecruitSupporters(Actor self)
			{
				var candidates = self.World.ActorsHavingTrait<AttackTeslaSupported>();
				var isVisited = new HashSet<Actor>() { self };
				var hops = new Dictionary<Actor, int>() { { self, 0 } };
				var parent = new Dictionary<Actor, Actor>() { { self, null } };
				var queue = new Queue<Actor>();
				var maxHops = 0;

				queue.Enqueue(self);
				while (queue.Count() > 0)
				{
					var node = queue.Dequeue();
					foreach (var adjacent in GetValidNeighborSupporters(node, candidates))
					{
						if (isVisited.Contains(adjacent))
							continue;

						isVisited.Add(adjacent);
						hops[adjacent] = hops[node] + 1;
						parent[adjacent] = node;

						if (maxHops < hops[adjacent])
							maxHops = hops[adjacent];

						if (isVisited.Count() > supportInfo.MaxSupportersPerAttacker)
						{
							queue.Clear(); // terminate the search
							break;
						}

						if (hops[adjacent] < supportInfo.MaxHops)
							queue.Enqueue(adjacent);
					}
				}

				foreach (var node in isVisited)
				{
					if (node == self)
						continue;

					yield return (node, parent[node], hops[node]);
				}
			}

			public IEnumerable<Actor> GetValidNeighborSupporters(Actor self, IEnumerable<Actor> traitedActors)
			{
				// My guess is that is is more common to have more actors within our range
				// than actors with this trait in the world.
				foreach (var cand in traitedActors)
				{
					if (cand == self)
						continue;

					if (!MaySupport(self, cand))
						continue;

					// Implemented this way as the support may have different armament than self.
					if (cand.Trait<AttackTeslaSupported>().CheckSupportRange(cand, self))
						yield return cand;
				}
			}

			bool IsValidStance(Actor self, Actor otherNode)
			{
				if (self.Owner == otherNode.Owner)
					return true;

				// It might be interesting if neutral actors support everybody hmmm...
				return supportInfo.CanSupportAllies && self.Owner.IsAlliedWith(otherNode.Owner);
			}

			// Check if the other actor may support self,
			// We do all but range check here.
			public bool MaySupport(Actor self, Actor otherNode)
			{
				if (otherNode.IsDead || !otherNode.IsInWorld)
					return false;

				if (!IsValidStance(self, otherNode))
					return false;

				if (otherNode.CurrentActivity != null)
					return false;

				// Check traits
				return supportInfo.SupportType == otherNode.Trait<AttackTeslaSupported>().SupportType;
			}

			public override bool Tick(Actor self)
			{
				if (IsCanceling || !attack.CanAttack(self, target))
					return true;

				if (attack.charges == 0)
					return false;

				foreach (var notify in self.TraitsImplementing<INotifyTeslaCharging>())
					notify.Charging(self, target);

				if (!string.IsNullOrEmpty(attack.info.ChargeAudio))
					Game.Sound.Play(SoundType.World, attack.info.ChargeAudio, self.CenterPosition);

				var maxHops = 0;
				var relays = RecruitSupporters(self); // You need to recruit every time you fire as the battlefield is a very dynamic place.
				if (relays.Any())
				{
					maxHops = relays.MaxBy(x => x.Item3).Item3;
					foreach ((var supporter, var relay, var hop) in relays)
					{
						var attack = supporter.Trait<AttackTeslaSupported>();
						supporter.QueueActivity(new FireSupportingWeapon(attack, Target.FromActor(relay), self, (maxHops - hop) * supportInfo.TicksPerHop, Color.OrangeRed));
					}
				}

				QueueChild(new Wait(attack.info.InitialChargeDelay + maxHops * supportInfo.TicksPerHop));
				QueueChild(new ChargeFireBuffed(attack, target));
				return false;
			}

			public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
			{
				if (targetLineColor != null)
					yield return new TargetLineNode(target, targetLineColor.Value);
			}
		}

		class ChargeAndFireSupportWeapon : Activity
		{
			readonly AttackTeslaSupported attack;
			readonly Target target;
			readonly Actor buffReceiver;

			public ChargeAndFireSupportWeapon(AttackTeslaSupported attack, in Target target, Actor buffReceiver)
			{
				this.attack = attack;
				this.target = target;
				this.buffReceiver = buffReceiver;
			}

			public override bool Tick(Actor self)
			{
				if (IsCanceling || !attack.CanAttack(self, target))
					return true;

				if (attack.charges == 0)
					return true;

				attack.FireSupportArmament(self, target, buffReceiver);

				QueueChild(new Wait(attack.info.ChargeDelay));
				return false;
			}
		}

		class FireSupportingWeapon : Activity
		{
			readonly AttackTeslaSupported attack;
			readonly Target target;
			readonly Actor buffReceiver; // buff receiver is NOT target, as the buff may travel multiple hops.
			readonly int hopDelay;
			readonly Color? targetLineColor;

			public FireSupportingWeapon(AttackTeslaSupported attack, Target target, Actor buffReceiver, int hopDelay, Color? targetLineColor = null)
			{
				this.attack = attack;
				this.target = target;
				this.buffReceiver = buffReceiver;
				this.hopDelay = hopDelay;
				this.targetLineColor = targetLineColor;
			}

			public override bool Tick(Actor self)
			{
				if (IsCanceling || !attack.CanAttack(self, target))
					return true;

				if (buffReceiver.IsIdle)
					return true;

				if (attack.charges == 0)
					return false;

				foreach (var notify in self.TraitsImplementing<INotifyTeslaCharging>())
					notify.Charging(self, target);

				if (!string.IsNullOrEmpty(attack.info.ChargeAudio))
					Game.Sound.Play(SoundType.World, attack.info.ChargeAudio, self.CenterPosition);

				QueueChild(new Wait(attack.info.InitialChargeDelay + hopDelay));
				QueueChild(new ChargeAndFireSupportWeapon(attack, target, buffReceiver));
				return true; // Works only once
			}

			public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
			{
				if (targetLineColor != null)
					yield return new TargetLineNode(target, targetLineColor.Value);
			}
		}

		protected class ChargeFireBuffed : Activity
		{
			readonly AttackTeslaSupported attack;
			readonly Target target;

			public ChargeFireBuffed(AttackTeslaSupported attack, in Target target)
			{
				this.attack = attack;
				this.target = target;
			}

			public override bool Tick(Actor self)
			{
				if (IsCanceling || !attack.CanAttack(self, target))
					return true;

				if (attack.charges == 0)
					return true;

				attack.DoAttack(self, target);
				attack.ClearBuffStack(self);

				QueueChild(new Wait(attack.info.ChargeDelay));
				return false;
			}
		}
	}
}
