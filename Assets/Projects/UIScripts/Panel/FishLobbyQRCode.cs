using System;
using BestHTTP;
using Encryption;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Authorization = Encryption.Authorization;

/// <summary>
/// 小鱼游戏进场二维码。
/// 1. 使用已授权 machineCode 请求 GetFishLobbyQRCode。
/// 2. 接口 Tag=1 时，Data 为二维码 PNG 地址。
/// 3. 下载 PNG 并显示到 RawImage。
/// </summary>
public class FishLobbyQRCode : MonoBehaviour
{
    [Header("接口")]
    public string qrApi =
        "https://api.itaocow.com.cn/Factory/Factory/GetFishLobbyQRCode";

    [Header("UI")]
    public RawImage qrImage;

    [Header("启动")]
    public bool loadOnStart = true;

    private Texture2D runtimeTexture;
    private bool isLoading;

    private void Start()
    {
        if (loadOnStart)
        {
            RefreshQRCode();
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
            return;
        }

        if (string.IsNullOrWhiteSpace(qrApi))
        {
            return;
        }

        string url =
            qrApi +
            "?machineCode=" +
            Uri.EscapeDataString(machineCode);

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
            return;
        }

        try
        {
            JObject root = JObject.Parse(response.DataAsText);
            int tag = root.Value<int?>("Tag") ?? 0;
            string message =
                root.Value<string>("Message") ?? string.Empty;
            string imageUrl =
                root.Value<string>("Data") ?? string.Empty;

            if (tag != 1 ||
                string.IsNullOrWhiteSpace(imageUrl))
            {
                isLoading = false;
                return;
            }

            DownloadQrTexture(imageUrl);
        }
        catch (Exception ex)
        {
            isLoading = false;
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

    private void OnQrTextureResponse(
        HTTPRequest request,
        HTTPResponse response)
    {
        isLoading = false;

        if (request.State != HTTPRequestStates.Finished ||
            response == null)
        {
            return;
        }

        Texture2D texture = response.DataAsTexture2D;

        if (texture == null)
        {
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
    }

    private static bool TryGetMachineCode(
        out string machineCode)
    {
        machineCode = string.Empty;

        if (MyMqtt.Instance != null &&
            !string.IsNullOrWhiteSpace(
                MyMqtt.Instance.MachineCode))
        {
            machineCode = MyMqtt.Instance.MachineCode;
            return true;
        }

        string value = string.Empty;
        string decrypted = string.Empty;

        AESEncryption.PlayerGet(
            Authorization.MachineKey,
            HardwareCodeUtil.GetMac(),
            ref value,
            ref decrypted
        );

        if (!string.IsNullOrWhiteSpace(value) &&
            value.Equals(decrypted))
        {
            machineCode = value;
            return true;
        }

        return false;
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
