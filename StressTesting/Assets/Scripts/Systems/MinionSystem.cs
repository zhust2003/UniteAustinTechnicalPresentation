using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Experimental.AI;
using Unity.Collections.LowLevel.Unsafe;

[UpdateAfter(typeof(MinionAttackSystem))]
public partial class MinionSystem : SystemBase
{
	private EntityQuery minionQuery;
	private NativeParallelMultiHashMap<int, int> collisionBuckets;
	private FormationSystem formationSystem;

	// 添加公共属性以访问碰撞桶
	public NativeParallelMultiHashMap<int, int> CollisionBuckets => collisionBuckets;

	// 添加方法以调整碰撞桶的容量
	public void ResizeCollisionBuckets(int newCapacity)
	{
		if (collisionBuckets.Capacity < newCapacity)
		{
			// 确保容量足够
			collisionBuckets.Capacity = newCapacity;
		}
	}

	public const int fieldWidth = 4000;
	public const int fieldWidthHalf = fieldWidth / 2;
	public const int fieldHeight = 4000;
	public const int fieldHeightHalf = fieldHeight / 2;
	public const float step = 2f;
	
	NavMeshQuery moveLocationQuery;

	protected override void OnCreate()
	{
		// 设置查询
		minionQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<MinionTarget>(),
			ComponentType.ReadWrite<MinionData>(),
			ComponentType.ReadWrite<UnitTransformData>(),
			ComponentType.ReadWrite<RigidbodyData>(),
			ComponentType.ReadWrite<TextureAnimatorData>(),
			ComponentType.ReadWrite<MinionBitmask>(),
			ComponentType.ReadWrite<NavMeshLocationComponent>(),
			ComponentType.ReadWrite<MinionAttackData>(),
			ComponentType.ReadWrite<IndexInFormationData>()
		);

		// 初始化碰撞桶
		collisionBuckets = new NativeParallelMultiHashMap<int, int>(1000, Allocator.Persistent);

		// 初始化NavMesh查询
		var navMeshWorld = NavMeshWorld.GetDefaultWorld();
		moveLocationQuery = new NavMeshQuery(navMeshWorld, Allocator.Persistent);
	}

	protected override void OnDestroy()
	{
		if (collisionBuckets.IsCreated) collisionBuckets.Dispose();
		moveLocationQuery.Dispose();
	}

	public static int Hash(float3 position)
	{
		int2 quantized = new int2(math.floor(position.xz / step));
		return quantized.x + fieldWidthHalf + (quantized.y + fieldHeightHalf) * fieldWidth;
	}

	public void ForceInjection()
	{
		// 不再需要此方法，可以移除
	}

	protected override void OnUpdate()
	{
		if (!Application.isPlaying || minionQuery.IsEmpty)
			return;

		int entityCount = minionQuery.CalculateEntityCount();
		var forwardsBuffer = new NativeArray<Vector3>(entityCount, Allocator.TempJob);
		var positionsBuffer = new NativeArray<Vector3>(entityCount, Allocator.TempJob);
		var locationsBuffer = new NativeArray<NavMeshLocation>(entityCount, Allocator.TempJob);

		float dt = SystemAPI.Time.DeltaTime;
		var formationClosestLookup = GetComponentLookup<FormationClosestData>(true);
		var formationLookup = GetComponentLookup<FormationData>(true);

		// 获取组件数据
		var rigidbodyDataArray = minionQuery.ToComponentDataArray<RigidbodyData>(Allocator.TempJob);
		var targetPositionsArray = minionQuery.ToComponentDataArray<MinionTarget>(Allocator.TempJob);
		var transformsArray = minionQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		var minionAttackDataArray = minionQuery.ToComponentDataArray<MinionAttackData>(Allocator.TempJob);
		var minionDataArray = minionQuery.ToComponentDataArray<MinionData>(Allocator.TempJob);
		var animatorDataArray = minionQuery.ToComponentDataArray<TextureAnimatorData>(Allocator.TempJob);
		var navMeshLocationsArray = minionQuery.ToComponentDataArray<NavMeshLocationComponent>(Allocator.TempJob);
		var bitmaskArray = minionQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);

		// 更新碰撞桶
		collisionBuckets.Clear();
		var prepareBucketsJob = new PrepareBucketsJob
		{
			transforms = transformsArray,
			minionBitmask = bitmaskArray,
			buckets = collisionBuckets
		}.Schedule(Dependency);
		prepareBucketsJob.Complete();

		// 创建作业
		var minionBehaviorJob = new MinionBehaviourJob
		{
			rigidbodyData = rigidbodyDataArray,
			targetPositions = targetPositionsArray,
			transforms = transformsArray,
			minionAttackData = minionAttackDataArray,
			minionData = minionDataArray,
			animatorData = animatorDataArray,
			navMeshLocations = navMeshLocationsArray,
			forwardsBuffer = forwardsBuffer,
			positionsBuffer = positionsBuffer,
			locationsBuffer = locationsBuffer,
			archerAttackTime = SimulationSettings.Instance.ArcherAttackTime,
			dt = dt,
			randomizer = UnityEngine.Random.Range(0, int.MaxValue)
		}.Schedule(entityCount, 32, prepareBucketsJob);

		// 确保NavMeshQuery在作业中的安全使用
		var navMeshWorld = NavMeshWorld.GetDefaultWorld();
		
		// 创建一个临时的NavMeshQuery副本，避免并发访问问题
		var tempMoveLocationQuery = new NavMeshQuery(navMeshWorld, Allocator.TempJob);
		
		var minionBehaviorMoveJob = new MinionBehaviourMoveJob
		{
			positionsBuffer = positionsBuffer,
			locationsBuffer = locationsBuffer,
			query = tempMoveLocationQuery
		}.Schedule(entityCount, 32, minionBehaviorJob);

		navMeshWorld.AddDependency(minionBehaviorMoveJob);

		// 更新组件数据
		var updatedTransformsArray = minionQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		var updatedNavMeshLocationsArray = minionQuery.ToComponentDataArray<NavMeshLocationComponent>(Allocator.TempJob);

		var minionBehaviorSyncbackJob = new MinionBehaviourSyncbackJob
		{
			transforms = updatedTransformsArray,
			navMeshLocations = updatedNavMeshLocationsArray,
			forwardsBuffer = forwardsBuffer,
			positionsBuffer = positionsBuffer,
			locationsBuffer = locationsBuffer
		}.Schedule(entityCount, 32, minionBehaviorMoveJob);

		// 确保所有作业完成
		minionBehaviorSyncbackJob.Complete();
		
		// 释放临时NavMeshQuery
		tempMoveLocationQuery.Dispose();
		
		// 将更新后的数据写回到实体
		minionQuery.CopyFromComponentDataArray(animatorDataArray);
		minionQuery.CopyFromComponentDataArray(updatedTransformsArray);
		minionQuery.CopyFromComponentDataArray(updatedNavMeshLocationsArray);
		
		// 清理临时分配的数组
		rigidbodyDataArray.Dispose();
		targetPositionsArray.Dispose();
		transformsArray.Dispose();
		minionAttackDataArray.Dispose();
		minionDataArray.Dispose();
		animatorDataArray.Dispose();
		navMeshLocationsArray.Dispose();
		updatedTransformsArray.Dispose();
		updatedNavMeshLocationsArray.Dispose();
		bitmaskArray.Dispose();
		
		// 更新依赖
		Dependency = minionBehaviorSyncbackJob;
	}
}
