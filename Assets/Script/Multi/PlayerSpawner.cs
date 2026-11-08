using Fusion;
using UnityEngine;

// Gắn vào 1 GameObject trống trong scene Gameplay.
// Không dùng callback PlayerJoined nữa (dễ bị bỏ lỡ khi scene load chậm hơn việc join phòng).
// Thay vào đó mỗi frame kiểm tra: runner đã chạy mà người chơi chưa có nhân vật thì spawn.
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    private NetworkRunner runner;
    private bool loggedMissingPrefab;

    private void Update()
    {
        if (runner == null)
        {
            // Runner được tạo ở scene Main và giữ lại bằng DontDestroyOnLoad.
            runner = FindObjectOfType<NetworkRunner>();
            return;
        }

        if (!runner.IsRunning)
            return;

        if (!playerPrefab.IsValid)
        {
            if (!loggedMissingPrefab)
            {
                Debug.LogError("PlayerSpawner: Chưa gán Player Prefab (hoặc prefab chưa được Bake trong Network Project Config).");
                loggedMissingPrefab = true;
            }
            return;
        }

        if (runner.GameMode == GameMode.Shared)
        {
            // Shared Mode: mỗi máy tự spawn nhân vật của mình.
            TrySpawn(runner.LocalPlayer);
        }
        else if (runner.IsServer)
        {
            // Host/Server Mode: máy host spawn cho tất cả người chơi.
            foreach (PlayerRef player in runner.ActivePlayers)
                TrySpawn(player);
        }
    }

    private void TrySpawn(PlayerRef player)
    {
        if (!player.IsRealPlayer)
            return;

        // Đã có nhân vật rồi thì thôi, tránh spawn trùng.
        if (runner.TryGetPlayerObject(player, out NetworkObject existing) && existing != null)
            return;

        Vector3 pos = Vector3.zero;
        if (spawnPoints != null && spawnPoints.Length > 0)
            pos = spawnPoints[player.RawEncoded % spawnPoints.Length].position;

        NetworkObject obj = runner.Spawn(playerPrefab, pos, Quaternion.identity, player);
        runner.SetPlayerObject(player, obj);

        Debug.Log("PlayerSpawner: đã spawn nhân vật cho player " + player.RawEncoded);
    }
}