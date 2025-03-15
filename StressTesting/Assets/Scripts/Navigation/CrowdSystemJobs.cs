using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;
using Unity.Burst;

public partial class CrowdSystem
{
    [BurstCompile]
    public struct CheckPathNeededJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<CrowdAgentNavigator> agentNavigators;
        [ReadOnly]
        public NativeArray<uint> pathRequestIdForAgent;

        public NativeArray<bool> planPathForAgent;

        public void Execute(int index)
        {
            var agentNavigator = agentNavigators[index];
            if (planPathForAgent[index] || index >= agentNavigators.Length)
                return;

            if (pathRequestIdForAgent[index] == PathQueryQueueEcs.RequestEcs.invalidId)
            {
                planPathForAgent[index] = agentNavigator.newDestinationRequested;
            }
        }
    }

    [BurstCompile]
    public struct MakePathRequestsJob : IJob
    {
        [ReadOnly]
        public NavMeshQuery query;
        [ReadOnly]
        public NativeArray<CrowdAgent> agents;

        public NativeArray<CrowdAgentNavigator> agentNavigators;

        public NativeArray<bool> planPathForAgent;
        public NativeArray<uint> pathRequestIdForAgent;
        public NativeArray<PathQueryQueueEcs.RequestEcs> pathRequests;
        public NativeArray<int> pathRequestsRange;
        public NativeArray<uint> uniqueIdStore;
        public NativeArray<int> currentAgentIndex;

        public void Execute()
        {
            if (agents.Length == 0)
                return;

            // add new requests to the end of the range
            var reqEnd = pathRequestsRange[k_Start] + pathRequestsRange[k_Count];
            var reqMax = pathRequests.Length - 1;
            var firstAgent = currentAgentIndex[0];
            for (var i = 0; i < agents.Length; ++i)
            {
                if (reqEnd > reqMax)
                    break;

                var index = (i + firstAgent) % agents.Length;
                var agentNavigator = agentNavigators[index];
                if (planPathForAgent.Length > 0 && planPathForAgent[index] ||
                    agentNavigator.newDestinationRequested && pathRequestIdForAgent[index] == PathQueryQueueEcs.RequestEcs.invalidId)
                {
                    if (!agentNavigator.active)
                    {
                        if (planPathForAgent.Length > 0)
                        {
                            planPathForAgent[index] = false;
                        }
                        agentNavigator.newDestinationRequested = false;
                        agentNavigators[index] = agentNavigator;
                        continue;
                    }

                    var agent = agents[index];
                    if (!query.IsValid(agent.location))
                        continue;

                    if (uniqueIdStore[0] == PathQueryQueueEcs.RequestEcs.invalidId)
                    {
                        uniqueIdStore[0] = 1 + PathQueryQueueEcs.RequestEcs.invalidId;
                    }

                    pathRequests[reqEnd++] = new PathQueryQueueEcs.RequestEcs()
                    {
                        agentIndex = index,
                        agentType = agent.type,
                        mask = NavMesh.AllAreas,
                        uid = uniqueIdStore[0],
                        start = agent.location.position,
                        end = agentNavigator.requestedDestination
                    };
                    pathRequestIdForAgent[index] = uniqueIdStore[0];
                    uniqueIdStore[0]++;
                    if (planPathForAgent.Length > 0)
                    {
                        planPathForAgent[index] = false;
                    }
                    agentNavigator.newDestinationRequested = false;
                    agentNavigators[index] = agentNavigator;
                }
                currentAgentIndex[0] = index;
            }
            pathRequestsRange[k_Count] = reqEnd - pathRequestsRange[k_Start];
        }
    }

    [BurstCompile]
    public struct EnqueueRequestsInQueriesJob : IJob
    {
        public NativeArray<PathQueryQueueEcs.RequestEcs> pathRequests;
        public NativeArray<int> pathRequestsRange;
        public PathQueryQueueEcs queryQueue;
        public int maxRequestsInQueue;

        public void Execute()
        {
            var reqCount = pathRequestsRange[k_Count];
            if (reqCount == 0)
                return;

            var reqIdx = pathRequestsRange[k_Start];
            var slotsRemaining = maxRequestsInQueue - queryQueue.GetRequestCount();
            if (slotsRemaining <= 0)
                return;

            var rangeEnd = reqIdx + Math.Min(slotsRemaining, reqCount);
            for (; reqIdx < rangeEnd; reqIdx++)
            {
                var pathRequest = pathRequests[reqIdx];
                if (queryQueue.Enqueue(pathRequest))
                {
                    pathRequest.uid = PathQueryQueueEcs.RequestEcs.invalidId;
                    pathRequests[reqIdx] = pathRequest;
                }
                else
                {
                    break;
                }
            }

            pathRequestsRange[k_Count] = reqCount - (reqIdx - pathRequestsRange[k_Start]);
            pathRequestsRange[k_Start] = reqIdx;
        }
    }

    [BurstCompile]
    public struct ForgetMovedRequestsJob : IJob
    {
        public NativeArray<PathQueryQueueEcs.RequestEcs> pathRequests;
        public NativeArray<int> pathRequestsRange;

        public void Execute()
        {
            var dst = 0;
            var src = pathRequestsRange[k_Start];
            if (src > dst)
            {
                var count = pathRequestsRange[k_Count];
                var rangeEnd = Math.Min(src + count, pathRequests.Length);
                for (; src < rangeEnd; src++, dst++)
                {
                    pathRequests[dst] = pathRequests[src];
                }
                pathRequestsRange[k_Count] = rangeEnd - pathRequestsRange[k_Start];
                pathRequestsRange[k_Start] = 0;

                // invalidate the remaining requests
                for (; dst < rangeEnd; dst++)
                {
                    var request = pathRequests[dst];
                    request.uid = PathQueryQueueEcs.RequestEcs.invalidId;
                    pathRequests[dst] = request;
                }
            }
        }
    }

    [BurstCompile]
    public struct AdvancePathJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<CrowdAgent> agents;

        public NativeArray<CrowdAgentNavigator> agentNavigators;
        [NativeDisableParallelForRestriction]
        public NativeArray<PolygonId> paths;
        public int pathsStride; // 每个代理的路径长度

        public void Execute(int index)
        {
            var agentNavigator = agentNavigators[index];
            if (!agentNavigator.active)
                return;

            // 计算当前代理的路径起始索引
            int pathStartIndex = index * pathsStride;
            
            if (pathStartIndex + agentNavigator.pathSize > paths.Length)
            {
                agentNavigator.pathSize = 0; // 重置路径大小以避免后续访问
                agentNavigators[index] = agentNavigator;
                return;
            }
            
            var agLoc = agents[index].location;
            var i = 0;
            for (; i < agentNavigator.pathSize; ++i)
            {
                if (paths[pathStartIndex + i] == agLoc.polygon)
                    break;
            }

            var agentNotOnPath = i == agentNavigator.pathSize && i > 0;
            if (agentNotOnPath)
            {
                agentNavigator.MoveTo(agentNavigator.requestedDestination);
                agentNavigators[index] = agentNavigator;
            }
            else if (agentNavigator.destinationInView)
            {
                var distToDest = math.distance(agLoc.position, agentNavigator.pathEnd.position );
                var stoppingDistance = 0.1f;
                agentNavigator.destinationReached = distToDest < stoppingDistance;
                agentNavigator.distanceToDestination = distToDest;
                agentNavigator.goToDestination &= !agentNavigator.destinationReached;
                agentNavigators[index] = agentNavigator;
                if (agentNavigator.destinationReached)
                {
                    i = agentNavigator.pathSize;
                }
            }
            if (i == 0 && !agentNavigator.destinationReached)
                return;

//#if DEBUG_CROWDSYSTEM_ASSERTS
            //var discardsPathWhenDestinationNotReached = (i == pathInfo.size) && !agentNavigator.destinationReached;
            //Debug.Assert(!discardsPathWhenDestinationNotReached);
//#endif

            // Shorten the path by discarding the first nodes
            if (i > 0)
            {
                for (int src = i, dst = 0; src < agentNavigator.pathSize; src++, dst++)
                {
                    paths[pathStartIndex + dst] = paths[pathStartIndex + src];
                }
                agentNavigator.pathSize -= i;
                agentNavigators[index] = agentNavigator;
            }
        }
    }

    [BurstCompile]
    public struct UpdateVelocityJob : IJobParallelFor
    {
        [ReadOnly]
        public NavMeshQuery query;
        [ReadOnly]
        [NativeDisableParallelForRestriction]
        public NativeArray<PolygonId> paths;
        public int pathsStride; // 每个代理的路径长度

        public NativeArray<CrowdAgentNavigator> agentNavigators;
        public NativeArray<CrowdAgent> agents;

        [DeallocateOnJobCompletion]
        [NativeDisableParallelForRestriction]
        public NativeArray<NavMeshLocation> straightPath;

        [DeallocateOnJobCompletion]
        [NativeDisableParallelForRestriction]
        public NativeArray<StraightPathFlags> straightPathFlags;

        [DeallocateOnJobCompletion]
        [NativeDisableParallelForRestriction]
        public NativeArray<float> vertexSide;

        public void Execute(int index)
        {
            var agent = agents[index];
            var agentNavigator = agentNavigators[index];
            if (!agentNavigator.active || !query.IsValid(agent.location))
            {
                if (math.any(agent.velocity))
                {
                    agent.velocity = new float3(0);
                    agents[index] = agent;
                }
                return;
            }

            if (agentNavigator.pathSize > 0 && agentNavigator.goToDestination)
            {
                float3 currentPos = agent.location.position;
                float3 endPos = agentNavigator.pathEnd.position;
                agentNavigator.steeringTarget = endPos;

                if (agentNavigator.pathSize > 1)
                {
                    var cornerCount = 0;
                    // 计算当前代理的路径起始索引
                    int pathStartIndex = index * pathsStride;
                    // 创建一个临时的NativeArray来存储当前代理的路径
                    var tempPath = new NativeArray<PolygonId>(agentNavigator.pathSize, Allocator.Temp);
                    for (int i = 0; i < agentNavigator.pathSize; i++)
                    {
                        tempPath[i] = paths[pathStartIndex + i];
                    }
                    
                    var pathStatus = PathUtils.FindStraightPath(query, currentPos, endPos, tempPath, agentNavigator.pathSize, ref straightPath, ref straightPathFlags, ref vertexSide, ref cornerCount, straightPath.Length);
                    tempPath.Dispose();

                    if (pathStatus.IsSuccess() && cornerCount > 1)
                    {
                        agentNavigator.steeringTarget = straightPath[1].position;
                        agentNavigator.destinationInView = straightPath[1].polygon == agentNavigator.pathEnd.polygon;
                        agentNavigator.nextCornerSide = vertexSide[1];
                    }
                }
                else
                {
                    agentNavigator.destinationInView = true;
                }
                agentNavigators[index] = agentNavigator;

                var velocity = agentNavigator.steeringTarget - currentPos;
                velocity.y = 0.0f;
                agent.velocity = math.any(velocity) ? agentNavigator.speed * math.normalize(velocity) : new float3(0);
            }
            else
            {
                agent.velocity = new float3(0);
            }

            agents[index] = agent;
        }
    }

    [BurstCompile]
    public struct MoveLocationsJob : IJobParallelFor
    {
        [ReadOnly]
        public NavMeshQuery query;

        public NativeArray<CrowdAgent> agents;
        public float dt;

        public void Execute(int index)
        {
            var agent = agents[index];
            var wantedPos = agent.worldPosition + agent.velocity * dt;

            if (query.IsValid(agent.location))
            {
                if (math.any(agent.velocity))
                {
                    agent.location = query.MoveLocation(agent.location, wantedPos);
                }
            }
            else
            {
                // Constrain the position using the location
                agent.location = query.MapLocation(wantedPos, 3 * Vector3.one, 0);
            }
            agent.worldPosition = agent.location.position;

            agents[index] = agent;
        }
    }

    [BurstCompile]
    public struct UpdateQueriesJob : IJob
    {
        public PathQueryQueueEcs queryQueue;
        public int maxIterations;

        public void Execute()
        {
            queryQueue.UpdateTimesliced(maxIterations);
        }
    }

    [BurstCompile]
    public struct ApplyQueryResultsJob : IJob
    {
        public PathQueryQueueEcs queryQueue;
        public NativeArray<PolygonId> paths;
        public NativeArray<CrowdAgentNavigator> agentNavigators;
        public int pathsStride; // 每个代理的路径长度

        public void Execute()
        {
            if (queryQueue.GetResultPathsCount() > 0)
            {
                queryQueue.CopyResultsTo(ref paths, ref agentNavigators, pathsStride);
                queryQueue.ClearResults();
            }
        }
    }

    [BurstCompile]
    public struct QueryCleanupJob : IJob
    {
        public PathQueryQueueEcs queryQueue;
        public NativeArray<uint> pathRequestIdForAgent;

        public void Execute()
        {
            queryQueue.CleanupProcessedRequests(ref pathRequestIdForAgent);
        }
    }
}
