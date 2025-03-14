using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(MinionSystem))]
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
		arrowQuery = GetEntityQuery(
			ComponentType.ReadWrite<ArrowData>()
		);
		
		minionQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<MinionBitmask>(),
			ComponentType.ReadOnly<UnitTransformData>()
		);
	}

	protected override void OnDestroy()
	{
		if (raycastHits.IsCreated) raycastHits.Dispose();
		if (raycastCommands.IsCreated) raycastCommands.Dispose();
	}

	protected override void OnUpdate()
	{
		// 获取系统引用
		if (minionSystem == null)
		{
			try
			{
				minionSystem = World.GetOrCreateSystemManaged<MinionSystem>();
			}
			catch
			{
				Debug.LogError("无法获取MinionSystem系统");
				return;
			}
		}
		
		if (lifecycleManager == null)
		{
			try
			{
				lifecycleManager = World.GetOrCreateSystemManaged<UnitLifecycleManager>();
			}
			catch
			{
				Debug.LogError("无法获取UnitLifecycleManager系统");
				return;
			}
		}
			
		if (minionSystem == null || lifecycleManager == null) return;

		int arrowCount = arrowQuery.CalculateEntityCount();
		int minionCount = minionQuery.CalculateEntityCount();
		
		if (arrowCount == 0) return;
		if (minionCount == 0) return;

		// 确保NativeArray大小足够，但避免频繁重新创建
		bool needToResizeArrays = false;
		
		if (!raycastHits.IsCreated)
		{
			raycastHits = new NativeArray<RaycastHit>(math.max(64, arrowCount), Allocator.Persistent);
			needToResizeArrays = false;
		}
		else if (raycastHits.Length < arrowCount)
		{
			needToResizeArrays = true;
		}
		
		if (!raycastCommands.IsCreated)
		{
			raycastCommands = new NativeArray<RaycastCommand>(math.max(64, arrowCount), Allocator.Persistent);
			needToResizeArrays = false;
		}
		else if (raycastCommands.Length < arrowCount)
		{
			needToResizeArrays = true;
		}
		
		// 只有在确实需要时才重新分配数组
		if (needToResizeArrays)
		{
			int newSize = math.max(raycastHits.Length * 2, arrowCount);
			
			var newRaycastHits = new NativeArray<RaycastHit>(newSize, Allocator.Persistent);
			var newRaycastCommands = new NativeArray<RaycastCommand>(newSize, Allocator.Persistent);
			
			// 复制旧数据
			NativeArray<RaycastHit>.Copy(raycastHits, newRaycastHits, raycastHits.Length);
			NativeArray<RaycastCommand>.Copy(raycastCommands, newRaycastCommands, raycastCommands.Length);
			
			// 释放旧数组
			raycastHits.Dispose();
			raycastCommands.Dispose();
			
			// 使用新数组
			raycastHits = newRaycastHits;
			raycastCommands = newRaycastCommands;
		}

		// 获取必要的组件访问器
		var queueForKillingEntities = lifecycleManager.queueForKillingEntities;
		var deathQueue = lifecycleManager.deathQueue;
		float deltaTime = SystemAPI.Time.DeltaTime;

		// 创建本地变量以避免捕获this
		var localRaycastCommands = raycastCommands;
		
		// 第一步：更新箭的位置
		Entities
			.WithName("UpdateArrowPositions")
			.WithAll<ArrowData>()
			.ForEach((Entity arrowEntity, int entityInQueryIndex, ref ArrowData arrow) =>
			{
				if (arrow.active)
				{
					arrow.position += arrow.velocity * deltaTime;
					arrow.velocity.y += SimulationState.Gravity * deltaTime;
					
					// 设置射线命令
					if (entityInQueryIndex < localRaycastCommands.Length)
					{
						localRaycastCommands[entityInQueryIndex] = new RaycastCommand(
							arrow.position,
							Vector3.down,
							QueryParameters.Default,
							distance: 100f);
					}
				}
			})
			.WithoutBurst()
			.Run();
			
		// 执行射线检测
		RaycastCommand.ScheduleBatch(raycastCommands, raycastHits, 8).Complete();
		
		// 第二步：处理射线检测结果和处理deathQueue
		Entities
			.WithName("ProcessRaycastResults")
			.WithAll<ArrowData>()
			.ForEach((Entity arrowEntity, int entityInQueryIndex, ref ArrowData arrow) =>
			{
				if (arrow.active && entityInQueryIndex < raycastHits.Length)
				{
					if (arrow.position.y <= raycastHits[entityInQueryIndex].point.y)
					{
						arrow.active = false;
						deathQueue.Enqueue(arrowEntity);
					}
				}
			})
			.WithoutBurst()
			.Run();
			
		// 更新CommandSystem的依赖关系
		CommandSystem.AttackCommandsConcurrentFence = JobHandle.CombineDependencies(Dependency, CommandSystem.AttackCommandsConcurrentFence);
	}
}