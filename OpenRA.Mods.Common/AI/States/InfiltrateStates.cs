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
using System;

namespace OpenRA.Mods.Common.AI
{
	abstract class InfiltrateStateBase : StateBase
	{
		protected virtual bool ShouldFlee(Squad owner)
		{
			return false; // Infiltrator shouldn't flee
		}
	}

	class InfiltrateUnitsDetourState : InfiltrateStateBase, IState
	{
		List<CPos> path = new List<CPos>();
		Actor leader;
		readonly int MaxTries = 100;
		int tries = 0;

		public void Activate(Squad owner) { }

		void FindPath(Squad owner)
		{
			// Compute these at activation time and stick with it.
			// If enemy builds more towers, tough.
			tries = 0;

			IEnumerable<Actor> enemyBuildings;
			if (owner.Bot.AttackCenter != null)
				enemyBuildings = owner.World.FindActorsInCircle(
						owner.World.Map.CenterOfCell(owner.Bot.AttackCenter.Value),
						WDist.FromCells(20))
					.Where(b => owner.Bot.IsOwnedByEnemy(b) && !b.IsDead && !b.Disposed);
			else
				enemyBuildings = owner.World.ActorsHavingTrait<Building>().Where(b
					=> owner.Bot.IsOwnedByEnemy(b) && !b.IsDead && !b.Disposed);
			if (!enemyBuildings.Any())
			{
				// We should have won by now, unless, short game.
				return;
			}

			var defenses = enemyBuildings.Where(a => owner.Bot.Info.BuildingCommonNames.Defense.Contains(a.Info.Name));
			if (!defenses.Any())
			{
				// We are in good situation :) No need for path finding.
				return;
			}

			enemyBuildings = enemyBuildings.Where(a => !owner.Bot.Info.BuildingCommonNames.Defense.Contains(a.Info.Name));
			path = FindSafeRoute(owner, enemyBuildings, defenses);
			if (path.Count == 0)
			{
				// Unreachable target by terrain or something. Just attack move in this case.
				return;
			}

			// Remove two path points so that the unit will not try to move "inside" the building.
			path.RemoveAt(0);
			if (path.Any())
				path.RemoveAt(0);

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
			if (!owner.IsValid)
				return;

			// Follow the path computed in Activate step until we run into something.
			if (leader == null || leader.IsDead || leader.Disposed)
				leader = PickLeader(owner);

			if (leader == null)
				return;

			// Make target position further.
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
					owner.Bot.QueueOrder(new Order("Move", u, false) { TargetLocation = p });

				if (tries++ > MaxTries)
					break; // and fall to attack move mode.

				var position = owner.World.Map.CenterOfCell(p);
				var beacon = owner.Bot.Player.PlayerActor.Info.TraitInfo<PlaceBeaconInfo>();
				var playerBeacon = new Effects.Beacon(owner.Bot.Player, position, 10 * 25, beacon.Palette,
					beacon.IsPlayerPalette, beacon.BeaconImage, beacon.ArrowSequence, beacon.CircleSequence);
				owner.Bot.Player.PlayerActor.World.AddFrameEndTask(w => w.Add(playerBeacon));

				return;
			}

			// We are at the destination safely.
			DoAction(owner);
		}

		protected virtual Actor PickTarget(Squad owner)
		{
			// find a capturer in the squad.
			var capturers = owner.Units.Where(a => a.Info.HasTraitInfo<ExternalCapturesInfo>());
			if (!capturers.Any())
				return null;

			var capturer = capturers.First();

			// Find a victim.
			var radius = WDist.FromCells(10);
			var candidates = owner.World.FindActorsInCircle(owner.CenterPosition, radius)
				.Where(b => b.Info.HasTraitInfo<ExternalCapturableInfo>());
			if (!candidates.Any())
				return null;

			// From candidates, find what can be captured.
			candidates = candidates.Where(b => b.Trait<ExternalCapturable>().CanBeTargetedBy(capturer, b.Owner));
			if (!candidates.Any())
				return null;

			return candidates.ClosestTo(capturer);
		}

		// Override this function to make units do something else.
		protected virtual void DoAction(Squad owner)
		{
			if (!owner.IsTargetValid)
				owner.TargetActor = PickTarget(owner);

			if (!owner.IsTargetValid)
			{
				FindPath(owner);
				return;
			}

			foreach (var u in owner.Units)
			{
				if (u.Info.HasTraitInfo<ExternalCapturesInfo>())
					owner.Bot.QueueOrder(new Order("ExternalCaptureActor", u, true) { TargetActor = owner.TargetActor });
				else
					// Yes you can "guard" enemy units (that means, attack it.)
					owner.Bot.QueueOrder(new Order("Guard", u, true) { TargetActor = owner.TargetActor });
			}
		}

		public void Deactivate(Squad bot) { }
	}
}
