using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using Unity.Burst;

public partial class FlightSystem : SystemBase
{
	private EntityQuery flyingUnitsQuery;
	
	private NativeArray<FlyingData> flyingSelector;
	private NativeArray<UnitTransformData> transforms;
	private NativeArray<RigidbodyData> rigidbodies;
	private NativeArray<MinionData> minionData;
	private NativeArray<TextureAnimatorData> animationData;
	private NativeArray<Entity> entities;

	private NativeArray<RaycastHit> raycastHits;
	private NativeArray<RaycastCommand> raycastCommands;

	protected override void OnCreate()
	{
		flyingUnitsQuery = GetEntityQuery(
			ComponentType.ReadOnly<FlyingData>(),
			ComponentType.ReadWrite<UnitTransformData>(),
			ComponentType.ReadWrite<RigidbodyData>(),
			ComponentType.ReadWrite<MinionData>(),
			ComponentType.ReadWrite<TextureAnimatorData>()
		);
	}

	protected override void OnDestroy()
	{
		DisposeArrays();
	}
	
	private void DisposeArrays()
	{
		if (flyingSelector.IsCreated) flyingSelector.Dispose();
		if (transforms.IsCreated) transforms.Dispose();
		if (rigidbodies.IsCreated) rigidbodies.Dispose();
		if (minionData.IsCreated) minionData.Dispose();
		if (animationData.IsCreated) animationData.Dispose();
		if (entities.IsCreated) entities.Dispose();
		
		if (raycastHits.IsCreated) raycastHits.Dispose();
		if (raycastCommands.IsCreated) raycastCommands.Dispose();
	}

	protected override void OnUpdate()
	{
		int flyingUnitsCount = flyingUnitsQuery.CalculateEntityCount();
		if (flyingUnitsCount == 0) return;

		// 获取组件数据
		entities = flyingUnitsQuery.ToEntityArray(Allocator.TempJob);
		flyingSelector = flyingUnitsQuery.ToComponentDataArray<FlyingData>(Allocator.TempJob);
		transforms = flyingUnitsQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		rigidbodies = flyingUnitsQuery.ToComponentDataArray<RigidbodyData>(Allocator.TempJob);
		minionData = flyingUnitsQuery.ToComponentDataArray<MinionData>(Allocator.TempJob);
		animationData = flyingUnitsQuery.ToComponentDataArray<TextureAnimatorData>(Allocator.TempJob);

		// 确保NativeArray大小足够
		if (!raycastHits.IsCreated || raycastHits.Length < flyingUnitsCount)
		{
			if (raycastHits.IsCreated) raycastHits.Dispose();
			raycastHits = new NativeArray<RaycastHit>(flyingUnitsCount, Allocator.Persistent);
		}
		
		if (!raycastCommands.IsCreated || raycastCommands.Length < flyingUnitsCount)
		{
			if (raycastCommands.IsCreated) raycastCommands.Dispose();
			raycastCommands = new NativeArray<RaycastCommand>(flyingUnitsCount, Allocator.Persistent);
		}

		// ============ JOB CREATION ===============
		var prepareRaycastsJob = new PrepareRaycasts
		{
			transforms = transforms,
			raycastCommands = raycastCommands
		};

		var flightJob = new MinionFlightJob
		{
			raycastHits = raycastHits,
			minionData = minionData,
			flyingUnits = transforms,
			rigidbodies = rigidbodies,
			textureAnimators = animationData,
			dt = SystemAPI.Time.DeltaTime,
		};

		// ==================== JOB SCHEDULING ==============
		var prepareRaycastFence = prepareRaycastsJob.Schedule(flyingUnitsCount, SimulationState.SmallBatchSize);
		prepareRaycastFence.Complete(); // TODO fix me
		var raycastJobFence = RaycastCommand.ScheduleBatch(raycastCommands, raycastHits, SimulationState.SmallBatchSize, prepareRaycastFence);
		var flightJobFence = flightJob.Schedule(flyingUnitsCount, SimulationState.SmallBatchSize, raycastJobFence);
		
		flightJobFence.Complete();
		
		// 清理临时分配的数组
		entities.Dispose();
		flyingSelector.Dispose();
		transforms.Dispose();
		rigidbodies.Dispose();
		minionData.Dispose();
		animationData.Dispose();
	}
}
