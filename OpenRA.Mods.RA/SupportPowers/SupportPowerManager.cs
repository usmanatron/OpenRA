#region Copyright & License Information
/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */
#endregion

using System.Linq;
using OpenRA.Mods.RA.Buildings;
using OpenRA.Traits;
using System.Collections.Generic;
using OpenRA.Graphics;

namespace OpenRA.Mods.RA
{
	public class SupportPowerManagerInfo : ITraitInfo
	{
		public object Create(ActorInitializer init) { return new SupportPowerManager(init); }
	}

	public class SupportPowerManager : ITick, IResolveOrder
	{
		public readonly Actor self;
		public Dictionary<string, SupportPowerInstance> Powers = new Dictionary<string, SupportPowerInstance>();

		public SupportPowerManager(ActorInitializer init)
		{
			self = init.self;
			
			init.world.ActorAdded += ActorAdded;
			init.world.ActorRemoved += ActorRemoved;
		}
		
		void ActorAdded(Actor a)
		{
			if (a.Owner != self.Owner || !a.HasTrait<SupportPower>())
				return;
			
			foreach (var t in a.TraitsImplementing<SupportPower>())
			{
				var key = (t.Info.AllowMultiple) ? t.Info.OrderName+"_"+a.ActorID : t.Info.OrderName;
				
				if (Powers.ContainsKey(key))
				{
					Powers[key].Instances.Add(t);
				}
				else
				{
					var si = new SupportPowerInstance(key, this)
					{
						Instances = new List<SupportPower>() { t },
						RemainingTime = t.Info.ChargeTime * 25,
						TotalTime = t.Info.ChargeTime * 25,
					};
					
					Powers.Add(key, si);
				}
			}
		}
		
		void ActorRemoved(Actor a)
		{
			if (a.Owner != self.Owner || !a.HasTrait<SupportPower>())
				return;
			
			foreach (var t in a.TraitsImplementing<SupportPower>())
			{
				var key = (t.Info.AllowMultiple) ? t.Info.OrderName+"_"+a.ActorID : t.Info.OrderName;
				Powers[key].Instances.Remove(t);
				if (Powers[key].Instances.Count == 0)
					Powers.Remove(key);
			}
		}
		
		public void Tick(Actor self)
		{
			foreach(var power in Powers.Values)
				power.Tick();
		}
			
		public void ResolveOrder(Actor self, Order order)
		{
			// order.OrderString is the key of the support power
			if (Powers.ContainsKey(order.OrderString))
				Powers[order.OrderString].Activate(order);
		}
		
		public void Target(string key)
		{
			if (Powers.ContainsKey(key))
				Powers[key].Target();
		}
		
		public class SupportPowerInstance
		{
			SupportPowerManager Manager;
			string Key;
			
			public List<SupportPower> Instances;
			public int RemainingTime;
			public int TotalTime;
			public bool Active;
			public bool Disabled;
			
			public SupportPowerInfo Info { get { return Instances.First().Info; } }
			public bool Ready { get { return Active && RemainingTime == 0; } }
			
			public SupportPowerInstance(string key, SupportPowerManager manager)
			{
				Manager = manager;
				Key = key;
			}
			
			bool notifiedCharging;
			bool notifiedReady;
			public void Tick()
			{
				Active = !Disabled && Instances.Any(i => !i.self.TraitsImplementing<IDisable>().Any(d => d.Disabled));
				var power = Instances.First();

				if (Active)
				{
					if (RemainingTime > 0) --RemainingTime;
					if (!notifiedCharging)
					{
						power.Charging(power.self, Key);
						notifiedCharging = true;
					}
				}
				
				if (RemainingTime == 0
					&& !notifiedReady)
				{
					power.Charged(power.self, Key);
					notifiedReady = true;
				}
			}
			
			public void Target()
			{
				if (!Ready)
					return;

				Manager.self.World.OrderGenerator = Instances.First().OrderGenerator(Key, Manager);
			}
						
			public void Activate(Order order)
			{
				if (!Ready)
					return;

				var power = Instances.First();
				// Note: order.Subject is the *player* actor
				power.Activate(power.self, order);
				RemainingTime = TotalTime;
				notifiedCharging = notifiedReady = false;
				
				if (Info.OneShot)
					Disabled = true;
			}
		}
	}
	
	public class SelectGenericPowerTarget : IOrderGenerator
	{
		readonly SupportPowerManager manager;
		readonly string order;
		readonly string cursor;
		readonly MouseButton expectedButton;

		public SelectGenericPowerTarget(string order, SupportPowerManager manager, string cursor, MouseButton button)
		{
			this.manager = manager;
			this.order = order;
			this.cursor = cursor;
			expectedButton = button;
		}

		public IEnumerable<Order> Order(World world, int2 xy, MouseInput mi)
		{
			world.CancelInputMode();				
			if (mi.Button == expectedButton && world.Map.IsInMap(xy))
				yield return new Order(order, manager.self, false) { TargetLocation = xy };
		}

		public virtual void Tick(World world)
		{
			// Cancel the OG if we can't use the power
			if (!manager.Powers.ContainsKey(order))
				world.CancelInputMode();
		}
				
		public void RenderBeforeWorld(WorldRenderer wr, World world) { }
		public void RenderAfterWorld(WorldRenderer wr, World world) { }
		public string GetCursor(World world, int2 xy, MouseInput mi) { return world.Map.IsInMap(xy) ? cursor : "generic-blocked"; }
	}
}
