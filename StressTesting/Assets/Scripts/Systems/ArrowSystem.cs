using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(MinionSystem))]
[RequireMatchingQueriesForUpdate]
public partial class ArrowSystem : SystemBase
{
	private EntityQuery arrowQuery;
	private EntityQuery minionQuery;
	private MinionSystem minionSystem;
	private UnitLifecycleManager lifecycleManager;
	
	private NativeArray<RaycastHit> raycastHits;
	private NativeArray<RaycastCommand> raycastCommands;

	protected override void OnCreate()
	{
		// 设置查询
		arrowQuery = GetEntityQuery(
			ComponentType.ReadWrite<ArrowData>()
		);

		minionQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<MinionBitmask>(),
			ComponentType.ReadOnly<UnitTransformData>()
		);

		minionSystem = World.GetExistingSystemManaged<MinionSystem>();
		lifecycleManager = World.GetExistingSystemManaged<UnitLifecycleManager>();
	}

	protected override void OnDestroy()
	{
		if (raycastHits.IsCreated) raycastHits.Dispose();
		if (raycastCommands.IsCreated) raycastCommands.Dispose();
		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		if (minionSystem == null) return;

		var arrowCount = arrowQuery.CalculateEntityCount();
		var minionCount = minionQuery.CalculateEntityCount();

		if (arrowCount == 0 || minionCount == 0) return;

		// 重新分配本地数组
		NativeArrayExtensions.ResizeNativeArray(ref raycastHits, math.max(raycastHits.Length, arrowCount));
		NativeArrayExtensions.ResizeNativeArray(ref raycastCommands, math.max(raycastCommands.Length, arrowCount));

		var deltaTime = SystemAPI.Time.DeltaTime;

		// 准备箭矢数据
		var arrowDataArray = arrowQuery.ToComponentDataArray<ArrowData>(Allocator.TempJob);
		var arrowEntities = arrowQuery.ToEntityArray(Allocator.TempJob);
		
		// 准备小兵数据
		var minionTransforms = minionQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		var minionConstData = minionQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);
		var minionEntities = minionQuery.ToEntityArray(Allocator.TempJob);

		try 
		{
			// 创建并配置ProgressArrowJob
			var progressArrowJob = new ProgressArrowJob
			{
				arrows = arrowDataArray,
				arrowEntities = arrowEntities,
				dt = deltaTime,
				allMinionTransforms = minionTransforms,
				buckets = minionSystem.CollisionBuckets,
				minionConstData = minionConstData,
				AttackCommands = CommandSystem.AttackCommandsConcurrent,
				minionEntities = minionEntities,
				queueForKillingEntities = lifecycleManager.queueForKillingEntities.AsParallelWriter(),
				raycastCommands = raycastCommands
			};

			// 调度箭矢移动Job
			var arrowJobHandle = progressArrowJob.Schedule(arrowCount, 32, Dependency);
			arrowJobHandle.Complete();

			// 将修改后的箭矢数据写回到实体组件
			for (int i = 0; i < arrowCount; i++)
			{
				SystemAPI.SetComponent(arrowEntities[i], arrowDataArray[i]);
			}

			// 执行射线检测
			var raycastJobHandle = RaycastCommand.ScheduleBatch(raycastCommands, raycastHits, 32);

			// 创建StopArrowsJob
			var deathQueue = lifecycleManager.deathQueue;
			var raycastHitsLocal = raycastHits;

			// 处理箭矢停止
			Dependency = Entities
				.WithName("StopArrowsJob")
				.WithAll<ArrowData>()
				.WithReadOnly(raycastHitsLocal)
				.ForEach((Entity entity, int entityInQueryIndex, ref ArrowData arrow) =>
				{
					if (!arrow.active) return;

					if (arrow.position.y <= raycastHitsLocal[entityInQueryIndex].point.y)
					{
						arrow.active = false;
						deathQueue.Enqueue(entity);
					}
				}).Schedule(raycastJobHandle);

			Dependency.Complete();

			Dependency = JobHandle.CombineDependencies(Dependency, CommandSystem.AttackCommandsConcurrentFence);
		}
		finally 
		{
			// 清理临时分配的数组
			if (arrowDataArray.IsCreated) arrowDataArray.Dispose();
			if (arrowEntities.IsCreated) arrowEntities.Dispose();
			if (minionTransforms.IsCreated) minionTransforms.Dispose();
			if (minionConstData.IsCreated) minionConstData.Dispose();
			if (minionEntities.IsCreated) minionEntities.Dispose();
		}
	}
}
