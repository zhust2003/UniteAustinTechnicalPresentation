using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;

[UpdateAfter(typeof(FormationMaintenanceSystem))]
public partial class PrepareMinionTargetsSystem : SystemBase
{
    private EntityQuery minionQuery;
    private EntityQuery formationQuery;
    private ComponentLookup<FormationData> formationDataFromEntity;

    protected override void OnCreate()
    {
        minionQuery = GetEntityQuery(
            ComponentType.ReadWrite<UnitTransformData>(),
            ComponentType.ReadWrite<MinionTarget>(),
            ComponentType.ReadOnly<MinionData>(),
            ComponentType.ReadWrite<MinionBitmask>(),
            ComponentType.ReadOnly<IndexInFormationData>(),
            ComponentType.ReadWrite<MinionPathData>()
        );

        // 创建专门用于FormationData的查询
        formationQuery = GetEntityQuery(ComponentType.ReadOnly<FormationData>());
        
        formationDataFromEntity = GetComponentLookup<FormationData>(true);
    }

    protected override void OnUpdate()
    {
        if (minionQuery.IsEmpty)
            return;

        // 更新ComponentLookup
        formationDataFromEntity.Update(this);
        
        // 获取FormationData的依赖
        var formationDependency = formationQuery.GetDependency();
        
        // 获取组件数据
        var transforms = minionQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
        var targets = minionQuery.ToComponentDataArray<MinionTarget>(Allocator.TempJob);
        var data = minionQuery.ToComponentDataArray<MinionData>(Allocator.TempJob);
        var bitmask = minionQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);
        var indicesInFormation = minionQuery.ToComponentDataArray<IndexInFormationData>(Allocator.TempJob);
        var pathInfos = minionQuery.ToComponentDataArray<MinionPathData>(Allocator.TempJob);

        // 创建并调度作业
        var prepareTargetsJob = new PrepareMinonTargets
        {
            formations = formationDataFromEntity,
            transforms = transforms,
            minionTargets = targets,
            minionData = data,
            minionBitmask = bitmask,
            baseMinionSpeed = SimulationSettings.Instance.MinionSpeed,
            pathInfos = pathInfos,
            IndicesInFormation = indicesInFormation
        };

        // 合并依赖
        Dependency = JobHandle.CombineDependencies(Dependency, formationDependency);
        
        var jobHandle = prepareTargetsJob.Schedule(transforms.Length, SimulationState.BigBatchSize, Dependency);
        
        // 更新FormationData的依赖
        formationQuery.AddDependency(jobHandle);
        
        // 等待作业完成
        jobHandle.Complete();
        
        // 将更新后的数据写回到实体
        minionQuery.CopyFromComponentDataArray(targets);
        minionQuery.CopyFromComponentDataArray(bitmask);
        minionQuery.CopyFromComponentDataArray(pathInfos);
        
        // 释放临时分配的数组
        transforms.Dispose();
        targets.Dispose();
        data.Dispose();
        bitmask.Dispose();
        indicesInFormation.Dispose();
        pathInfos.Dispose();
        
        // 更新依赖
        Dependency = jobHandle;
    }

    [BurstCompile]
    private struct PrepareMinonTargets : IJobParallelFor
    {
        [ReadOnly]
        public ComponentLookup<FormationData> formations;

        [ReadOnly]
        public NativeArray<UnitTransformData> transforms;

        [ReadOnly]
        public NativeArray<MinionData> minionData;

        public NativeArray<MinionBitmask> minionBitmask;

        public NativeArray<MinionTarget> minionTargets;
        
        public NativeArray<MinionPathData> pathInfos;

        [ReadOnly]
        public NativeArray<IndexInFormationData> IndicesInFormation;

        [ReadOnly]
        public float baseMinionSpeed;

        public void Execute(int index)
        {
            var formation = formations[transforms[index].FormationEntity];
            var minionTarget = minionTargets[index];
            var pathInfo = pathInfos[index];

            var target = transforms[index].Position;

            var unitCanMove = IndicesInFormation[index].IndexInFormation >= formation.UnitCount - formation.SpawnedCount;
            if (unitCanMove)
            {
                target = formation.Position + formation.GetOffsetFromCenter(IndicesInFormation[index].IndexInFormation);

                // Set the flag that the minion is alive
                var bitmask = minionBitmask[index];
                bitmask.IsSpawned = true;
                minionBitmask[index] = bitmask;
            }

            minionTarget.Target = target;
            minionTarget.speed = formation.SpawnedCount < formation.UnitCount ? baseMinionSpeed * 1.75f : baseMinionSpeed;

            var distance = math.length(target - transforms[index].Position);
                
            if (distance < FormationPathFindSystem.FarDistance)
            {
                pathInfo.bitmasks = 0;
                //pathInfo.targetPosition = new float3(100000f, 0, 100000f);
                //pathInfo.pathFoundToPosition = -pathInfo.targetPosition;
            }
            else
            {
                pathInfo.bitmasks |= 1;
                pathInfo.targetPosition = target;
            }
            
            minionTargets[index] = minionTarget;
            pathInfos[index] = pathInfo;
        }
    }
}