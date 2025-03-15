using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
//using UnityEngine.AI;
//using UnityEngine.Assertions;
using UnityEngine.Experimental.AI;
using UnityEngine.Profiling;
using Unity.Burst;

// 创建一个包装float3的缓冲元素类型
public struct PathPointElement : IBufferElementData
{
    public float3 Value;
    
    public static implicit operator float3(PathPointElement e) { return e.Value; }
    public static implicit operator PathPointElement(float3 e) { return new PathPointElement { Value = e }; }
    public static implicit operator PathPointElement(Vector3 v) { return new PathPointElement { Value = v }; }
}

[UpdateAfter(typeof(FormationIntegritySystem))]
public partial class FormationPathFindSystem : SystemBase
{
    private EntityQuery formationsQuery;
    private EntityQuery minionsQuery;
    
    private BufferLookup<PathPointElement> minionPathsLookup;
    private ComponentLookup<NavMeshLocationComponent> minionNavMeshLocationLookup;
    private ComponentLookup<MinionPathData> minionPathsInfoLookup;
    private ComponentLookup<FormationIntegrityData> formationIntegrityDataLookup;

    //private NativeArray<float> costs;

    public const float FarDistance = 15f;
    public const float CloseDistance = 2f;

    private NativeQueue<Entity> newPathQueries;
    private NativeQueue<Entity> completePathQueries;

    public const int MaxNavMeshQueries = 10;
    private const int MaxNavMeshNodes = 2048;
    private NavMeshQuery[] queries;
    private NativeList<int> queryIndexUsed;
    private NativeList<int> queryIndexFree;
    private NativeList<Entity> findingEntities;

    protected override void OnCreate()
    {
        formationsQuery = GetEntityQuery(
            ComponentType.ReadOnly<FormationData>(),
            ComponentType.ReadOnly<CrowdAgentNavigator>(),
            ComponentType.ReadOnly<FormationIntegrityData>()
        );

        minionsQuery = GetEntityQuery(
            ComponentType.ReadOnly<MinionTarget>(),
            ComponentType.ReadOnly<MinionPathData>(),
            ComponentType.ReadOnly<NavMeshLocationComponent>()
        );

        minionPathsLookup = GetBufferLookup<PathPointElement>();
        minionNavMeshLocationLookup = GetComponentLookup<NavMeshLocationComponent>(true);
        minionPathsInfoLookup = GetComponentLookup<MinionPathData>();
        formationIntegrityDataLookup = GetComponentLookup<FormationIntegrityData>(true);

        //costs = new NativeArray<float>(32, Allocator.Persistent);
        //for (int i = 0; i < 32; i++) costs[i] = 1;

        newPathQueries = new NativeQueue<Entity>(Allocator.Persistent);
        completePathQueries = new NativeQueue<Entity>(Allocator.Persistent);

        var navMeshWorld = NavMeshWorld.GetDefaultWorld();
        queries = new NavMeshQuery[MaxNavMeshQueries];
        queryIndexUsed = new NativeList<int>(MaxNavMeshQueries, Allocator.Persistent);
        queryIndexFree = new NativeList<int>(MaxNavMeshQueries, Allocator.Persistent);
        for (var i = 0; i < MaxNavMeshQueries; ++i)
        {
            queries[i] = new NavMeshQuery(navMeshWorld, Allocator.Persistent, MaxNavMeshNodes);
            queryIndexFree.Add(i);
        }
        findingEntities = new NativeList<Entity>(MaxNavMeshQueries, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        //if (costs.IsCreated) costs.Dispose();
        if (newPathQueries.IsCreated) newPathQueries.Dispose();
        if (completePathQueries.IsCreated) completePathQueries.Dispose();
        
        for (int i = 0; i < MaxNavMeshQueries; ++i)
            queries[i].Dispose();
            
        if (queryIndexUsed.IsCreated) queryIndexUsed.Dispose();
        if (queryIndexFree.IsCreated) queryIndexFree.Dispose();
        if (findingEntities.IsCreated) findingEntities.Dispose();
    }

    protected override void OnUpdate()
    {
        if (formationsQuery.IsEmpty) return;

        // 更新组件查询
        minionPathsLookup.Update(this);
        minionNavMeshLocationLookup.Update(this);
        minionPathsInfoLookup.Update(this);
        formationIntegrityDataLookup.Update(this);

        // 获取组件数据
        var formationDataArray = formationsQuery.ToComponentDataArray<FormationData>(Allocator.TempJob);
        var navigatorsArray = formationsQuery.ToComponentDataArray<CrowdAgentNavigator>(Allocator.TempJob);
        var integrityDataArray = formationsQuery.ToComponentDataArray<FormationIntegrityData>(Allocator.TempJob);
        var formationEntities = formationsQuery.ToEntityArray(Allocator.TempJob);

        var minionTargetsArray = minionsQuery.ToComponentDataArray<MinionTarget>(Allocator.TempJob);
        var minionPathsInfoArray = minionsQuery.ToComponentDataArray<MinionPathData>(Allocator.TempJob);
        var minionNavMeshLocationArray = minionsQuery.ToComponentDataArray<NavMeshLocationComponent>(Allocator.TempJob);
        var minionEntities = minionsQuery.ToEntityArray(Allocator.TempJob);

        var assign = new AssignFormationSpeed
        {
            formations = formationDataArray,
            navigators = navigatorsArray,
            integrityData = integrityDataArray
        };

        Profiler.BeginSample("Alloc");
        var pathFollow = new MinionFollowPath
        {
            entities = minionEntities,
            newPathQueries = newPathQueries.AsParallelWriter(),
            pathsInfo = minionPathsInfoArray,
            minionPathsLookup = minionPathsLookup,
            minionTargets = minionTargetsArray,
            navMeshLocation = minionNavMeshLocationArray,
            maxPathSize = SimulationState.MaxPathSize
        };
        
        Entity rmEnt;
        while (completePathQueries.TryDequeue(out rmEnt))
        {
            // TODO: avoid linear search
            for (int i = 0; i < findingEntities.Length; ++i)
            {
                if (findingEntities[i] == rmEnt)
                {
                    findingEntities.RemoveAtSwapBack(i);
                    queryIndexFree.Add(queryIndexUsed[i]);
                    queryIndexUsed.RemoveAtSwapBack(i);
                    break;
                }
            }
        }
        
        for (int i = 0; i < findingEntities.Length; ++i)
        {
            if (!EntityManager.Exists(findingEntities[i]))
            {
                findingEntities.RemoveAtSwapBack(i);
                queryIndexFree.Add(queryIndexUsed[i]);
                queryIndexUsed.RemoveAtSwapBack(i);
                --i;
            }
        }
        
        // Refill with new path queries
        while (findingEntities.Length < MaxNavMeshQueries && newPathQueries.Count > 0)
        {
            // TODO: should use some kind of round robin to make sure all minions get a chance to path find
            findingEntities.Add(newPathQueries.Dequeue());
            queryIndexUsed.Add(queryIndexFree[queryIndexFree.Length - 1]);
            queryIndexFree.RemoveAtSwapBack(queryIndexFree.Length - 1);
        }

        //Debug.Assert(queryIndexFree.Length + queryIndexUsed.Length == MaxNavMeshQueries);

        var navMeshWorld = NavMeshWorld.GetDefaultWorld();

        newPathQueries.Clear();
        
        // 创建并调度作业
        var assignJobHandle = assign.Schedule(formationDataArray.Length, SimulationState.BigBatchSize);
        
        var pathFindJobHandle = new JobHandle();
        for (int i = 0; i < findingEntities.Length; ++i)
        {
            var pathFind = new MinionPathFind
            {
                query = queries[queryIndexUsed[i]],
                entity = findingEntities[i],
                completePathQueries = completePathQueries,

                pathsInfoLookup = minionPathsInfoLookup,
                minionPathsLookup = minionPathsLookup,
                navMeshLocationLookup = minionNavMeshLocationLookup,
                maxPathSize = SimulationState.MaxPathSize,
                //costs = costs,
                polygons = new NativeArray<PolygonId>(100, Allocator.TempJob),
                straightPath = new NativeArray<NavMeshLocation>(SimulationState.MaxPathSize, Allocator.TempJob),
                straightPathFlags = new NativeArray<StraightPathFlags>(SimulationState.MaxPathSize, Allocator.TempJob),
                vertexSide = new NativeArray<float>(SimulationState.MaxPathSize, Allocator.TempJob)
            };
            // TODO: figure out how to run these in parallel, they write to different parts of the same array
            pathFindJobHandle = pathFind.Schedule(pathFindJobHandle);
            navMeshWorld.AddDependency(pathFindJobHandle);
        }
        
        if (findingEntities.Length > 0)
        {
            navMeshWorld.AddDependency(pathFindJobHandle);
        }
        Profiler.EndSample();

        var pathFollowJobHandle = pathFollow.Schedule(minionEntities.Length, SimulationState.BigBatchSize, pathFindJobHandle);
        
        // 合并作业依赖
        var combinedJobHandle = JobHandle.CombineDependencies(assignJobHandle, pathFollowJobHandle);
        combinedJobHandle.Complete();
        
        // 将计算结果写回到实体
        for (int i = 0; i < formationEntities.Length; i++)
        {
            EntityManager.SetComponentData(formationEntities[i], navigatorsArray[i]);
        }
        
        for (int i = 0; i < minionEntities.Length; i++)
        {
            EntityManager.SetComponentData(minionEntities[i], minionTargetsArray[i]);
            EntityManager.SetComponentData(minionEntities[i], minionPathsInfoArray[i]);
        }

        // 清理临时分配的数组
        formationDataArray.Dispose();
        navigatorsArray.Dispose();
        integrityDataArray.Dispose();
        formationEntities.Dispose();
        
        minionTargetsArray.Dispose();
        minionPathsInfoArray.Dispose();
        minionNavMeshLocationArray.Dispose();
        minionEntities.Dispose();
        
        navMeshWorld.AddDependency(combinedJobHandle);
    }

    [BurstCompile]
    private struct MinionPathFind : IJob
    {
        public NavMeshQuery query;

        // Minion data
        public Entity entity;
        public NativeQueue<Entity> completePathQueries;
        public ComponentLookup<MinionPathData> pathsInfoLookup;
        public BufferLookup<PathPointElement> minionPathsLookup;
        [ReadOnly]
        public ComponentLookup<NavMeshLocationComponent> navMeshLocationLookup;

        // Temp data for path finding
        public int maxPathSize;
        [DeallocateOnJobCompletion]
        public NativeArray<PolygonId> polygons;
        [DeallocateOnJobCompletion]
        public NativeArray<NavMeshLocation> straightPath;
        [DeallocateOnJobCompletion]
        public NativeArray<StraightPathFlags> straightPathFlags;

        [DeallocateOnJobCompletion]
        public NativeArray<float> vertexSide;
        
        // Mostly static data for path finding
        // TODO: cannot be read only, should investigate allowing that in the nav mesh apis
        //[ReadOnly]
        //public NativeArray<float> costs;

        public void Execute()
        {
            if (!pathsInfoLookup.HasComponent(entity) || !navMeshLocationLookup.HasComponent(entity) || !minionPathsLookup.HasBuffer(entity))
            {
                completePathQueries.Enqueue(entity);
                return;
            }
            
            var pathInfo = pathsInfoLookup[entity];

            // Check bit 1 and 2
            if ((pathInfo.bitmasks & 6) == 0)
            {
                //m_InitFindPath.Begin();
                // We need to do path finding
                var end = query.MapLocation(pathInfo.targetPosition, new Vector3(100, 100, 100), 0);
                query.BeginFindPath(navMeshLocationLookup[entity].NavMeshLocation, end); //, NavMesh.AllAreas, costs);
                pathInfo.bitmasks |= 2;
                //m_InitFindPath.End();

                //Debug.Log("START PATH FINDING");
            }

            // Path searching is initialized, we should update stuff
            //m_UpdatePath.Begin();
            int performed;
            var status = query.UpdateFindPath(10, out performed);
            //m_UpdatePath.End();

            //Debug.Log("UPDATE SLICE " + performed + " " + status);

            if (status.IsSuccess())
            {
                //m_MovePath.Begin();
                int polySize;
                status = query.EndFindPath(out polySize);
                if (status.IsSuccess())
                {
                    query.GetPathResult(polygons);
                    pathInfo.currentCornerIndex = 0;

                    // Update the bitmask: Path finding done & path found
                    pathInfo.bitmasks &= ~2;
                    pathInfo.bitmasks |= 4;
                    completePathQueries.Enqueue(entity);

                    var minionPath = minionPathsLookup[entity];

                    pathInfo.pathSize = 0;

                    var cornerCount = 0;
                    var pathStatus = PathUtils.FindStraightPath(query, navMeshLocationLookup[entity].NavMeshLocation.position,
                                                                pathInfo.targetPosition,
                                                                polygons, polySize,
                                                                ref straightPath, ref straightPathFlags, ref vertexSide,
                                                                ref cornerCount, maxPathSize);

                    if (pathStatus.IsSuccess() && cornerCount > 1 && cornerCount <= maxPathSize)
                    {
                        for (var i = 0; i < cornerCount; i++)
                        {
                            if (i < minionPath.Length)
                            {
                                minionPath[i] = straightPath[i].position;
                            }
                        }

                        pathInfo.pathFoundToPosition = straightPath[cornerCount - 1].position;
                        pathInfo.pathSize = cornerCount;
                    }

                    //Debug.Log("PATH FINDING DONE " + path.pathSize);
                }

                //m_MovePath.End();
            }

            if (status.IsFailure())
            {
                // Failure happened, reset stuff
                pathInfo.bitmasks &= ~2;
                completePathQueries.Enqueue(entity);
            }
            pathsInfoLookup[entity] = pathInfo;
        }
    }
    
    [BurstCompile]
    private struct MinionFollowPath : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<Entity> entities;
        public NativeQueue<Entity>.ParallelWriter newPathQueries;
        public NativeArray<MinionPathData> pathsInfo;
        [ReadOnly]
        public BufferLookup<PathPointElement> minionPathsLookup;
        public NativeArray<MinionTarget> minionTargets;
        public NativeArray<NavMeshLocationComponent> navMeshLocation;

        public int maxPathSize;

        public void Execute(int index)
        {
            var pathInfo = pathsInfo[index];
            var pathDataChanged = false;

            if ((pathInfo.bitmasks & 1) == 1)
            {
                var minionTarget = minionTargets[index];

                if (mathx.lengthSqr(pathInfo.pathFoundToPosition - pathInfo.targetPosition) > FarDistance * FarDistance)
                {
                    pathInfo.bitmasks &= ~4;
                    pathDataChanged = true;
                }

                // Check bit 1 and 2
                if ((pathInfo.bitmasks & 6) == 0)
                {
                    newPathQueries.Enqueue(entities[index]);
                }

                if ((pathInfo.bitmasks & 4) != 0)
                {
                    // The path was previously found. We need to move on it
                    if (minionPathsLookup.HasBuffer(entities[index]))
                    {
                        var minionPath = minionPathsLookup[entities[index]];

                        if (maxPathSize != 0 && pathInfo.currentCornerIndex < minionPath.Length)
                        {
                            var potentialTarget = minionPath[pathInfo.currentCornerIndex];

                            if (mathx.lengthSqr((float3)navMeshLocation[index].NavMeshLocation.position - potentialTarget) < 0.01f)
                            {
                                // Increase the index if needed
                                if (pathInfo.currentCornerIndex < pathInfo.pathSize)
                                {
                                    // Go to next corner
                                    pathInfo.currentCornerIndex++;
                                    pathDataChanged = true;
                                    
                                    if (pathInfo.currentCornerIndex < minionPath.Length)
                                    {
                                        potentialTarget = minionPath[pathInfo.currentCornerIndex];
                                    }
                                }
                            }

                            minionTarget.Target = potentialTarget;
                        }
                    }
                }

                minionTargets[index] = minionTarget;
            }

            if (pathDataChanged)
            {
                pathsInfo[index] = pathInfo;
            }
        }
    }

    [BurstCompile]
    private struct AssignFormationSpeed : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<FormationData> formations;
        public NativeArray<CrowdAgentNavigator> navigators;
        [ReadOnly]
        public NativeArray<FormationIntegrityData> integrityData;

        public void Execute(int index)
        {
            var data = integrityData[index];
            
            var n = navigators[index];
            if (!formations[index].EnableMovement || data.unitsFar + data.unitsClose >= data.unitCount ||
                formations[index].FormationState == FormationData.State.Spawning)
            {
                n.speed = 0f;
            }
            else
            {
                n.speed = math.lerp(1f, 2.1f, 1 - (data.unitsClose / (float)data.unitCount));
            }

            navigators[index] = n;
        }
    }
}
