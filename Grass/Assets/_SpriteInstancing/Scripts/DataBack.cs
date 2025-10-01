using UnityEngine;

public class GPUDeletionReader : MonoBehaviour
{
    ComputeBuffer deletionIndicesBuffer;
    ComputeBuffer deletionCountBuffer;
    int[] deletionIndicesCPU;   // GPU’dan çekilen silinmiş indexler
    int[] deletionCountCPU = new int[1];
    void Awake()
    {
        deletionCountBuffer = GPUInstancingSystem.Instance.DeletionCountBuffer;
        deletionIndicesBuffer = GPUInstancingSystem.Instance.DeletionIndicesBuffer;
    }


    void Update()
    {
        if (deletionCountBuffer == null || GPUInstancingSystem.Instance.DeletionIndicesBuffer == null)
            return;

        // GPU’daki silinen eleman sayısını oku
        deletionCountBuffer.GetData(deletionCountCPU);
        int count = deletionCountCPU[0];

        if (count > 0)
        {
            if (deletionIndicesCPU == null || deletionIndicesCPU.Length < count)
                deletionIndicesCPU = new int[count];

            // GPU’dan indexleri CPU array’e al
            deletionIndicesBuffer.GetData(deletionIndicesCPU, 0, 0, count);

            // Burada silinen indexleri işleyebilirsin
            for (int i = 0; i < count; i++)
            {
                int deadIndex = deletionIndicesCPU[i];
                Debug.Log("Silinen index: " + deadIndex);
                // TODO: CPU tarafında listelerden/objelerden silme işlemi
            }

            // AppendStructuredBuffer olduğu için counter’ı resetlemek lazım
            deletionIndicesBuffer.SetCounterValue(0);
            deletionCountBuffer.SetData(new int[1] { 0 });
        }
    }

    void OnDestroy()
    {
        deletionIndicesCPU = null;
    }
}

