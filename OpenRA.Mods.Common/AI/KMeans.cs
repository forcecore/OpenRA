using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Common.AI
{
	public class KMeans
	{
		// Given an actor, find the closest centroid and return the index.
		static int ClosestCentroid(Actor a, List<WPos> centroids)
		{
			int bestIndex = -1;
			long bestDist = 0;

			for (int i = 0; i < centroids.Count(); i++)
			{
				var dist = (centroids[i] - a.CenterPosition).HorizontalLengthSquared;
				if (bestIndex == -1 || bestDist < dist)
				{
					bestDist = dist;
					bestIndex = i;
				}
			}

			return bestIndex;
		}

		static List<WPos> ComputeNewCentroids(List<Actor> actors, int[] cluster, int k)
		{
			int[] x = new int[k];
			int[] y = new int[k];
			int[] cnt = new int[k];

			for (int i = 0; i < actors.Count(); i++)
			{
				var pos = actors[i].CenterPosition;
				x[cluster[i]] += pos.X;
				y[cluster[i]] += pos.Y;
				cnt[cluster[i]]++;
			}

			for (int i = 0; i < k; i++)
			{
				// I assume cnt[i] can't be zero! It can't be! I chose an actor as a starting point so... Hopefully it will hold.
				x[i] /= cnt[i];
				y[i] /= cnt[i];
			}

			var result = new List<WPos>();
			for (int i = 0; i < k; i++)
				result.Add(new WPos(x[i], y[i], 0));

			return result;
		}

		static List<List<Actor>> MakeListOfList(List<Actor> actors, int[] cluster, int k)
		{
			var result = new List<List<Actor>>();
			for (int i = 0; i < k; i++)
				result.Add(new List<Actor>());

			for (int i = 0; i < actors.Count(); i++)
				result[cluster[i]].Add(actors[i]);

			return result;
		}

		// Assuming actor is already filtered by some criterion, cluster them into k groups by their locations.
		public static List<List<Actor>> ClusterActors(List<Actor> actors, int k, out List<WPos> centroids)
		{
			// Let's say given i'th actor in the actors list.
			// cluster[i] == current cluster the actor belongs to.
			int[] cluster = new int[actors.Count()];

			// Base case I'd say. Too few units to cluster I guess.
			// Probably have won the game already?
			if (actors.Count() <= k)
				k = 1;

			// Initialize with "random" centroids.
			int step = actors.Count() / k;
			List<WPos> _centroids = new List<WPos>();
			for (int i = 0; i < k; i++)
				_centroids.Add(actors[i * step].CenterPosition);

			for (;;)
			{
				bool dirty = false;

				for (int i = 0; i < actors.Count(); i++)
				{
					// Find closest centroid.
					var a = actors[i];
					int belongsTo = ClosestCentroid(a, _centroids);

					if (belongsTo != cluster[i])
						dirty = true;
					cluster[i] = belongsTo;
				}

				if (dirty)
					_centroids = ComputeNewCentroids(actors, cluster, k);
				else
					break;
			}

			centroids = _centroids;
			return MakeListOfList(actors, cluster, k);
		}
	}
}