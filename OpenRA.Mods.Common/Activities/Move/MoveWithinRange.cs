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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	public class MoveWithinRange : MoveAdjacentTo
	{
		readonly WDist maxRange;
		readonly WDist minRange;

		public MoveWithinRange(Actor self, Target target, WDist minRange, WDist maxRange)
			: base(self, target)
		{
			this.minRange = minRange;
			this.maxRange = maxRange;
		}

		protected override bool ShouldStop(Actor self, CPos oldTargetPosition)
		{
			// We are now in range. Don't move any further!
			// HACK: This works around the pathfinder not returning the shortest path
			return AtCorrectRange(self.CenterPosition) && Mobile.CanInteractWithGroundLayer(self);
		}

		protected override bool ShouldRepath(Actor self, CPos oldTargetPosition)
		{
			return targetPosition != oldTargetPosition && (!AtCorrectRange(self.CenterPosition)
				|| !Mobile.CanInteractWithGroundLayer(self));
		}

		protected override IEnumerable<CPos> CandidateMovementCells(Actor self)
		{
			var map = self.World.Map;
			var maxCells = (maxRange.Length + 1023) / 1024;
			var minCells = minRange.Length / 1024;

			if (minCells != 0 && Target.IsInRange(self.CenterPosition, minRange))
			{
				return map.FindTilesInAnnulus(targetPosition, minCells, maxCells)
					.Where(c => AtCorrectRange(map.CenterOfCell(c)) // With only this, seige weapons CHARGE to the target. Annoying.
					&& CVec.Dot(c - self.Location, targetPosition - self.Location) < 0);
			}

			// AtCorrectRange(map.CenterOfCell(c)) will return the current cell if the cell center is in range,
			// even if the actor is actually out of range due to its current subcell not being the center.
			// Avoid that by not including the current Location if the current actor.CenterPosition is out of range.
			var ignoreCurrentCell = !AtCorrectRange(self.CenterPosition);

			return map.FindTilesInAnnulus(targetPosition, minCells, maxCells)
				.Where(c => !(ignoreCurrentCell && c == self.Location) && AtCorrectRange(map.CenterOfCell(c)));
		}

		bool AtCorrectRange(WPos origin)
		{
			return Target.IsInRange(origin, maxRange) && !Target.IsInRange(origin, minRange);
		}
	}
}
