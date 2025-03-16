using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;

[UpdateAfter(typeof(MinionCollisionSystem))]
public partial class ArcherMinionSystem : SystemBase
{
	// 使用EntityQuery替代RangedMinions结构体
	private EntityQuery rangedMinionsQuery;
	
	// 组件查询
	private ComponentLookup<FormationClosestData> formationClosestDataFromEntity;
	private ComponentLookup<FormationData> formationsFromEntity;
	private UnitLifecycleManager lifeCycleManager;

	private JobHandle archerJobFence;

	public float archerAttackCycle = 0;

	protected override void OnCreate()
	{
		// 初始化EntityQuery
		rangedMinionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<RangedUnitData>(),
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadWrite<MinionData>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadOnly<MinionBitmask>()
		);
		
		// 获取系统引用
		lifeCycleManager = World.GetOrCreateSystemManaged<UnitLifecycleManager>();
	}

	protected override void OnUpdate()
	{
		// 获取组件查询
		formationClosestDataFromEntity = GetComponentLookup<FormationClosestData>(true);
		formationsFromEntity = GetComponentLookup<FormationData>(true);
		
		// 检查是否有符合条件的实体
		if (rangedMinionsQuery.IsEmpty || !lifeCycleManager.createdArrows.IsCreated) 
			return;

		float prevArcherAttackCycle = archerAttackCycle;
		archerAttackCycle += SystemAPI.Time.DeltaTime;
		if (archerAttackCycle > SimulationSettings.Instance.ArcherAttackTime)
		{
			archerAttackCycle -= SimulationSettings.Instance.ArcherAttackTime;
		}

		// 获取组件数据
		var minions = rangedMinionsQuery.ToComponentDataArray<MinionData>(Allocator.TempJob);
		var transforms = rangedMinionsQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		var bitmask = rangedMinionsQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);
		
		var archerJob = new ArcherJob
		{
			createdArrowsQueue = lifeCycleManager.createdArrows.AsParallelWriter(),
			archers = minions,
			transforms = transforms,
			formations = formationsFromEntity,
			closestFormationsFromEntity = formationClosestDataFromEntity,
			minionConstData = bitmask,
			randomizer = (int)SystemAPI.Time.ElapsedTime % 10000,
			archerHitTime = SimulationSettings.Instance.ArcherHitTime,
			archerAttackCycle = archerAttackCycle,
			prevArcherAttackCycle = prevArcherAttackCycle
		};

		archerJobFence = archerJob.Schedule(minions.Length, SimulationState.SmallBatchSize);

		archerJobFence.Complete();
		// 将修改后的箭矢数据写回到实体组件
		rangedMinionsQuery.CopyFromComponentDataArray(minions);
		
		// 确保在下一帧开始前完成作业
		Dependency = archerJobFence;
		
		// 释放临时分配的内存
		CompleteDependency();
		minions.Dispose();
		transforms.Dispose();
		bitmask.Dispose();
	}
}
