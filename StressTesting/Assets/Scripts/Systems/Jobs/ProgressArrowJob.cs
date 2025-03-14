using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using Unity.Burst;

//[ComputeJobOptimization]
[BurstCompile]
public struct ProgressArrowJob : IJobParallelFor
{
	public NativeArray<ArrowData> arrows;
	[ReadOnly]
	public NativeArray<Entity> arrowEntities;

	[ReadOnly]
	public NativeParallelMultiHashMap<int, int> buckets;

	[ReadOnly]
	public NativeArray<UnitTransformData> allMinionTransforms;
	[ReadOnly]
	public NativeArray<MinionBitmask> minionConstData;

	
	public NativeQueue<AttackCommand>.ParallelWriter AttackCommands;
	public NativeQueue<Entity>.ParallelWriter queueForKillingEntities;

	public NativeArray<RaycastCommand> raycastCommands;

	[ReadOnly]
	public NativeArray<Entity> minionEntities;

	[ReadOnly]
	public float dt;

	public void Execute(int index)
	{
		var arrow = arrows[index];

		// Check if the arrow is fired
		if (arrow.active)
		{
			arrow.position += arrow.velocity * dt;
			arrow.velocity.y += SimulationState.Gravity * dt;

			// Check if we hit something
			int i = 0;
			int hash = MinionSystem.Hash(arrow.position);
			NativeParallelMultiHashMapIterator<int> iterator;
			bool found = buckets.TryGetFirstValue(hash, out i, out iterator);
			int iterations = 0;

			while (found)
			{
				if (iterations++ > 3) break;

				// This freezes :/
				var relative = arrow.position - allMinionTransforms[i].Position;
				var distance = math.length(relative);

				if (distance < 1f)
				{
					if ((arrow.IsFriendly == 1) != minionConstData[i].IsFriendly)
					{
						// Deal damage and deactivate the arrow
						AttackCommands.Enqueue(new AttackCommand(arrowEntities[index], minionEntities[i], 34));

						// Send it to the end of the earth
						arrow.active = false;
						arrow.position = Vector3.one * -1000000;
						queueForKillingEntities.Enqueue(arrowEntities[index]);

						break;
					}
				}

				found = buckets.TryGetNextValue(out i, ref iterator);
			}

			if (arrow.active)
			{
				raycastCommands[index] = RaycastHelper.CreateRaycastCommand(arrow.position);
			}
			arrows[index] = arrow;
		}
	}
}
