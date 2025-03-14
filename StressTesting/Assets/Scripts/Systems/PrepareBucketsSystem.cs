using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine.Profiling;
using Unity.Burst;
using UnityEngine;

[UpdateAfter(typeof(CrowdSystem))]
public partial class PrepareBucketsSystem : SystemBase
{
	private EntityQuery minionQuery;
	private MinionSystem minionSystem;

	[BurstCompile]
	public struct PrepareBucketsJob : IJob
	{
		[ReadOnly]
		public NativeArray<UnitTransformData> transforms;
		[ReadOnly]
		public NativeArray<MinionBitmask> minionBitmask;
		
		public NativeParallelMultiHashMap<int, int>.ParallelWriter buckets;

		public void Execute()
		{
			for (int i = 0; i < transforms.Length; i++)
			{
				if (!minionBitmask[i].IsSpawned) continue;

				var hash = MinionSystem.Hash(transforms[i].Position);
				buckets.Add(hash, i);
			}
		}
	}

	protected override void OnCreate()
	{
		minionQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadOnly<MinionBitmask>()
		);
		
		// 在新版ECS API中获取系统实例
		minionSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<MinionSystem>();
	}

	protected override void OnUpdate()
	{
		if (minionQuery.IsEmpty)
			return;

		Profiler.BeginSample("Clearing buckets");

		var collisionBuckets = minionSystem.CollisionBuckets;
		collisionBuckets.Clear();

		// 获取实体数量
		int minionCount = minionQuery.CalculateEntityCount();
		
		// 确保容量足够
		if (collisionBuckets.Capacity < minionCount)
		{
			// 使用MinionSystem中的方法调整容量
			minionSystem.ResizeCollisionBuckets(minionCount);
		}

		Profiler.EndSample();

		// 获取组件数据
		var transforms = minionQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		var bitmasks = minionQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);

		// 创建并调度作业
		var prepareBucketsJob = new PrepareBucketsJob
		{
			transforms = transforms,
			minionBitmask = bitmasks,
			buckets = collisionBuckets.AsParallelWriter()
		};

		var jobHandle = prepareBucketsJob.Schedule(Dependency);
		
		// 等待作业完成
		jobHandle.Complete();
		
		// 释放临时分配的数组
		transforms.Dispose();
		bitmasks.Dispose();
		
		// 更新依赖
		Dependency = jobHandle;
	}
}