﻿#region Copyright & License Information
/*
 * Dock client module by Boolbada of OP Mod.
 *
 * OpenRA Copyright info:
 *
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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class DockClientInfo : ITraitInfo
	{
		public object Create(ActorInitializer init) { return new DockClient(init, this); }
	}

	public enum DockState
	{
		NotAssigned,
		WaitAssigned,
		ServiceAssigned
	}

	// When dockmanager manages docked units, these units require dock client trait.
	public class DockClient : INotifyKilled, INotifyBecomingIdle, INotifyActorDisposing, INotifyOwnerChanged, IResolveOrder
	{
		// readonly DockClientInfo info;
		readonly Actor self;
		public Dock CurrentDock;
		public DockState DockState = DockState.NotAssigned;
		public IDockActivity Requester; // The activity that requested dock.

		int acquireTimeStamp = -1;

		public DockClient(ActorInitializer init, DockClientInfo info)
		{
			// this.info = info;
			self = init.Self;
		}

		public void Acquire(Dock dock, DockState dockState)
		{
			// You are to acquire only when you don't have one.
			// i.e., release first.
			Release(CurrentDock);

			System.Diagnostics.Debug.Assert(CurrentDock == null, "To acquire dock, release first.");
			dock.Reserver = self;
			CurrentDock = dock;
			DockState = dockState;

			acquireTimeStamp = self.World.WorldTick;
		}

		public void Release(Dock dock)
		{
			// You are to release only what you have.
			if (dock != null && CurrentDock != null && CurrentDock != dock)
				System.Diagnostics.Debug.Assert(dock == CurrentDock, "To release, you must have it first.");

			CurrentDock = null;
			DockState = DockState.NotAssigned;
			acquireTimeStamp = -1;

			if (dock != null)
				dock.Reserver = null;
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			Release(CurrentDock);
		}

		void INotifyBecomingIdle.OnBecomingIdle(Actor self)
		{
			Release(CurrentDock);
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			Release(CurrentDock);
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			Release(CurrentDock);
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.Queued)
				return;

			switch (order.OrderString)
			{
				case "Enter":
				case "Deliver":
				case "ReturnToBase":
				case "Repair":
					// Prevent race condition.
					// i.e., other order acquires the dock then
					// this gets evaled and releases it! Not good.
					break;
				default:
					Release(CurrentDock);
					break;
			}
		}

		public bool WaitedLong(int threshold)
		{
			if (acquireTimeStamp < 0)
				return false;
			return (self.World.WorldTick - acquireTimeStamp) >= threshold;
		}
	}
}
