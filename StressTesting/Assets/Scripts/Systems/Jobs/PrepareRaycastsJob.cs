using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;
using Unity.Burst;

[BurstCompile]
public struct PrepareRaycasts : IJobParallelFor
{
	[ReadOnly]
	public NativeArray<UnitTransformData> transforms;

	public NativeArray<RaycastCommand> raycastCommands;

	public void Execute(int index)
	{
		var transform = transforms[index];
		raycastCommands[index] = new RaycastCommand(
			transform.Position,
			Vector3.down,
			QueryParameters.Default,
			distance: 100f);
	}
}
