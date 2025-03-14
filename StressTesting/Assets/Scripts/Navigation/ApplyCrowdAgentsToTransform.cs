using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Burst;

[UpdateAfter(typeof(CrowdSystem))]
public partial class CrowdAgentsToTransformSystem : SystemBase
{
    private EntityQuery crowdQuery;
    private TransformAccessArray transformAccessArray;

    protected override void OnCreate()
    {
        crowdQuery = GetEntityQuery(
            ComponentType.ReadOnly<CrowdAgent>(),
            ComponentType.ReadOnly<WriteToTransformMarker>()
        );
    }

    protected override void OnDestroy()
    {
        if (transformAccessArray.isCreated)
        {
            transformAccessArray.Dispose();
        }
    }

    [BurstCompile]
    struct WriteCrowdAgentsToTransformsJob : IJobParallelForTransform
    {
        [ReadOnly]
        public NativeArray<CrowdAgent> crowdAgents;

        public void Execute(int index, TransformAccess transform)
        {
            var agent = crowdAgents[index];
            transform.position = agent.worldPosition;
            if (math.length(agent.velocity) > 0.1f)
                transform.rotation = Quaternion.LookRotation(agent.velocity);
        }
    }

    protected override void OnUpdate()
    {
        if (crowdQuery.IsEmpty)
            return;

        // 获取组件数据
        var crowdAgents = crowdQuery.ToComponentDataArray<CrowdAgent>(Allocator.TempJob);
        
        // 获取或更新TransformAccessArray
        if (!transformAccessArray.isCreated || transformAccessArray.length != crowdAgents.Length)
        {
            if (transformAccessArray.isCreated)
            {
                transformAccessArray.Dispose();
            }
            
            // 获取所有实体的Transform组件
            var entities = crowdQuery.ToEntityArray(Allocator.Temp);
            var transforms = new Transform[entities.Length];
            
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                transforms[i] = EntityManager.GetComponentObject<Transform>(entity);
            }
            
            transformAccessArray = new TransformAccessArray(transforms);
            entities.Dispose();
        }

        // 创建并调度作业
        var writeJob = new WriteCrowdAgentsToTransformsJob
        {
            crowdAgents = crowdAgents
        };
        
        var jobHandle = writeJob.Schedule(transformAccessArray, Dependency);
        
        // 等待作业完成
        jobHandle.Complete();
        
        // 释放临时分配的数组
        crowdAgents.Dispose();
        
        // 更新依赖
        Dependency = jobHandle;
    }
}
