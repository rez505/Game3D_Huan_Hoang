using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Gắn vào 1 GameObject trong scene Main.
public class MainMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField roomIdInput;
    [SerializeField] private Button startButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Scene")]
    [Tooltip("Tên scene gameplay (phải được thêm vào Build Settings)")]
    [SerializeField] private string gameplaySceneName = "Gameplay";

    [Header("Mạng")]
    [SerializeField] private GameMode gameMode = GameMode.Shared;

    private NetworkRunner _runner;
    private bool _starting;

    private void Awake()
    {
        // Khoá 60 FPS để mọi máy chạy cùng nhịp, chuyển động đều và đồng bộ.
        // vSync phải tắt thì targetFrameRate mới có tác dụng.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        // Cho game vẫn chạy khi cửa sổ không được chọn.
        // Nếu không, khi mở 2 bản game cùng lúc để thử, bản không được chọn sẽ bị chậm / đứng
        // làm nhìn như hai bên lệch tốc độ.
        Application.runInBackground = true;

        startButton.onClick.AddListener(() => _ = StartGame());
    }

    private async Task StartGame()
    {
        if (_starting) return;

        string roomId = roomIdInput.text.Trim();
        if (string.IsNullOrEmpty(roomId))
        {
            SetStatus("Vui lòng nhập ID phòng!");
            return;
        }

        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{gameplaySceneName}.unity");
        if (sceneIndex < 0)
        {
            // Nếu scene không nằm ở Assets/Scenes thì tìm theo tên
            sceneIndex = FindSceneIndexByName(gameplaySceneName);
        }
        if (sceneIndex < 0)
        {
            SetStatus($"Không tìm thấy scene '{gameplaySceneName}' trong Build Settings!");
            return;
        }

        _starting = true;
        startButton.interactable = false;
        SetStatus("Đang kết nối...");

        // Tạo NetworkRunner và giữ lại khi chuyển scene
        _runner = new GameObject("NetworkRunner").AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;
        DontDestroyOnLoad(_runner.gameObject);

        var sceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = gameMode,
            SessionName = roomId,
            Scene = SceneRef.FromIndex(sceneIndex),
            SceneManager = sceneManager
        });

        if (!result.Ok)
        {
            SetStatus($"Lỗi: {result.ShutdownReason}");
            Destroy(_runner.gameObject);
            _starting = false;
            startButton.interactable = true;
        }
        // Nếu thành công, Fusion tự động load scene Gameplay.
    }

    private static int FindSceneIndexByName(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                return i;
        }
        return -1;
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log(message);
    }
}