using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using UnityEngine.UI;
using System;
using System.Linq;
using System.Net.Http;

public static class NetManager
{
    static Socket socket;
    static ByteArray readBuff;
    static Queue<ByteArray> writeQueue;

    public delegate void EventListener(String str);

    private static Dictionary<NetEvent, EventListener> _eventListeners = new Dictionary<NetEvent, EventListener>();

    private static bool isConnecting = false;
    private static bool isClosing = false;

    public delegate void MsgListener(MsgBase mgMsgBase);

    private static Dictionary<string, MsgListener> msgListeners = new Dictionary<string, MsgListener>();

    private static List<MsgBase> msgList = new List<MsgBase>();
    private static int msgCount = 0;
    private readonly static int MAX_MESSAGE_FIRE = 10;

    public static bool isUsePing = true;
    public static int pingInterval = 10;
    private static float lastPingTime = 0;
    private static float lastPongTime = 0;

    public enum NetEvent
    {
        ConnectSucc = 1,
        ConnectFail = 2,
        Close = 3
    }

    #region 初始化

    private static void InitState()
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        readBuff = new ByteArray();
        writeQueue = new Queue<ByteArray>();
        isConnecting = false;
        isClosing = false;

        msgList = new List<MsgBase>();
        msgCount = 0;

        lastPingTime = 0;
        lastPongTime = 0;
        if (!msgListeners.ContainsKey("MsgPong"))
        {
            AddMsgListener("MsgPong",OnMsgPong);
        }
    }

    #endregion

    #region 注册移除事件

    public static void AddEventListener(NetEvent netEvent, EventListener eventListener)
    {
        if (_eventListeners.ContainsKey(netEvent))
        {
            _eventListeners[netEvent] += eventListener;
        }
        else
        {
            _eventListeners[netEvent] = eventListener;
        }
    }

    public static void RemoveEventListener(NetEvent netEvent, EventListener eventListener)
    {
        if (_eventListeners.ContainsKey(netEvent))
        {
            _eventListeners[netEvent] -= eventListener;
            if (_eventListeners[netEvent] == null)
            {
                _eventListeners.Remove(netEvent);
            }
        }
    }

    public static void AddMsgListener(String msgName, MsgListener listener)
    {
        if (msgListeners.ContainsKey(msgName))
        {
            msgListeners[msgName] += listener;
        }
        else
        {
            msgListeners[msgName] = listener;
        }
    }

    public static void RemoveMsgListener(String msgName, MsgListener listener)
    {
        if (msgListeners.ContainsKey(msgName))
        {
            msgListeners[msgName] -= listener;
            if (msgListeners[msgName] == null)
            {
                msgListeners.Remove(msgName);
            }
        }
    }

    #endregion

    #region 派发事件

    public static void FireEvent(NetEvent netEvent, String err)
    {
        if (_eventListeners.ContainsKey(netEvent))
        {
            _eventListeners[netEvent](err);
        }
    }

    public static void FireEvent(String msgName, MsgBase msgBase)
    {
        if (msgListeners.ContainsKey(msgName))
        {
            msgListeners[msgName](msgBase);
        }
    }

    #endregion

    #region 连接

    public static void Connect(String ip, int prot)
    {
        if (socket != null && socket.Connected)
        {
            Debug.Log("Connect fail,already connected");
            return;
        }

        if (isConnecting)
        {
            Debug.Log("Connect fail,isConnecting");
            return;
        }

        InitState();
        socket.NoDelay = true;
        isConnecting = true;
        socket.BeginConnect(ip, prot, ConnectCallback, socket);
    }

    private static void ConnectCallback(IAsyncResult ar)
    {
        try
        {
            Socket socket = (Socket) ar.AsyncState;
            socket.EndConnect(ar);
            Debug.Log("Connect Succ");
            FireEvent(NetEvent.ConnectSucc, "");
            isConnecting = false;

            socket.BeginReceive(readBuff.bytes, readBuff.writeIdx, readBuff.remain, 0, ReceiveCallback, socket);
        }
        catch (Exception e)
        {
            Debug.Log("Connect fail" + e);
            FireEvent(NetEvent.ConnectFail, e.ToString());
            isConnecting = false;
        }
    }

    #endregion

    #region 关闭

    public static void Close()
    {
        if (socket == null || !socket.Connected)
        {
            return;
        }

        if (isConnecting)
        {
            return;
        }

        if (writeQueue.Count > 0)
        {
            isClosing = true;
        }
        else
        {
            socket.Close();
            FireEvent(NetEvent.Close, "");
        }
    }

    #endregion

    #region 发送数据

    public static void Send(MsgBase msgBase)
    {
        if (socket == null || !socket.Connected)
        {
            return;
        }

        if (isConnecting)
        {
            return;
        }

        if (isClosing)
        {
            return;
        }

        byte[] bodyBytes = MsgBase.EnCode(msgBase);
        byte[] nameBytes = MsgBase.EnCodeName(msgBase);
        int len = bodyBytes.Length + nameBytes.Length;

        byte[] sendBytes = new byte[len + 2];
        sendBytes[0] = (byte) (len % 256);
        sendBytes[1] = (byte) (len / 256);

        Array.Copy(bodyBytes, 0, sendBytes, 2 + nameBytes.Length, bodyBytes.Length);
        Array.Copy(nameBytes, 0, sendBytes, 2, nameBytes.Length);

        ByteArray array = new ByteArray(sendBytes);
        int count = 0;

        lock (writeQueue)
        {
            writeQueue.Enqueue(array);
            count = writeQueue.Count;
        }

        if (count == 1)
        {
            socket.BeginSend(sendBytes, 0, sendBytes.Length, 0, SendCallback, socket);
        }
    }

    private static void SendCallback(IAsyncResult ar)
    {
        Socket socket = (Socket) ar.AsyncState;
        if (socket == null || !socket.Connected)
            return;

        int count = socket.EndSend(ar);
        ByteArray ba;
        lock (writeQueue)
        {
            ba = writeQueue.First();
        }

        ba.readIdx += count;
        if (ba.length == 0)
        {
            lock (writeQueue)
            {
                writeQueue.Dequeue();
                ba = writeQueue.First();
            }
        }

        if (ba != null)
        {
            socket.BeginSend(ba.bytes, ba.readIdx, ba.length, 0, SendCallback, socket);
        }
        else if (isClosing)
        {
            socket.Close();
        }
    }

    #endregion

    #region 接收数据

    private static void ReceiveCallback(IAsyncResult ar)
    {
        try
        {
            Socket socket = (Socket) ar.AsyncState;
            int count = socket.EndReceive(ar);
            if (count == 0)
            {
                Close();
                return;
            }

            readBuff.writeIdx += count;
            OnReceiveData();
            if (readBuff.remain < 8)
            {
                readBuff.MoveBytes();
                readBuff.ReSize(readBuff.length * 2);
            }

            socket.BeginReceive(readBuff.bytes, readBuff.writeIdx, readBuff.remain, 0, ReceiveCallback, socket);
        }
        catch (Exception e)
        {
            Debug.Log("Socket Receive fail" + e.ToString());
        }
    }

    private static void OnReceiveData()
    {
        if (readBuff.length < 2)
        {
            return;
        }

        int readIdx = readBuff.readIdx;
        byte[] bytes = readBuff.bytes;
        Int16 bodyLength = (Int16) (bytes[readIdx + 1] << 8 | bytes[readIdx]);

        if (readBuff.length < bodyLength)
        {
            return;
        }

        readBuff.readIdx += 2;
        int nameCount = 0;
        string protoName = MsgBase.DeCodeName(readBuff.bytes, readBuff.readIdx, out nameCount);
        if (protoName == "")
        {
            Debug.Log("Recevie MsgBase.DeCodename fail");
            return;
        }

        readBuff.readIdx += nameCount;
        int bodyCount = bodyLength - nameCount;
        MsgBase msgBase = MsgBase.DeCode(protoName, readBuff.bytes, readBuff.readIdx, bodyCount);
        readBuff.readIdx += bodyCount;
        readBuff.CheckAndMoveBytes();

        lock (msgList)
        {
            msgList.Add(msgBase);
        }

        if (readBuff.length > 2)
        {
            OnReceiveData();
        }
    }

    #endregion

    #region 处理数据

    private static void MsgUpdate()
    {
        if (msgCount == 0)
        {
            return;
        }

        for (int i = 0; i < MAX_MESSAGE_FIRE; i++)
        {
            MsgBase msgBase = null;
            lock (msgList)
            {
                if (msgList.Count > 0)
                {
                    msgBase = msgList[0];
                    msgList.RemoveAt(0);
                    msgCount--;
                }
            }

            if (msgBase != null)
            {
                FireEvent(msgBase.protoName, msgBase);
            }
            else
            {
                break;
            }
        }
    }

    #endregion

    #region 心跳检测

    public static void PingUpdate()
    {
        if (!isUsePing)
        {
            return;
        }

        if (Time.time - lastPingTime > pingInterval)
        {
            SysMsg.MsgPing ping = new SysMsg.MsgPing();
            Send(ping);
            lastPingTime = Time.time;
        }

        if (Time.time - lastPongTime > pingInterval * 4)
        {
            Close();
        }
    }

    public static void OnMsgPong(MsgBase msgBase)
    {
        lastPongTime = Time.time;
    }

    #endregion

    #region Update

    public static void Update()
    {
        MsgUpdate();
        PingUpdate();
    }

    #endregion
}