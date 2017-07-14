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
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.AI
{
	public sealed class DummyAIInfo : ITraitInfo, IBotInfo
	{
		[Desc("Ingame name this bot uses.")]
		public readonly string Name = "Unnamed Bot";

		string IBotInfo.Name { get { return Name; } }

		public object Create(ActorInitializer init) { return new DummyAI(this); }
	}

	public sealed class DummyAI : IBot
	{
		readonly DummyAIInfo info;
		public bool Enabled { get; private set; }

		public DummyAI(DummyAIInfo info)
		{
			this.info = info;
		}

		void IBot.Activate(Player p)
		{
			Enabled = true;
		}

		public void QueueOrder(Order order)
		{
			throw new NotImplementedException();
		}

		public bool IsStaticAntiAir(Actor a)
		{
			throw new NotImplementedException();
		}

		public bool IsEnemyUnit(Actor a)
		{
			throw new NotImplementedException();
		}

		public bool IsStaticDefense(Actor a)
		{
			throw new NotImplementedException();
		}

		public bool IsSeigeUnit(Actor a)
		{
			throw new NotImplementedException();
		}

		public bool IsFriendlyUnitTargeter(Actor a)
		{
			throw new NotImplementedException();
		}

		public bool IsNavalBuilding(string name)
		{
			throw new NotImplementedException();
		}

		public bool IsNavalBuilding(Actor a)
		{
			throw new NotImplementedException();
		}

		public CPos GetRandomBaseCenter()
		{
			throw new NotImplementedException();
		}

		IBotInfo IBot.Info { get { return info; } }

		public Player Player
		{
			get
			{
				throw new NotImplementedException();
			}
		}

		public MersenneTwister Random
		{
			get
			{
				throw new NotImplementedException();
			}
		}

		public int GetNextSuperweaponScanTime
		{
			get
			{
				throw new NotImplementedException();
			}
		}

		public CPos? AttackCenter
		{
			get
			{
				throw new NotImplementedException();
			}
		}

		public World World
		{
			get
			{
				throw new NotImplementedException();
			}
		}

		public int MaxBaseRadius
		{
			get
			{
				throw new NotImplementedException();
			}
		}
	}
}
