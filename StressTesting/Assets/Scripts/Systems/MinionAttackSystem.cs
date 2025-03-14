using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(ArcherMinionSystem))]
public partial class MinionAttackSystem : SystemBase
{
	private EntityQuery minionsQuery;
	
	private NativeArray<MinionData> minionsData;
	private NativeArray<UnitTransformData> transformsData;
	private NativeArray<MinionAttackData> attackData;
	private NativeArray<Entity> entities;

	protected override void OnCreate()
	{
		minionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadWrite<MinionData>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadOnly<MinionAttackData>()
		);
	}
	
	protected override void OnDestroy()
	{
		DisposeArrays();
	}
	
	private void DisposeArrays()
	{
		if (minionsData.IsCreated) minionsData.Dispose();
		if (transformsData.IsCreated) transformsData.Dispose();
		if (attackData.IsCreated) attackData.Dispose();
		if (entities.IsCreated) entities.Dispose();
	}

	protected override void OnUpdate()
	{
		int minionCount = minionsQuery.CalculateEntityCount();
		if (minionCount == 0) return;

		// 获取组件数据
		entities = minionsQuery.ToEntityArray(Allocator.TempJob);
		minionsData = minionsQuery.ToComponentDataArray<MinionData>(Allocator.TempJob);
		transformsData = minionsQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		attackData = minionsQuery.ToComponentDataArray<MinionAttackData>(Allocator.TempJob);

		var attackTargetJob = new AttackTargetJob
		{
			minions = minionsData,
			minionTransforms = transformsData,
			dt = SystemAPI.Time.DeltaTime,
			AttackCommands = CommandSystem.AttackCommandsConcurrent,
			minionAttacks = attackData,
			entities = entities
		};

		var attackJobFence = attackTargetJob.Schedule(minionCount, SimulationState.SmallBatchSize, JobHandle.CombineDependencies(Dependency, CommandSystem.AttackCommandsFence));
		CommandSystem.AttackCommandsConcurrentFence = JobHandle.CombineDependencies(attackJobFence, CommandSystem.AttackCommandsConcurrentFence);

		// 等待作业完成
		attackJobFence.Complete();
		
		// 清理临时分配的数组
		entities.Dispose();
		minionsData.Dispose();
		transformsData.Dispose();
		attackData.Dispose();
	}
}
