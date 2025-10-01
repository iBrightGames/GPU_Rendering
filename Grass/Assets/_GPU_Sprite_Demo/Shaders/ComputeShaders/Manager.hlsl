// struct ItemData{
//     float3 Position;
//     int IsActive;//0= görünmez, 1= görünür
// }

// RWStructuredBuffer<ItemData> pingBuffer;
// RWStructuredBuffer<ItemData> pongBuffer;

// StructuredBuffer<ItemData> newItemsBuffer;//cpudan gelecek sonra 0lanacak- sadece okunması lazım
// AppendStructuredBuffer<ItemData> deletedItemsBuffer;// cpuya gidecek sonra 0lanacak- yazılması lazım 

// AppendStructuredBuffer<ItemData> activeItemsBuffer;//deleted items buradan seçilecek
// StructuredBuffer<ItemData> renderBuffer;//render için
// // AppendStructuredBuffer<ItemData> deActiveItemsBuffer;
// RWStructuredBuffer<uint> argsBuffer;

