using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using Encryption;
using System.Net;
using Authorization = Encryption.Authorization;
using MqttData;
using UnityEngine.Networking;
using System.IO;

public class MyMqtt : MonoSingle<MyMqtt>
{
    private MQTT mqtt;

    // MQTT 底层回调不保证在 Unity 主线程。游戏逻辑统一从此事件接收主线程消息。
    public static event Action<MQTTMsg> MainThreadMessageReceived;

    //测试使用的授权唯一码
    string machineCode = string.Empty;

    public string MachineCode
    {
        get
        {
            if (string.IsNullOrWhiteSpace(machineCode)) TryInitializeMachineCode();
            return machineCode;
        }
    }

    private float nextMachineCodeRetryTime;
    private bool machineCodeNetworkCheckStarted;

    public bool IsNetwork = false;

    [Header("MQTT 主线程派发")]
    [Min(1)]
    public int maxMessagesPerFrame = 256;

    private static readonly object o = new object();

    private readonly Queue<MQTTMsg> MsgQueue = new Queue<MQTTMsg>();

    protected override void Awake()
    {
        base.Awake();
        MqttEvent.Mqttevent -= MqttEvent_Mqttevent;
        MqttEvent.Mqttevent += MqttEvent_Mqttevent;
        Loom.Initialize();
        TryInitializeMachineCode();
    }

    private bool TryInitializeMachineCode()
    {
        if (!string.IsNullOrWhiteSpace(machineCode)) return true;
        if (Time.unscaledTime < nextMachineCodeRetryTime) return false;
        nextMachineCodeRetryTime = Time.unscaledTime + 2f;

        string resolvedMachineCode;
        if (!Authorization.TryGetStoredMachineCode(out resolvedMachineCode) ||
            string.IsNullOrWhiteSpace(resolvedMachineCode))
            return false;

        machineCode = resolvedMachineCode;
        if (DebugManager.Instance != null) DebugManager.Instance.AddData("设备编码", machineCode);
        Debug.Log("[MyMqtt] 已从当前授权缓存读取设备编码，开始检查网络。");

        if (!machineCodeNetworkCheckStarted)
        {
            machineCodeNetworkCheckStarted = true;
            IsGetHostAsync(MQTT.Host, OnGetHostCallBack);
        }
        return true;
    }

    void OnGetHostCallBack(bool b)
    {
        IsNetwork = b;
        if (IsNetwork)
        {
            if (mqtt == null)
            {
                Connet();
            }
        }
        else
        {
            if (mqtt != null)
            {
                mqtt.DisConnect();
                mqtt = null;
            }
        }
    }

    private void MqttEvent_Mqttevent(MQTTMsg obj)
    {
        if (obj == null)
        {
            return;
        }

        lock (o)
        {
            MsgQueue.Enqueue(obj);
        }
    }

    private void Update()
    {
        // Authorization may finish after this persistent singleton's Awake; retry without blocking the main thread.
        if (string.IsNullOrWhiteSpace(machineCode) && Time.unscaledTime >= nextMachineCodeRetryTime)
            TryInitializeMachineCode();

        int processed = 0;
        int safeLimit = Mathf.Max(1, maxMessagesPerFrame);

        while (processed < safeLimit)
        {
            MQTTMsg msg = null;

            lock (o)
            {
                if (MsgQueue.Count > 0)
                {
                    msg = MsgQueue.Dequeue();
                }
            }

            if (msg == null)
            {
                break;
            }

            processed++;
            MainThreadMessageReceived?.Invoke(msg);
        }
    }

    private void IsGetHostAsync(string host, Action<bool> action)
    {
        try
        {
            Dns.BeginGetHostAddresses(host, (ia) =>
            {
                try
                {
                    IPAddress[] iPAddresses = Dns.EndGetHostAddresses(ia);
                    Loom.QueueOnMainThread(() =>
                    {
                        action.Invoke(iPAddresses.Length > 0);
                        InvokeUtil.Instance.Run(() =>
                        {
                            IsGetHostAsync(host, action);
                        }, 1f);
                    });
                }
                catch
                {
                    Loom.QueueOnMainThread(() =>
                    {
                        action.Invoke(false);
                        InvokeUtil.Instance.Run(() =>
                        {
                            IsGetHostAsync(host, action);
                        }, 1f);
                    });
                }
            }, null);
        }
        catch
        {
            Loom.QueueOnMainThread(() =>
            {
                action.Invoke(false);
                InvokeUtil.Instance.Run(() =>
                {
                    IsGetHostAsync(host, action);
                }, 1f);
            });
        }
    }

    private void Connet()
    {
        mqtt = new MQTT();
        string usr, pwd;
        GenUserInfo(machineCode, out usr, out pwd);
        bool isconnet = mqtt.ConnectMqtt(machineCode, usr, pwd);
        if (!isconnet)
        {
            mqtt.DisConnect();
            mqtt = null;
        }
        else
        {
            mqtt.Subscribe(machineCode);
            CancelInvoke(nameof(Heartbeat));
            InvokeRepeating(nameof(Heartbeat),60,60);
        }
    }

    private void Heartbeat()
    {
        Send(MqttData.MsgType.心跳包,string.Empty);
    }

    public void Send(MqttData.MsgType msgType,string data)
    {
        MQTTMsg msg = new MQTTMsg(msgType,data);
        if(mqtt != null && mqtt.IsConnected && !string.IsNullOrEmpty(machineCode) && msg != null)
        {
            if(Application.platform == RuntimePlatform.Android)
            {
                string temp = JsonUtility.ToJson(msg);
                //Debug.Log("发送==" +temp);
                mqtt.Publish(machineCode, temp);
            }
            else
            {
                string temp = JsonConvert.SerializeObject(msg);
                //Debug.Log("发送==" +temp);
                mqtt.Publish(machineCode, temp);
            }
        }
    }

    /// <summary>
    /// 小鱼游戏 Join ACK。只有 subcmd=1 调用这个方法。
    /// </summary>
    public void SendFishJoinAck(
        long playerId,
        bool success,
        string message)
    {
        CS_小鱼游戏_进场ACK ack =
            new CS_小鱼游戏_进场ACK(
                playerId,
                success,
                message
            );

        Send(
            MsgType.小鱼游戏,
            JsonConvert.SerializeObject(ack)
        );
    }

    private void OnDestroy()
    {
        if (mqtt != null)
        {
            mqtt.DisConnect();
        }

        MqttEvent.Mqttevent -= MqttEvent_Mqttevent;

        lock (o)
        {
            MsgQueue.Clear();
        }
    }

    public static DateTime GetBeiJingTime()
    {
        return DateTime.UtcNow + new TimeSpan(8, 0, 0);
    }

    /// <summary>
    /// 使用设备唯一码计算mqtt连接账户密码
    /// </summary>
    /// <param name="machineCode">设备授权唯一码</param>
    /// <param name="user">用户名</param>
    /// <param name="pwd">密码</param>
    public static void GenUserInfo(string machineCode, out string user, out string pwd)
    {
        const string productKey = "&zsproducts";
        user = machineCode + productKey;
        TimeSpan ts = GetBeiJingTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        var utcTime = Convert.ToInt64(ts.TotalMilliseconds);
        utcTime = 2524608000000;
        string Pwd = string.Format("clientId{0}deviceName{1}productKey{2}timestamp{3}", machineCode, machineCode, "zsproducts", utcTime);
        string key0 = user;

        Encoding encoding = Encoding.ASCII;

        HMACSHA1 myHMACSHA0 = new HMACSHA1(encoding.GetBytes(key0));
        byte[] byteKey = myHMACSHA0.ComputeHash(encoding.GetBytes(machineCode));
        var enText = new StringBuilder();
        foreach (byte iByte in byteKey)
        {
            enText.AppendFormat("{0:X2}", iByte);
        }
        var key1 = enText.ToString();

        encoding = Encoding.ASCII;
        HMACSHA1 myHMACSHA1 = new HMACSHA1(encoding.GetBytes(key1));
        byte[] byteText = myHMACSHA1.ComputeHash(encoding.GetBytes(Pwd));

        enText = new StringBuilder();
        foreach (byte iByte in byteText)
        {
            enText.AppendFormat("{0:X2}", iByte);
        }
        pwd = enText.ToString();
    }

    //private int msgIndex = 0;
    //private void Update()
    //{
    //    //if(Input.GetKeyDown(KeyCode.Space))
    //    //{
    //    //    msgIndex++;
    //    //    MsgType msgType = (MsgType)msgIndex;
    //    //    switch (msgType)
    //    //    {
    //    //        case MsgType.心跳包:
    //    //            Send(msgType,string.Empty);
    //    //            break;
    //    //        case MsgType.安装应用:
    //    //            Send(msgType,JsonConvert.SerializeObject(new CS_安装应用("001",576758124745068544,1)));
    //    //            break;
    //    //        case MsgType.播放视频_启动游戏:
    //    //            Send(msgType,"1");
    //    //            break;
    //    //        case MsgType.获取当前设备已安装产品:
    //    //            break;
    //    //        case MsgType.下载应用_视频成功通知:
    //    //            Send(msgType,JsonConvert.SerializeObject(new CS_下载资源("001",576758124745068544,1)));
    //    //            break;
    //    //        case MsgType.获取服务器视频_游戏列表:
    //    //            Send(msgType,string.Empty);
    //    //            break;
    //    //        case MsgType.设置参数:
    //    //            break;
    //    //        case MsgType.获取设备硬件信息:
    //    //            break;
    //    //        default:
    //    //            break;
    //    //    }
    //    //}

    //    //if(Input.GetKeyDown(KeyCode.P))
    //    //{
    //    //    Send(MsgType.获取服务器视频_游戏列表,string.Empty);
    //    //}
    //}
}
