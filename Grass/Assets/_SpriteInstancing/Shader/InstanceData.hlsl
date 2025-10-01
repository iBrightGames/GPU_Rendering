
struct InstanceData {
    float3 position;
    float3 velocity;
    float3 rotation;
    int active;
};

// Buffers
ComputeBuffer positionsBuffer;      // RWStructuredBuffer<float3>
ComputeBuffer visibilityBuffer;     // RWStructuredBuffer<int> (0 or 1)
ComputeBuffer aliveBuffer;          // RWStructuredBuffer<int> (0 = dead, 1 = alive)
ComputeBuffer deletionIndicesBuffer; // AppendStructuredBuffer<int>
ComputeBuffer deletionCountBuffer;  // RWStructuredBuffer<int> (atomic counter)
ComputeBuffer spawnParamsBuffer;    // StructuredBuffer (CPU sends params)

// Shader parameters
float3 playerPos;
float radius;
uint maxInstances; // CSMain için (başlangıçtaki max buffer boyutu)
uint spawnCount;   // CSSpawnNew için (her frame'de kaç tane ekleneceği)