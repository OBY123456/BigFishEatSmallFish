using System;
using System.Collections;
using System.IO;
using BestHTTP;
using Encryption;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 小鱼游戏进场二维码。
/// 1. 使用已授权 machineCode 请求 GetFishLobbyQRCode。
/// 2. 接口 Tag=1 时，Data 为二维码 PNG 地址。
/// 3. 下载 PNG、保存原始图片字节，并显示到 RawImage。
/// </summary>
public class FishLobbyQRCode : MonoBehaviour
{
    [Header("接口")]
    private string qrApi = "https://api.itaocow.com.cn/Factory/Factory/GetFishLobbyQRCode";

    [Header("UI")]
    public RawImage qrImage;

    private Texture2D runtimeTexture;
    private bool isLoading;

    private IEnumerator Start()
    {
        bool waitingLogged = false;
        while (!isLoading)
        {
            string machineCode;
            if (TryGetMachineCode(out machineCode))
            {
                RefreshQRCode();
                yield break;
            }

            if (!waitingLogged)
            {
                Debug.LogWarning("[FishLobbyQRCode] 当前授权设备编码尚未就绪，正在等待授权缓存。", this);
                waitingLogged = true;
            }

            yield return new WaitForSeconds(2f);
        }
    }

    [ContextMenu("刷新小鱼进场二维码")]
    public void RefreshQRCode()
    {
        if (isLoading)
        {
            return;
        }

        string machineCode;

        if (!TryGetMachineCode(out machineCode))
        {
            Debug.LogWarning("[FishLobbyQRCode] 未获取到 machineCode。", this);
            return;
        }

        if (string.IsNullOrWhiteSpace(qrApi))
        {
            Debug.LogWarning("[FishLobbyQRCode] qrApi 为空。", this);
            return;
        }

        string url = qrApi + "?machineCode=" + Uri.EscapeDataString(machineCode);

        isLoading = true;

        HTTPRequest request = new HTTPRequest(
            new Uri(url),
            HTTPMethods.Get,
            OnQrApiResponse
        );

        request.SetHeader("accept", "application/json");
        request.Send();
    }

    private void OnQrApiResponse(
        HTTPRequest request,
        HTTPResponse response)
    {
        if (request.State != HTTPRequestStates.Finished ||
            response == null ||
            string.IsNullOrWhiteSpace(response.DataAsText))
        {
            isLoading = false;
            Debug.LogWarning("[FishLobbyQRCode] 获取二维码地址失败。", this);
            return;
        }

        try
        {
            JObject root = JObject.Parse(response.DataAsText);
            int tag = root.Value<int?>("Tag") ?? 0;
            string message = root.Value<string>("Message") ?? string.Empty;
            string imageUrl = root.Value<string>("Data") ?? string.Empty;

            if (tag != 1 || string.IsNullOrWhiteSpace(imageUrl))
            {
                isLoading = false;
                Debug.LogWarning($"[FishLobbyQRCode] 获取二维码地址失败，Tag:{tag} Message:{message}",this);
                return;
            }

            DownloadQrTexture(imageUrl);
        }
        catch (Exception ex)
        {
            isLoading = false;
            Debug.LogError($"[FishLobbyQRCode] 解析二维码接口数据失败：{ex.Message}",this);
        }
    }

    private void DownloadQrTexture(string imageUrl)
    {
        HTTPRequest request = new HTTPRequest(
            new Uri(imageUrl),
            HTTPMethods.Get,
            OnQrTextureResponse
        );

        request.SetHeader("accept", "image/*");
        request.Send();
    }

    private void OnQrTextureResponse(HTTPRequest request,HTTPResponse response)
    {
        isLoading = false;

        if (request.State != HTTPRequestStates.Finished ||
            response == null)
        {
            Debug.LogWarning("[FishLobbyQRCode] 下载二维码图片失败。", this);
            return;
        }

        byte[] imageBytes = response.Data;

        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogWarning("[FishLobbyQRCode] 二维码图片数据为空。", this);
            return;
        }

        // 直接保存 HTTP 下载到的原始 PNG 字节。
        // 不再对 Texture2D 调 EncodeToPNG，因此完全不依赖 Texture 是否 Read/Write。
        SaveQRCodeToLocal(imageBytes);

        // 不使用 response.DataAsTexture2D。
        // BestHTTP 的 DataAsTexture2D 内部调用 LoadImage(Data, true)，
        // 其中 true 的含义是 markNonReadable=true，会释放 CPU 端像素数据。
        Texture2D texture = new Texture2D(
            2,
            2,
            TextureFormat.RGBA32,
            false
        );

        // false = 保持 Texture 可读。
        bool loadSuccess = texture.LoadImage(imageBytes, false);

        if (!loadSuccess)
        {
            Destroy(texture);
            Debug.LogError("[FishLobbyQRCode] 二维码 Texture2D.LoadImage 失败。", this);
            return;
        }

        if (runtimeTexture != null &&
            runtimeTexture != texture)
        {
            Destroy(runtimeTexture);
        }

        runtimeTexture = texture;

        if (qrImage != null)
        {
            qrImage.texture = runtimeTexture;
            qrImage.gameObject.SetActive(true);
        }

        Debug.Log($"[FishLobbyQRCode] 二维码加载完成：{runtimeTexture.width}x{runtimeTexture.height}",this);
    }

    /// <summary>
    /// 直接保存服务器返回的原始图片字节。
    /// Editor / PC：StreamingAssets/二维码.png。
    /// Android：StreamingAssets 在 APK/AAB 内不可写，因此保存到 persistentDataPath/二维码.png。
    /// </summary>
    private void SaveQRCodeToLocal(byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return;
        }

        try
        {
            string saveDirectory;

#if UNITY_ANDROID && !UNITY_EDITOR
            saveDirectory = Application.persistentDataPath;
#else
            saveDirectory = Application.streamingAssetsPath;
#endif

            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            string savePath = Path.Combine(saveDirectory, "二维码.png");
            File.WriteAllBytes(savePath, pngBytes);

            Debug.Log(
                $"[FishLobbyQRCode] 二维码已保存：{savePath}，大小：{pngBytes.Length} bytes",
                this
            );

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
        catch (Exception ex)
        {
            Debug.LogError(
                $"[FishLobbyQRCode] 保存二维码失败：{ex.Message}",
                this
            );
        }
    }

    private static bool TryGetMachineCode(out string machineCode)
    {
        machineCode = string.Empty;

        if (MyMqtt.Instance != null)
        {
            string mqttMachineCode = MyMqtt.Instance.MachineCode;
            if (!string.IsNullOrWhiteSpace(mqttMachineCode))
            {
                machineCode = mqttMachineCode;
                return true;
            }
        }

        return Authorization.TryGetStoredMachineCode(out machineCode);
    }

    private void OnDestroy()
    {
        if (runtimeTexture != null)
        {
            Destroy(runtimeTexture);
            runtimeTexture = null;
        }
    }
}
